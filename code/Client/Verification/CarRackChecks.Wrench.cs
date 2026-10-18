using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Verification;

public sealed partial class CarRackChecks
{
    private WrenchArm? Arm(int peer) => _arenas[peer].Bodies[Shooter].Rack.GetParent<Node3D>()
        .GetNode<Node3D>("WeaponRack").GetChildren().OfType<WrenchArm>().SingleOrDefault();

    private async Task CheckWrenchArm()
    {
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://.godot/ts293"));
        var host = _arenas[0].Driver.Host!;
        if (OS.GetCmdlineUserArgs().Contains("--wrench-impaired"))
        {
            _gateways[0].ConfigureSimulation(new(30, 5, 2, 0, 0));
            Check(true, "Host outbound impairment: 30 ms delay, 5 ms jitter, 2% loss");
        }
        for (int round = 0; round < 3; round++)
        {
            WrenchRepairSettings.Current = round switch
            {
                1 => new() { DeploymentDuration = .95f, RetractionDuration = .95f, Duration = 1,
                    ContactSpacing = .55f, ContactForward = .02f, HoverHeight = .24f, Vibration = .002f },
                2 => new() { DeploymentDuration = .85f, RetractionDuration = .85f, Duration = .8f,
                    ContactSpacing = .45f, ContactForward = -.02f, HoverHeight = .18f, Vibration = 0 },
                _ => new()
            };
            var collisionWalls = new List<StaticBody3D>();
            host.Items.RemovePlayer(Shooter);
            await Frames(150);
            Position(round == 2 ? 1000 : 500, false);
            await Frames(20);
            Check(host.Items.Grant(host.World, Shooter, HeldItem.Wrench), "Wrench grant " + round);
            await Until(() => Arm(0) is not null && Arm(1) is not null, "Imported arm installed on both peers");
            if (round == 0)
            {
                foreach (int peer in new[] { 0, 1 })
                {
                    var arm = Arm(peer)!;
                    Check(!arm.ServicesVisible, "Underslung services are hidden while the rack is stowed, matching Oil presentation");
                    Check(arm.Nested, "Starts at the folded pose before rack clearance");
                    foreach (var mesh in arm.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                    {
                        if (!mesh.IsVisibleInTree()) { continue; }
                        var bounds = mesh.GetAabb();
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 point = arm.ToLocal(mesh.ToGlobal(bounds.GetEndpoint(i)));
                            Check(point.Y > -.185f && point.Y < .445f && point.X > -.98f && point.X < .98f && point.Z > -.425f && point.Z < .79f,
                                "Folded mesh stays inside production trunk: " + mesh.Name + " " + point);
                        }
                    }
                }
                await WrenchCapture("folded", new Vector3(-3.6f, 3.0f, 4.8f));
            }
            if (round == 1)
            {
                Check(!_arenas[1].Driver.RequestItemUse(), "Early local use rejected during deployment");
                var slot = host.Items.Slots.Single(s => s.Vehicle == Shooter);
                Check(!host.Items.RequestUse(host.World, Shooter, slot.Life, slot.Active.Token), "Authority also rejects early remote capability use");
                Check(host.World.GetVehicle(Shooter).Damage.CurrentHP == 500, "Early input does not heal");
            }
            {
                for (int frame = 0; frame < 420 && !(Arm(0)?.IsReady == true && Arm(1)?.IsReady == true); frame++)
                {
                    await Frames(1);
                    foreach (int peer in new[] { 0, 1 }) { if (Arm(peer) is { } active) { CheckWrenchServices(active); } }
                    foreach (int peer in new[] { 0, 1 })
                        if (Arm(peer) is { Nested: false } deploying)
                        {
                            Check(deploying.JointError < .001f, "Deployment keeps fixed-length links attached");
                            if (frame % 12 == 0) { CheckWrenchClearance(deploying); }
                        }
                }
                Check(Arm(0)?.IsReady == true && Arm(1)?.IsReady == true, "Both arms unfolded to rest");
                foreach (int peer in new[] { 0, 1 })
                    Check(Arm(peer)!.ReadySeconds <= 1, $"Full Wrench deployment to ready: {Arm(peer)!.ReadySeconds:F3}s (round {round}, peer {peer})");
            }
            if (round == 0)
            {
                Check(Arm(0)!.ServicesVisible && Arm(1)!.ServicesVisible, "Full-size tank and battery visible beneath raised racks on both peers");
                CheckWrenchClearance(Arm(1)!);
                await WrenchCapture("ready-side", new Vector3(-6.8f, 3.0f, 0));
                await WrenchCapture("rear-power", new Vector3(-2.8f, 2.4f, 4));
                await WrenchCapture("under-rack", new Vector3(0, 1.5f, 4.7f), new Vector3(0, .95f, 2.2f));
                // Preserve the approved independent geometry controls as a separate rig regression.
                foreach (int contact in new[] { 0, 1, 2 })
                {
                    foreach (int peer in new[] { 0, 1 }) { Arm(peer)!.RequestHoodPose(contact); }
                    await Until(() => Arm(0)?.ContactIndex == contact && Arm(1)?.ContactIndex == contact, "Independent hood pose " + contact);
                    foreach (int peer in new[] { 0, 1 })
                    {
                        Check(Arm(peer)!.Tip.DistanceTo(WrenchArm.HoodContact(contact)) < .002f, "Drill meets actual sloping hood ahead of scoop");
                        CheckWrenchClearance(Arm(peer)!);
                    }
                    await WrenchCapture("contact-" + contact, new Vector3(-5.8f, 2.8f, -3.2f));
                }
                foreach (int peer in new[] { 0, 1 }) { Arm(peer)!.RequestHoodPose(null); }
                await Frames(100);
            }
            if (round == 2)
            {
                var ramps = new List<StaticBody3D>();
                foreach (var arena in _arenas)
                {
                    var ramp = new StaticBody3D { Position = new Vector3(0, 20.55f, 32), Rotation = new Vector3(.12f, 0, .07f), CollisionLayer = 1 };
                    ramp.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(8, .20f, 3) } });
                    ramp.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(8, .20f, 3) } });
                    arena.AddChild(ramp); ramps.Add(ramp);
                }
                foreach (int peer in new[] { 0, 1 }) { Arm(peer)!.RequestHoodPose(1); }
                await Until(() => Arm(0)?.ContactIndex == 1 && Arm(1)?.ContactIndex == 1, "Moving hood pose ready");
                float speed = 0, tilt = 0;
                for (int frame = 0; frame < 110; frame++)
                {
                    await Frames(1, throttle: 60000, steer: 1800);
                    speed = Math.Max(speed, Math.Abs(_arenas[1].LocalState!.Movement.CommandSpeed));
                    tilt = Math.Max(tilt, 1 - _arenas[1].Bodies[Shooter].VisualTransform.Basis.Y.Dot(Vector3.Up));
                    foreach (int peer in new[] { 0, 1 })
                    {
                        Check(Arm(peer)!.Tip.DistanceTo(WrenchArm.HoodContact(1)) < .002f, "Hood alignment survives native driving/turning/uneven support");
                        Check(Arm(peer)!.JointError < .001f, "Moving telescopic joints remain attached");
                    }
                    if (frame is 45 or 85) { await WrenchCapture("terrain-" + frame, new Vector3(-5.8f, 3, -3)); }
                }
                Check(speed > 3 && tilt > .001f, $"Native uneven-terrain run: peak speed {speed}, tilt {tilt}");
                float beforeBrake = Math.Abs(_arenas[1].LocalState!.Movement.CommandSpeed);
                await Frames(15, brake: ushort.MaxValue);
                Check(Math.Abs(_arenas[1].LocalState!.Movement.CommandSpeed) < beforeBrake, "Braking reduces speed with arm at hood");
                CheckWrenchClearance(Arm(1)!);
                await WrenchCapture("braking-contact", new Vector3(0, 3.1f, 6));
                foreach (int peer in new[] { 0, 1 }) { Arm(peer)!.RequestHoodPose(null); }
                await Frames(100);
                foreach (var ramp in ramps) { ramp.QueueFree(); }
                Position(1000, false); await Frames(30);
            }
            int before = _events.Count;
            var measuredArms = new[] { Arm(0)!, Arm(1)! };
            Camera3D? repairCamera = null;
            if (round == 1 && DisplayServer.GetName() != "headless")
            {
                var body = _arenas[1].Bodies[Shooter];
                repairCamera = new Camera3D { Fov = 42, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
                _arenas[1].AddChild(repairCamera);
                repairCamera.GlobalPosition = body.VisualPosition + body.VisualTransform.Basis * new Vector3(-5.8f, 2.8f, -3.2f);
                repairCamera.LookAt(body.VisualPosition + body.VisualTransform.Basis * new Vector3(0, .9f, .65f));
                repairCamera.MakeCurrent();
            }
            await Until(() => host.Items.WrenchReady(host.Items.Slots.Single(s => s.Vehicle == Shooter)) && Arm(1)?.IsReady == true, "Authority and owner fully deployed");
            Check(_arenas[1].Driver.RequestItemUse(), "Repair use accepted");
            await Until(() => _events.Count > before, "Reliable Wrench use reaches observer");
            Check(host.Items.Slots.Single(s => s.Vehicle == Shooter).Active.Item == HeldItem.None, "Wrench consumed without waiting for cosmetic repair");
            Check(host.World.GetVehicle(Shooter).Damage.CurrentHP == 1000, "Existing 500 HP repair/full-health clamp retained");
            if (round == 2)
            {
                foreach (var arena in _arenas)
                {
                    var wall = new StaticBody3D { Position = new Vector3(0, 22, 26), CollisionLayer = 1 };
                    wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(16, 3, 1) } });
                    wall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(16, 3, 1) } });
                    arena.AddChild(wall);
                    collisionWalls.Add(wall);
                }
            }
            var contacts = new List<int>[] { [], [] };
            var sparkContacts = new HashSet<int>[] { [], [] };
            var repairStart = new double?[2];
            var repairEnd = new double?[2];
            var captured = new HashSet<int>();
            var pictures = new List<(string Name, Image Image)>();
            var previousTips = new Vector3?[2];
            var sampleTimes = new double[2];
            float peakSpeed = 0;
            for (int frame = 0; frame < 330; frame++)
            {
                await Frames(1, throttle: round == 2 ? (ushort)60000 : (ushort)0, steer: round == 2 ? (short)2000 : (short)0);
                peakSpeed = Math.Max(peakSpeed, Math.Abs(_arenas[1].LocalState!.Movement.CommandSpeed));
                for (int peer = 0; peer < 2; peer++)
                {
                    if (Arm(peer) is not { } arm) { continue; }
                    var rack = _arenas[peer].Bodies[Shooter].Rack;
                    if (rack.WrenchOwnsRack)
                    {
                        Check(rack.PresentedInventory(_arenas[peer].Driver.ItemState!.Slots.Single(s => s.Vehicle == Shooter))!.Item == HeldItem.Wrench,
                            "Normal repair retains spent slot until actual full stow");
                    }
                    double stepLimit = .03 + 35 * (arm.AnimationSeconds - sampleTimes[peer]);
                    Check(arm.JointError < .001f, "Fixed link IK endpoint remains attached");
                    Check(arm.Scale.IsEqualApprox(Vector3.One), "Arm keeps full physical scale");
                    if (previousTips[peer] is Vector3 previous)
                        Check(previous.DistanceTo(arm.Tip) < stepLimit, $"Vehicle-local tool motion is continuous (round {round}, frame {frame}, peer {peer}, {previous} -> {arm.Tip}, limit {stepLimit})");
                    previousTips[peer] = arm.Tip;
                    sampleTimes[peer] = arm.AnimationSeconds;
                    if (arm.Repairing) { repairStart[peer] ??= arm.AnimationSeconds; }
                    else if (repairStart[peer].HasValue) { repairEnd[peer] ??= arm.AnimationSeconds; }
                    if (arm.ContactIndex >= 0)
                    {
                        if (contacts[peer].Count == 0 || contacts[peer][^1] != arm.ContactIndex) { contacts[peer].Add(arm.ContactIndex); }
                        Vector3 expected = WrenchArm.HoodContact(arm.ContactIndex);
                        expected.X = (arm.ContactIndex - 1) * WrenchRepairSettings.Current.ContactSpacing;
                        expected.Z -= WrenchRepairSettings.Current.ContactForward;
                        expected.Y -= WrenchRepairSettings.Current.ContactForward * .13f / 1.18f;
                        Check(arm.Tip.DistanceTo(expected) < .003f, "Actual tuned repair meets hood with bounded vibration");
                        if (arm.SparksActive) { sparkContacts[peer].Add(arm.ContactIndex); }
                    }
                    if (!arm.Nested)
                    {
                        Check(_arenas[peer].Bodies[Shooter].Rack.Progress >= .999f, "Rack stays raised until arm is safely nested");
                        if (frame % 12 == 0) { CheckWrenchClearance(arm); }
                    }
                }
                if (round < 2 && DisplayServer.GetName() != "headless" && Arm(1) is { SparkAge: >= .06f, SparksActive: true } contactArm &&
                    contacts[1].Count > 0 && captured.Add(contacts[1][^1]))
                {
                    pictures.Add(($"{(round == 0 ? "chase" : "side")}-repair-{contacts[1][^1]}", _views[1].GetTexture().GetImage()));
                }
                if (round == 2 && frame is 65 or 130)
                    await WrenchCapture("driving-" + frame, new Vector3(frame == 65 ? 5 : -5, 3, frame == 65 ? -4 : 4));
                if (round != 2 && frame > 120 && _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0)) { break; }
            }
            foreach (var picture in pictures)
            {
                using var image = picture.Image;
                Check(image.SavePng(ProjectSettings.GlobalizePath("res://.godot/ts293/" + picture.Name + ".png")) == Error.Ok, "Rendered repair " + picture.Name);
            }
            if (round < 2 && DisplayServer.GetName() != "headless") { Check(captured.Count == 3, "Fresh rendered evidence for all three contacts"); }
            repairCamera?.QueueFree();
            Check(contacts.All(c => c.SequenceEqual(new[] { 0, 1, 2 })), $"Both peers perform exactly left, center, right contacts ({string.Join(',', contacts[0])}; {string.Join(',', contacts[1])})");
            Check(sparkContacts.All(c => c.SetEquals(new[] { 0, 1, 2 })), "Each hood contact emits its own localized spark burst on both peers");
            Check(Enumerable.Range(0, 2).All(p => repairEnd[p] - repairStart[p] <= 1.05), $"Three-contact sequence completes within one second plus frame tolerance ({repairEnd[0] - repairStart[0]:F3}s; {repairEnd[1] - repairStart[1]:F3}s)");
            Check(_events.Count(e => e.Item == HeldItem.Wrench) == round + 1, "Exactly one confirmed use per fresh pickup");
            await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Repair returns, folds and closes trunk");
            foreach (var measuredArm in measuredArms)
            {
                Check(measuredArm.RepairCompletedSeconds - measuredArm.RepairStartedSeconds <= 1.01,
                    $"Complete repair clock: {measuredArm.RepairCompletedSeconds - measuredArm.RepairStartedSeconds:F3}s (round {round})");
                Check(measuredArm.ThirdContactCompletedSeconds > 0 && measuredArm.AnimationSeconds - measuredArm.ThirdContactCompletedSeconds <= 1,
                    $"Third contact end (including final lift) to full rack/trunk closure: {measuredArm.AnimationSeconds - measuredArm.ThirdContactCompletedSeconds:F3}s (round {round})");
            }
            if (round == 2)
            {
                Check(peakSpeed > 3, $"Repair exercised during native driving and turning (peak {peakSpeed})");
                Check(host.World.GetVehicle(Shooter).Damage.CurrentHP < 1000, $"Native collision occurred during repair/return (position {host.World.GetVehicle(Shooter).ObservedPhysics.Position}, HP {host.World.GetVehicle(Shooter).Damage.CurrentHP})");
            }
            foreach (var wall in collisionWalls) { wall.QueueFree(); }
        }
        WrenchRepairSettings.Current = new();
        host.Items.RemovePlayer(Shooter); Position(500, false); await Frames(30);
        Check(host.Items.Grant(host.World, Shooter, HeldItem.Wrench), "Interrupt fixture Wrench");
        Check(host.Items.Grant(host.World, Shooter, HeldItem.Salvo), "Interrupt fixture second payload");
        await Until(() => Arm(1)?.IsReady == true, "Interrupt fixture ready");
        await Until(() => host.Items.WrenchReady(host.Items.Slots.Single(s => s.Vehicle == Shooter)), "Interrupt authority ready");
        Check(_arenas[1].Driver.RequestItemUse(), "Interrupted repair starts");
        await Until(() => Arm(1)?.Repairing == true, "Interrupt accepted-use cue");
        Check(_arenas[1].Driver.RequestItemSwitch(), "Switch during active repair");
        await Until(() => AllPresent(HeldItem.Salvo), "Arm safely folds before next payload owns rack", 720);
        Check(host.Items.Slots.Single(s => s.Vehicle == Shooter).SecondItem == HeldItem.Salvo, "Visual interruption preserves new capability");
        Check(Arm(0) is null && Arm(1) is null, "Interrupted arm and its sparks are freed on both peers");
        host.Items.RemovePlayer(Shooter); await Frames(150);
        // Removal resets the authority selection watermark; a fresh fixture life
        // also clears the remote driver's pending switch from the interruption.
        Position(500, true); await Frames(30);
        Check(host.Items.Grant(host.World, Shooter, HeldItem.Wrench), "Recovery fixture Wrench");
        await Until(() => Arm(1)?.IsReady == true, "Recovery fixture ready");
        foreach (var arena in _arenas)
        {
            var state = arena.Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == Shooter).State;
            var publication = arena.Driver.ItemState!;
            arena.Bodies[Shooter].Reseed(state);
            arena.Bodies[Shooter].Rack.Observe(state.LifeId, true, publication.Slots.Single(s => s.Vehicle == Shooter), [], tick: publication.World.Tick);
        }
        await Until(() => Arm(0)?.IsReady == true && Arm(1)?.IsReady == true, "Accepted ownership reconstructs arm after reseed");
        Check(Arm(0)!.ContactIndex == -1 && Arm(1)!.ContactIndex == -1, "Recovery never replays a historical repair");
        Check(_arenas[1].Driver.RequestItemDiscard(), "Discard fully deployed Wrench");
        await Until(() => Arm(0) is null && Arm(1) is null, "Discard returns and frees both arms without repair");
        Check(host.Items.Grant(host.World, Shooter, HeldItem.Wrench), "Fresh pickup after discard");
        await Until(() => Arm(0)?.IsReady == true && Arm(1)?.IsReady == true && host.Items.WrenchReady(host.Items.Slots.Single(s => s.Vehicle == Shooter)), "Fresh capability ready");
        Check(_arenas[1].Driver.RequestItemUse(), "Repair before life interruption");
        await Until(() => Arm(1)?.SparksActive == true, "Interrupt while actual sparks are active");
        Position(0, false); await Frames(40);
        Check(_arenas.All(a => a.Bodies[Shooter].Rack.PresentedItem == HeldItem.None), "Death clears articulated payload");
        Check(Arm(0) is null && Arm(1) is null, "Death removes active sparks with their owning arms");
        Position(1000, true); await Frames(90);
        Check(_arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Respawn starts stowed");
        await CheckWrenchPickupRaces();
    }

    private void CheckWrenchServices(WrenchArm arm)
    {
        if (!arm.ServicesVisible) { return; }
        var services = (Node3D)arm.FindChild("Services", true, false);
        foreach (var mesh in services.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            var bounds = mesh.GetAabb();
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = arm.ToLocal(mesh.ToGlobal(bounds.GetEndpoint(i))) + arm.GetParent<Node3D>().Position;
                if (p.Y < -.255f || Math.Abs(p.X) > .98f || p.Z < 1.42f || p.Z > 2.64f)
                    throw new InvalidOperationException($"Visible under-rack service leaves the open bay envelope: {mesh.Name} {p}");
            }
        }
        Check(true, "Visible full-size services clear the measured bay floor and perimeter");
    }

    private void CheckWrenchClearance(WrenchArm arm)
    {
        CheckWrenchServices(arm);
        foreach (var mesh in arm.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            if (!mesh.IsVisibleInTree()) { continue; }
            Transform3D local = arm.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                using var arrays = mesh.Mesh.SurfaceGetArrays(surface);
                var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                foreach (var vertex in vertices)
                {
                    Vector3 p = local * vertex + new Vector3(0, 1.34f, 1.845f);
                    if (Math.Abs(p.X) > .74f) { continue; }
                    float top = p.Z >= -.51f && p.Z <= .51f ? .98f :
                        p.Z < -.51f && p.Z > -.94f ? .93f - (-p.Z - .44f) * .53f / .48f :
                        p.Z <= -.94f && p.Z > -2.12f ? .40f - (-p.Z - .94f) * .13f / 1.18f : -100;
                    if (p.Z >= -.50f && p.Z <= -.30f) { top = Math.Max(top, 1.17f); }
                    if (p.Y < top - .004f)
                        throw new InvalidOperationException($"Arm intersects vehicle envelope: {mesh.Name} at {p}, top {top}");
                }
            }
        }
        Check(true, "Imported arm vertices clear roof, lamps, windshield and hood");
    }

    private async Task WrenchCapture(string name, Vector3 offset, Vector3? focus = null)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        var body = _arenas[1].Bodies[Shooter];
        var camera = new Camera3D { Fov = 42, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        _arenas[1].AddChild(camera);
        camera.GlobalPosition = body.VisualPosition + body.VisualTransform.Basis * offset;
        camera.LookAt(body.VisualPosition + body.VisualTransform.Basis * (focus ?? new Vector3(0, .9f, .65f)));
        camera.MakeCurrent();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = _views[1].GetTexture().GetImage();
        Check(image.SavePng(ProjectSettings.GlobalizePath("res://.godot/ts293/" + name + ".png")) == Error.Ok, "Captured Wrench " + name);
        camera.QueueFree();
    }

}
