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
        System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath("res://.godot/ts292"));
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
                    Check(arm.Nested, "Starts at the folded pose before rack clearance");
                    foreach (var mesh in arm.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                    {
                        var bounds = mesh.GetAabb();
                        for (int i = 0; i < 8; i++)
                        {
                            Vector3 point = arm.ToLocal(mesh.ToGlobal(bounds.GetEndpoint(i)));
                            Check(point.Y < .445f && point.X > -.98f && point.X < .98f && point.Z > -.425f && point.Z < .79f,
                                "Folded mesh stays inside production trunk: " + mesh.Name);
                        }
                    }
                }
                await WrenchCapture("folded", new Vector3(-3.6f, 3.0f, 4.8f));
            }
            if (round != 1) { await Until(() => Arm(0)?.IsReady == true && Arm(1)?.IsReady == true, "Both arms unfolded to rest"); }
            if (round == 0) { await WrenchCapture("ready-side", new Vector3(-5.8f, 2.6f, 1)); }
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
                    if (arm.ContactIndex >= 0)
                    {
                        bool first = contacts[peer].Add(arm.ContactIndex);
                        Vector3 tip = arm.Tip + new Vector3(0, 1.34f, 1.845f);
                        Check(Math.Abs(tip.Y - .98f) < .005f && Math.Abs(tip.Z - .32f) < .001f,
                            "Tool reaches actual roof plane without penetrating");
                        if (first && peer == 1 && round == 0)
                            await WrenchCapture("contact-" + arm.ContactIndex, new Vector3(-4.8f, 3.2f, 2.8f));
                    }
                    if (!arm.Nested) { Check(_arenas[peer].Bodies[Shooter].Rack.Progress >= .999f, "Rack stays raised until arm is safely nested"); }
                }
                if (Arm(0) is { Repairing: true } owner && Arm(1) is { Repairing: true } remote)
                    Check(owner.Tip.DistanceTo(remote.Tip) < .35f, "Owner and remote cosmetic phases remain close");
                if (round == 2 && frame is 65 or 130)
                    await WrenchCapture("driving-" + frame, new Vector3(frame == 65 ? 5 : -5, 3, frame == 65 ? -4 : 4));
                if (frame > 120 && _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0)) { break; }
            }
            Check(contacts.All(c => c.SetEquals(new[] { 0, 1, 2 })), "Both peers visit left/middle/right roof contacts " + round);
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
        await Until(() => Arm(1)?.ContactIndex == 0, "Interrupt at left roof contact");
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

    private async Task WrenchCapture(string name, Vector3 offset)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        var body = _arenas[1].Bodies[Shooter];
        var camera = new Camera3D { Fov = 42, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        _arenas[1].AddChild(camera);
        camera.GlobalPosition = body.VisualPosition + body.VisualTransform.Basis * offset;
        camera.LookAt(body.VisualPosition + body.VisualTransform.Basis * new Vector3(0, .9f, .65f));
        camera.MakeCurrent();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = _views[1].GetTexture().GetImage();
        Check(image.SavePng(ProjectSettings.GlobalizePath("res://.godot/ts292/" + name + ".png")) == Error.Ok, "Captured Wrench " + name);
        camera.QueueFree();
    }
}
