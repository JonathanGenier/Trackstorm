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

/// <summary>Production UDP arenas exercise deployment, native rigid motion, weapons and late admission.</summary>
public sealed partial class WorldWallChecks : Node
{
    private readonly List<GameNetworkingSocketsTransport> _wires = [];
    private readonly List<NetworkVehicleArena> _arenas = [];
    private readonly List<SubViewport> _views = [];
    private readonly List<string> _evidence = [];
    private readonly Dictionary<ulong, ShieldState[]> _boundaries = new();
    private string _endpoint = "", _output = "";
    private int _frame, _stage, _boundary, _scenario;
    private ShieldState[] _seeds = [];
    private N.Vector3 _pushStart;
    private bool _done;
    private int _stressCount;
    private float _weaponHP;
    private N.Quaternion _beforeImpact;
    private StaticBody3D? _blocker;
    private readonly Dictionary<ulong, N.Vector3> _releasedFacing = new();
    private float? _impactSpeed;
    private N.Vector3 _groundStart;
    private ulong _impactWall, _expiryWall, _expiryTick;
    private bool _sawTipping;
    private bool _sawFalling;
    private bool _sawBreak;
    private float _peakCarRise, _peakCarUpSpeed;
    private N.Vector3 _ramStart, _ramNormal = N.Vector3.UnitY;
    private int _ramCase;
    private ulong RammingVehicle => _ramCase % 2 == 0 ? 2ul : 1ul;
    private bool ProductionMap => OS.GetCmdlineUserArgs().Contains("--world-wall-production");

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _output = ProjectSettings.GlobalizePath(ProductionMap ? "res://.godot/world-wall-production-checks" : "res://.godot/world-wall-checks");
        System.IO.Directory.CreateDirectory(_output);
        using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        _endpoint = $"127.0.0.1:{((IPEndPoint)socket.Client.LocalEndPoint!).Port}"; socket.Close();
        AddPeer(); AddPeer();
        _arenas[0].Driver.ItemsReceived += p =>
        {
            _boundaries[p.World.Tick] = p.Shields.ToArray();
            foreach (var wall in p.Shields.Where(s => !s.Attached && !_releasedFacing.ContainsKey(s.Id)))
            {
                var vehicle = p.World.Vehicles.Single(v => v.State.VehicleId == wall.Owner).State;
                var facing = HorizontalFacing(vehicle.ObservedPhysics.Orientation);
                Check(N.Vector3.Dot(HorizontalFacing(wall.Orientation), facing) > 0.9999f, "Deployment preserves current vehicle heading");
                _releasedFacing[wall.Id] = facing;
            }
        };
    }

    private void AddPeer()
    {
        int index = _arenas.Count;
        var wire = new GameNetworkingSocketsTransport(); ulong server = 0;
        if (index == 0) { wire.Listen(TransportEndpoint.DirectIp(_endpoint)); }
        else { server = wire.Connect(TransportEndpoint.DirectIp(_endpoint)); }
        if (OS.GetCmdlineUserArgs().Contains("--world-wall-impaired")) { wire.ConfigureSimulation(new(30, 5, 2, 0, 0)); }
        _wires.Add(wire);
        var view = new SubViewport { Size = new(1280, 720), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _views.Add(view);
        if (index == 0) { var display = new SubViewportContainer(); AddChild(display); display.AddChild(view); }
        else { AddChild(view); }
        // Keep map pickups from refilling a just-cleared slot during deployment assertions.
        var arena = new NetworkVehicleArena { PrototypeMapForVerification = !ProductionMap, SpawnConfiguration = new() { PickupRadius = 0.01f } };
        arena.Initialize(wire, index == 0 ? 98ul : 0, server); view.AddChild(arena);
        foreach (int slope in new[] { 0, 1 })
        {
            var floor = new StaticBody3D { Position = new(slope * 100, 200, 0), Rotation = new(0, 0, slope * 0.12f), CollisionLayer = 1 };
            floor.AddToGroup("landing_terrain");
            floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(70, 1, 400) } });
            floor.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(70, 1, 400) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new(0.24f, 0.27f, 0.3f) } });
            arena.AddChild(floor);
        }
        var camera = new Camera3D { Name = "WallCamera", Position = new(20, 214, 24) };
        arena.AddChild(camera); camera.LookAt(new(6, 201, 3)); camera.MakeCurrent();
        _arenas.Add(arena);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) { if (++_frame > _boundary + 10) { GetTree().Quit(_stage); } return; }
        _frame++; int elapsed = _frame - _boundary;
        try
        {
            var host = _arenas[0].Driver.Host!;
            foreach (var wall in host.Items.Shields.Where(s => !s.Attached && !s.Tipping))
            {
                Check(N.Vector3.Transform(N.Vector3.UnitY, wall.Orientation).Y > 0.55f, "Wall remains standing throughout native motion and impacts");
                Check(Math.Abs(wall.AngularVelocity.X) < 0.001f && Math.Abs(wall.AngularVelocity.Z) < 0.001f, "No pitch/roll velocity");
            }
            if (_stage is 11 or 6) { AimAtWall(); }
            if (_stage is 12 or 13 && elapsed == 15)
            {
                var target = host.Items.Shields.Single();
                Position(1, target.Position + new N.Vector3(0, 3, _stage == 12 ? 8 : 65), N.Vector3.Zero,
                    _stage == 12 ? N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, -MathF.Atan2(3, 8)) : N.Quaternion.Identity);
            }
            float incomingSpeed = _stage == 4 ? host.World.GetVehicle(2).Movement.Physics.LinearVelocity.Length() : 0;
            for (int i = 0; i < _arenas.Count; i++)
            {
                bool use = ((_stage == 2 || _stage == 18) && elapsed == 1) || (_stage == 14 && elapsed == 1 && i == 0) || (_stage == 6 && i == 0) || (_stage is 8 or 12 or 13 && elapsed == 15 && i == 0);
                bool pressed = use && (elapsed == 1 || (_stage is 8 or 12 or 13 && elapsed == 15));
                ushort throttle = ((_stage == 3 && (!ProductionMap || _scenario < 4))) ? ushort.MaxValue : (ushort)0;
                _arenas[i].Advance(new((ulong)_frame, 0, throttle, 0, use ? InputButtons.UseItem : 0, pressed ? InputButtons.UseItem : 0, 0));
                Check(_arenas[i].Driver.Failure.Length == 0, _arenas[i].Driver.Failure);
            }
            if (_stage == 4 && _impactSpeed is null && host.Items.Shields.Last().HP < 875)
            {
                _impactSpeed = host.World.GetVehicle(2).ObservedPhysics.LinearVelocity.Length();
                Check(_impactSpeed > incomingSpeed * 0.65f && _impactSpeed < incomingSpeed, $"Wall slows the car without stopping it: {incomingSpeed:F2} -> {_impactSpeed:F2} m/s");
                Record($"First wall contact: car {incomingSpeed:F2} -> {_impactSpeed:F2} m/s, ordinary collision damage retained.");
            }
            if (_stage == 4) { ObserveRam(host.World.GetVehicle(2).ObservedPhysics); }
            if (_stage == 6 && elapsed == 15) { Capture("firing.png"); }
            _boundaries[host.World.State.Tick] = host.Items.Shields.ToArray();
            Check(elapsed < 1500, $"Stage {_stage}, scenario {_scenario} timed out");
            switch (_stage)
            {
                case 0 when _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 2):
                    Check(host.TryConfigure(0, new Dictionary<string, double> { ["match.countdown_ticks"] = 1, ["match.minimum_players"] = 1,
                        ["items.machine_gun_spread"] = 0, ["items.machine_gun_fire_rate"] = 60, ["items.machine_gun_damage"] = 30 }, out _), "Configure fixture");
                    Next(1); break;
                case 1 when host.World.State.Match?.Phase == Core.Matches.MatchPhase.Active:
                    Setup(); Next(10); break;
                case 10 when elapsed > 50 && _arenas.All(a => a.Driver.LocalItem is { } inventory &&
                    a.Bodies[a.Driver.LocalVehicleId].CanDeployShield(inventory)):
                    Check(_arenas.All(a => a.Bodies.Values.All(b => b.HasRearShield)), "Selected shields visible before use");
                    foreach (var vehicle in host.World.State.Vehicles)
                    { Check(_arenas[0].Walls.Place(vehicle.ObservedPhysics, host.Items.Configuration, _arenas[0].Bodies[vehicle.VehicleId]) is not null, $"Rear placement available at {vehicle.ObservedPhysics}"); }
                    if (_scenario is 1 or 2)
                    {
                        foreach (var v in host.World.State.Vehicles)
                        { Position(v.VehicleId, v.ObservedPhysics.Position, N.Vector3.Transform(new(0, 0, _scenario == 1 ? -60 : 3), v.ObservedPhysics.Orientation), v.ObservedPhysics.Orientation); }
                    }
                    if (_scenario == 0)
                    {
                        var pose = host.World.GetVehicle(1).ObservedPhysics;
                        var back = Vehicles.VehicleBody.ToGodot(N.Vector3.Transform(N.Vector3.UnitZ, pose.Orientation)); back.Y = 0; back = back.Normalized();
                        _blocker = new StaticBody3D { CollisionLayer = 1, Position = Vehicles.VehicleBody.ToGodot(pose.Position) + back * 4.4f + Vector3.Up * 2 };
                        _blocker.Basis = new(Vector3.Up.Cross(back), Vector3.Up, back);
                        _blocker.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(8, 6, 1) } });
                        _arenas[0].AddChild(_blocker); Next(14);
                    }
                    else { Next(2); }
                    break;
                case 14 when elapsed > 30:
                    Check(host.Items.Shields.SequenceEqual(_seeds), "Blocked normal use preserves exact identity, HP, slot capability and attached state");
                    Check(_arenas[0].Walls.Bodies.Count == 0, "Blocked use creates no world collider");
                    _blocker!.CollisionLayer = 0; _blocker.QueueFree(); _blocker = null;
                    Record("Blocked normal use retained the shield; retry after clearing the obstruction uses the same capability.");
                    Next(2); break;
                case 2 when elapsed > 50:
                    Check(host.Items.Shields.Count(s => !s.Attached) == 2, "One ordinary use per player deploys exactly two walls");
                    foreach (var seed in _seeds)
                    {
                        var wall = host.Items.Shields.Single(s => s.Id == seed.Id);
                        Check(wall.HP == 875 && wall.DamageSequence == 7, "Same damaged pool and watermark");
                        Check(host.Items.Slots.Single(s => s.Vehicle == seed.Owner).Active.Item == HeldItem.None, "Exact slot clears");
                        Check(!host.Items.RequestUse(host.World, seed.Owner, seed.Life, seed.Token), "Retired use capability rejects replay");
                        if (_scenario is 0 or 3)
                        { Check(N.Vector3.Dot(_releasedFacing[wall.Id], HorizontalFacing(wall.Orientation)) > 0.999f, "No spontaneous deployment yaw during settling"); }
                        if (!ProductionMap && _scenario == 3)
                        {
                            var normal = new N.Vector3(-MathF.Sin(0.12f), MathF.Cos(0.12f), 0);
                            Check(N.Vector3.Dot(normal, N.Vector3.Transform(N.Vector3.UnitY, wall.Orientation)) > 0.9999f, "Both ends follow the bank rather than one end floating");
                        }
                    }
                    Check(_arenas.All(a => a.Walls.Bodies.Count == 2 && a.Bodies.Values.All(b => !b.HasRearShield)), "Native shield-to-world transition on both peers");
                    _seeds = host.Items.Shields.ToArray(); Capture($"{_scenario}-expanded.png"); Next(3); break;
                case 3 when elapsed >= 90:
                    // Straight-line driving is valid on the grid/platforms. Infield pickup
                    // areas include walls/water and do not imply a drivable forward corridor.
                    if (!ProductionMap || _scenario < 4)
                    {
                        foreach (var wall in host.Items.Shields)
                        { Check(N.Vector3.Distance(wall.Position, host.World.GetVehicle(wall.Owner).ObservedPhysics.Position) > 8, "Drive away leaves independent wall"); }
                    }
                    Record($"Scenario {_scenario}: {(ProductionMap ? "production map" : "flat/banked fixture")} deployment keeps identity, 875 HP, exact slot, independent upright native walls.");
                    if (ProductionMap && _scenario == 7)
                    {
                        VerifyBoundaries(); System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
                        GD.Print("World wall integration passed: sixteen one-use deployments on the production oval/infield with upright motion and exact UDP convergence.");
                        Finish(0); break;
                    }
                    if (_scenario++ < (ProductionMap ? 7 : 3))
                    {
                        foreach (var wall in host.Items.Shields.ToArray()) { host.Items.DamageShield(host.World, wall.Id, wall.DamageSequence + 1, 1000, new("world", 0, "fixture-reset")); }
                        Next(1); break;
                    }
                    var sliding = host.Items.Shields.Last(); _groundStart = sliding.Position;
                    _arenas[0].Walls.Bodies[sliding.Id].ApplyCentralImpulse(new(-sliding.WallMass * 5, 0, 0));
                    Next(15); break;
                case 15 when elapsed >= 60:
                    var grounded = host.Items.Shields.Last();
                    Check(grounded.Position.X < _groundStart.X - 1 && grounded.Position.Y < _groundStart.Y - 0.1f, "Sliding wall follows changing ground elevation");
                    Check(N.Vector3.Dot(new(-MathF.Sin(0.12f), MathF.Cos(0.12f), 0), N.Vector3.Transform(N.Vector3.UnitY, grounded.Orientation)) > 0.9999f, "Sliding wall stays aligned to the bank");
                    Record($"Bank slide follows elevation: {_groundStart.Y:F2} -> {grounded.Position.Y:F2} m; both ends remain terrain-aligned.");
                    RestWall();
                    _groundStart = grounded.Position;
                    _arenas[0].Walls.Bodies[grounded.Id].ApplyCentralImpulse(new(grounded.WallMass * 30, 0, 0));
                    Next(16); break;
                case 16:
                    var uphill = host.Items.Shields.Last();
                    float expectedHeight = _groundStart.Y + (uphill.Position.X - _groundStart.X) * MathF.Tan(0.12f);
                    Check(Math.Abs(uphill.Position.Y - expectedHeight) < 0.03f, "Fast uphill slide follows support rather than being classified as airborne");
                    if (elapsed < 30) { break; }
                    Check(uphill.Position.Y > _groundStart.Y + 1, "Fast slide gains terrain elevation");
                    Record($"Fast uphill slide follows elevation: {_groundStart.Y:F2} -> {uphill.Position.Y:F2} m.");
                    RestWall();
                    Position(2, uphill.Position + new N.Vector3(2, 0, 8), new(0, 0, -18));
                    _ramStart = host.World.GetVehicle(2).ObservedPhysics.Position;
                    _ramNormal = new(-MathF.Sin(0.12f), MathF.Cos(0.12f), 0);
                    _beforeImpact = uphill.Orientation;
                    _pushStart = host.Items.Shields.Last().Position; Next(4); break;
                case 4 when elapsed >= 90:
                    var pushed = host.Items.Shields.Last();
                    Check(N.Vector3.Distance(pushed.Position, _pushStart) > 0.25f, $"Other vehicle physically moves wall: start {_pushStart}, wall {pushed.Position}, vehicle {host.World.GetVehicle(2).ObservedPhysics.Position}, HP {pushed.HP}, frozen {_arenas[0].Walls.Bodies[pushed.Id].Freeze}");
                    Check(pushed.HP < 875, "Native wall contact damages its independent pool");
                    Check(Math.Abs(N.Quaternion.Dot(pushed.Orientation, _beforeImpact)) < 0.999f, "An off-centre vehicle hit rotates the wall without an artificial torque impulse");
                    Record($"Off-centre vehicle hit moved wall {N.Vector3.Distance(pushed.Position, _pushStart):F2} m; heading changed {2 * MathF.Acos(Math.Clamp(Math.Abs(N.Quaternion.Dot(pushed.Orientation, _beforeImpact)), 0, 1)) * 180 / MathF.PI:F1} degrees; HP {pushed.HP:F1}.");
                    Record($"Banked off-centre car peak support-relative rise {_peakCarRise:F2} m; upward speed {_peakCarUpSpeed:F2} m/s.");
                    Check(_peakCarRise < 0.75f && _peakCarUpSpeed < 3, "Ordinary wall contact must not launch the car off the bank");
                    Position(2, new(120, 204, 60), N.Vector3.Zero);
                    Capture("pushed.png"); AddPeer();
                    RestWall(flatWeaponTarget: true);
                    _groundStart = _arenas[0].Walls.Bodies[pushed.Id].Observe().Position;
                    _arenas[0].Walls.Bodies[pushed.Id].ApplyCentralImpulse(new(0, 0, -pushed.WallMass * 8));
                    Next(20); break;
                case 20 when elapsed >= 180:
                    var coasted = host.Items.Shields.Last();
                    float coastDistance = N.Vector3.Distance(coasted.Position, _groundStart);
                    Record($"Unpowered wall coast from 8 m/s: {coastDistance:F2} m travel; {coasted.LinearVelocity.Length():F2} m/s after three seconds.");
                    Check(coastDistance > 0.5f && coastDistance < 5 && coasted.LinearVelocity.Length() < 0.5f,
                        "Wall yields to a push but settles without prolonged sliding");
                    Next(5); break;
                case 5 when elapsed > 120 && _arenas.All(a => a.Driver.Latest?.Vehicles.Count == 3):
                    Check(_arenas.All(a => a.Walls.Bodies.Count == 2), "Fresh third peer reconstructs both native walls");
                    VerifyBoundaries();
                    Check(host.Items.Grant(host.World, 1, HeldItem.MachineGun), "Grant shooter weapon");
                    Next(11); break;
                case 11 when elapsed > 180:
                    Next(6); break;
                case 6 when elapsed >= 75:
                    Check(host.Items.Shields.All(s => s.Id != _seeds[0].Id), "Sustained ordinary fire destroys only targeted wall");
                    Check(host.Items.Shields.Count == 1, "Second wall survives independently");
                    Record("Late join restored both walls; sustained native Machine Gun fire destroyed the targeted pool and removed its collider.");
                    Capture("destroyed.png"); Next(7); break;
                case 7 when elapsed > 300:
                    VerifyBoundaries();
                    Check(_arenas.All(a => a.Walls.Bodies.Count == 1), "Terminal removal converges and stays removed");
                    Record("Three UDP peers retained the surviving movable wall through 300 further native physics frames.");
                    host.Items.RemovePlayer(1);
                    RestWall(flatWeaponTarget: true);
                    _weaponHP = host.Items.Shields.Single().HP;
                    Check(host.Items.Grant(host.World, 1, HeldItem.Missile), "Native missile grant"); Next(12); break;
                case 12 when elapsed > 90:
                    Check(host.Items.Shields.Single().HP < _weaponHP, "Native Missile sweep/blast damages wall");
                    Record($"Native Missile wall damage: {_weaponHP:F1} -> {host.Items.Shields.Single().HP:F1} HP.");
                    host.Items.RemovePlayer(1); _weaponHP = host.Items.Shields.Single().HP; RestWall(flatWeaponTarget: true);
                    Check(host.Items.Grant(host.World, 1, HeldItem.Salvo), "Native Salvo grant"); Next(13); break;
                case 13 when elapsed > 120:
                    Check(host.Items.Shields.Single().HP < _weaponHP, "Native Salvo sweep/blast damages wall");
                    Record($"Native Salvo wall damage: {_weaponHP:F1} -> {host.Items.Shields.Single().HP:F1} HP.");
                    host.Items.RemovePlayer(1); StressGrant(); break;
                case 8 when elapsed >= 30 && host.Items.Shields.Count(s => !s.Attached) >= _stressCount + 1:
                    Check(host.Items.Shields.Count(s => !s.Attached) == _stressCount + 1, "Stress use commits one independent wall");
                    if (_stressCount < 15) { StressGrant(); }
                    else { Next(9); }
                    break;
                case 9 when elapsed >= 600:
                    Check(host.Items.Shields.Count == 16 && _arenas.All(a => a.Walls.Bodies.Count == 16), "Sixteen live native walls remain bounded and replicated");
                    Check(host.Items.Shields.All(s => VehiclePhysicsState.IsFinite(s.Position) && VehiclePhysicsState.IsFinite(s.LinearVelocity)), "Finite sustained wall state");
                    VerifyBoundaries(); Capture("sixteen-walls.png");
                    Record("Sixteen walls deployed via ordinary use, then sustained for 600 physics frames across three UDP arenas.");
                    BeginRam(); break;
                case 17:
                    var struckCar = host.World.GetVehicle(RammingVehicle).ObservedPhysics;
                    ObserveRam(struckCar);
                    var tipping = host.Items.Shields.FirstOrDefault(s => s.Id == _impactWall);
                    if (tipping is { Tipping: true } && !_sawTipping)
                    {
                        _sawTipping = true; Capture($"tipping-{_ramCase}.png");
                        Record($"Hard native vehicle hit released tipping at {tipping.HP:F1} HP; angular speed {tipping.AngularVelocity.Length():F2} rad/s.");
                    }
                    if (tipping is { Tipping: true } && !_sawFalling && N.Vector3.Transform(N.Vector3.UnitY, tipping.Orientation).Y < 0.75f)
                    { _sawFalling = true; Capture($"falling-{_ramCase}.png"); }
                    if (tipping is not null) { break; }
                    Check(_sawTipping && _sawFalling, "Wall must visibly enter tipping before its ground break");
                    if (!_sawBreak)
                    {
                        Check(elapsed < 240, "Hard impact topples and breaks promptly");
                        _sawBreak = true;
                        Record("Tipped wall broke on native side/ground contact and left the live set.");
                    }
                    if (elapsed < 180) { break; }
                    Record($"Hard impact {_ramCase}, vehicle {RammingVehicle}: car peak rise {_peakCarRise:F2} m; peak upward speed {_peakCarUpSpeed:F2} m/s over three seconds.");
                    Check(_peakCarRise < 0.75f && _peakCarUpSpeed < 3, "Ramming the wall must not launch the car");
                    Check(struckCar.Position.Z < -65, "Car continues through the collapsed wall");
                    Check(_arenas.All(a => !a.Walls.Bodies.ContainsKey(_impactWall)), "Toppled wall collider removed on all peers");
                    Capture($"toppled-removed-{_ramCase}.png");
                    Position(RammingVehicle, new(120, 204, 60), N.Vector3.Zero);
                    if (++_ramCase < 5) { BeginRam(); break; }
                    Check(host.TryConfigure(0, new Dictionary<string, double> { ["items.shield_lifetime"] = 2 }, out _), "Configure short native expiry fixture");
                    Position(1, new(0, 201.65f, 0), N.Vector3.Zero);
                    Check(host.Items.Grant(host.World, 1, HeldItem.Shield), "Expiry fixture grant");
                    _expiryWall = host.Items.Shields.Last().Id; Next(18); break;
                case 18:
                    var expiring = host.Items.Shields.Single(s => s.Id == _expiryWall);
                    if (expiring.Attached) { break; }
                    _expiryTick = expiring.ExpiresAtTick;
                    Check(_expiryTick == host.World.State.Tick + 120, "Lifetime starts on deployment");
                    Check(host.TryConfigure(0, new Dictionary<string, double> { ["items.shield_lifetime"] = 120 }, out _), "Retuning cannot restart captured expiry");
                    Next(19); break;
                case 19:
                    Check(host.Items.Shields.Any(s => s.Id == _expiryWall) == (host.World.State.Tick < _expiryTick), "Expiry removes the wall at its exact captured deadline");
                    if (host.World.State.Tick < _expiryTick + 60) { break; }
                    Check(_arenas.All(a => !a.Walls.Bodies.ContainsKey(_expiryWall) && !a.Walls.Bodies.ContainsKey(_impactWall)), "Break and expiry remove every peer collider");
                    VerifyBoundaries();
                    Record("Configured native expiry fired at its exact captured deadline despite retuning; all three peer colliders removed.");
                    System.IO.File.WriteAllLines(System.IO.Path.Combine(_output, "evidence.txt"), _evidence);
                    GD.Print("World wall integration passed: native contact spin, tipping/breakage, expiry, deployment, motion, weapons, late join and UDP convergence.");
                    Finish(0); break;
            }
        }
        catch (Exception error) { GD.PrintErr(error); Finish(1); }
    }

    private void Setup()
    {
        var host = _arenas[0].Driver.Host!;
        for (ulong id = 1; id <= 2; id++)
        {
            host.Items.RemovePlayer(id);
            Check(host.Items.Grant(host.World, id, HeldItem.Shield), "Ordinary Shield grant");
            var shield = host.Items.Shields.Last(); host.Items.DamageShield(host.World, shield.Id, 7, 125, new("world", 0, "fixture"));
            float x = (_scenario == 3 ? 100 : 0) + (id == 1 ? 0 : 12);
            Position(id, new(x, 201.65f + (_scenario == 3 ? (x - 100) * 0.12f : 0), 0), new(0, 0, _scenario == 1 ? -6 : _scenario == 2 ? 3 : 0));
            if (ProductionMap)
            {
                var markers = _arenas[0].MapConfiguration.Items.Where(s => s.Id.StartsWith("item-infield-", StringComparison.Ordinal)).ToArray();
                var spawn = _scenario < 4 ? _arenas[0].MapConfiguration.Players[_scenario * 2 + (int)id - 1] : markers[((_scenario - 4) * 2 + (int)id - 1) % markers.Length];
                Position(id, spawn.Position + (_scenario < 4 ? N.Vector3.Zero : N.Vector3.UnitY * VehicleDimensions.RideHeight), N.Vector3.Zero, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, spawn.Yaw));
            }
        }
        _seeds = host.Items.Shields.ToArray();
        var camera = _arenas[0].GetNode<Camera3D>("WallCamera");
        camera.Position = new((_scenario == 3 ? 100 : 0) + 20, 214, 24); camera.LookAt(new((_scenario == 3 ? 100 : 0) + 6, 201, 3));
        if (ProductionMap)
        {
            var point = Vehicles.VehicleBody.ToGodot(host.World.GetVehicle(1).ObservedPhysics.Position);
            camera.Position = point + new Vector3(20, 14, 24); camera.LookAt(point);
        }
    }

    private void BeginRam()
    {
        var host = _arenas[0].Driver.Host!;
        _impactWall = host.Items.Shields.Last().Id;
        RestWall(flatWeaponTarget: true);
        _sawTipping = _sawFalling = _sawBreak = false;
        _peakCarRise = _peakCarUpSpeed = 0;
        bool banked = _ramCase == 4;
        _ramNormal = banked ? new(-MathF.Sin(0.12f), MathF.Cos(0.12f), 0) : N.Vector3.UnitY;
        _ramStart = new(banked ? 100 : _ramCase == 1 ? 2 : 0, 201.7f, -50);
        if (banked)
        { _arenas[0].Walls.Bodies[_impactWall].GlobalTransform = new(new Basis(Vector3.Back, 0.12f), new(100, 201.765f, -60)); }
        Position(RammingVehicle, _ramStart, new(0, 0, _ramCase == 2 ? -60 : -40),
            banked ? N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, 0.12f) :
            _ramCase == 3 ? N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, MathF.PI) : N.Quaternion.Identity);
        var camera = _arenas[0].GetNode<Camera3D>("WallCamera");
        camera.Position = new((banked ? 100 : 0) + 14, 208, -47); camera.LookAt(new(banked ? 100 : 0, 202, -60));
        Next(17);
    }

    private void ObserveRam(VehiclePhysicsState car)
    {
        _peakCarRise = Math.Max(_peakCarRise, N.Vector3.Dot(car.Position - _ramStart, _ramNormal));
        _peakCarUpSpeed = Math.Max(_peakCarUpSpeed, N.Vector3.Dot(car.LinearVelocity, _ramNormal));
    }

    private void Position(ulong id, N.Vector3 point, N.Vector3 velocity, N.Quaternion? heading = null)
    {
        var host = _arenas[0].Driver.Host!; var world = host.World.State;
        var pose = new VehiclePhysicsState(point, heading ?? N.Quaternion.Identity, velocity, N.Vector3.Zero);
        _arenas[0].Bodies[id].Apply(pose);
        host.World.Restore(new(world.Tick, world.LastInput, world.Vehicles.Select(v => v.VehicleId != id ? v :
            new VehicleSnapshot(v.VehicleId, v.LifeId, new VehicleState(world.Tick, pose, true, false, 0, 0), v.Damage, pose)), world.Match));
    }

    private void StressGrant()
    {
        var host = _arenas[0].Driver.Host!;
        int index = _stressCount++;
        if (index == 0)
        {
            var camera = _arenas[0].GetNode<Camera3D>("WallCamera");
            camera.Position = new(35, 235, 28); camera.LookAt(new(0, 201, -24));
        }
        Position(1, new(-24 + (index % 5) * 11, 201.65f, -20 - (index / 5) * 10), N.Vector3.Zero);
        Check(host.Items.Grant(host.World, 1, HeldItem.Shield), "Stress grant respects live bound");
        Next(8);
    }

    private void AimAtWall()
    {
        var wall = _arenas[0].Driver.Host!.Items.Shields.FirstOrDefault(s => s.Id == _seeds[0].Id);
        if (wall is not null)
        {
            var position = wall.Position + new N.Vector3(0, 0, 9);
            Position(1, position, N.Vector3.Zero);
            _arenas[0].Driver.DesiredAim = N.Vector3.Normalize(wall.Position + N.Vector3.UnitY - (position + WeaponAim.Pivot));
        }
    }
    private void VerifyBoundaries()
    {
        foreach (var arena in _arenas.Skip(1))
        {
            var p = arena.Driver.ItemState!;
            Check(_boundaries.TryGetValue(p.World.Tick, out var expected) && p.Shields.SequenceEqual(expected), "Accepted peer wall publication equals exact committed host boundary");
        }
    }
    private void Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        using var frame = _views[0].GetTexture().GetImage(); frame.SavePng(System.IO.Path.Combine(_output, name));
    }
    private void Record(string message) { _evidence.Add(message); GD.Print(message); }
    private void RestWall(bool flatWeaponTarget = false)
    {
        // Isolate later weapon aiming from the preceding movement test's travelling target.
        var body = _arenas[0].Walls.Bodies[_arenas[0].Driver.Host!.Items.Shields.Last().Id];
        if (flatWeaponTarget) { body.GlobalTransform = new(Basis.Identity, new(0, 201.765f, -60)); }
        body.LinearVelocity = Vector3.Zero; body.AngularVelocity = Vector3.Zero; body.Sleeping = true;
    }
    private static N.Vector3 HorizontalFacing(N.Quaternion rotation)
    {
        var facing = N.Vector3.Transform(N.Vector3.UnitZ, rotation); facing.Y = 0;
        return N.Vector3.Normalize(facing);
    }
    private void Next(int stage) { _stage = stage; _boundary = _frame; }
    private void Finish(int code)
    {
        _done = true;
        foreach (var arena in _arenas) { arena.QueueFree(); }
        foreach (var wire in _wires) { wire.Dispose(); }
        _stage = code; _boundary = _frame;
    }
    private static void Check(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
}
