using System.Text.Json;
using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Arenas;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Combined production-map native traffic, persistent tracks and bounded destruction load.</summary>
public sealed partial class IntegratedDrivingChecks : Node3D
{
    private readonly Core.Simulation.Simulation _world = new(new(60));
    private readonly List<VehicleBody> _cars = new();
    private readonly List<Vector3[]> _routes = new();
    private readonly int[] _progress = new int[8];
    private readonly int[] _initial = new int[8];
    private readonly double[] _intervals = new double[5400];
    private EnvironmentAuthority _authority = null!;
    private EnvironmentLayout _layout = null!;
    private DestructibleEnvironment _view = null!;
    private readonly ItemAuthority _items = new(new() { MaximumDamage = 300, ExplosionRadius = 12 });
    private bool _running;
    private int _frames;
    private int _unsupported;
    private int _tipped;
    private ulong _previous;
    private string _output = "";

    public override void _Ready() => CallDeferred(MethodName.Run);

    public override void _PhysicsProcess(double delta)
    {
        if (!_running) { return; }
        ulong now = Time.GetTicksUsec();
        if (_frames > 0) { _intervals[_frames - 1] = (now - _previous) / 1000.0; }
        _previous = now;
        ulong tick = _world.State.Tick + 1;
        var requests = new List<VehicleStepRequest>();
        for (int slot = 0; slot < _cars.Count; slot++)
        {
            var car = _cars[slot];
            var route = _routes[slot];
            Vector3 position = car.Position with { Y = 0 };
            int nearest = _progress[slot];
            for (int candidate = nearest; candidate <= _progress[slot] + 12; candidate++)
            {
                if (position.DistanceTo(route[candidate % route.Length]) < position.DistanceTo(route[nearest % route.Length])) { nearest = candidate; }
            }
            _progress[slot] = nearest;
            Vector3 target = route[(nearest + 4) % route.Length] - position;
            float angle = ((-car.Basis.Z) with { Y = 0 }).SignedAngleTo(target, Vector3.Up);
            float targetSpeed = Math.Abs(angle) > .35f ? 6 : 12;
            float speed = car.LinearVelocity.Length();
            bool drive = _frames >= 90 && car.State.Grounded;
            var input = new InputFrame(tick, drive ? (short)(Math.Clamp(-angle * 2.5f, -1, 1) * short.MaxValue) : (short)0,
                drive && speed < targetSpeed ? (ushort)40000 : (ushort)0,
                drive && speed > targetSpeed + 1 ? (ushort)18000 : (ushort)0, 0, 0, 0);
            requests.Add(car.Capture(input));
            _unsupported += _frames >= 90 && !car.State.Grounded ? 1 : 0;
            _tipped += _frames >= 90 && car.Basis.Y.Y < .5f ? 1 : 0;
        }
        var results = _world.Step(requests[0].Input, requests);
        // Committed weapon-outcome seam: ordinary destruction authority and native
        // views still process the event. Projectile flight/UDP have separate checks.
        ItemEvent[] impacts = _frames % 120 == 0
            ? [new(tick, 1, HeldItem.Missile, _layout.Rocks[(_frames / 120 % _layout.RootCount) * EnvironmentLayout.PiecesPerRock], true)]
            : [];
        _authority.Advance(tick, requests.Where(r => _world.GetVehicle(r.VehicleId).CanInteract).ToArray(), impacts, _items);
        _view.Apply(_authority.Snapshot(1, tick));
        for (int i = 0; i < _cars.Count; i++) { _cars[i].Apply(results[i]); _cars[i].Publish(); }
        if (++_frames >= 5400) { _running = false; }
    }

    private async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/integrated-driving-checks");
            System.IO.Directory.CreateDirectory(_output);
            AddChild(new EnvironmentPresentation());
            var map = ActiveMap.Load(); AddChild(map);
            _layout = DestructibleEnvironment.ReadLayout(map)!;
            _authority = new(_layout); _view = new(map);
            var camera = new Camera3D { Current = true, Position = new(-70, 65, 65), Far = 1800 };
            AddChild(camera); camera.LookAt(new Vector3(-50, 0, 0));
            await Frames(3);
            using var json = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://assets/maps/infield/layout.json"));
            var routes = json.RootElement.GetProperty("routes").EnumerateArray().Take(2).Select(r =>
                r.GetProperty("points").EnumerateArray().Select(p => new Vector3(p[0].GetSingle(), 0, p[1].GetSingle())).ToArray()).ToArray();
            for (int slot = 0; slot < 8; slot++)
            {
                var route = routes[slot / 4]; _routes.Add(route);
                int start = slot % 4 * (route.Length - 1) / 4;
                _progress[slot] = _initial[slot] = start;
                using var ray = PhysicsRayQueryParameters3D.Create(route[start] + Vector3.Up * 20, route[start] + Vector3.Down * 10, 1);
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
                Require(hit.Count > 0, "Traffic spawn has production support");
                Vector3 position = hit["position"].AsVector3() + Vector3.Up * VehicleDimensions.RideHeight;
                var basis = Basis.LookingAt(route[(start + 1) % route.Length] - route[start]);
                var car = new VehicleBody { VehicleId = (ulong)slot + 1, Position = position, Basis = basis,
                    DamageConfiguration = new() { MaxHP = 1000, CollisionScale = 5 } };
                var rotation = basis.GetRotationQuaternion();
                _world.AddVehicle(car.VehicleId, car.Configuration, car.DamageConfiguration,
                    new(VehicleBody.ToCore(position), new N.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), default, default));
                car.Initialize(_world); AddChild(car); _cars.Add(car);
            }
            var batch = TireMarkBatch.For(this);
            await Frames(3);
            int nodes = CountNodes(this);
            _running = true;
            while (_running) { await Frames(1); }
            var state = _authority.Snapshot(1, _world.State.Tick);
            var initialStages = _layout.InitialStages;
            int changed = state.Rocks.Where((r, i) => r.Stage != initialStages[i]).Count();
            Array.Sort(_intervals, 0, _frames - 1);
            var evidence = new
            {
                frames = _frames, cars = _cars.Count, progress = _progress.Zip(_initial, (a, b) => a - b).ToArray(),
                unsupported = _unsupported, tipped = _tipped, changedPieces = changed, plants = state.Plants.Count(p => p),
                batch.Written, batch.Alive, batch.Submitted, batch.Budget, nodesBefore = nodes, nodesAfter = CountNodes(this),
                p50Milliseconds = _intervals[2699], p95Milliseconds = _intervals[5129], p99Milliseconds = _intervals[5345],
                hp = _cars.Select(c => c.DamageState.CurrentHP).ToArray(),
                positions = _cars.Select(c => new[] { c.Position.X, c.Position.Y, c.Position.Z }).ToArray(),
            };
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "evidence.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            Require(_progress.Zip(_initial, (a, b) => a - b).All(p => p > 100), "Every car continues along its production route");
            Require(_tipped == 0 && _unsupported < 8 * _frames / 20, "Combined driving remains supported without routine rollover");
            Require(changed > 0 && state.Plants.Any(p => p), "Combined run exercises rocks and soft plants");
            // Broken visuals allocate one five-node presentation per reserved piece
            // on first use. Track emission itself must not add any scene nodes.
            Require(batch.Written > 2000 && batch.Submitted <= batch.Budget && CountNodes(this) <= nodes + _layout.Rocks.Count * 5, "Persistent tracks and destruction remain bounded");
            int settledNodes = CountNodes(this);
            for (int repeat = 0; repeat < 100; repeat++) { _view.Apply(state); }
            Require(CountNodes(this) == settledNodes, "Repeated destruction publication reuses existing piece nodes");
            Require(_cars.All(c => c.Position.IsFinite() && c.DamageState.CurrentHP > 0), "All eight vehicles remain live and finite");
            if (DisplayServer.GetName() != "headless")
            {
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = GetViewport().GetTexture().GetImage();
                image.SavePng(System.IO.Path.Combine(_output, "traffic.png"));
            }
            GD.Print("Integrated driving passed: eight cars, 90 seconds, production routes, persistent marks and destruction.");
            GetTree().Quit();
        }
        catch (Exception exception) { _running = false; GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidOperationException(message); } }
    private static int CountNodes(Node node) => 1 + node.GetChildren().Sum(CountNodes);
    private async Task Frames(int count) { for (int i = 0; i < count; i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); } }
}
