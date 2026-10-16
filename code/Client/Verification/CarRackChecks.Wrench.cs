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
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://.godot/ts292/round3"));
        var host = _arenas[0].Driver.Host!;
        if (OS.GetCmdlineUserArgs().Contains("--wrench-impaired"))
        {
            _gateways[0].ConfigureSimulation(new(30, 5, 2, 0, 0));
            Check(true, "Host outbound impairment: 30 ms delay, 5 ms jitter, 2% loss");
        }
        for (int round = 0; round < 3; round++)
        {
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
                foreach (int peer in new[] { 0, 1 }) { Arm(peer)!.RequestHoodPose(0); }
            }
            if (round != 1)
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
            }
            if (round == 0)
            {
                await Until(() => Arm(0)?.ContactIndex == 0 && Arm(1)?.ContactIndex == 0, "Early pose request waits for deployment then reaches left hood");
                foreach (int peer in new[] { 0, 1 }) { Arm(peer)!.RequestHoodPose(null); }
                await Frames(100);
                Check(Arm(0)!.ServicesVisible && Arm(1)!.ServicesVisible, "Full-size tank and battery visible beneath raised racks on both peers");
                CheckWrenchClearance(Arm(1)!);
                await WrenchCapture("ready-side", new Vector3(-6.8f, 3.0f, 0));
                await WrenchCapture("rear-power", new Vector3(-2.8f, 2.4f, 4));
                await WrenchCapture("under-rack", new Vector3(0, 1.5f, 4.7f), new Vector3(0, .95f, 2.2f));
                // Explicit pose requests test the rig contract; production activation does not sequence them.
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
            Check(_arenas[1].Driver.RequestItemUse(), round == 1 ? "Early use accepted during rack deployment" : "Repair use accepted");
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
                }
            }
            var contacts = new HashSet<int>[] { [], [] };
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
                    double stepLimit = .03 + 8 * (arm.AnimationSeconds - sampleTimes[peer]);
                    Check(arm.JointError < .001f, "Fixed link IK endpoint remains attached");
                    Check(arm.Scale.IsEqualApprox(Vector3.One), "Arm keeps full physical scale");
                    if (previousTips[peer] is Vector3 previous)
                        Check(previous.DistanceTo(arm.Tip) < stepLimit, $"Vehicle-local tool motion is continuous (round {round}, frame {frame}, peer {peer}, {previous} -> {arm.Tip}, limit {stepLimit})");
                    previousTips[peer] = arm.Tip;
                    sampleTimes[peer] = arm.AnimationSeconds;
                    if (arm.ContactIndex >= 0) { contacts[peer].Add(arm.ContactIndex); }
                    if (!arm.Nested)
                    {
                        Check(_arenas[peer].Bodies[Shooter].Rack.Progress >= .999f, "Rack stays raised until arm is safely nested");
                        if (frame % 12 == 0) { CheckWrenchClearance(arm); }
                    }
                }
                if (Arm(0) is { Repairing: true } owner && Arm(1) is { Repairing: true } remote)
                    Check(owner.Tip.DistanceTo(remote.Tip) < .35f, "Owner and remote cosmetic phases remain close");
                if (round == 2 && frame is 65 or 130)
                    await WrenchCapture("driving-" + frame, new Vector3(frame == 65 ? 5 : -5, 3, frame == 65 ? -4 : 4));
                if (round != 2 && frame > 120 && _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0)) { break; }
            }
            Check(contacts.All(c => c.Count == 0), "Activation leaves three-contact sequence to TS-293");
            await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Repair returns, folds and closes trunk");
            if (round == 2)
            {
                Check(peakSpeed > 3, $"Repair exercised during native driving and turning (peak {peakSpeed})");
                Check(host.World.GetVehicle(Shooter).Damage.CurrentHP < 1000, $"Native collision occurred during repair/return (position {host.World.GetVehicle(Shooter).ObservedPhysics.Position}, HP {host.World.GetVehicle(Shooter).Damage.CurrentHP})");
            }
        }
        host.Items.RemovePlayer(Shooter); Position(500, false); await Frames(30);
        Check(host.Items.Grant(host.World, Shooter, HeldItem.Wrench), "Interrupt fixture Wrench");
        Check(host.Items.Grant(host.World, Shooter, HeldItem.Salvo), "Interrupt fixture second payload");
        await Until(() => Arm(1)?.IsReady == true, "Interrupt fixture ready");
        Check(_arenas[1].Driver.RequestItemUse(), "Interrupted repair starts");
        await Until(() => Arm(1)?.Repairing == true, "Interrupt accepted-use cue");
        Check(_arenas[1].Driver.RequestItemSwitch(), "Switch during active repair");
        await Until(() => AllPresent(HeldItem.Salvo), "Arm safely folds before next payload owns rack", 720);
        Check(host.Items.Slots.Single(s => s.Vehicle == Shooter).SecondItem == HeldItem.Salvo, "Visual interruption preserves new capability");
        host.Items.RemovePlayer(Shooter); await Frames(150);
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
        Position(0, false); await Frames(40);
        Check(_arenas.All(a => a.Bodies[Shooter].Rack.PresentedItem == HeldItem.None), "Death clears articulated payload");
        Position(1000, true); await Frames(90);
        Check(_arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Respawn starts stowed");
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
            for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
            {
                var vertices = mesh.Mesh.SurfaceGetArrays(surface)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                foreach (var vertex in vertices)
                {
                    Vector3 p = arm.ToLocal(mesh.ToGlobal(vertex)) + new Vector3(0, 1.34f, 1.845f);
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
        Check(image.SavePng(ProjectSettings.GlobalizePath("res://.godot/ts292/round3/" + name + ".png")) == Error.Ok, "Captured Wrench " + name);
        camera.QueueFree();
    }
}
