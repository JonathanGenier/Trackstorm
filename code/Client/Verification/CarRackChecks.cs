using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Real UDP authority and rendered owner/observer rack acceptance using the current item catalog.</summary>
public sealed partial class CarRackChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _gateways = [];
    private readonly List<NetworkVehicleArena> _arenas = [];
    private readonly List<SubViewport> _views = [];
    private readonly List<string> _evidence = [];
    private readonly List<ItemEvent> _events = [];
    private readonly string _output = "res://.godot/ts259-rack";
    private int _checks;
    private ulong Shooter => _arenas[1].Driver.LocalVehicleId;

    public override void _Ready() => CallDeferred(MethodName.Run);

    public async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            System.IO.Directory.CreateDirectory(ProjectSettings.GlobalizePath(_output));
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
            socket.Close();
            for (int i = 0; i < 2; i++)
            {
                var gateway = new GameNetworkingSocketsTransport();
                ulong server = 0;
                if (i == 0) { gateway.Listen(TransportEndpoint.DirectIp(endpoint)); }
                else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
                _gateways.Add(gateway);
                var view = new SubViewport { Size = new(1280, 800), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
                if (i == 1) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
                else { AddChild(view); }
                _views.Add(view);
                var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
                arena.Initialize(gateway, i == 0 ? 88ul : 0, server);
                view.AddChild(arena);
                var ground = new StaticBody3D { Position = new(0, 20, 0), CollisionLayer = 1 };
                ground.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(400, 1, 400) } });
                ground.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(400, 1, 400) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.27f, 0.29f, 0.31f) } });
                arena.AddChild(ground);
                _arenas.Add(arena);
            }
            await Until(() => _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2), "Two native UDP peers ready", 1200);
            var host = _arenas[0].Driver.Host!;
            Check(host.TryConfigure(0, new Dictionary<string, double> { ["match.countdown_ticks"] = 1, ["match.minimum_players"] = 1 }, out _), "Fixture configuration accepted");
            Position(1000, false);
            _arenas[1].Driver.ItemsReceived += p => _events.AddRange(p.Events.Where(e => e.Owner == Shooter));
            await Frames(90);
            if (OS.GetCmdlineUserArgs().Contains("--consecutive-mines"))
            {
                host.Items.RemovePlayer(Shooter);
                await Frames(130);
                Position(1000, false, new N.Vector3(100, 21.65f, 35));
                await Frames(20);
                Check(host.Items.Grant(host.World, Shooter, HeldItem.ProxyMine), "First consecutive mine granted");
                Check(host.Items.Grant(host.World, Shooter, HeldItem.ProxyMine), "Second consecutive mine granted");
                await Until(() => AllPresent(HeldItem.ProxyMine), "First mine mounted on both peers");
                Check(_arenas[1].Driver.RequestItemUse(), "First consecutive mine used");
                await Until(() => host.Items.Mines.Any(m => m.Owner == Shooter && m.IsPlacing), "First mine placement starts");
                ulong firstMine = host.Items.Mines.Single(m => m.Owner == Shooter && m.IsPlacing).Id;
                await Until(() => host.Items.Mines.Any(m => m.Id == firstMine && !m.IsPlacing), "First mine releases");
                Position(1000, false, new N.Vector3(150, 21.65f, 35));
                ProxyMineRack[] arms = _arenas.Select(a => a.Bodies[Shooter].Rack.GetParent<Node3D>()
                    .GetNode<Node3D>("WeaponRack").GetChildren().OfType<ProxyMineRack>().Single()).ToArray();
                await Until(() => arms.All(arm => arm.Returning), "Empty arms begin returning on both peers");
                Check(_arenas[1].Driver.RequestItemSwitch(), "Select second mine during empty return");
                await Until(() => _arenas[1].Driver.LocalItem?.Active.Item == HeldItem.ProxyMine, "Second mine selected");
                Check(arms.All(arm => arm.Returning), "Previous empty return remains unfinished at second use");
                Check(_arenas[1].Driver.RequestItemUse(), "Second consecutive mine used");
                await Until(() => host.Items.Mines.Any(m => m.Owner == Shooter && m.Id != firstMine && m.IsPlacing), "Second authoritative placement starts");
                ulong secondMine = host.Items.Mines.Single(m => m.Owner == Shooter && m.Id != firstMine && m.IsPlacing).Id;
                await Until(() => _arenas.All(a => a.Driver.ItemState?.Mines.Any(m => m.Id == secondMine && m.IsPlacing) == true),
                    "Both peers receive second placement");
                foreach (var arena in _arenas)
                {
                    var rack = arena.Bodies[Shooter].Rack;
                    // Two process steps without another publication reproduce the reviewed gap.
                    rack._Process(0);
                    rack._Process(0);
                    Check(rack.GetParent<Node3D>().GetChildren().OfType<CarDeployment>().Single().Deployed,
                        "Second placement keeps the rack requested through a publication gap");
                }
                int activeFrames = 0;
                int[] observedFrames = new int[_arenas.Count];
                while (host.Items.Mines.Any(m => m.Id == secondMine && m.IsPlacing) && activeFrames < 90)
                {
                    await Frames(1);
                    for (int peer = 0; peer < _arenas.Count; peer++)
                    {
                        var arena = _arenas[peer];
                        if (arena.Driver.ItemState?.Mines.Any(m => m.Id == secondMine && m.IsPlacing) != true) { continue; }
                        var rack = arena.Bodies[Shooter].Rack;
                        if (rack.PresentedItem != HeldItem.ProxyMine || rack.Progress < .999f ||
                            !arms[peer].GetChildren().OfType<ProxyMineVisual>().Single().Visible)
                        {
                            throw new InvalidOperationException($"Second active mine left the rack on peer {peer} at placement frame {activeFrames}");
                        }
                        observedFrames[peer]++;
                    }
                    activeFrames++;
                    if (activeFrames == 30) { await Capture("second-mine-active"); }
                }
                Check(observedFrames.All(frames => frames >= 40), "Second mine stays mounted and visible through placement on both peers");
                var secondState = host.Items.Mines.SingleOrDefault(m => m.Id == secondMine);
                Check(activeFrames >= 45 && secondState is { IsPlacing: false },
                    $"Second mine completes its full authoritative placement (frames {activeFrames}, ticks {secondState?.PlacementTicks}, car {host.World.GetVehicle(Shooter).Movement.Physics.Position})");
                await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0),
                    "Rack returns and stows after second release");
                System.IO.File.WriteAllLines(ProjectSettings.GlobalizePath(_output + "/consecutive-mine-evidence.txt"), _evidence);
                GD.Print($"Car rack consecutive mine passed: {_checks} checks on two UDP peers.");
                foreach (var arena in _arenas) { arena.QueueFree(); }
                foreach (var view in _views) { view.QueueFree(); }
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                GetTree().Quit();
                return;
            }
            foreach (ItemDefinition item in ItemRegistry.All)
            {
                host.Items.RemovePlayer(Shooter);
                await Frames(130);
                Check(host.Items.Grant(host.World, Shooter, item.Identity, pickup: true), "Pickup " + item.DisplayName);
                Check(host.Items.Grant(host.World, Shooter, HeldItem.Wrench), "Two occupied slots");
                await Until(() => AllPresent(item.Identity), item.DisplayName + " visible to owner and other peer");
                if (item.Identity == HeldItem.Nitro)
                {
                    Check(_arenas.All(a => !a.Bodies[Shooter].Rack.Boost.FlameVisible && !a.Bodies[Shooter].Rack.Boost.SmokeEmitting), "Selected Nitro is ready without ignition on both peers");
                }
                await Capture(item.Key + "-equipped");
                Check(_arenas[1].Driver.RequestItemSwitch(), "Switch away");
                await Until(() => AllPresent(HeldItem.Wrench), "Wrench selected on both peers");
                Check(_arenas[1].Driver.RequestItemSwitch(), "Switch back");
                await Until(() => AllPresent(item.Identity), "Selected item re-deployed");
                int before = _events.Count;
                if (item.Identity == HeldItem.Shield)
                {
                    var shield = host.Items.Shields.Single(s => s.Owner == Shooter && s.Stage == ShieldStage.RearShield);
                    Check(_arenas[1].Driver.RequestItemUse(), "Remote Shield deployment request");
                    await Until(() => _arenas.All(a => !a.Bodies[Shooter].HasRearShield && a.Bodies[Shooter].Rack.Progress == 0), "Deployment retracts the carriage on both peers");
                    Check(_events.Count == before + 1 && host.Items.Shields.Any(s => s.Id == shield.Id && s.Stage == ShieldStage.WorldWall && s.HP == shield.HP), "Use transfers the same shield and health to one world wall");
                    Check(_arenas[1].Driver.LocalItem?.SecondItem == HeldItem.Wrench, "Shield use preserves the other physical slot");
                    continue;
                }
                Check(_arenas[1].Driver.RequestItemUse(), "Use request accepted for " + item.DisplayName);
                await Frames(item.Sustained ? 45 : 12, item.Sustained ? InputButtons.UseItem : 0);
                await Frames(1, 0, InputButtons.UseItem);
                await Until(() => _events.Count > before, "Confirmed use/fire outcome for " + item.DisplayName);
                HeldItem remaining = _arenas[1].Driver.LocalItem?.Active.Item ?? HeldItem.None;
                if (remaining == HeldItem.None)
                {
                    await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Depletion retracts and closes on both peers");
                }
                else
                {
                    await Frames(90);
                    Check(AllPresent(remaining), "Usable item stays deployed after release on both peers");
                    if (remaining == HeldItem.Nitro)
                    {
                        Check(_arenas.All(a => !a.Bodies[Shooter].Rack.Boost.FlameVisible && !a.Bodies[Shooter].Rack.Boost.SmokeEmitting), "Released Nitro stays extended without thrust effects on both peers");
                    }
                }
                Check(_arenas[1].Driver.LocalItem?.SecondItem == HeldItem.Wrench, "Use preserves second physical slot");
                await Capture(item.Key + "-after-use");
            }
            host.Items.RemovePlayer(Shooter);
            await Frames(130);
            Check(host.Items.Grant(host.World, Shooter, HeldItem.Salvo), "Five-shot lifecycle grant");
            await Until(() => AllPresent(HeldItem.Salvo), "Multi-shot rack deployed");
            Check(_arenas[1].Driver.RequestItemSwitch(), "Select empty physical slot");
            await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Empty selection stays stowed");
            await Frames(60);
            Check(_arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Empty slot does not reopen");
            Check(_arenas[1].Driver.RequestItemSwitch(), "Return to usable slot");
            await Until(() => AllPresent(HeldItem.Salvo), "Usable slot opens again");
            for (int shots = 4; shots >= 0; shots--)
            {
                Check(_arenas[1].Driver.RequestItemUse(), "Repeated shot accepted");
                await Until(() => _arenas[1].Driver.LocalItem?.Active.SalvoShots == shots, "Confirmed remaining shots " + shots);
                if (shots > 0)
                {
                    for (int frame = 0; frame < 90; frame++)
                    {
                        await Frames(1);
                        if (!AllPresent(HeldItem.Salvo)) { throw new InvalidOperationException("Rack cycled between usable shots"); }
                    }
                    Check(AllPresent(HeldItem.Salvo), "Both peers remain fully deployed between shots");
                }
                else
                {
                    await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Final shot retracts both racks");
                    await Frames(90);
                    Check(_arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Depleted rack remains stowed");
                    await Capture("salvo-depleted");
                }
            }
            host.Items.RemovePlayer(Shooter);
            await Frames(130);
            Check(host.Items.Grant(host.World, Shooter, HeldItem.MachineGun), "Duplicate first grant");
            Check(host.Items.Grant(host.World, Shooter, HeldItem.MachineGun), "Duplicate second grant");
            await Until(() => AllPresent(HeldItem.MachineGun), "Duplicate first selected");
            Check(_arenas[1].Driver.RequestItemSwitch(), "Duplicate switch");
            await Frames(25);
            Check(_arenas.All(a => a.Bodies[Shooter].Rack.Progress < 0.95f), "Duplicate switch retracts despite equal item type");
            await Until(() => AllPresent(HeldItem.MachineGun), "Duplicate second re-deployed");
            // Recovery installs ownership without replaying historical fire effects.
            foreach (var arena in _arenas)
            {
                var state = arena.Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == Shooter).State;
                arena.Bodies[Shooter].Reseed(state);
                arena.Bodies[Shooter].Rack.Observe(state.LifeId, true, arena.Driver.ItemState!.Slots.Single(s => s.Vehicle == Shooter), []);
            }
            await Until(() => AllPresent(HeldItem.MachineGun), "Recovery reconstructs selected duplicate without use replay");
            Position(0, false);
            await Frames(60);
            Check(_arenas.All(a => a.Bodies[Shooter].Rack.PresentedItem == HeldItem.None), "Death removes rack payload");
            Position(1000, true);
            await Frames(90);
            Check(_arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Respawn starts with closed empty rack");
            Check(host.Items.Grant(host.World, Shooter, HeldItem.Nitro), "New-life pickup");
            await Until(() => AllPresent(HeldItem.Nitro), "New-life Nitro readies its rack-mounted jet");
            await Frames(90, 0, 0, 40000, 7000);
            Check(_arenas[1].LocalState!.Movement.CommandSpeed > 3, "Normal driving with deployed rack");
            await Capture("driving-equipped");
            await Frames(10);
            Check(Lamps().All(l => !l.Braking && !l.Reversing), "Both UDP peers show coasting lamps");
            await Frames(8, brake: ushort.MaxValue);
            Check(Lamps().All(l => l.Braking), "Both UDP peers show moving brake lamps");
            await Capture("network-braking");
            await Frames(180, brake: ushort.MaxValue);
            Check(Lamps().All(l => l.Reversing && !l.Braking), "Both UDP peers show reverse lamps from accepted movement");
            await Capture("network-reversing");
            host.Items.RemovePlayer(Shooter);
            await Frames(130);
            Position(1000, false, new N.Vector3(-46, 1.15f, 0));
            await Until(() => _arenas[1].Driver.LocalItem?.Active.Item is { } picked && picked != HeldItem.None, "Native proximity pickup fills selected slot");
            HeldItem pickedItem = _arenas[1].Driver.LocalItem!.Active.Item;
            Check(host.Spawns!.States.Any(s => s.ClaimedBy == Shooter), "Authoritative marker claim recorded");
            await Until(() => AllPresent(pickedItem), "Actual pickup deploys correct rack on both peers");
            await Capture("proximity-pickup");
            host.Items.RemovePlayer(Shooter);
            Position(1000, false);
            await Frames(130);
            Check(host.Items.Grant(host.World, Shooter, HeldItem.Salvo), "Rapid-use first slot");
            Check(host.Items.Grant(host.World, Shooter, HeldItem.Wrench), "Rapid-use second slot");
            await Until(() => AllPresent(HeldItem.Salvo), "Rapid-use setup visible");
            int rapidBefore = _events.Count;
            Check(_arenas[1].Driver.RequestItemSwitch(), "Immediate switch command");
            Check(_arenas[1].Driver.RequestItemUse(), "Immediate switched use command");
            await Until(() => _events.Count > rapidBefore, "Immediate switched use confirmed");
            await Until(() => AllPresent(HeldItem.Wrench), "Consumed switched Wrench presented rather than old Salvo");
            await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Rapid switched use closes coherently");
            Check(_arenas[1].Driver.LocalItem?.Item == HeldItem.Salvo, "Rapid use preserves unselected Salvo");
            System.IO.File.WriteAllLines(ProjectSettings.GlobalizePath(_output + "/rack-evidence.txt"), _evidence);
            GD.Print($"Car rack integration passed: {_checks} checks; seven items, switching/use, duplicates, lifecycle and two UDP peers.");
            foreach (var arena in _arenas) { arena.QueueFree(); }
            foreach (var view in _views) { view.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private bool AllPresent(HeldItem item) => _arenas.All(a => a.Bodies.TryGetValue(Shooter, out var body) &&
        (item == HeldItem.Shield ? body.HasRearShield && body.Rack.PresentedItem == item && body.Rack.Progress >= .999f :
        body.Rack.PresentedItem == item && body.Rack.Progress >= 0.999f && !body.HasRearShield &&
        (item != HeldItem.Nitro || body.Rack.Boost.Deployment >= .999f)));
    private IEnumerable<CarLighting> Lamps() => _arenas.Select(a => a.Bodies[Shooter].Rack.GetParent<Node3D>().GetChildren().OfType<CarLighting>().Single());
    private async Task Frames(int count, InputButtons held = 0, InputButtons released = 0, ushort throttle = 0, short steer = 0, ushort brake = 0)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            for (int peer = 0; peer < _arenas.Count; peer++)
            {
                _arenas[peer].Advance(peer == 1 ? new InputFrame(0, steer, throttle, brake, held, 0, released) : default);
                if (_arenas[peer].Driver.Failure.Length > 0) { throw new InvalidOperationException(_arenas[peer].Driver.Failure); }
            }
        }
    }
    private async Task Until(Func<bool> condition, string evidence, int limit = 420)
    {
        for (int i = 0; i < limit && !condition(); i++) { await Frames(1); }
        Check(condition(), evidence);
    }
    private void Position(float hp, bool newLife, N.Vector3? shooterPosition = null)
    {
        var world = _arenas[0].Driver.Host!.World;
        var state = world.State;
        world.Restore(new(state.Tick, state.LastInput, state.Vehicles.Select(v =>
        {
            var pose = new VehiclePhysicsState(v.VehicleId == Shooter && shooterPosition.HasValue ? shooterPosition.Value : new N.Vector3(v.VehicleId == Shooter ? 0 : 8, 21.65f, 35), N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
            _arenas[0].Bodies[v.VehicleId].Apply(pose);
            return new VehicleSnapshot(v.VehicleId, v.LifeId + (newLife ? 1ul : 0), new VehicleState(state.Tick, pose, true, false, 0, 0), new VehicleDamageState(1000, v.VehicleId == Shooter ? hp : 1000, null, null), pose);
        }), state.Match));
    }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        var camera = new Camera3D { PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off, Fov = 48 };
        _arenas[1].AddChild(camera);
        var body = _arenas[1].Bodies[Shooter];
        camera.GlobalPosition = body.VisualPosition + body.VisualTransform.Basis * new Vector3(-4, 3.4f, 5);
        camera.LookAt(body.VisualPosition + Vector3.Up * .25f);
        camera.MakeCurrent();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = _views[1].GetTexture().GetImage();
        Check(image.SavePng(ProjectSettings.GlobalizePath(_output + "/" + name + ".png")) == Error.Ok, "Captured " + name);
        camera.QueueFree();
    }
    private void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
        _checks++; _evidence.Add(message); GD.Print(message);
    }
    public override void _ExitTree() { foreach (var gateway in _gateways) { gateway.Dispose(); } }
}
