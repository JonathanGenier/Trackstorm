using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
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
    private readonly string _output = "res://.godot/ts164-round2";
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
            foreach (ItemDefinition item in ItemRegistry.All)
            {
                host.Items.RemovePlayer(Shooter);
                await Frames(130);
                Check(host.Items.Grant(host.World, Shooter, item.Identity, pickup: true), "Pickup " + item.DisplayName);
                Check(host.Items.Grant(host.World, Shooter, HeldItem.Wrench), "Two occupied slots");
                await Until(() => AllPresent(item.Identity), item.DisplayName + " visible to owner and other peer");
                await Capture(item.Key + "-equipped");
                Check(_arenas[1].Driver.RequestItemSwitch(), "Switch away");
                await Until(() => AllPresent(HeldItem.Wrench), "Wrench selected on both peers");
                Check(_arenas[1].Driver.RequestItemSwitch(), "Switch back");
                await Until(() => AllPresent(item.Identity), "Selected item re-deployed");
                int before = _events.Count;
                Check(_arenas[1].Driver.RequestItemUse(), "Use request accepted for " + item.DisplayName);
                await Frames(item.Sustained ? 45 : 12, item.Sustained ? InputButtons.UseItem : 0);
                await Frames(1, 0, InputButtons.UseItem);
                await Until(() => _events.Count > before, "Confirmed use/fire outcome for " + item.DisplayName);
                await Until(() => _arenas.All(a => a.Bodies[Shooter].Rack.Progress == 0), "Use retracts and closes on both peers");
                Check(_arenas[1].Driver.LocalItem?.SecondItem == HeldItem.Wrench, "Use preserves second physical slot");
                await Capture(item.Key + "-used-closed");
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
            await Until(() => AllPresent(HeldItem.Nitro), "New-life rack deploys normally");
            await Frames(90, 0, 0, 40000, 7000);
            Check(_arenas[1].LocalState!.Movement.CommandSpeed > 3, "Normal driving with deployed rack");
            await Capture("driving-equipped");
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
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private bool AllPresent(HeldItem item) => _arenas.All(a => a.Bodies.TryGetValue(Shooter, out var body) && body.Rack.PresentedItem == item && body.Rack.Progress >= 0.999f);
    private async Task Frames(int count, InputButtons held = 0, InputButtons released = 0, ushort throttle = 0, short steer = 0)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            for (int peer = 0; peer < _arenas.Count; peer++)
            {
                _arenas[peer].Advance(peer == 1 ? new InputFrame(0, steer, throttle, 0, held, 0, released) : default);
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
