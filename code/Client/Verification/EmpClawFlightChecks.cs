using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native production arenas over three impaired UDP peers. Explicit pose fixtures isolate guidance and collisions.</summary>
public sealed partial class EmpClawFlightChecks : Node3D
{
    private readonly List<NetworkVehicleArena> _arenas = [];
    private readonly List<GameNetworkingSocketsTransport> _gateways = [];
    private readonly List<SubViewport> _views = [];
    private readonly Dictionary<ulong, N.Vector3> _positions = [];
    private readonly List<object> _evidence = [];
    private readonly List<ItemEvent>[] _events = [[], [], []];
    private readonly int[] _motions = new int[3];
    private readonly N.Vector3[] _directions = [-N.Vector3.UnitZ, -N.Vector3.UnitZ, -N.Vector3.UnitZ];
    private readonly ulong[] _targets = new ulong[3];
    private readonly List<StaticBody3D> _obstacles = [];
    private string _output = "";
    private HostVehicleSession Host => _arenas[0].Driver.Host!;
    private int _checks;
    private bool _failed;
    private N.Quaternion _shooterOrientation = N.Quaternion.Identity;

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            _output = ProjectSettings.GlobalizePath("res://.godot/emp-flight-checks");
            if (OS.GetCmdlineUserArgs().Contains("--emp-attachments")) { _output = ProjectSettings.GlobalizePath("res://.godot/emp-attachment-checks/round2"); }
            if (OS.GetCmdlineUserArgs().Contains("--emp-power")) { _output = ProjectSettings.GlobalizePath("res://.godot/emp-attachment-checks/round2/readability"); }
            if (OS.GetCmdlineUserArgs().Contains("--emp-production")) { _output += "/production"; }
            if (OS.GetCmdlineUserArgs().Contains("--emp-long-flight")) { _output += "/long-flight"; }
            if (OS.GetCmdlineUserArgs().Contains("--emp-counterplay")) { _output = ProjectSettings.GlobalizePath("res://.godot/emp-counterplay-checks"); }
            System.IO.Directory.CreateDirectory(_output);
            if (OS.GetCmdlineUserArgs().Contains("--emp-production"))
            {
                await ProductionTerrain();
                GD.Print($"EMP production terrain passed: {_checks} checks.");
            }
            else
            {
                await Connect();
                if (OS.GetCmdlineUserArgs().Contains("--emp-counterplay")) { await Counterplay(); }
                else if (OS.GetCmdlineUserArgs().Contains("--emp-power")) { await NativePowerComparison(); }
                else if (OS.GetCmdlineUserArgs().Contains("--emp-attachments")) { await AttachmentLifecycle(); }
                else if (OS.GetCmdlineUserArgs().Contains("--emp-long-flight"))
                {
                    await LongFlight();
                    GD.Print($"EMP long flight passed: {_checks} checks.");
                }
                else
                {
                    await LockedFlight();
                    await Interception();
                    await OwnerAndWeapons();
                    await Acquisition();
                    await MultipleAndRecovery();
                    await Terrain();
                }
                if (!OS.GetCmdlineUserArgs().Contains("--emp-power"))
                { Check(_motions.All(n => n > 30), "All three peers received sustained replaceable motion."); }
                Record(new { scenario = "network", delayMs = 100, jitterMs = 15, lossPercent = 3, motions = _motions,
                    launches = _events.Select(es => es.Count(e => !e.Impact)).ToArray(), impacts = _events.Select(es => es.Count(e => e.Impact)).ToArray() });
                System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "results.json"), System.Text.Json.JsonSerializer.Serialize(_evidence, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
                GD.Print(OS.GetCmdlineUserArgs().Contains("--emp-power")
                    ? $"EMP power integration passed: {_checks} checks; host, client and practice-driver launches, capped power and native respawn cleanup."
                    : $"EMP flight integration passed: {_checks} checks; three impaired UDP peers, native hits, terrain, recovery and cleanup.");
            }
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); _failed = true; }
        finally
        {
            foreach (var arena in _arenas) { arena.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            foreach (var gateway in _gateways) { gateway.ConfigureSimulation(new()); gateway.Dispose(); }
            foreach (var view in _views) { view.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        // Give native audio playback teardown real mixer time, including headless runs.
        await ToSignal(GetTree().CreateTimer(.25), SceneTreeTimer.SignalName.Timeout);
        GetTree().Quit(_failed ? 1 : 0);
    }

    private async Task Connect()
    {
        using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        string endpoint = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
        reservation.Close();
        for (int i = 0; i < 3; i++)
        {
            var gateway = new GameNetworkingSocketsTransport(); _gateways.Add(gateway);
            ulong server = 0;
            if (i == 0) { gateway.Listen(TransportEndpoint.DirectIp(endpoint)); }
            else { server = gateway.Connect(TransportEndpoint.DirectIp(endpoint)); }
            gateway.ConfigureSimulation(new NetworkSimulation(100, 15, 3, 0, 0));
            var view = new SubViewport { OwnWorld3D = true, Size = new(1280, 720), RenderTargetUpdateMode = i == 0 ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled };
            if (i == 0) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
            else { AddChild(view); }
            _views.Add(view);
            var arena = new NetworkVehicleArena { PrototypeMapForVerification = true };
            arena.Initialize(gateway, i == 0 ? 297ul : 0, server); view.AddChild(arena); _arenas.Add(arena);
            AddBox(arena, new(0, 99, -150), new(600, 2, 800), true);
            int index = i;
            arena.Driver.ItemsReceived += p => _events[index].AddRange(p.Events.Where(e => e.Item == HeldItem.EmpClaw));
            arena.Driver.ProjectileMotionReceived += _ => _motions[index]++;
            // Admit one peer at a time: fixtures address cars 1/2/3, but simultaneous
            // impaired connections may arrive in a different order.
            await Until(() => arena.Driver.LocalState?.VehicleId == (ulong)i + 1, "Fixture peer receives its expected stable identity.");
        }
        await Until(() => _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 3), "Three production arenas connected.");
        Configure(new() { ["match.minimum_players"] = 1, ["match.countdown_ticks"] = 1, ["items.emp_claw_lifetime"] = 8 });
        await Until(() => Host.World.State.Match?.Phase == Core.Matches.MatchPhase.Active, "Active match.");
        await Reset();
    }

    private async Task Frames(int count, Action<int>? action = null)
    {
        for (int frame = 0; frame < count; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            action?.Invoke(frame);
            if (_positions.Count > 0)
            {
                var state = Host.World.State;
                Host.World.Restore(new SimulationState(state.Tick, state.LastInput, state.Vehicles.Select(v =>
                {
                    if (!_positions.TryGetValue(v.VehicleId, out var position)) { return v; }
                    var pose = new VehiclePhysicsState(position, v.VehicleId == 1 ? _shooterOrientation : v.VehicleId == 2 ? _victimOrientation : _powerFixture && v.VehicleId == 4 ? N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -MathF.PI / 2) : N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
                    return new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(state.Tick, pose, false, false, 0, 0), v.Damage, pose);
                }), state.Match));
            }
            for (int i = 0; i < _arenas.Count; i++)
            {
                var driver = _arenas[i].Driver;
                driver.DesiredAim = _directions[i];
                driver.DesiredAimTarget = _targets[i];
                driver.DesiredAimTargetLife = _targets[i] == 0 ? 0 : Host.World.GetVehicle(_targets[i]).LifeId;
                _arenas[i].Advance(i == 0 ? _hostInput : i == 1 ? _victimInput : default);
                if (driver.Failure.Length > 0) { throw new InvalidOperationException(driver.Failure); }
            }
        }
    }

    private async Task Until(Func<bool> condition, string reason, int maximum = 900)
    {
        for (int i = 0; i < maximum && !condition(); i++) { await Frames(1); }
        Check(condition(), reason);
    }

    private async Task Reset()
    {
        foreach (var obstacle in _obstacles) { obstacle.QueueFree(); } _obstacles.Clear();
        await Until(() => Host.Items.Missiles.Count == 0, "Previous flights end within captured budget.", 1200);
        _shooterOrientation = N.Quaternion.Identity;
        _positions[1] = new(0, 100.8f, 0); _positions[2] = new(0, 101.5f, -90); _positions[3] = new(60, 101.5f, 40);
        for (int i = 0; i < 3; i++) { _directions[i] = -N.Vector3.UnitZ; _targets[i] = 0; }
        foreach (var slot in Host.Items.Slots.ToArray())
        {
            if (slot.Active.Item != HeldItem.None) { Host.Items.Discard(Host.World, slot.Vehicle, slot.Life, slot.Active.Token, slot.SelectionRevision); }
        }
        await Frames(120);
    }

    private async Task<ulong> Fire(int peer, ulong target = 0)
    {
        ulong owner = _arenas[peer].Driver.LocalState!.VehicleId;
        var existing = Host.Items.Slots.FirstOrDefault(s => s.Vehicle == owner);
        if (existing?.Active.Item != HeldItem.EmpClaw) { Check(Host.Items.Grant(Host.World, owner, HeldItem.EmpClaw), "Existing grant supplies EMP for controlled flight fixture."); }
        _targets[peer] = target;
        await Until(() => _arenas[peer].Driver.LocalItem is { } inventory && inventory.Active.Item == HeldItem.EmpClaw &&
            _arenas[peer].Bodies[owner].Rack.IsEmpClawReady(inventory) && Host.Items.EmpClawReady(Host.Items.Slots.Single(s => s.Vehicle == owner)), "Local and authoritative launcher readiness.");
        await Frames(25);
        var slot = Host.Items.Slots.Single(s => s.Vehicle == owner).Active;
        Check(_arenas[peer].Driver.RequestItemUse(), "Ordinary reliable use request accepted.");
        await Until(() => _events[0].Any(e => e.Token == slot.Token && !e.Impact), "Host committed launch.");
        return slot.Token;
    }

    private async Task LockedFlight()
    {
        ulong id = await Fire(0, 2);
        var shot = Host.Items.Missiles.Single(p => p.Id == id);
        Check(shot.EmpClaw!.Target == 2, "Locked fire captures the exact rival.");
        float launch = shot.Velocity.Length() * 3.6f;
        Check(launch > 200, "Measured launch exceeds 200 km/h.");
        await Frames(35);
        shot = Host.Items.Missiles.Single(p => p.Id == id);
        Check(Math.Abs(shot.Velocity.Length() * 3.6f - 100) < .05f, "Rapid deceleration reaches 100 km/h.");
        // Cover now blocks the shooter's view behind the flying claw; no flight obstruction is removed.
        foreach (var arena in _arenas) { _obstacles.Add(AddBox(arena, new(0, 104, -8), new(20, 8, 1))); }
        _directions[0] = N.Vector3.UnitX; _targets[0] = 3;
        _positions[2] = new(18, 101.5f, -90);
        await Frames(35);
        shot = Host.Items.Missiles.Single(p => p.Id == id);
        Check(shot.EmpClaw!.Target == 2 && shot.Velocity.X > 1, "Camera departure and shooter occlusion retain a smoothly curving target.");
        await Capture("01-locked-homing", new(45, 120, -20), VehicleBody.ToGodot(shot.Position));
        await Until(() => Host.Items.Missiles.All(p => p.Id != id), "Locked claw reaches a native terminal collision.");
        var impact = _events[0].Single(e => e.Token == id && e.Impact);
        Check(impact.EmpClawContact?.Vehicle == 2, "Native hit hands off the intended car.");
        await Frames(45);
        Check(_events.All(es => es.Count(e => e.Token == id && e.Impact) == 1), "All peers receive exactly one contact.");
        Record(new { scenario = "locked", launchKmh = launch, cruiseKmh = shot.Velocity.Length() * 3.6f, impact.Position, impact.EmpClawContact });
        await Reset();
    }

    private async Task Interception()
    {
        ulong id = await Fire(0, 2);
        await Frames(20);
        var shot = Host.Items.Missiles.Single(p => p.Id == id);
        _positions[3] = shot.Position + N.Vector3.Normalize(shot.Velocity) * 12;
        await Until(() => Host.Items.Missiles.All(p => p.Id != id), "A different car physically intercepts.");
        var impact = _events[0].Single(e => e.Token == id && e.Impact);
        Check(impact.EmpClawContact?.Vehicle == 3, "Interception hands off actual car, not homing victim.");
        Check(impact.EmpClawContact!.LocalPosition.Length() < 5, "Contact remains on the struck body.");
        Record(new { scenario = "interception", impact.Position, impact.EmpClawContact });
        await Reset();
    }

    private async Task OwnerAndWeapons()
    {
        _positions[2] = new(0, 101.5f, 120);
        ulong id = await Fire(0);
        await Frames(25);
        var shot = Host.Items.Missiles.Single(p => p.Id == id);
        // Place the original owner directly across the complete next swept segment.
        _positions[1] = shot.Position + N.Vector3.Normalize(shot.Velocity) * .2f;
        await Frames(1);
        Check(Host.Items.Missiles.Any(p => p.Id == id), "Native full-flight sweep ignores its owner even after leaving the barrel.");
        shot = Host.Items.Missiles.Single(p => p.Id == id);
        var ray = _arenas[0].Driver.RaycastWeapon!;
        var bullet = ray(1, shot.Position - N.Vector3.UnitX * 2, shot.Position + N.Vector3.UnitX * 2);
        Check(bullet is null, "A native weapon ray passes through the flight visual; there is no shootable projectile body.");
        _positions[1] = new(0, 100.8f, 0);
        foreach (var arena in _arenas) { _obstacles.Add(AddBox(arena, VehicleBody.ToGodot(shot.Position - N.Vector3.UnitZ * 6), new(40, 12, .1f))); }
        await Until(() => Host.Items.Missiles.All(p => p.Id != id), "A thin wall terminates swept flight without tunneling.");
        Check(_events[0].Single(e => e.Token == id && e.Impact).EmpClawContact is null, "World impact never attaches to a car.");
        Record(new { scenario = "owner-weapon-world", passed = true });
        await Reset();
    }

    private async Task Acquisition()
    {
        _positions[2] = new(0, 101.5f, 60); _positions[3] = new(50, 101.5f, -25);
        ulong id = await Fire(0);
        await Frames(20);
        Check(Host.Items.Missiles.Single(p => p.Id == id).EmpClaw!.Target == 0, "Behind and off-cone rivals are excluded.");
        _positions[3] = new(4, 101.5f, -80);
        await Frames(3);
        Check(Host.Items.Missiles.Single(p => p.Id == id).EmpClaw!.Target == 3, "First visible forward rival is acquired.");
        _positions[2] = new(0, 101.5f, -50);
        await Frames(5);
        Check(Host.Items.Missiles.Single(p => p.Id == id).EmpClaw!.Target == 3, "A newly nearer rival cannot replace committed identity.");
        Record(new { scenario = "forward-acquisition", passed = true });
        await Reset();
    }

    private async Task MultipleAndRecovery()
    {
        Configure(new() { ["items.emp_claw_lifetime"] = 4 });
        _positions[1] = new(0, 110, 0); _positions[2] = new(30, 110, 0); _positions[3] = new(60, 110, 0);
        ulong first = await Fire(0);
        ulong second = await Fire(1);
        Check(Host.Items.Missiles.Count >= 2, "Two independent players have simultaneous guided projectiles.");
        var publication = new ItemPublication(Host.Items.Revision, Host.Snapshot(), Host.Items.Slots, Host.Items.Missiles, []);
        var restored = new ItemAuthority(Host.Items.Configuration);
        var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(publication));
        restored.Restore(decoded, Host.Items.Revision, Host.Items.TokenHighWater);
        Check(restored.Missiles.SequenceEqual(Host.Items.Missiles), "Recovery preserves exact live identity, velocity and deadlines.");
        await Capture("02-simultaneous", new(55, 122, 15), new(20, 110, -40));
        await Until(() => Host.Items.Missiles.Count == 0, "All shots expire on their independent captured budgets.");
        await Frames(60);
        Check(_arenas.All(a => a.Driver.ItemState?.Missiles.Count == 0), "Reliable expiry clears all peer memberships.");
        Check(_arenas.All(a => !a.FindChildren("*", "Node", true, false).OfType<EmpClawFlightVisual>().Any()), "Missing membership removes every flight model and electric trail.");
        Check(_events.All(es => es.Any(e => e.Token == first && !e.Impact) && es.Any(e => e.Token == second && !e.Impact)), "Remote fire replicates across all peers under latency and loss.");
        Record(new { scenario = "simultaneous-recovery-expiry", first, second, restored = restored.Missiles.Count });
        Configure(new() { ["items.emp_claw_lifetime"] = 8 });
        await Reset();
    }

    private async Task Terrain()
    {
        // Three native drivable surfaces: climb, descending slope and cross-bank.
        foreach (float degrees in new[] { 12f, -12f, 0f })
        {
            foreach (var arena in _arenas)
            {
                var ramp = AddBox(arena, new(0, 104, -65), new(35, 1, 80), true);
                ramp.RotationDegrees = degrees == 0 ? new(0, 0, 14) : new(degrees, 0, 0);
                _obstacles.Add(ramp);
            }
            await Frames(2);
            var query = _arenas[0].Driver.QueryMissileTerrain!;
            var support = query(new(0, 125, -40), new(0, 90, -40))!;
            _positions[1] = support.Position + N.Vector3.UnitY * .5f;
            _positions[2] = new(100, 110, 40); _positions[3] = new(120, 110, 40);
            _shooterOrientation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, degrees * MathF.PI / 180);
            _directions[0] = N.Vector3.Normalize(new(0, MathF.Tan(degrees * MathF.PI / 180), -1));
            ulong id = await Fire(0);
            float minimum = float.MaxValue, maximum = 0;
            await Frames(55, _ =>
            {
                var shot = Host.Items.Missiles.FirstOrDefault(p => p.Id == id);
                Check(shot is not null, "Claw survives reasonable native terrain.");
                var ground = query(shot!.Position, shot.Position - N.Vector3.UnitY * 10);
                if (ground is not null) { float clearance = shot.Position.Y - ground.Position.Y; minimum = Math.Min(minimum, clearance); maximum = Math.Max(maximum, clearance); }
            });
            Check(minimum > .1f && maximum < 3.5f, $"Terrain {degrees}: bounded clearance {minimum:F3}..{maximum:F3}.");
            await Capture($"03-terrain-{degrees}", new(30, 120, -30), VehicleBody.ToGodot(Host.Items.Missiles.Single(p => p.Id == id).Position));
            Record(new { scenario = "native-terrain", slopeDegrees = degrees, minimum, maximum });
            await Reset();
        }
    }

    private void Record(object result)
    {
        _evidence.Add(result);
        GD.Print(System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { IncludeFields = true }));
        System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "progress.json"), System.Text.Json.JsonSerializer.Serialize(_evidence, new System.Text.Json.JsonSerializerOptions { IncludeFields = true }));
    }

    private void Configure(Dictionary<string, double> values) => Check(Host.TryConfigure(0, values, out var error), "Configuration accepted: " + error);
    private void Check(bool value, string reason) { if (!value) { throw new InvalidOperationException(reason); } _checks++; }

    private static StaticBody3D AddBox(Node3D arena, Vector3 position, Vector3 size, bool terrain = false)
    {
        var body = new StaticBody3D { Position = position, CollisionLayer = 1 };
        if (terrain) { body.SetMeta("surface_identity", "Asphalt"); }
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = new StandardMaterial3D { AlbedoColor = terrain ? new("4a555c") : new("ac7565") } });
        arena.AddChild(body); return body;
    }

    private async Task Capture(string name, Vector3 position, Vector3 target)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        var camera = new Camera3D { Position = position, Current = true, Far = 3000 };
        _arenas[0].AddChild(camera); camera.LookAt(target);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        _views[0].GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, name + ".png"));
        camera.Position = target + (position - target).Normalized() * 4.9f; camera.LookAt(target);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        _views[0].GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, name + "-close.png"));
        camera.QueueFree();
    }
}
