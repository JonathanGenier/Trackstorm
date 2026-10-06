using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Production-adapter driving, landing and momentum fixtures with per-tick evidence.</summary>
public sealed partial class TrophyTruckChecks : Node3D
{
    private Core.Simulation.Simulation _world = null!;
    private readonly Dictionary<ulong, VehicleBody> _native = new();
    private readonly Dictionary<ulong, NetworkVehicleBody> _network = new();
    private Func<ulong, ulong, InputFrame> _pilot = (tick, _) => new(tick, 0, 0, 0, 0, 0, 0);
    private readonly List<object> _trace = new();
    private readonly List<VehicleState> _movementTrace = new();
    private Node3D _fixture = null!;
    private Node3D? _map;
    private bool _running;
    private string _case = "";
    private string _output = "";
    private int _assertions;
    private float _yawTravel;
    private int _pairContactTicks;
    private bool _chassisContact;
    private float _rebound;
    private float _maximumCompression;
    private float _minimumHeight;
    private float _minimumUp = 1;
    private readonly List<VehicleEffectRequest> _effects = new();
    private readonly List<Resource> _fixtureResources = new();
    private readonly MatchResourceLoader _resources = new(Core.Sessions.MatchMap.OldMap);

    public override void _Ready() => CallDeferred(MethodName.Run);

    public override void _PhysicsProcess(double delta)
    {
        if (!_running) { return; }
        ulong tick = _world.State.Tick + 1;
        var observations = _network.Count > 0 ? NetworkVehicleBody.ObserveBatch(_network, _world.State.Vehicles) : null;
        var requests = _world.State.Vehicles.Select(s => _native.TryGetValue(s.VehicleId, out var body)
            ? body.Capture(_pilot(tick, s.VehicleId))
            : new VehicleStepRequest(s.VehicleId, _pilot(tick, s.VehicleId), observations![s.VehicleId], effects: s.VehicleId == 1 ? _effects : null)).ToArray();
        _effects.Clear();
        var results = _world.Step(_pilot(tick, 1), requests);
        foreach (var result in results)
        {
            var s = result.Snapshot;
            if (_native.TryGetValue(s.VehicleId, out var body)) { body.Apply(result); }
            else { _network[s.VehicleId].Apply(s.Movement.Physics); }
            var p = s.ObservedPhysics;
            var w = s.Movement.Wheels.Compression;
            var up = N.Vector3.Transform(N.Vector3.UnitY, p.Orientation);
            if (s.VehicleId == 1)
            {
                _movementTrace.Add(s.Movement);
                if (requests.Single(r => r.VehicleId == 1).Observation.Contacts.Any(c => c.OtherVehicleId != 0)) { _pairContactTicks++; }
                _minimumUp = Math.Min(_minimumUp, up.Y);
                _yawTravel += p.AngularVelocity.Y / 60;
                _maximumCompression = Math.Max(_maximumCompression, Math.Max(Math.Max(w.X, w.Y), Math.Max(w.Z, w.W)));
                _minimumHeight = Math.Min(_minimumHeight, p.Position.Y);
                _chassisContact |= requests.Single(r => r.VehicleId == 1).Observation.Contacts.Count > 0;
                if (_chassisContact) { _rebound = Math.Max(_rebound, p.LinearVelocity.Y); }
            }
            _trace.Add(new { tick, id = s.VehicleId, position = new[] { p.Position.X, p.Position.Y, p.Position.Z },
                velocity = new[] { p.LinearVelocity.X, p.LinearVelocity.Y, p.LinearVelocity.Z },
                angular = new[] { p.AngularVelocity.X, p.AngularVelocity.Y, p.AngularVelocity.Z }, up = up.Y,
                compression = new[] { w.X, w.Y, w.Z, w.W }, s.Movement.Grounded, s.Movement.Handbrake,
                s.Movement.SteeringAngle, s.Movement.CrashSeconds, airSeconds = s.Movement.Air.Seconds, airInput = new[] { s.Movement.Air.Input.X, s.Movement.Air.Input.Y, s.Movement.Air.Input.Z }, hp = s.Damage.CurrentHP, contacts = requests.Single(r => r.VehicleId == s.VehicleId).Observation.Contacts.Count });
        }
    }

    private async void Run()
    {
        try
        {
            // Keep production match resources alive across the fixture's many vehicle rebuilds,
            // just as the application match loader does for a running match.
            while (!_resources.Complete)
            {
                _resources.Advance();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            // Keep tire shader wrappers alive across repeated fixture destruction/reload.
            // They are normally retained by live match vehicles; this harness removes all of them.
            _fixtureResources.Add(MatchResourceLoader.LoadResource<Shader>("res://assets/effects/WaterWake.gdshader"));
            _fixtureResources.Add(MatchResourceLoader.LoadResource<Shader>("res://assets/effects/TireTrack.gdshader"));
            _output = ProjectSettings.GlobalizePath("res://.godot/ts-197/trophy");
            System.IO.Directory.CreateDirectory(_output);
            if (OS.GetCmdlineUserArgs().Contains("--trophy-rwd"))
            {
                foreach (bool network in new[] { false, true }) { await RearDrive(network); }
                GD.Print($"RWD handling checks passed: {_assertions} assertions.");
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--trophy-contact"))
            {
                foreach (bool network in new[] { false, true }) { await HighSpeedContact(network); }
                GD.Print($"Trophy contact diagnostics passed: {_assertions} assertions.");
                GetTree().Quit();
                return;
            }
            if (OS.GetCmdlineUserArgs().Contains("--trophy-grass"))
            {
                foreach (bool network in new[] { false, true }) { await GrassTransitions(network); }
                GD.Print($"Grass transition checks passed: {_assertions} assertions.");
                GetTree().Quit();
                return;
            }
            foreach (bool network in new[] { false, true })
            {
                if (!OS.GetCmdlineUserArgs().Contains("--trophy-dynamics"))
                {
                    await Braking(network);
                    await TerrainTransitions(network);
                    await GrassTransitions(network);
                }
                if (OS.GetCmdlineUserArgs().Contains("--trophy-polish")) { continue; }
                await Driving(network);
                await RearDrive(network);
                await RepeatedContacts(network);
                await PowerCorners(network);
                await TightTurns(network);
                await HardLandings(network);
                await AwkwardCrashes(network);
                await RepeatedRolls(network);
                foreach (var attitude in new[] { ("wheels", N.Quaternion.Identity), ("two-wheel-roll", N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, 1)), ("two-wheel-pitch", N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, 0.65f)), ("side", N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, MathF.PI / 2)), ("roof", N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitZ, MathF.PI)), ("bumper", N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, 1.3f)), ("trunk", N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, -1.3f)) })
                {
                    await Setup(network, attitude.Item1, new(0, 5.5f, 0), attitude.Item2);
                    await Frames(600);
                    var final = _world.GetVehicle(1).ObservedPhysics;
                    Check(VehiclePhysicsState.IsFinite(final.LinearVelocity) && final.LinearVelocity.Length() < 8, _case + " settles with bounded motion");
                    if (attitude.Item1 == "wheels")
                    {
                        Check(_world.GetVehicle(1).Movement.Grounded && N.Vector3.Transform(N.Vector3.UnitY, final.Orientation).Y > 0.95f, _case + " returns to level wheel support");
                        Check(Math.Abs(final.Position.Y - VehicleDimensions.RideHeight) < 0.08f, _case + " returns to ride height");
                    }
                    if (attitude.Item1 is "roof" or "side" or "two-wheel-roll" or "two-wheel-pitch") { Check(N.Vector3.Transform(N.Vector3.UnitY, final.Orientation).Y > 0.9f, _case + " gradually recovers after the crash"); }
                    // Momentum-driven rolling may briefly hop as the body pivots.
                    // 3.5 m/s bounds the ballistic rise to 0.56 m at production gravity.
                    Check(_rebound < 3.5f, _case + " bounds the physical contact hop below a launch");
                    GD.Print($"{_case}: peak upward velocity after chassis contact={_rebound:F3}");
                    await Finish();
                }
                float equalMassTravel = 0;
                foreach (var impact in new[] { ("rear-low", 8f, 0f, 3000f, 0f), ("rear-medium", 20f, 0f, 3000f, 0f), ("rear-fast", 30f, 0f, 3000f, 0f), ("head-on", 20f, -20f, 3000f, 0f), ("heavy-rear", 20f, 0f, 1500f, 0f), ("glance", 20f, 0f, 3000f, 1.5f) })
                {
                    await Setup(network, impact.Item1, new(0, VehicleDimensions.RideHeight, 4), N.Quaternion.Identity, new(0, 0, -impact.Item2));
                    AddCar(network, 2, new(impact.Item5, VehicleDimensions.RideHeight, -4), N.Quaternion.Identity, new(0, 0, -impact.Item3), new() { Mass = impact.Item4 });
                    await Frames(150);
                    var target = _world.GetVehicle(2).ObservedPhysics;
                    var source = _world.GetVehicle(1).ObservedPhysics;
                    Check(source.LinearVelocity.Length() < impact.Item2 + 2, _case + " does not add rebound speed");
                    if (impact.Item3 == 0) { Check(target.Position.Z < -4.1f, _case + " displaces the target"); }
                    if (impact.Item1 == "head-on") { Check(Math.Abs(source.LinearVelocity.Z) < 3 && Math.Abs(target.LinearVelocity.Z) < 3, _case + " consumes opposed momentum"); }
                    if (impact.Item1 == "rear-medium") { equalMassTravel = -target.Position.Z - 4; }
                    if (impact.Item1 == "heavy-rear") { Check(-target.Position.Z - 4 > equalMassTravel * 1.1f, _case + " pushes a lighter target farther at the same impact speed"); }
                    await Finish();
                }
            }
            GD.Print($"Trophy truck integration passed: {_assertions} assertions, both production adapters.");
            GetTree().Quit();
        }
        catch (Exception error)
        {
            _running = false;
            Save();
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    private async Task Braking(bool network)
    {
        foreach (int kmh in new[] { 30, 60, 100, 150 })
        foreach (bool turn in new[] { false, true })
        {
            await Setup(network, $"brake-{kmh}-{turn}", new(0, VehicleDimensions.RideHeight, 0), N.Quaternion.Identity, new(0, 0, -kmh / 3.6f));
            bool stoppedForward = false;
            _pilot = (tick, _) =>
            {
                var body = _world.GetVehicle(1).ObservedPhysics;
                float forwardSpeed = N.Vector3.Dot(body.LinearVelocity, N.Vector3.Transform(-N.Vector3.UnitZ, body.Orientation));
                // Brake and reverse share a pedal. Release after forward motion stops,
                // allowing residual lateral slip to settle without commanding reverse.
                stoppedForward |= forwardSpeed < 0.5f;
                return new(tick, turn && !stoppedForward ? (short)16000 : (short)0, 0, stoppedForward ? (ushort)0 : ushort.MaxValue, 0, 0, 0);
            };
            int frames = 0;
            while (_world.GetVehicle(1).Speed > 0.5f && frames++ < 300) { await Frames(1); }
            var state = _world.GetVehicle(1);
            GD.Print($"{_case}: stop={frames / 60f:F3}s distance={state.ObservedPhysics.Position.Length():F3}m up={N.Vector3.Transform(N.Vector3.UnitY, state.ObservedPhysics.Orientation).Y:F3}");
            Check(frames < 300 && N.Vector3.Transform(N.Vector3.UnitY, state.ObservedPhysics.Orientation).Y > 0.9f, _case + " brakes to rest without rollover");
            await Finish();
        }
    }

    private async Task TerrainTransitions(bool network)
    {
        foreach (var route in new[] {
            ("west", new N.Vector3(-115, VehicleDimensions.RideHeight, 0), -MathF.PI / 2),
            ("east", new N.Vector3(115, VehicleDimensions.RideHeight, 0), MathF.PI / 2),
            ("side", new N.Vector3(30, VehicleDimensions.RideHeight, 40), 0f),
            ("oblique", new N.Vector3(25, VehicleDimensions.RideHeight, 40), -0.4f),
            ("downhill", new N.Vector3(30, 6.35f + VehicleDimensions.RideHeight, 0), MathF.PI) })
        foreach (float speed in new[] { 18f, 30f, -11f })
        {
            float yaw = route.Item3 + (speed < 0 ? MathF.PI : 0);
            var orientation = N.Quaternion.CreateFromYawPitchRoll(yaw, 0, 0);
            await Setup(network, $"terrain-{route.Item1}-{speed}", route.Item2, orientation,
                N.Vector3.Transform(new(0, 0, -speed), orientation), productionMap: true);
            // Coast across the authored transition so the test does not replace the player's
            // requested entry speed with a path-following controller's automatic brake.
            for (int frame = 0; frame < 300; frame++)
            {
                await Frames(1);
                if (route.Item1 == "downhill" && _world.GetVehicle(1).ObservedPhysics.Position.Z > 42 && _world.GetVehicle(1).Movement.Grounded) { break; }
            }
            GD.Print($"{_case}: compression={_maximumCompression:F3} chassis={_chassisContact} rebound={_rebound:F3}");
            Check(VehiclePhysicsState.IsFinite(_world.GetVehicle(1).ObservedPhysics.LinearVelocity), _case + " stays finite");
            if (!OS.GetCmdlineUserArgs().Contains("--trophy-baseline"))
            {
                Check(_maximumCompression < 0.85f && !_chassisContact, _case + " retains suspension travel without chassis impact");
            }
            await Finish();
        }
        _map!.QueueFree(); _map = null;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Driving(bool network)
    {
        await Setup(network, "driving", new(0, VehicleDimensions.RideHeight, 0), N.Quaternion.Identity);
        _pilot = (tick, _) => new(tick, 0, ushort.MaxValue, 0, 0, 0, 0);
        await Frames(120);
        Check(_world.GetVehicle(1).Speed is > 10 and < 27.78f, _case + " builds through intermediate dirt speed instead of reaching 100 km/h immediately");
        await Frames(120);
        Check(_world.GetVehicle(1).Speed > 27.78f, _case + " retains powerful acceleration past 100 km/h within four seconds on dirt");
        _pilot = (tick, _) => new(tick, 18000, 30000, 0, 0, 0, 0);
        await Frames(90);
        Check(_world.GetVehicle(1).Movement.Physics.AngularVelocity.Y < -0.15f, _case + " responds to fast right steering");
        float entrySpeed = _world.GetVehicle(1).Speed;
        _pilot = (tick, _) => new(tick, -24000, 0, 0, InputButtons.Drift, 0, 0);
        await Frames(60);
        var reversed = _world.GetVehicle(1);
        // With progressive steering and no engine demand, rear braking may stop the
        // car before full opposite lock. Do not require free yaw energy at rest.
        GD.Print($"{_case}: counter wheel={reversed.Movement.SteeringAngle:F3}, entry speed={entrySpeed:F3}, exit speed={reversed.Speed:F3}");
        Check(reversed.Movement.SteeringAngle < -0.1f && reversed.Speed < entrySpeed,
            _case + " opposite wheel input remains available as rear braking consumes momentum");
        await Frames(60);
        GD.Print($"{_case}: two-second dirt rear-lock speed={_world.GetVehicle(1).Speed:F3}, yaw={_world.GetVehicle(1).Movement.Physics.AngularVelocity.Y:F3}");
        // Dirt deliberately carries a longer moving slide than Asphalt. Wait for the
        // bounded stop, then verify that holding the lock cannot sustain free rotation.
        for (int frame = 0; frame < 240 && _world.GetVehicle(1).Speed >= 0.5f; frame++) { await Frames(1); }
        await Frames(60);
        Check(_world.GetVehicle(1).Speed < 1 && Math.Abs(_world.GetVehicle(1).Movement.Physics.AngularVelocity.Y) < 0.3f,
            _case + " unpowered held rear lock settles rather than sustaining rotation at rest");
        _pilot = (tick, _) => new(tick, 0, 50000, 0, 0, 0, 0);
        await Frames(180);
        var state = _world.GetVehicle(1);
        var right = N.Vector3.Transform(N.Vector3.UnitX, state.ObservedPhysics.Orientation);
        Check(state.Speed > 15 && Math.Abs(N.Vector3.Dot(right, state.ObservedPhysics.LinearVelocity)) < 3, _case + " release recovers a powered line");
        await Finish();
    }

    private async Task PowerCorners(bool network)
    {
        foreach (float speed in new[] { 40 / 3.6f, 50 / 3.6f, 60 / 3.6f })
        {
            float baselineTurn = 0;
            float baselineTravel = 0;
            foreach (bool assisted in new[] { false, true })
            {
                await Setup(network, $"power-{speed * 3.6f:F0}-{assisted}", new(0, VehicleDimensions.RideHeight, 0), N.Quaternion.Identity,
                    new(0, 0, -speed), new() { DirtCornering = assisted ? 1 : 0 });
                _pilot = (tick, _) => new(tick, short.MaxValue, ushort.MaxValue, 0, 0, 0, 0);
                await Frames(45);
                var entryVelocity = _world.GetVehicle(1).ObservedPhysics.LinearVelocity;
                float travel = Math.Abs(MathF.Atan2(entryVelocity.X, -entryVelocity.Z));
                GD.Print($"{_case}: entry heading={Math.Abs(_yawTravel):F4} travel={travel:F4} baseline heading={baselineTurn:F4} travel={baselineTravel:F4}");
                // More yaw alone can reward the unwanted rear swing. Require the travel
                // direction to follow the stronger dirt turn, then observe the held turn.
                if (assisted) { Check(Math.Abs(_yawTravel) > baselineTurn && travel > baselineTravel, _case + " turns both chassis and travel direction more directly within 0.75 seconds"); }
                else { baselineTurn = Math.Abs(_yawTravel); baselineTravel = travel; }
                await Frames(15);
                var state = _world.GetVehicle(1);
                var right = N.Vector3.Transform(N.Vector3.UnitX, state.ObservedPhysics.Orientation);
                GD.Print($"{_case}: speed={state.Speed:F3} yaw={state.Movement.Physics.AngularVelocity.Y:F3} side={N.Vector3.Dot(right, state.ObservedPhysics.LinearVelocity):F3}");
                Check(N.Vector3.Transform(N.Vector3.UnitY, state.ObservedPhysics.Orientation).Y > 0.9f, _case + " remains planted");
                await Frames(120);
                state = _world.GetVehicle(1);
                right = N.Vector3.Transform(N.Vector3.UnitX, state.ObservedPhysics.Orientation);
                float side = Math.Abs(N.Vector3.Dot(right, state.ObservedPhysics.LinearVelocity));
                GD.Print($"{_case}: held 3 seconds speed={state.Speed:F3} side={side:F3} yaw={state.Movement.Physics.AngularVelocity.Y:F3}");
                if (assisted) { Check(side < state.Speed * 0.25f && _minimumUp > 0.9f, _case + " sustains the powered turn with rear alignment and wheel support"); }
                _pilot = (tick, _) => new(tick, 0, 40000, 0, 0, 0, 0);
                await Frames(180);
                state = _world.GetVehicle(1);
                right = N.Vector3.Transform(N.Vector3.UnitX, state.ObservedPhysics.Orientation);
                Check(Math.Abs(state.Movement.Physics.AngularVelocity.Y) < 0.1f && Math.Abs(N.Vector3.Dot(right, state.ObservedPhysics.LinearVelocity)) < 0.5f, _case + " straightens after release");
                await Finish();
            }
        }
    }

    private async Task TightTurns(bool network)
    {
        foreach (int kmh in new[] { 30, 40, 60, 90, 100, 120, 150 })
        {
            await Setup(network, $"tight-{kmh}", new(0, VehicleDimensions.RideHeight, 0), N.Quaternion.Identity, new(0, 0, -kmh / 3.6f));
            _pilot = (tick, _) => new(tick, short.MaxValue, 18000, 0, 0, 0, 0);
            await Frames(120);
            GD.Print($"{_case}: heading={Math.Abs(_yawTravel):F3} speed={_world.GetVehicle(1).Speed:F3}");
            Check(kmh > 40 || Math.Abs(_yawTravel) > MathF.PI, _case + " reverses direction tightly at low speed");
            Check(Math.Abs(_world.GetVehicle(1).Movement.SteeringAngle) > 0.2f && _minimumUp > 0.9f,
                _case + " retains usable speed-sensitive steering and stays wheel-down");
            _pilot = (tick, _) => new(tick, 0, 40000, 0, 0, 0, 0);
            await Frames(180);
            Check(Math.Abs(_world.GetVehicle(1).Movement.Physics.AngularVelocity.Y) < 0.1f, _case + " releases without sustained fishtailing");
            await Finish();
        }
    }

    private async Task HardLandings(bool network)
    {
        foreach (float height in new[] { 4f, 8f, 12f })
        {
            await Setup(network, $"hard-{height}", new(0, VehicleDimensions.RideHeight + height, 0), N.Quaternion.Identity, new(0, 0, -15));
            await Frames(480);
            GD.Print($"{_case}: compression={_maximumCompression:F3} minimum height={_minimumHeight:F3}");
            Check(!_chassisContact && _maximumCompression < 0.95f, _case + " absorbs the drop without bottoming or chassis contact");
            var final = _world.GetVehicle(1);
            Check(final.Movement.Grounded && Math.Abs(final.ObservedPhysics.Position.Y - VehicleDimensions.RideHeight) < 0.03f && Math.Abs(final.ObservedPhysics.LinearVelocity.Y) < 0.05f,
                _case + " settles back to wheel support");
            await Finish();
        }
    }

    private async Task AwkwardCrashes(bool network)
    {
        foreach (float pitch in new[] { -2.1f, -1.57f, 1.57f, 2.1f })
        foreach (bool powered in new[] { false, true })
        {
            await Setup(network, $"partial-{pitch}-{powered}", new(0, 3, 0), N.Quaternion.CreateFromYawPitchRoll(0, pitch, 0.3f), new(6, 0, -8));
            _pilot = (tick, _) => new(tick, 0, powered ? ushort.MaxValue : (ushort)0, 0, 0, 0, 0);
            await Frames(600);
            var p = _world.GetVehicle(1).ObservedPhysics;
            GD.Print($"{_case}: final up={N.Vector3.Transform(N.Vector3.UnitY, p.Orientation).Y:F3} height={p.Position.Y:F3} rebound={_rebound:F3}");
            Check(N.Vector3.Transform(N.Vector3.UnitY, p.Orientation).Y > 0.95f && _world.GetVehicle(1).Movement.Grounded,
                _case + " returns from diagonal bumper balance to its wheels");
            Check(_rebound < 3.5f, _case + " dissipates chassis impact with less than 0.56 m ballistic rise");
            await Finish();
        }
    }

    private async Task RepeatedRolls(bool network)
    {
        await Setup(network, "repeated-rolls", new(0, VehicleDimensions.RideHeight, 0), N.Quaternion.Identity);
        await Frames(60);
        for (int cycle = 0; cycle < 4; cycle++)
        {
            _minimumUp = 1;
            // Establish the same deliberate rollover velocity across mass defaults;
            // a fixed impulse now correctly may fail to overturn the heavier chassis.
            float impulse = 20000 * new VehicleConfiguration().Mass / 1400;
            var effect = new DamageEffect(0, new(cycle % 2 == 0 ? impulse : -impulse, 0, 0), new(0, 4, 0));
            var attribution = new DamageContext("verification", 0, $"roll-{cycle}");
            if (network) { _effects.Add(new(effect, attribution)); }
            else { _native[1].ApplyEffect(effect, attribution); }
            await Frames(600);
            var state = _world.GetVehicle(1);
            float up = N.Vector3.Transform(N.Vector3.UnitY, state.ObservedPhysics.Orientation).Y;
            GD.Print($"{_case}-{cycle}: lowest up={_minimumUp:F3}, final up={up:F3}, timer={state.Movement.CrashSeconds:F3}");
            Check(_minimumUp < 0.5f && up > 0.95f && state.Movement.Grounded && state.Movement.CrashSeconds == 0,
                _case + " repeats crash/recovery in the same life without a pose reset");
        }
        await Finish();
    }

    private async Task Setup(bool network, string name, N.Vector3 position, N.Quaternion orientation, N.Vector3 velocity = default, VehicleConfiguration? tuning = null, bool productionMap = false, bool alignSurface = false)
    {
        _minimumUp = 1;
        _case = (network ? "network-" : "native-") + name;
        _fixture = new Node3D(); AddChild(_fixture);
        if (productionMap)
        {
            if (_map is null) { _map = Arenas.ActiveMap.Load(); AddChild(_map); }
        }
        else
        {
            var road = new StaticBody3D(); road.SetMeta("surface_identity", "Dirt"); road.AddToGroup("landing_terrain");
            road.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(2000, 2, 2000) }, Position = new(0, -1, 0) });
            _fixture.AddChild(road);
        }
        if (productionMap)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            // Start clear of the actual local terrain, not an assumed flat infield height.
            using var ray = PhysicsRayQueryParameters3D.Create(new(position.X, 50, position.Z), new(position.X, -5, position.Z), 1);
            var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (hit.Count == 0) { throw new InvalidOperationException("Missing terrain at fixture start"); }
            position.Y = hit["position"].AsVector3().Y + VehicleDimensions.RideHeight + 0.1f;
            if (alignSurface)
            {
                Vector3 normal = hit["normal"].AsVector3();
                Vector3 heading = VehicleBody.ToGodot(N.Vector3.Transform(-N.Vector3.UnitZ, orientation));
                heading = (heading - normal * heading.Dot(normal)).Normalized();
                var rotation = Basis.LookingAt(heading, normal).GetRotationQuaternion();
                orientation = new(rotation.X, rotation.Y, rotation.Z, rotation.W);
                position = VehicleBody.ToCore(hit["position"].AsVector3() + normal * VehicleDimensions.RideHeight);
                velocity = VehicleBody.ToCore(heading) * velocity.Length();
            }
        }
        _world = new(new(60));
        _trace.Clear(); _movementTrace.Clear(); _pairContactTicks = 0; _yawTravel = 0; _chassisContact = false; _rebound = 0;
        _maximumCompression = 0; _minimumHeight = position.Y;
        _pilot = (tick, _) => new(tick, 0, 0, 0, 0, 0, 0);
        if (OS.GetCmdlineUserArgs().Contains("--trophy-baseline"))
        {
            tuning = new() { Braking = 28, WheelDamping = 10, WheelReboundDamping = 14, WheelBumpStart = 0.6f, WheelBumpSpring = 900 };
        }
        AddCar(network, 1, position, orientation, velocity, tuning ?? new());
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        _running = true;
    }

    private void AddCar(bool network, ulong id, N.Vector3 position, N.Quaternion orientation, N.Vector3 velocity, VehicleConfiguration tuning)
    {
        var pose = new VehiclePhysicsState(position, orientation, velocity, N.Vector3.Zero);
        // Isolate movement from the existing health/lifecycle rules during repeated crash fixtures.
        var damage = new DamageConfiguration { MaxHP = 100000, CollisionScale = 0 };
        _world.AddVehicle(id, tuning, damage, pose);
        if (network)
        {
            var body = new NetworkVehicleBody { VehicleId = id };
            _fixture.AddChild(body); body.ApplyConfiguration(tuning); body.Apply(_world.GetVehicle(id)); _network.Add(id, body);
        }
        else
        {
            var body = new VehicleBody { VehicleId = id, Configuration = tuning, DamageConfiguration = damage,
                Position = VehicleBody.ToGodot(position), Quaternion = VehicleBody.ToGodot(orientation), LinearVelocity = VehicleBody.ToGodot(velocity) };
            body.Initialize(_world); _fixture.AddChild(body); _native.Add(id, body);
        }
    }

    private async Task Frames(int count)
    {
        ulong until = _world.State.Tick + (ulong)count;
        while (_world.State.Tick < until) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
    }

    private async Task Finish()
    {
        _running = false; Save(); _native.Clear(); _network.Clear(); _fixture.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Save() => System.IO.File.WriteAllText(System.IO.Path.Combine(_output, _case + ".json"), JsonSerializer.Serialize(_trace));
    private void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
        _assertions++; GD.Print(message);
    }
}
