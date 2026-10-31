using System.Diagnostics;
using System.Text.Json;
using Godot;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Items;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Verification;

/// <summary>Opt-in fixed-workload benchmark. Wall-clock frame intervals are distinct from engine CPU monitors.</summary>
public sealed partial class PerformanceBenchmark : Node3D
{
    private readonly List<object> _results = [];
    private readonly List<double> _frames = [], _physics = [], _process = [], _advance = [], _draws = [];
    private NetworkVehicleArena? _arena;
    private bool _measure, _drive, _fire;
    private long _lastFrame;
    private ulong _tick;
    private string _output = "";
    private int _ticks;
    private int _shots;
    private ulong _pressedToken;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready() => CallDeferred(MethodName.Run);

    public override void _Process(double delta)
    {
        // Hidden/minimized Windows verification windows otherwise skip rendering.
        // Disable the automatic loop and submit exactly one draw after scene processing.
        if (DisplayServer.GetName() != "headless") CallDeferred(MethodName.Draw);
        long now = Stopwatch.GetTimestamp();
        if (_measure && _lastFrame != 0)
        {
            _frames.Add(Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds);
            _physics.Add(Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000);
            _process.Add(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000);
            _draws.Add(Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame));
        }
        _lastFrame = now;
    }

    private void Draw() => RenderingServer.ForceDraw(false);

    public override void _PhysicsProcess(double delta)
    {
        if (_arena is null) return;
        var host = _arena.Driver.Host!;
        if (_fire && host.Items.Slots.All(s => s.Vehicle != 1 || s.Active.Item == HeldItem.None))
            host.Items.Grant(host.World, 1, HeldItem.MachineGun);
        ulong token = _fire ? _arena.Driver.LocalItem?.Active.Token ?? 0 : 0;
        bool press = token != 0 && token != _pressedToken;
        _pressedToken = token;
        if (_fire && _arena.Driver.LocalState is { } shooter)
            _arena.Driver.DesiredAim = System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, shooter.ObservedPhysics.Orientation);
        long start = Stopwatch.GetTimestamp();
        _arena.Advance(new(++_tick, 0, _drive ? ushort.MaxValue : (ushort)0, 0,
            _fire ? InputButtons.UseItem : 0, press ? InputButtons.UseItem : 0, 0));
        if (_measure) _advance.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private async void Run()
    {
        try
        {
            string Argument(string key, string fallback) => OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? fallback;
            _output = Argument("--benchmark-output", ProjectSettings.GlobalizePath("res://.godot/performance"));
            _ticks = int.Parse(Argument("--benchmark-ticks", "1200"), System.Globalization.CultureInfo.InvariantCulture);
            if (_ticks < 60 || _ticks > 36000) throw new ArgumentOutOfRangeException(nameof(_ticks));
            System.IO.Directory.CreateDirectory(_output);
            Engine.MaxFps = 0;
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            Engine.PhysicsTicksPerSecond = 60;
            RenderingServer.RenderLoopEnabled = false;
            ShieldGround.Profile = true;
            var metadata = new
            {
                schema = 2, godot = Engine.GetVersionInfo()["string"].AsString(), debug = OS.IsDebugBuild(),
                display = DisplayServer.GetName(), renderer = RenderingServer.GetCurrentRenderingMethod(),
                gpu = RenderingServer.GetVideoAdapterName(), cpu = OS.GetProcessorName(), processors = OS.GetProcessorCount(),
                resolution = GetViewport().GetVisibleRect().Size.ToString(), vsync = DisplayServer.WindowGetVsyncMode().ToString(),
                maxFps = Engine.MaxFps, physicsHz = Engine.PhysicsTicksPerSecond, warmupTicks = 180, sampleTicks = _ticks,
                drawing = "One forced offscreen draw per process frame, automatic render loop disabled; hidden window safe",
                note = "Synthetic fixed-step workloads; not human input, remote multiplayer, GPU timings or historical regression proof."
            };
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "environment.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

            var mapRoot = new Node3D(); AddChild(mapRoot);
            var map = ActiveMap.Load(); mapRoot.AddChild(map);
            mapRoot.AddChild(new EnvironmentPresentation());
            var config = ActiveMap.ReadConfiguration(map);
            var camera = new Camera3D { Current = true, Far = 1600 };
            mapRoot.AddChild(camera);
            var spawn = config.Players[0];
            camera.Position = Vehicles.VehicleBody.ToGodot(spawn.Position) + new Vector3(0, 8, 14);
            camera.LookAt(Vehicles.VehicleBody.ToGodot(spawn.Position));
            await Sample("map-only");

            var pickups = new ItemSpawnPresentation(); mapRoot.AddChild(pickups);
            pickups.Initialize(config, map.GetNode<Node3D>("ItemSpawns"));
            var publicationHost = new HostVehicleSession(322); publicationHost.RegisterSpawns(config);
            ulong revision = 0;
            ItemPublication Publication(ulong token) => new(++revision, publicationHost.Snapshot(), [], [], [],
                publicationHost.Spawns!.States.Select(s => token == 0 ? s : new ItemSpawnState(s.Id, false, 600, 1, token, HeldItem.Missile)));
            pickups.Apply(Publication(0));
            await Sample("map-pickups");

            // Accepted newer publications with unchanged availability are common during combat.
            // Prebuild outside the timed region to isolate Apply, including managed allocations.
            var publications = Enumerable.Range(0, 1000).Select(_ => Publication(0)).ToArray();
            var applyTimes = new List<double>(); var allocated = new List<double>();
            for (int batch = 0; batch < 12; batch++)
            {
                publications = Enumerable.Range(0, 1000).Select(_ => Publication(0)).ToArray();
                long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                foreach (var publication in publications) pickups.Apply(publication);
                double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (batch > 1) { applyTimes.Add(elapsed / publications.Length); allocated.Add((GC.GetAllocatedBytesForCurrentThread() - bytes) / (double)publications.Length); }
                await Frames(1);
            }
            _results.Add(new { scenario = "pickup-publication-cpu", calls = 10000, milliseconds = Summary(applyTimes), bytesPerCall = Summary(allocated) });
            await Sample("crate-pressure", tick => { if (tick % 30 == 0) pickups.Apply(Publication((ulong)tick + 1)); });
            await Frames(780);
            if (pickups.ActiveSmashCount != 0) throw new InvalidOperationException("Crate effects did not expire.");
            await Sample("crate-cleanup");
            mapRoot.QueueFree(); await Frames(3);

            using var wire = new GameNetworkingSocketsTransport();
            using var port = new System.Net.Sockets.UdpClient(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
            int number = ((System.Net.IPEndPoint)port.Client.LocalEndPoint!).Port; port.Close();
            wire.Listen(TransportEndpoint.DirectIp($"127.0.0.1:{number}"));
            _arena = new NetworkVehicleArena(); _arena.Initialize(wire, 322, 0); AddChild(_arena);
            _arena.Driver.ItemsReceived += p => _shots += p.Events.Count(e => e.Item == HeldItem.MachineGun);
            _arena.Driver.Host!.TryConfigure(0, new Dictionary<string, double> { ["match.minimum_players"] = 1, ["match.countdown_ticks"] = 1 }, out _);
            await Sample("idle-one");
            _drive = true; await Sample("driving-one"); _drive = false;
            _arena.Driver.Host.DrivePracticeCar(2, true);
            _arena.Driver.Host.DrivePracticeCar(3, true);
            await Sample("three-cars-prior-roster");
            _arena.Driver.Host.DrivePracticeCar(4, true, pose => ActiveMap.PlacePracticeCar(_arena.Map, pose));
            await Sample("four-cars");
            foreach (var inventory in _arena.Driver.Host.Items.Slots.Where(s => s.Vehicle == 1).ToArray())
                for (int i = 0; i < 2; i++)
                {
                    var current = _arena.Driver.Host.Items.Slots.Single(s => s.Vehicle == 1);
                    _arena.Driver.Host.Items.Discard(_arena.Driver.Host.World, 1, current.Life, current.Active.Token, current.SelectionRevision);
                }
            _fire = true; await Sample("combat-machine-gun"); _fire = false;
            if (_shots == 0) throw new InvalidOperationException("Combat benchmark emitted no Machine Gun rounds.");
            await Sample("sustained-four-cars");
            // Ordinary New Map hosting also includes the pond-side practice target:
            // one human plus four practice cars is a five-vehicle workload.
            _arena.Driver.Host.DrivePracticeCar(5, true);
            await Sample("five-cars-application-roster");
            _arena.QueueFree(); _arena = null; await Frames(5);
            _results.Add(new { scenario = "arena-teardown", memory = Memory() });
            System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "results.json"), JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
            GD.Print($"Performance benchmark completed in {_clock.Elapsed.TotalSeconds:F1}s: {_output}");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    private async Task Sample(string name, Action<int>? workload = null)
    {
        await Frames(180);
        _frames.Clear(); _physics.Clear(); _process.Clear(); _advance.Clear(); _draws.Clear();
        var before = Memory();
        int shotsBefore = _shots;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        TimeSpan cpu = process.TotalProcessorTime;
        long start = Stopwatch.GetTimestamp(); _lastFrame = 0; _measure = true;
        for (int tick = 0; tick < _ticks; tick++) { workload?.Invoke(tick); await Frames(1); }
        _measure = false;
        double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        process.Refresh();
        _results.Add(new
        {
            scenario = name, seconds, renderSamples = _frames.Count, simulationSamples = _advance.Count,
            fpsFromIntervals = _frames.Count == 0 ? 0 : 1000 / _frames.Average(), frameMs = Summary(_frames),
            framesOver33Ms = _frames.Count(t => t > 33.333), framesOver50Ms = _frames.Count(t => t > 50),
            godotPhysicsMs = Summary(_physics), godotProcessMs = Summary(_process), advanceCpuMs = Summary(_advance),
            drawCalls = Summary(_draws), cpuCoreEquivalent = (process.TotalProcessorTime - cpu).TotalSeconds / seconds,
            machineGunRounds = _shots - shotsBefore, vehicles = _arena?.Bodies.Count ?? 0,
            before, after = Memory(), shieldFits = ShieldGround.Fits, shieldRays = ShieldGround.Rays
        });
        System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "results.json"), JsonSerializer.Serialize(_results, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"BENCHMARK {name}: {_frames.Count} frames, mean FPS {(_frames.Count == 0 ? 0 : 1000 / _frames.Average()):F1}");
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var screenshot = GetViewport().GetTexture().GetImage();
            screenshot.SavePng(System.IO.Path.Combine(_output, name + ".png"));
        }
    }

    private static object Summary(List<double> values)
    {
        if (values.Count == 0) return new { count = 0 };
        double[] ordered = values.Order().ToArray();
        double Percentile(double p) => ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * p) - 1, 0, ordered.Length - 1)];
        return new { count = ordered.Length, mean = values.Average(), median = Percentile(.5), p95 = Percentile(.95), p99 = Percentile(.99), max = ordered[^1] };
    }

    private static object Memory()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return new { managed = GC.GetTotalMemory(false), privateBytes = process.PrivateMemorySize64,
            nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), resources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),
            orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount), engineBytes = Performance.GetMonitor(Performance.Monitor.MemoryStatic),
            videoBytes = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed), gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2) };
    }

    private async Task Frames(int count)
    {
        for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
