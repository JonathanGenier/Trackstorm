using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class CarRackChecks
{
    private float _catapultAim;
    private Action? _sampleCatapult;
    private IEnumerable<SalvoCatapult> Catapults => _arenas.Select(a => a.Bodies[Shooter].Rack.Catapult!);

    private async Task CheckSalvoCatapult()
    {
        string output = ProjectSettings.GlobalizePath("res://.godot/ts175");
        System.IO.Directory.CreateDirectory(output);
        ProcessPriority = 1000;
        string? connectionFailure = null;
        int connectionSamples = 0;
        _sampleCatapult = () =>
        {
            if (connectionFailure is not null) { return; }
            foreach (var catapult in Catapults.Where(c => c is { Visible: true }))
            {
                try { CatapultConnectionAudit.Verify(catapult); connectionSamples++; }
                catch (InvalidOperationException exception) { connectionFailure = exception.Message; }
            }
        };
        await SetupHandoff(HeldItem.Salvo, HeldItem.MachineGun);
        await Until(() => AllPresent(HeldItem.Salvo), "Both UDP peers complete rack, slide, telescope, winch and captive feed");
        Check(Catapults.All(c => c.VisibleRounds == 5 && c.ArmDegrees is > -5.1f and < -4.9f), "Five horizontal rounds; cocked spoon slopes down toward bowl");
        foreach (var arena in _arenas)
        {
            var catapult = arena.Bodies[Shooter].Rack.Catapult!;
            catapult.PrepareRelease(0); catapult.Present(1, false, null, 0, 1);
            var model = arena.Bodies[Shooter].Rack.GetParent<Node3D>();
            var expected = SalvoFlight.LaunchOrigin(new(N.Vector3.Zero, N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero), catapult.Yaw);
            Check(model.ToLocal(catapult.Bowl.GlobalPosition).DistanceTo(Vehicles.VehicleBody.ToGodot(expected)) < .005f,
                "Authored bowl at release agrees with authoritative launch socket");
            catapult.Present(1, false, null, 1, 1);
        }
        foreach (var catapult in Catapults)
        {
            var yaw = (Node3D)catapult.FindChild("TurretYaw", true, false);
            var foot = (Node3D)catapult.FindChild("SpringFoot", true, false);
            Vector3 seated = yaw.ToLocal(foot.GlobalPosition);
            foreach (float progress in new[] { .65f, .68f, .72f, .80f, .84f, .92f, 1f })
            {
                catapult.Present(progress, false, null, 0, 1);
                Check(yaw.ToLocal(foot.GlobalPosition).DistanceTo(seated) < .001f,
                    "Spring base is already seated and stationary throughout cocking: " + progress);
            }
        }
        await CaptureCatapult("side-ready", new(-7, 2.7f, 1));
        await CaptureCatapult("rear-winch", new(-1.9f, 2.2f, 6));
        await CaptureCatapult("magazine", new(-3, 4.5f, 3));
        await CaptureCatapult("mechanics-close", new(-4, 2.7f, .5f), 1.8f);
        await CaptureCatapult("winch-close", new(-.5f, 2.2f, 5), 1.1f);
        var readyDrums = Catapults.Select(c => ((Node3D)c.FindChild("WinchDrum", true, false)).Rotation.X).ToArray();
        foreach (float angle in new[] { 45f, 90f, 135f, 180f, -135f, -90f, -45f, 0f })
        {
            _catapultAim = Mathf.DegToRad(angle);
            await Until(() => Catapults.All(c => Math.Abs(Mathf.AngleDifference(c.Yaw, _catapultAim)) < .025f), "Accepted camera articulation reaches " + angle + " degrees on both peers");
            foreach (var arena in _arenas)
            {
                var catapult = arena.Bodies[Shooter].Rack.Catapult!;
                var model = arena.Bodies[Shooter].Rack.GetParent<Node3D>();
                Check(model.ToLocal(catapult.Bowl.GlobalPosition).Y > 1.3f, "Loaded bowl remains above conservative cabin at yaw " + angle);
                Check(catapult.VisibleRounds == 5, "Aiming never consumes ammunition");
            }
            await CaptureCatapult("yaw-" + angle, new(-6, 4, 5));
            Check(Catapults.Select((c, i) => Math.Abs(Mathf.AngleDifference(
                ((Node3D)c.FindChild("WinchDrum", true, false)).Rotation.X, readyDrums[i])) < .002f).All(v => v),
                "Yaw alone does not wind or unwind the fixed winch: " + angle);
        }
        // Native driving uses the normal authority and replicated presentation.
        await Frames(90, throttle: 18000, steer: 8000);
        Check(Catapults.All(c => c.VisibleRounds == 5 && c.IsReady), "Driving and steering retain captive ammunition on both peers");
        Position(1000, false);
        await Frames(30);
        foreach (float roll in new[] { 0f, 90f, 180f })
        {
            var world = _arenas[0].Driver.Host!.World;
            var state = world.State;
            var pose = new VehiclePhysicsState(new(0, 40, 35), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, Mathf.DegToRad(roll)), new(0, 6, -5), N.Vector3.Zero);
            _arenas[0].Bodies[Shooter].Apply(pose);
            world.Restore(new(state.Tick, state.LastInput, state.Vehicles.Select(vehicle => vehicle.VehicleId != Shooter ? vehicle :
                new VehicleSnapshot(vehicle.VehicleId, vehicle.LifeId, new VehicleState(state.Tick, pose, false, false, 0, 0), vehicle.Damage, pose)), state.Match));
            await Frames(25);
            Check(!_arenas[0].Driver.Host!.World.GetVehicle(Shooter).Movement.Grounded && Catapults.All(c => c.VisibleRounds == 5 && c.IsReady),
                "Native airborne motion retains loaded presentation at initial roll " + roll);
            await CaptureCatapult("airborne-" + roll, new(-6, 4, 5));
        }
        Position(1000, false);
        await Frames(30);
        for (int cycle = 0; cycle < 2; cycle++)
        {
            await SwitchHandoff(HeldItem.MachineGun, "catapult-safe-stow-" + cycle);
            await SwitchHandoff(HeldItem.Salvo, "catapult-redeploy-" + cycle);
            Check(Catapults.All(c => c.VisibleRounds == 5), "Selection round trip does not consume or duplicate rounds");
        }
        // Exercise the visual rig at extreme chassis orientations without inventing
        // a new flight/roll controller: all coordinates remain vehicle-local.
        var original = _arenas[1].Bodies[Shooter].Rack.GetParent<Node3D>();
        var transform = original.Transform;
        foreach (float roll in new[] { 90f, 180f, 270f })
        {
            original.Transform = new(transform.Basis.Rotated(Vector3.Forward, Mathf.DegToRad(roll)), transform.Origin);
            var catapult = _arenas[1].Bodies[Shooter].Rack.Catapult!;
            catapult.Present(1, false, null, 0, 1);
            Check(catapult.Rounds.Count(r => r.Visible) == 5 &&
                catapult.Rounds[4].GlobalPosition.DistanceTo(catapult.Bowl.GlobalPosition) < .001f, "Loaded round remains captive at roll " + roll);
            await CaptureCatapult("roll-" + roll, new(-6, 4, 5));
        }
        original.Transform = transform;
        _catapultAim = Mathf.Pi / 2;
        await Until(() => Catapults.All(c => Math.Abs(Mathf.AngleDifference(c.Yaw, _catapultAim)) < .025f), "Side aim established before repeated releases");
        float compressed = Catapults.First().SpringLength;
        float highest = -5, longest = compressed;
        int samples = 0;
        var previousDrums = Catapults.ToDictionary(c => c, c => ((Node3D)c.FindChild("WinchDrum", true, false)).Rotation.X);
        float paidOut = 0, hauledIn = 0;
        var anticipations = Catapults.ToDictionary(c => c, _ => new HashSet<ulong>());
        _sampleHandoff = () =>
        {
            foreach (var arena in _arenas)
            {
                var slot = arena.Driver.ItemState!.Slots.Single(s => s.Vehicle == Shooter).Active;
                var catapult = arena.Bodies[Shooter].Rack.Catapult!;
                ulong tick = arena.Driver.Latest!.Tick;
                if (slot.SalvoCommitted && slot.SalvoShots < 5 && slot.SalvoReadyTick > tick &&
                    slot.SalvoReadyTick - tick <= ItemSlot.SalvoReleaseTicks && catapult.ArmDegrees > -4)
                { anticipations[catapult].Add(slot.SalvoReadyTick); }
            }
            foreach (var catapult in Catapults.Where(c => c is not null))
            {
                highest = Math.Max(highest, catapult.ArmDegrees);
                longest = Math.Max(longest, catapult.SpringLength);
                float drum = ((Node3D)catapult.FindChild("WinchDrum", true, false)).Rotation.X;
                float movement = Mathf.AngleDifference(previousDrums[catapult], drum);
                paidOut += Math.Max(0, -movement); hauledIn += Math.Max(0, movement);
                previousDrums[catapult] = drum;
                if (catapult.ArmDegrees > 45.01f) { throw new InvalidOperationException("Spoon passed its mechanical stop."); }
                if (catapult.VisibleRounds > 0 && Math.Abs(Mathf.AngleDifference(catapult.Yaw, _catapultAim)) > .1f)
                { throw new InvalidOperationException("An accepted shot reset turret heading."); }
                samples++;
            }
        };
        Check(_arenas[1].Driver.RequestItemUse(), "One activation commits five mechanical cycles");
        for (int round = 4; round >= 0; round--)
        {
            await Until(() => _arenas.All(a => a.Driver.ItemState!.Slots.Single(s => s.Vehicle == Shooter).Active.SalvoShots == round), "Accepted remaining rounds " + round);
            if (round == 4)
            {
                await Frames(3);
                await CaptureCatapult("release-stop", new(-7, 4.5f, 5), 2.0f);
                await Frames(10);
                await CaptureCatapult("winch-reset", new(-7, 4.5f, 5), 2.0f);
            }
            if (round > 0)
            {
                await Until(() => Catapults.All(c => c.IsReady && c.VisibleRounds == round), "Winch/feed reset retains exactly " + round + " rounds");
                await CaptureCatapult("remaining-" + round, new(-3, 4, 3));
            }
            else { await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Final round releases then fully nests rack and closes trunk"); }
        }
        _sampleHandoff = null;
        Check(anticipations.Values.All(deadlines => deadlines.Count == 4),
            "Both peers start all four subsequent release strokes before launch: " + string.Join(',', anticipations.Values.Select(v => v.Count)));
        _catapultAim = 0;
        Check(samples > 30 && highest > 42 && longest > compressed + .3f, "Release reaches approximately 45 degrees and spring extends/recompresses");
        Check(paidOut > 1 && hauledIn > 1, $"Winch visibly pays out and hauls in: {paidOut:F3}/{hauledIn:F3} radians across both peers");
        Check(_arenas[0].Driver.Host!.Items.Slots.Single(s => s.Vehicle == Shooter).SecondItem == HeldItem.MachineGun, "Existing firing leaves second-slot capability untouched");
        await SetupHandoff(HeldItem.Salvo, HeldItem.Shield);
        await Until(() => AllPresent(HeldItem.Salvo), "Fresh magazine deploys for recovery");
        foreach (var arena in _arenas)
        {
            var state = arena.Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == Shooter).State;
            arena.Bodies[Shooter].Reseed(state);
            var publication = arena.Driver.ItemState!;
            arena.Bodies[Shooter].ObserveShields(state, publication.Shields);
            arena.Bodies[Shooter].Rack.Observe(state.LifeId, state.CanInteract,
                publication.Slots.Single(s => s.Vehicle == Shooter), [], tick: publication.World.Tick);
        }
        await Until(() => AllPresent(HeldItem.Salvo), "Reconstruction redeploys both peer mechanisms");
        Check(Catapults.All(c => c.VisibleRounds == 5 && c.ReleaseCount == 0), "Recovery never replays a historical release");
        _gateways[0].ConfigureSimulation(new(30, 5, 2, 0, 0));
        await SwitchHandoff(HeldItem.Shield, "impaired-catapult-to-shield");
        await SwitchHandoff(HeldItem.Salvo, "impaired-shield-to-catapult");
        _gateways[0].ConfigureSimulation(new());
        await CaptureWinchCycle();
        await CaptureWinchCycle(supportView: true);
        _sampleCatapult = null;
        Check(connectionFailure is null && connectionSamples > 300,
            $"Rendered coil, seats, cable endpoints and platform overlap: {connectionSamples} temporal samples; failure={connectionFailure ?? "none"}");
        System.IO.File.WriteAllLines(System.IO.Path.Combine(output, "mechanical-evidence.txt"), _evidence);
        GD.Print($"Salvo catapult integration passed: {_checks} checks on two UDP peers.");
    }

    private async Task CaptureWinchCycle(bool supportView = false)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        await SetupHandoff(HeldItem.Salvo, HeldItem.Shield);
        await Until(() => AllPresent(HeldItem.Salvo) && _arenas[0].Driver.Host!.Items.SalvoReady(_arenas[0].Driver.Host!.Items.Slots.Single(s => s.Vehicle == Shooter)), "Fresh ready magazine for recorded mechanical cycle");
        var arena = _arenas[1]; var body = arena.Bodies[Shooter];
        int expectedRounds = body.Rack.Catapult!.VisibleRounds - 1;
        var camera = new Camera3D { Fov = 42, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        arena.AddChild(camera);
        var winch = (Node3D)body.Rack.Catapult!.FindChild("WinchHousing", true, false);
        camera.GlobalPosition = winch.GlobalPosition + body.VisualTransform.Basis * new Vector3(-.35f, .35f, 2.0f);
        camera.LookAt(winch.GlobalPosition + Vector3.Up * .10f);
        if (supportView)
        {
            var yaw = (Node3D)body.Rack.Catapult.FindChild("TurretYaw", true, false);
            var top = (Node3D)body.Rack.Catapult.FindChild("SpringTop", true, false);
            Vector3 focus = (yaw.GlobalPosition + top.GlobalPosition) / 2 + Vector3.Up * .55f;
            camera.GlobalPosition = focus + body.VisualTransform.Basis * new Vector3(-5.2f, .3f, 0);
            camera.LookAt(focus);
        }
        camera.MakeCurrent();
        string directory = ProjectSettings.GlobalizePath("res://.godot/ts175/" + (supportView ? "support-cycle" : "winch-cycle"));
        System.IO.Directory.CreateDirectory(directory);
        var evidence = new List<string> { "frame,milliseconds,drum_radians,spoon_degrees,guide_x,plate_pitch,foot_x,foot_y,foot_z,spring_length" };
        var images = new List<Image>();
        ulong start = Time.GetTicksMsec();
        for (int frame = 0; frame < 30; frame++)
        {
            if (frame == 2) { Check(arena.Driver.RequestItemUse(), "Winch capture uses an ordinary accepted shot"); }
            await Frames(2);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var catapult = body.Rack.Catapult!;
            var drum = (Node3D)catapult.FindChild("WinchDrum", true, false);
            var guide = (Node3D)catapult.FindChild("WinchLevelWind", true, false);
            var plate = (Node3D)catapult.FindChild("PlateHinge", true, false);
            var yaw = (Node3D)catapult.FindChild("TurretYaw", true, false);
            Vector3 foot = yaw.ToLocal(((Node3D)catapult.FindChild("SpringFoot", true, false)).GlobalPosition);
            evidence.Add(FormattableString.Invariant($"{frame},{Time.GetTicksMsec()-start},{drum.Rotation.X},{catapult.ArmDegrees},{guide.Position.X},{plate.Rotation.X},{foot.X},{foot.Y},{foot.Z},{catapult.SpringLength}"));
            images.Add(_views[1].GetTexture().GetImage());
        }
        for (int frame = 0; frame < images.Count; frame++)
        {
            using var image = images[frame];
            Check(image.SavePng(System.IO.Path.Combine(directory, $"frame-{frame:D2}.png")) == Error.Ok, "Captured winch cycle " + frame);
        }
        System.IO.File.WriteAllLines(System.IO.Path.Combine(directory, "poses.csv"), evidence);
        Check(Catapults.All(c => c.IsReady && c.VisibleRounds == expectedRounds), "Recorded mechanical cycle finishes reset with " + expectedRounds + " rounds on both peers");
        camera.QueueFree();
        await Until(() => _arenas[1].Driver.LocalItem?.Active.Item == HeldItem.None, "Recorded activation finishes all five automatic rounds");
        await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Recorded volley stows normally");
    }

    private async Task CaptureCatapult(string name, Vector3 offset, float focusHeight = 1)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        var arena = _arenas[1]; var body = arena.Bodies[Shooter];
        var camera = new Camera3D { Fov = 42, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        arena.AddChild(camera);
        camera.GlobalPosition = body.VisualPosition + body.VisualTransform.Basis * offset;
        camera.LookAt(body.VisualPosition + Vector3.Up * focusHeight + body.VisualTransform.Basis.Z * .25f);
        camera.MakeCurrent();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = _views[1].GetTexture().GetImage();
        Check(image.SavePng(ProjectSettings.GlobalizePath("res://.godot/ts175/" + name + ".png")) == Error.Ok, "Captured " + name);
        camera.QueueFree();
    }
}
