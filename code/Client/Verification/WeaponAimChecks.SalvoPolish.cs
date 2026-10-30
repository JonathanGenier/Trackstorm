using Godot;
using Trackstorm.Core.Items;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

public sealed partial class WeaponAimChecks
{
    private async Task CheckSalvoAimPolish()
    {
        var host = _arenas[0].Driver.Host!;
        var driver = _arenas[1].Driver;
        // An isolated native deck keeps the 130 m sightlines clear of the small
        // prototype's perimeter. Cars, camera, targeting and UDP remain production.
        foreach (var arena in _arenas)
        {
            var deck = new StaticBody3D { Position = new(500, 20, 500), CollisionLayer = 1 };
            deck.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(500, 1, 500) } });
            deck.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(500, 1, 500) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.26f, .28f, .30f) } });
            arena.AddChild(deck);
        }
        Require(host.TryConfigure(0, new Dictionary<string, double>
        { ["items.salvo_damage"] = 0, ["items.salvo_impulse"] = 0, ["items.salvo_speed"] = 20 }, out _),
            "Zero damage/impulse keeps targets valid; slow overlapping arcs expose later steering");
        _heldShooter = new(500, Ground, 500);
        _heldTargets = (new(500, Ground, 455), new(535, Ground, 460));
        Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
        var launches = new Dictionary<ulong, (ulong Tick, SalvoFlight Arc)>();
        var launchTargets = new List<ulong>();
        var unaimedTargets = new Dictionary<ulong, N.Vector3>();
        var acceptedTargets = new Dictionary<ulong, N.Vector3>();
        bool verifyUnaimed = false;
        var events = new List<ItemEvent>(); var remote = new List<ItemEvent>();
        _arenas[0].Driver.ItemsReceived += p => events.AddRange(p.Events.Where(e => e.Item == HeldItem.Salvo));
        _arenas[2].Driver.ItemsReceived += p => remote.AddRange(p.Events.Where(e => e.Item == HeldItem.Salvo));
        var releases = new List<string>();
        var native = _arenas[0].Driver.CollideMissile!;
        _arenas[1].AimReleaseObserved = reason =>
        {
            if (host.Items.Slots.Any(s => s.Vehicle == Shooter && s.ReservedSalvoShots > 0)) { releases.Add(reason); }
        };
        _arenas[0].Driver.CollideMissile = (missile, end) =>
        {
            if (missile.Arc is { } arc)
            {
                if (launches.TryGetValue(missile.Id, out var prior))
                { if (prior.Arc.Target != arc.Target) { throw new InvalidOperationException("An earlier arc was steered."); } }
                else
                {
                    launches.Add(missile.Id, (host.World.State.Tick + 1, arc));
                    launchTargets.Add(_arenas[1].AssistedCar);
                    var shotPose = host.World.GetVehicle(Shooter).ObservedPhysics;
                    var shotDirection = host.Items.Aims.FirstOrDefault(a => a.Vehicle == Shooter)?.Direction ?? -N.Vector3.UnitZ;
                    acceptedTargets.Add(missile.Id, SalvoFlight.ResolveTarget(shotPose, shotDirection, verifyUnaimed ? 65 : 130,
                        _arenas[0].Driver.ProjectSalvoGround, (from, to) => _arenas[0].Driver.RaycastWeapon!(Shooter, from, to)));
                    if (verifyUnaimed)
                    {
                        var pose = host.World.GetVehicle(Shooter).ObservedPhysics;
                        var direction = host.Items.Aims.FirstOrDefault(a => a.Vehicle == Shooter)?.Direction ?? -N.Vector3.UnitZ;
                        unaimedTargets.Add(missile.Id, SalvoFlight.ResolveTarget(pose, direction, 65,
                            _arenas[0].Driver.ProjectSalvoGround, (from, to) => _arenas[0].Driver.RaycastWeapon!(Shooter, from, to)));
                    }
                }
            }
            return native(missile, end);
        };
        await Frames(60);

        async Task Equip()
        {
            host.Items.RemovePlayer(Shooter);
            await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Previous volley fully stows");
            Require(host.Items.Grant(host.World, Shooter, HeldItem.Salvo), "Fresh five-round magazine granted");
            await Until(() => driver.LocalItem is { } slot && driver.SalvoReady?.Invoke(slot) == true &&
                host.Items.SalvoReady(host.Items.Slots.Single(s => s.Vehicle == Shooter)), "Native and authoritative preload complete");
            await Frames(30);
        }

        async Task HoldVolley(string name, bool moving = false)
        {
            int start = launches.Count;
            float ringError = 0, yawError = 0;
            int alignedSamples = 0;
            bool captured = false;
            releases.Clear();
            Require(driver.RequestItemUse(), name + " accepts one activation");
            for (int frame = 0; frame < 500 && (launches.Count < start + 5 || driver.LocalItem?.Active.Item != HeldItem.None); frame++)
            {
                if (moving && _heldTargets is { } targets) { _heldTargets = (targets.First + new N.Vector3(.015f, 0, 0), targets.Second); }
                await Frames(1);
                // Marker vertices are rebuilt during render presentation, after the
                // physics publication. Compare only once that boundary is displayed.
                if (DisplayServer.GetName() != "headless")
                { await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw); }
                if (driver.LocalItem?.Active.Item != HeldItem.Salvo) { continue; }
                var vertices = _arenas[1].SalvoMarker.SurfaceVertices;
                // The final launch retires the authoritative selection. An empty
                // replaceable aim publication can beat reliable inventory depletion;
                // the ordinary forward fallback then belongs to completed-volley
                // presentation, not target retention during an active magazine.
                if (launches.Count >= start + 5)
                {
                    if (vertices.Count > 0 && driver.PresentedAim(driver.LocalItem) is null)
                    {
                        var completedCenter = vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / vertices.Count;
                        GD.Print($"Completed-volley publication gap: host shots={host.Items.Slots.Single(s => s.Vehicle == Shooter).Active.SalvoShots}, local shots={driver.LocalItem.Active.SalvoShots}, marker={completedCenter}; all five launches already accepted.");
                    }
                    continue;
                }
                if (vertices.Count == 0) { throw new InvalidOperationException("The active aiming circle disappeared."); }
                Vector3 center = vertices.Aggregate(Vector3.Zero, (sum, v) => sum + v) / vertices.Count;
                var target = _heldTargets!.Value.First;
                ringError = Math.Max(ringError, new Vector2(center.X - target.X, center.Z - target.Z).Length());
                var aim = host.Items.Aims.FirstOrDefault(a => a.Vehicle == Shooter);
                if (aim is not null)
                {
                    foreach (var arena in _arenas)
                    { yawError = Math.Max(yawError, Math.Abs(Mathf.AngleDifference(arena.Bodies[Shooter].Rack.Catapult!.Yaw, aim.Yaw))); }
                    alignedSamples++;
                }
                if (moving && !captured && launches.Count >= start + 2)
                { captured = true; await Capture("salvo-retained-after-shot-two"); }
            }
            Require(launches.Count == start + 5, name + " launches exactly five rounds");
            Require(releases.Count == 0, name + " retains the valid target through every shot; releases=" + releases.Count + ": " + string.Join("; ", releases.Take(3)));
            Require(launchTargets.Skip(start).All(id => id == 1), name + " samples the same assisted car at all five launches");
            var volley = launches.Values.Skip(start).ToArray();
            Require(volley.Zip(volley.Skip(1)).All(p => p.Second.Tick - p.First.Tick == 75), name + " preserves 75-tick cadence");
            Require(alignedSamples > 200 && ringError < 4 && yawError < .12f,
                $"{name} keeps circle/turret aligned through all shots: {alignedSamples} samples, ring error {ringError:F2} m, peer yaw error {yawError:F3} rad");
            if (moving) { Require(N.Vector3.Distance(volley[0].Arc.Target, volley[^1].Arc.Target) > 2, "Moving target changes each launch snapshot without freezing the first direction"); }
            await Frames(30);
        }

        if (!OS.GetCmdlineUserArgs().Contains("--salvo-switch-only"))
        {
        await Equip();
        await AimAtCurrent(() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
        await Frames(40);
        Require(_arenas[1].AssistedCar == 1, "Near target acquired before retention regression");
        await HoldVolley("Near moving target", moving: true);

        foreach (float sign in new[] { -1f, 1f })
        {
            await Equip();
            await AimAtCurrent(() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
            Require(_arenas[1].AssistedCar == 1, "Manual lead starts from acquired car");
            for (int i = 0; i < 40; i++)
            {
                Send(new InputEventMouseMotion { ScreenRelative = new(sign * 1.5f, 0) });
                await Frames(2);
            }
            Require(_arenas[1].AssistedCar == 0, "Manual lead releases before first Salvo shot");
            int start = launches.Count;
            Require(driver.RequestItemUse(), "Manual lead volley commits");
            for (int i = 0; i < 900 && launches.Count < start + 5; i++)
            {
                if (_heldTargets is { } targets)
                    _heldTargets = (targets.First + new N.Vector3(sign * .01f, 0, 0), targets.Second);
                if (i % 8 == 0) Send(new InputEventMouseMotion { ScreenRelative = new(sign * .3f, 0) });
                await Frames(1);
                RequireShot(_arenas[1].AssistedCar == 0, "Manual lead remains free between Salvo shots");
                RequireShot(_arenas[1].AimOverlay.Marker is not null, "Manual lead square stays visible");
            }
            Require(launches.Count == start + 5 && launchTargets.Skip(start).All(t => t == 0),
                "All five manual Salvo shots remain unassisted");
            await Frames(30);
        }

        foreach (float degrees in new[] { 0f, 90f, 180f, -90f })
        {
            var offset = new N.Vector3(-MathF.Sin(Mathf.DegToRad(degrees)), 0, -MathF.Cos(Mathf.DegToRad(degrees))) * 120;
            _heldTargets = (_heldShooter.Value + offset, _heldShooter.Value + new N.Vector3(180, 0, 180));
            Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
            await Equip();
            await AimAtCurrent(() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
            await Frames(50);
            Require(_arenas[1].AssistedCar == 1 && _arenas[1].TargetIndicator.Target == 1, "Shared assistance acquires a 120 m car at bearing " + degrees);
            int start = launches.Count;
            await Capture("salvo-long-range-" + degrees);
            await HoldVolley("120 m bearing " + degrees);
            Require(launches.Values.Skip(start).All(v => N.Vector3.Distance(v.Arc.Target, _heldTargets.Value.First) < 4),
                "All five authoritative arcs reach the 120 m target at bearing " + degrees);
        }

        }
        _heldTargets = (_heldShooter.Value + new N.Vector3(0, 0, -200), _heldShooter.Value + new N.Vector3(180, 0, 180));
        Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
        await Equip();
        await AimAtCurrent(() => _arenas[1].Bodies[1].VisualTransform * new Vector3(0, .3f, 0));
        await Frames(50);
        Require(_arenas[1].AssistedCar == 1 && _arenas[1].TargetIndicator.Target == 1,
            "Salvo acquires and visibly locks a car at 200 m beyond projectile reach");
        int distantStart = launches.Count;
        await Frames(90);
        Require(launches.Count == distantStart, "Out-of-range lock never fires automatically");
        Require(driver.RequestItemUse(), "Player may commit a volley while locked beyond reach");
        await Until(() => launches.Count == distantStart + 5, "Out-of-range lock preserves five-shot ammunition behavior");
        Require(launches.Values.Skip(distantStart).All(v =>
            new N.Vector2(v.Arc.Target.X - _heldShooter.Value.X, v.Arc.Target.Z - _heldShooter.Value.Z).Length() <= 131),
            "Out-of-range lock does not extend projectile reach");
        _heldTargets = (_heldShooter.Value + new N.Vector3(0, 0, -120), _heldShooter.Value + new N.Vector3(70, 0, -90));
        Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
        await Equip();
        await CheckAcceptedAimOutage();
        // A reacquired identity precedes progressive cursor attraction. This is a
        // hit-on-car scenario, so wait for the actual accepted ray, not just green.
        await Until(() =>
        {
            var aim = host.Items.Aims.FirstOrDefault(a => a.Vehicle == Shooter);
            if (aim is null) return false;
            var pose = host.World.GetVehicle(Shooter).ObservedPhysics;
            var endpoint = SalvoFlight.ResolveTarget(pose, aim.Direction, 130, _arenas[0].Driver.ProjectSalvoGround,
                (from, to) => _arenas[0].Driver.RaycastWeapon!(Shooter, from, to));
            return N.Vector3.Distance(endpoint, _heldTargets.Value.First) < 3;
        }, "Recovered accepted ray converges onto the first car before the hit volley");
        int switchedStart = launches.Count;
        releases.Clear();
        Require(driver.RequestItemUse(), "Target-switching volley accepts one activation");
        await Until(() => launches.Count == switchedStart + 1, "First shot samples the forward rival");
        await ChangeTarget(3);
        await Until(() => launches.Count == switchedStart + 3, "Second and third shots sample the second rival");
        await ChangeTarget(1);
        await Until(() => launches.Count == switchedStart + 5, "Fourth and fifth shots return to the first rival");
        Require(launchTargets.Skip(switchedStart).SequenceEqual(new ulong[] { 1, 3, 3, 1, 1 }),
            "Deliberate camera switching is independently sampled between consecutive launches");
        var destinations = new[] { _heldTargets.Value.First, _heldTargets.Value.Second, _heldTargets.Value.Second,
            _heldTargets.Value.First, _heldTargets.Value.First };
        foreach (var pair in launches.Values.Skip(switchedStart).Zip(destinations))
            GD.Print($"SWITCH_ENDPOINT actual={pair.First.Arc.Target} expected={pair.Second} error={N.Vector3.Distance(pair.First.Arc.Target, pair.Second):F4}");
        Require(launches.Values.Skip(switchedStart).Zip(destinations).All(p => N.Vector3.Distance(p.First.Arc.Target, p.Second) < 4),
            "All switched authoritative arcs reach their newly selected rival; old arcs remain unchanged");
        Require(releases.Count >= 2 && releases.All(r => !r.Contains("accepted aim expired")),
            "Switching releases deliberate camera intent without authority-token expiry");

        // Empty sightline: verify the range itself, independent of a car or early cover hit.
        _heldTargets = (_heldShooter.Value + new N.Vector3(180, 0, 180), _heldShooter.Value + new N.Vector3(-180, 0, 180));
        Position(_heldShooter.Value, _heldTargets.Value.First, _heldTargets.Value.Second);
        foreach (bool activelyAiming in new[] { true, false })
        {
            await Equip();
            await AimAt(_arenas[1].Bodies[Shooter].VisualPosition + new Vector3(0, 2, -150));
            if (!activelyAiming)
            { Send(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = false }); Camera.ResetFollow(); }
            await Frames(90);
            Require(_arenas[1].AssistedCar == 0 && _input.Adapter.CameraAimActive == activelyAiming,
                "Unassisted input state matches deliberate aiming=" + activelyAiming);
            int start = launches.Count;
            verifyUnaimed = !activelyAiming;
            Require(driver.RequestItemUse(), "Unassisted volley commits with aiming=" + activelyAiming);
            await Until(() => launches.Count == start + 5, "Unassisted volley completes with aiming=" + activelyAiming);
            float expected = activelyAiming ? 130 : 65;
            var pose = host.World.GetVehicle(Shooter).ObservedPhysics;
            N.Vector3 pivot = pose.Position + N.Vector3.Transform(WeaponAim.Pivot, pose.Orientation);
            var shots = launches.Skip(start).ToArray();
            if (activelyAiming)
            {
                Require(shots.All(v => Math.Abs(new N.Vector2(v.Value.Arc.Target.X - pivot.X, v.Value.Arc.Target.Z - pivot.Z).Length() - expected) < 1),
                    "All five actively aimed, unassisted endpoints reach 130 m");
            }
            else
            {
                Require(shots.All(v => N.Vector3.Distance(v.Value.Arc.Target, unaimedTargets[v.Key]) < .1f),
                    "All five unaimed endpoints preserve prior 65 m base-range and native ground-intersection behavior");
            }
            verifyUnaimed = false;
        }
        await Until(() => host.Items.Missiles.Count == 0 && _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0),
            "All repeated polish volleys finish flight and fully stow", 1900);
        await Frames(120);
        Require(events.SequenceEqual(remote), "Observer receives the same ordered authoritative launch/impact outcomes for every polish volley");
        Require(launches.All(p => N.Vector3.Distance(p.Value.Arc.Target, acceptedTargets[p.Key]) < .1f),
            "Every locked, switched and manually led shot snapshots its accepted direction independently");
        Require(_arenas.All(a => a.Bodies[Shooter].Rack.Catapult is null && !a.SalvoMarker.Visible),
            "Owner and observers clean up the exhausted magazine and targeting circles");
        _arenas[1].AimReleaseObserved = null;
        _arenas[0].Driver.CollideMissile = native;

        async Task ChangeTarget(ulong target)
        {
            for (int i = 0; i < 2; i++)
            {
                Vector3 direction = (_arenas[1].Bodies[target].VisualTransform * new Vector3(0, .3f, 0) - Camera.GlobalPosition).Normalized();
                float yaw = MathF.Atan2(-direction.X, -direction.Z), pitch = MathF.Asin(direction.Y);
                Vector3 cursorRay = Camera.ProjectRayNormal(_arenas[1].AimOverlay.Marker!.Value);
                float cursorYaw = MathF.Atan2(-cursorRay.X, -cursorRay.Z), cursorPitch = MathF.Asin(cursorRay.Y);
                Send(new InputEventMouseMotion { ScreenRelative = new(-Mathf.AngleDifference(cursorYaw, yaw) / .003f, -(pitch - cursorPitch) / .003f) });
                await Frames(12);
            }
            Require(_arenas[1].AssistedCar == target, "Camera deliberately acquires rival " + target + " before the next shot");
        }
    }
}
