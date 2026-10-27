using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native tire support, real UDP contamination visibility and bounded cosmetic continuation.</summary>
public sealed partial class OilTireChecks : Node
{
    private readonly List<NetworkVehicleArena> _arenas = new();
    private readonly List<ReplicationTrafficGateway> _gateways = new();
    private readonly List<SubViewport> _views = new();
    private readonly List<object> _evidence = new();
    private bool _running;
    private int _assertions;
    private string _output = "";

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _output = ProjectSettings.GlobalizePath("res://.godot/oil-tire-checks");
        System.IO.Directory.CreateDirectory(_output);
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}";
        socket.Close();
        for (int peer = 0; peer < 2; peer++)
        {
            var gateway = new ReplicationTrafficGateway();
            ulong server = 0;
            if (peer == 0)
            {
                gateway.Listen(TransportEndpoint.DirectIp(endpoint));
                if (OS.GetCmdlineUserArgs().Contains("--oil-impaired")) { gateway.ConfigureSimulation(new NetworkSimulation(30, 5, 2, 0, 0)); }
            }
            else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
            _gateways.Add(gateway);
            var view = new SubViewport { Size = new Vector2I(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
            AddChild(view); _views.Add(view);
            var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
            arena.Initialize(gateway, peer == 0 ? 88ul : 0, server);
            view.AddChild(arena); _arenas.Add(arena);
            var pad = new StaticBody3D { Position = new Vector3(0, 20, 0), CollisionLayer = 1 };
            pad.SetMeta("surface_identity", "Concrete");
            pad.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(100, 1, 100) } });
            pad.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(100, 1, 100) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.3f, .32f, .33f) } });
            arena.AddChild(pad);
            var camera = new Camera3D { Position = new Vector3(9, 29, 15), Current = true };
            arena.AddChild(camera); camera.LookAt(new Vector3(0, 20, 0));
        }
        _running = true;
        CallDeferred(MethodName.Run);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_running) { foreach (var arena in _arenas) { arena.Advance(default); } }
    }

    private TireFeedback Feedback(int peer, ulong car) => _arenas[peer].Bodies[car].GetChildren().OfType<TireFeedback>().Single();

    private async void Run()
    {
        try
        {
            await Until(() => _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2) && _arenas[0].Driver.Host!.World.State.Match?.Phase == MatchPhase.Active);
            Place(1, new(0, 21.6f, 0)); Place(2, new(-12, 21.6f, 0));
            await Frames(90);
            Check(Core.Settings.TireEffectSettings.Defaults.TryApply(new Dictionary<string, double> { ["tire.lifetime"] = 2 }, out var tireSettings, out _), "Short ordinary skid lifetime isolates oil trail expiry");
            foreach (var arena in _arenas) { TireMarkBatch.For(arena).SettingsSource = () => tireSettings; }
            var host = _arenas[0].Driver.Host!;
            var defaults = new VehicleConfiguration();
            Check(host.TryConfigure(0, new Dictionary<string, double>
            {
                ["vehicle.oil_impairment_seconds"] = defaults.OilImpairmentSeconds,
                ["vehicle.oil_recovery_seconds"] = defaults.OilRecoverySeconds,
                ["vehicle.oil_grip_reduction"] = defaults.OilGripReduction,
                ["vehicle.oil_rear_drive_reduction"] = defaults.OilRearDriveReduction,
                ["vehicle.oil_rear_lateral_reduction"] = defaults.OilRearLateralReduction,
            }, out _), "Fixture uses current Oil defaults rather than saved host tuning");
            var observation = _arenas[0].Bodies[1].Observe(host.World.GetVehicle(1));
            var point = observation.Wheels!.Value.Contacts!.Value[0]!.Value;
            var patch = new OilPatch(900001, 2, point, N.Vector3.UnitY, .1f) { ExpiresAtTick = host.World.State.Tick + 30 };
            host.Items.Restore(new(host.Items.Revision + 1, host.Snapshot(), host.Items.Slots, [], [], patches: [patch], discardRevision: host.Items.DiscardRevision), host.Items.Revision + 1, 900001);
            await Until(() => _arenas.All(a => a.Driver.Latest!.Vehicles.Single(v => v.State.VehicleId == 1).State.Movement.Oil.FrontLeft > 0));
            await Frames(12);
            for (int peer = 0; peer < 2; peer++)
            {
                Check(Feedback(peer, 1).OilCoverage(0) > .7f, "Accepted FL contamination visible on peer " + peer);
                Check(Feedback(peer, 1).OilSplashes > 0, "Fresh accepted contact emits Oil splash on peer " + peer);
                for (int wheel = 1; wheel < 4; wheel++) { Check(Feedback(peer, 1).OilCoverage(wheel) == 0, "Other tire stays clean"); }
                Check(Feedback(peer, 2).OilCoverage(0) == 0, "Independent vehicle stays clean");
            }
            await Capture("contact");
            int[] before = [Feedback(0, 1).OilMarksWritten, Feedback(1, 1).OilMarksWritten];
            var initial = host.World.GetVehicle(1).Movement.Physics.Position;
            for (int frame = 0; frame < 140; frame++)
            {
                float angle = frame * .013f;
                Place(1, initial + new N.Vector3(6 * (1 - MathF.Cos(angle)), 0, -6 * MathF.Sin(angle)), -angle, new(4, 0, -6));
                await Frames(1);
            }
            for (int peer = 0; peer < 2; peer++) { Check(Feedback(peer, 1).OilMarksWritten > before[peer] + 10, "Curved sliding path produces independent wet marks on peer " + peer); }
            Check(host.World.GetVehicle(1).Movement.Oil.RemainingTicks == 0, "Handling recovers within two seconds");
            Check(host.Items.Patches.Count == 0 && host.Items.OilContacts.Count == 0, "Original hazard expires independently");
            Check(Feedback(0, 1).OilCoverage(0) > .1f, "Cosmetic contamination outlives handling and hazard");
            await Capture("curved-trail");
            int[] fastMarks = [Feedback(0, 1).OilMarksWritten, Feedback(1, 1).OilMarksWritten];
            var fastStart = host.World.GetVehicle(1).Movement.Physics.Position;
            for (int frame = 1; frame <= 20; frame++)
            {
                Place(1, fastStart + new N.Vector3(frame, 0, 0), velocity: new(60, 0, 0));
                await Frames(1);
            }
            for (int peer = 0; peer < 2; peer++) { Check(Feedback(peer, 1).OilMarksWritten > fastMarks[peer], "Sixty m/s contact path retains trails on peer " + peer); }
            await Capture("high-speed");
            Place(1, new(3, 28, -6), velocity: new(2, 0, -5));
            await Frames(9);
            int airborne = Feedback(0, 1).OilMarksWritten;
            for (int i = 0; i < 25; i++) { Place(1, new(3 + i * .05f, 28, -6 - i * .1f), velocity: new(2, 0, -5)); await Frames(1); }
            Check(Feedback(0, 1).OilMarksWritten == airborne, "No marks on airborne path");
            await Capture("airborne");
            Place(1, new(4, 21.65f, -9)); await Frames(20);
            int landed = Feedback(0, 1).OilMarksWritten;
            var landing = host.World.GetVehicle(1).Movement.Physics.Position;
            for (int i = 0; i < 35; i++) { Place(1, landing + new N.Vector3(0, 0, -i * .08f), velocity: new(0, 0, -5)); await Frames(1); }
            Check(Feedback(0, 1).OilMarksWritten > landed, "Grounded trail resumes without airborne bridge");
            await Capture("landing");
            int nodes = Descendants(this);
            long memory = GC.GetTotalMemory(true);
            for (int cycle = 0; cycle < 24; cycle++)
            {
                ulong car = (ulong)(cycle % 2 + 1);
                int wheel = cycle / 2 % 4;
                Place(car, new(car == 1 ? 8 : -12, 21.65f, 0));
                await Frames(12);
                var contact = _arenas[0].Bodies[car].Observe(host.World.GetVehicle(car)).Wheels!.Value.Contacts!.Value[wheel];
                Check(contact.HasValue, "Repeated-use tire has native support");
                ulong token = (ulong)(900002 + cycle);
                var repeated = new OilPatch(token, car == 1 ? 2ul : 1ul, contact!.Value, N.Vector3.UnitY, .1f) { ExpiresAtTick = host.World.State.Tick + 15 };
                host.Items.Restore(new(host.Items.Revision + 1, host.Snapshot(), host.Items.Slots, [], [], patches: [repeated], discardRevision: host.Items.DiscardRevision), host.Items.Revision + 1, token);
                await Frames(30);
                for (int peer = 0; peer < 2; peer++) { Check(Feedback(peer, car).OilCoverage(wheel) > .5f, "Repeated independent contact visible on both peers"); }
            }
            // Existing hazard surfaces retain their approved two-second cosmetic expiry fade.
            await Frames(150);
            Check(Descendants(this) <= nodes, $"Twenty-four contacts across eight tires retire scene nodes ({nodes} -> {Descendants(this)})");
            _evidence.Add(new { cycles = 24, nodesBefore = nodes, nodesAfter = Descendants(this), managedBytesBefore = memory, managedBytesAfter = GC.GetTotalMemory(true), markCapacity = TireMarkBatch.MaximumCapacity });
            await Capture("multiple-tires");
            await Frames(300);
            Check(Feedback(0, 1).OilCoverage(0) == 0 && Feedback(1, 1).OilCoverage(0) == 0, "Contamination completely clears on both peers");
            await Frames(750);
            Check(TireMarkBatch.For(_arenas[0]).Alive == 0, "All cosmetic trails expire");
            await Capture("faded");
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "results.json"), System.Text.Json.JsonSerializer.Serialize(new { assertions = _assertions, evidence = _evidence }));
            GD.Print($"Oil tire checks passed: {_assertions} assertions.");
            _running = false;
            foreach (var arena in _arenas) { arena.QueueFree(); }
            foreach (var gateway in _gateways) { gateway.Dispose(); }
            await Frames(5); GetTree().Quit();
        }
        catch (Exception error) { _running = false; GD.PrintErr(error); foreach (var gateway in _gateways) { gateway.Dispose(); } GetTree().Quit(1); }
    }

    private void Place(ulong id, N.Vector3 position, float yaw = 0, N.Vector3 velocity = default)
    {
        var host = _arenas[0].Driver.Host!;
        var world = host.World.State;
        var pose = new VehiclePhysicsState(position, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, yaw), velocity, N.Vector3.Zero);
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId != id ? v :
            new VehicleSnapshot(id, v.LifeId, new VehicleState(world.Tick, pose, true, false, 0, 0, oil: v.Movement.Oil), v.Damage, pose)), world.Match));
        _arenas[0].Bodies[id].Apply(pose);
    }

    private async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 600; i++) { if (condition()) { return; } await Frames(1); }
        throw new InvalidOperationException("Oil tire scenario timed out.");
    }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); } }
    private static int Descendants(Node node) => 1 + node.GetChildren().Sum(Descendants);
    private void Check(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } _assertions++; _evidence.Add(message); }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        if (name == "contact")
        {
            foreach (var view in _views)
            {
                var camera = view.GetCamera3D();
                camera.Position = new Vector3(-5, 23.5f, -6);
                camera.LookAt(new Vector3(-1, 21, -1));
            }
        }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        for (int peer = 0; peer < _views.Count; peer++)
        {
            using var image = _views[peer].GetTexture().GetImage();
            image.SavePng(System.IO.Path.Combine(_output, $"{name}-peer{peer}.png"));
        }
        if (name == "contact")
        {
            foreach (var view in _views)
            {
                var camera = view.GetCamera3D();
                camera.Position = new Vector3(9, 29, 15);
                camera.LookAt(new Vector3(0, 20, 0));
            }
        }
    }
}
