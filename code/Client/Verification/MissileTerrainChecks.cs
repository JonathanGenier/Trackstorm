using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Real native terrain rays and impact sweeps with production item authority and presentation.</summary>
public sealed partial class MissileTerrainChecks : Node3D
{
    private readonly List<object> _trace = new();
    private readonly List<object> _results = new();
    private HostVehicleSession _host = null!;
    private Node3D _geometry = null!;
    private ItemPresentation _presentation = null!;
    private Camera3D _camera = null!;
    private Func<float, float?> _profile = _ => 0;
    private VehiclePhysicsState _launch;
    private string _case = "", _output = "";
    private int _ticks, _impacts, _shots;
    private float _minimum, _maximum, _peakRate;
    private bool _running, _failed;

    public override void _Ready() => CallDeferred(MethodName.Run);

    private async void Run()
    {
        try
        {
            Engine.MaxFps = 60;
            _output = ProjectSettings.GlobalizePath("res://.godot/missile-checks");
            System.IO.Directory.CreateDirectory(_output);
            AddChild(new DirectionalLight3D { RotationDegrees = new(-55, -25, 0), LightEnergy = 1.8f });
            _camera = new Camera3D { Current = true, Far = 3000 };
            AddChild(_camera);
            if (OS.GetCmdlineUserArgs().Contains("--missile-production"))
            {
                await RunProduction();
                GD.Print(OS.GetCmdlineUserArgs().Contains("--missile-diagnose")
                    ? "Missile terrain diagnosis complete: recorded outcomes are not a verification pass."
                    : "Missile terrain integration passed: production tabletop and bank measurements.");
                GetTree().Quit();
                return;
            }
            foreach (var name in new[] { "flat", "ramp", "bank", "uneven", "cliff", "reacquire", "sky", "down", "wall", "steep", "tuning", "sustained" })
            {
                await Prepare(name);
                _running = true;
                int duration = name == "sustained" ? 1200 : name is "down" or "wall" or "steep" ? 220 : 340;
                for (int i = 0; i < duration && !_failed; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
                _running = false;
                if (_failed) { return; }
                if (name is "flat" or "ramp" or "bank" or "uneven" or "cliff" or "reacquire" or "sky" or "tuning" or "sustained")
                { Require(_impacts == 0, $"{name}: unintended terrain impact"); }
                if (name is "down" or "wall" or "steep") { Require(_impacts == 1, $"{name}: exactly one native impact"); }
                if (name is "flat" or "ramp" or "bank" or "uneven") { Require(_minimum > 0.1f && _maximum < 3, $"{name}: car-hit clearance {_minimum}..{_maximum}"); }
                if (name == "tuning") { Require(_minimum > 1.4f, "Tuned clearance is functional"); }
                Require(_host.Items.Missiles.Count == 0, $"{name}: all shots retired at expiry/impact");
                _results.Add(new { name, ticks = _ticks, shots = _shots, impacts = _impacts, minimumClearance = _minimum == float.MaxValue ? (float?)null : _minimum, maximumClearance = _maximum, peakPitchDegreesPerSecond = _peakRate });
                GD.Print($"Missile {name}: {_shots} shots, {_impacts} impacts, clearance {(_minimum == float.MaxValue ? "n/a" : $"{_minimum:F3}..{_maximum:F3}")}, peak pitch {_peakRate:F2} deg/s");
            }
            await RunNetwork();
            await RunOval();
            await RunProduction();
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "summary.json"), System.Text.Json.JsonSerializer.Serialize(_results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "trace.json"), System.Text.Json.JsonSerializer.Serialize(_trace));
            GD.Print("Missile terrain integration passed: native traversal, transitions, impacts, tuning, serialized continuation and sustained cleanup.");
            GetTree().Quit();
        }
        catch (Exception exception) { Fail(exception); }
    }

    private async Task Prepare(string name)
    {
        _case = name; _ticks = _impacts = _shots = 0; _minimum = float.MaxValue; _maximum = _peakRate = 0;
        if (_geometry is not null) { _geometry.QueueFree(); _presentation.QueueFree(); await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); }
        _geometry = new Node3D(); AddChild(_geometry);
        _presentation = new ItemPresentation(); AddChild(_presentation);
        float slope = MathF.Tan(10 * MathF.PI / 180);
        _profile = name switch
        {
            "ramp" => d => Math.Max(0, d - 35) * slope,
            "uneven" => d => 0.35f * MathF.Sin(d / 35),
            "cliff" => d => d is > 35 and < 100 or > 190 and < 260 ? -8 : 0,
            "reacquire" => d => d is > 35 and < 100 or > 190 and < 260 ? null : 0,
            "steep" => d => d < 35 ? 0 : (d - 35) * 2,
            _ => _ => 0,
        };
        foreach (var interval in new[] { (Start: -50, End: 100, Identity: "Asphalt"), (Start: 100, End: 1000, Identity: "Dirt") })
        {
        var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
        for (int d = interval.Start; d < interval.End; d++)
        {
            if (_profile(d) is not float y0 || _profile(d + 1) is not float y1) { continue; }
            // Do not bridge a cliff into a sloped drivable face.
            if (Math.Abs(y1 - y0) > 3) { continue; }
            float bank = name == "bank" ? 0.2f : 0;
            Vector3 a = new(-15, y0 - 15 * bank, -d), b = new(15, y0 + 15 * bank, -d);
            Vector3 c = new(-15, y1 - 15 * bank, -d - 1), e = new(15, y1 + 15 * bank, -d - 1);
            foreach (var vertex in new[] { a, c, b, b, c, e }) { surface.AddVertex(vertex); }
        }
        surface.GenerateNormals(); var mesh = surface.Commit();
        var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        body.SetMeta("surface_identity", interval.Identity);
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.24f, 0.32f, 0.23f), CullMode = BaseMaterial3D.CullModeEnum.Disabled } });
        _geometry.AddChild(body);
        }
        if (name == "wall")
        {
            var wall = new StaticBody3D { Position = new(0, 10, -35), CollisionLayer = 1 };
            wall.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new(30, 20, 1) } });
            wall.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new(30, 20, 1) } });
            _geometry.AddChild(wall);
        }
        var config = GameplayConfiguration.HostedDefaults.Items with { MissileLifetimeTicks = name == "sustained" ? 90 : 180 };
        if (name == "tuning") { config = config with { MissileTerrain = config.MissileTerrain with { Clearance = 1.6f }, MissileSpeed = 80 }; }
        _host = new HostVehicleSession(99, config);
        float angle = name == "sky" ? 75 : name == "down" ? -60 : 0;
        _launch = new(new N.Vector3(0, name == "tuning" ? 1.6f : name == "flat" || name == "reacquire" ? 2 : 1, 0), N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitX, angle * MathF.PI / 180), N.Vector3.Zero, N.Vector3.Zero);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        var sample = MissileTerrainQuery.Cast(GetWorld3D().DirectSpaceState, new(0, 2, 0), new(0, -2, 0));
        Require(sample is not null && sample.Normal.Y > 0.9f, $"{name}: native authored ground query");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_running || _failed) { return; }
        try
        {
            if (_ticks == 0 || _case == "sustained" && _ticks < 900 && _host.Items.Slots.Single().Active.Item == HeldItem.None && _host.World.State.Tick >= _host.Items.Slots.Single().MissileStowEndTick)
            {
                Require(_host.Items.Grant(_host.World, 1, HeldItem.Missile), "Repeated fixture acquisition");
            }
            if (_host.Items.Slots.SingleOrDefault()?.Active.Item == HeldItem.Missile &&
                _host.World.State.Tick >= _host.Items.Slots.Single().MissileReadyTick)
            {
                Require(_host.UseItem(0, 99, 1, _host.Items.Slots.Single().Active.Token), "Real capability-bound ready launch");
                _shots++;
            }
            var prior = _host.Items.Missiles.ToDictionary(m => m.Id);
            _host.Step(default, state => new VehicleObservation(_launch, N.Vector3.UnitY), Sweep,
                missileTerrain: (a, b) => MissileTerrainQuery.Cast(GetWorld3D().DirectSpaceState, a, b));
            _ticks++;
            _impacts += _host.Items.Events.Count(e => e.Impact);
            var publication = new ItemPublication((ulong)_ticks, _host.Snapshot(), _host.Items.Slots, _host.Items.Missiles, _host.Items.Events);
            var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(publication));
            Require(decoded.Missiles.SequenceEqual(_host.Items.Missiles), "Curved flight round-trip retains exact continuation");
            _presentation.Apply(publication);
            foreach (var missile in _host.Items.Missiles)
            {
                float? ground = _profile(-missile.Position.Z);
                float? clearance = ground.HasValue ? missile.Position.Y - ground : null;
                if (clearance is float measured) { _minimum = Math.Min(_minimum, measured); _maximum = Math.Max(_maximum, measured); }
                if (prior.TryGetValue(missile.Id, out var before))
                {
                    float rate = Math.Abs(Pitch(before.Velocity) - Pitch(missile.Velocity)) * 60;
                    _peakRate = Math.Max(_peakRate, rate);
                    Require(rate <= _host.Items.Configuration.MissileTerrain.TurnRate + 0.01, "Native pitch is rate bounded");
                }
                if (missile == _host.Items.Missiles[0])
                {
                    _trace.Add(new { scenario = _case, tick = _ticks, x = missile.Position.X, y = missile.Position.Y, z = missile.Position.Z, pitch = Pitch(missile.Velocity), clearance });
                    var position = VehicleBody.ToGodot(missile.Position);
                    _camera.Position = position + new Vector3(12, 5, 16); _camera.LookAt(position);
                }
            }
            if (_ticks is 45 or 90 && DisplayServer.GetName() != "headless") { Capture(_case + "-" + _ticks); }
        }
        catch (Exception exception) { Fail(exception); }
    }
    private float? Sweep(MissileState missile, N.Vector3 end)
    {
        var start = VehicleBody.ToGodot(missile.Position); var finish = VehicleBody.ToGodot(end);
        using var ray = PhysicsRayQueryParameters3D.Create(start, finish, 1); ray.HitFromInside = true;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        return hit.Count == 0 ? null : start.DistanceTo(hit["position"].AsVector3()) / start.DistanceTo(finish);
    }
    private async void Capture(string name)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(_output, name + ".png"));
    }
    private static float Pitch(N.Vector3 velocity) => MathF.Atan2(velocity.Y, new N.Vector2(velocity.X, velocity.Z).Length()) * 180 / MathF.PI;
    private static void Require(bool value, string message) { if (!value) { throw new InvalidOperationException(message); } }
    private void Fail(Exception exception) { _failed = true; _running = false; GD.PrintErr(exception); GetTree().Quit(1); }
}
