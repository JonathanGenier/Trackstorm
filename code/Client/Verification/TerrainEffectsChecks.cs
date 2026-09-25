using Godot;
using Trackstorm.Core.Settings;
using Trackstorm.Client.Development;
using Trackstorm.Client.Settings;
using Trackstorm.Client.Arenas;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native wheel feedback, bounded repeated use, and rendered discrete environment packages.</summary>
public sealed partial class TerrainEffectsChecks : Node3D
{
    private readonly Core.Simulation.Simulation _simulation = new(new Core.Simulation.SimulationConfiguration(60));
    private readonly List<string> _evidence = new();
    private readonly List<VehicleBody> _cars = new();
    private VehicleBody? _car;
    private Camera3D _camera = null!;
    private ulong _tick;
    private bool _drive;
    private bool _hard;
    private bool _stress;
    private string _output = string.Empty;
    private readonly double[] _frameTimes = new double[12000];
    private int _frameCount;
    private ulong _previousFrame;

    public override void _Ready() => CallDeferred(MethodName.Run);
    public override void _PhysicsProcess(double delta)
    {
        if (_stress && _frameCount < _frameTimes.Length)
        {
            ulong now = Time.GetTicksUsec();
            if (_previousFrame > 0) { _frameTimes[_frameCount++] = (now - _previousFrame) / 1000.0; }
            _previousFrame = now;
        }
        if (_car is null) { return; }
        var input = new InputFrame(++_tick, _hard ? (short)16000 : (short)0, _drive ? ushort.MaxValue : (ushort)0, 0, 0, 0, _hard ? InputButtons.Drift : InputButtons.None);
        var result = _simulation.Step(input, _cars.Select(car => car.Capture(_stress || car == _car ? input : new InputFrame(_tick, 0, 0, 0, 0, 0, 0))).ToArray());
        for (int i = 0; i < _cars.Count; i++) { _cars[i].Apply(result[i]); _cars[i].Publish(); }
    }

    private async void Run()
    {
        try
        {
            _output = ProjectSettings.GlobalizePath("res://.godot/terrain-effects-checks");
            System.IO.Directory.CreateDirectory(_output);
            var environment = new EnvironmentPresentation();
            AddChild(environment);
            _camera = new Camera3D { Current = true, Far = 1800, Fov = 60 };
            AddChild(_camera);
            if (!OS.GetCmdlineUserArgs().Contains("--presets-only"))
            {
            var pad = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
            pad.AddToGroup("landing_terrain");
            pad.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(120, 1, 120) }, Position = new Vector3(0, -.5f, 0) });
            var material = new StandardMaterial3D { AlbedoColor = new Color("837251") };
            pad.AddChild(new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(120,120), Material = material } });
            AddChild(pad);
            _car = new VehicleBody { Position = new Vector3(0,.9f,15), VehicleId = 1, CollisionLayer = 2, CollisionMask = 1 };
            _simulation.AddVehicle(1, _car.Configuration, _car.DamageConfiguration, Physics(_car.Position));
            _car.Initialize(_simulation);
            AddChild(_car);
            _cars.Add(_car);
            for (int slot = 1; slot < 8; slot++)
            {
                var extra = new VehicleBody { Position = new Vector3(-49+slot*14,.9f,50), VehicleId = (ulong)slot+1, CollisionLayer = 2, CollisionMask = 1 };
                _simulation.AddVehicle(extra.VehicleId, extra.Configuration, extra.DamageConfiguration, Physics(extra.Position));
                extra.Initialize(_simulation);
                AddChild(extra);
                _cars.Add(extra);
            }

            var effects = _car.GetChildren().OfType<TireFeedback>().Single();
            var batch = TireMarkBatch.For(this);
            var player = new Input.PlayerInput();
            AddChild(player);
            player.SetPhysicsProcess(false);
            var settings = new PlayerSettingsController();
            string settingsPath = System.IO.Path.Combine(_output, "tire-settings.json");
            settings.Initialize(player.Adapter, settingsPath);
            AddChild(settings);
            settings.UpdateSettings(settings.Current with { TireEffects = TireEffectSettings.Defaults });
            batch.SettingsSource = () => settings.Current.TireEffects;
            var shell = new DevToolsShell();
            shell.Initialize(player.Adapter);
            shell.Configs.LocalSettings = settings;
            AddChild(shell);
            await Frames(10);
            int surfaceIndex = 0;
            foreach (var identity in new[] { SurfaceIdentity.Asphalt, SurfaceIdentity.Concrete, SurfaceIdentity.Dirt, SurfaceIdentity.Grass, SurfaceIdentity.Mud, SurfaceIdentity.DeepMud, SurfaceIdentity.Water })
            {
                pad.SetMeta("surface_identity", identity.ToString());
                material.AlbedoColor = identity switch
                {
                    SurfaceIdentity.Asphalt => new Color("383938"), SurfaceIdentity.Concrete => new Color("b4b1a4"),
                    SurfaceIdentity.Grass => new Color("59603a"), SurfaceIdentity.Mud => new Color("4c3725"),
                    SurfaceIdentity.DeepMud => new Color("382b22"), SurfaceIdentity.Water => new Color("23535b"), _ => new Color("876b45"),
                };
                if (identity == SurfaceIdentity.Water)
                {
                    pad.AddToGroup("water_terrain");
                    pad.SetMeta("water_level", .1f);
                    pad.SetMeta("surface_bounds", new Vector4(-60,-60,120,120));
                }
                _hard = identity is SurfaceIdentity.Asphalt or SurfaceIdentity.Concrete;
                _drive = true;
                _car.ResetBody(Physics(new Vector3(-48 + surfaceIndex++ * 16,.9f,15), _hard ? new Vector3(7,0,-9) : new Vector3(0,0,-7)));
                int marks = effects.MarksWritten;
                int water = effects.WaterSamples;
                await Frames(_hard ? 35 : identity == SurfaceIdentity.Water ? 20 : 65);
                Check(effects.Observed.Contains(identity), "Wheel contacts resolve " + identity);
                if (identity == SurfaceIdentity.Water)
                {
                    Check(effects.MarksWritten == marks, "Water creates zero persistent marks");
                    Check(effects.WaterSamples > water, "Water samples generate wet interaction");
                }
                else { Check(effects.MarksWritten > marks, identity + " produces tracks: " + (effects.MarksWritten-marks)); }
                await View(identity.ToString(), _car.Position + new Vector3(7,5,8), _car.Position + new Vector3(0,-.5f,2));
            }
            pad.RemoveFromGroup("water_terrain");
            pad.SetMeta("surface_identity", "Dirt");
            _hard = false;
            _stress = true;
            _camera.Position = new Vector3(0,35,30);
            _camera.LookAt(Vector3.Zero);
            await Frames(3);
            int nodes = Descendants(this);
            long retainedStart = GC.GetTotalMemory(true);
            ulong started = Time.GetTicksMsec();
            for (int repetition = 0; repetition < 120; repetition++)
            {
                for (int slot = 0; slot < _cars.Count; slot++) { _cars[slot].ResetBody(Physics(new Vector3(-49+slot*14,.9f,15), new Vector3(0,0,-9))); }
                await Frames(100);
                if (repetition % 24 == 23)
                {
                    Check(batch.Submitted <= batch.Budget && Descendants(this) == nodes, $"Stress checkpoint {repetition + 1}: bounded nodes and submissions");
                    _evidence.Add($"Checkpoint {repetition + 1}: managed retained={GC.GetTotalMemory(true)}, engine static={Godot.Performance.GetMonitor(Godot.Performance.Monitor.MemoryStatic)}, video={Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderVideoMemUsed)}, draw calls={Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderTotalDrawCallsInFrame)}, alive={batch.Alive}, submitted={batch.Submitted}.");
                }
            }
            Check(batch.Recycled > 0 && batch.Written > batch.Budget, "Eight-car repeated use recycles the shared ring: written=" + batch.Written + ", recycled=" + batch.Recycled);
            Check(Descendants(this) == nodes, "Repeated tire use creates no additional nodes");
            Check(batch.Submitted <= batch.Budget && batch.GetChildren().OfType<MultiMeshInstance3D>().Single().Multimesh.InstanceCount == TireMarkBatch.MaximumCapacity, "Arena-wide track geometry remains fixed and active submissions obey budget");
            _evidence.Add($"Eight vehicles, 12000 sustained physics frames in {Time.GetTicksMsec()-started} ms; scene nodes {nodes}; no performance threshold inferred.");
            _evidence.Add($"Retained managed bytes: start={retainedStart}, end={GC.GetTotalMemory(true)}; alive={batch.Alive}, submitted={batch.Submitted}, written={batch.Written}, recycled={batch.Recycled}.");
            Array.Sort(_frameTimes, 0, _frameCount);
            _evidence.Add($"Eight-car observed frame intervals ({_frameCount}): p50={_frameTimes[_frameCount / 2]:F2} ms, p95={_frameTimes[(int)(_frameCount * .95)]:F2} ms, p99={_frameTimes[(int)(_frameCount * .99)]:F2} ms; includes rendering/scheduling, not isolated GPU time.");
            await View("eight-car-stress", new Vector3(0,35,30), Vector3.Zero);
            foreach (var car in _cars) { car.GetChildren().OfType<TireFeedback>().Single().Density = 0; }
            int stopped = effects.MarksWritten;
            started = Time.GetTicksMsec();
            for (int repetition = 0; repetition < 37; repetition++)
            {
                for (int slot = 0; slot < _cars.Count; slot++) { _cars[slot].ResetBody(Physics(new Vector3(-49+slot*14,.9f,15), new Vector3(0,0,-9))); }
                await Frames(100);
                if (repetition == 28) { await View("fade-near-expiry", new Vector3(0,35,30), Vector3.Zero); }
            }
            _evidence.Add($"Eight vehicles, density zero: 3700 physics frames in {Time.GetTicksMsec()-started} ms; existing marks still fade. This is not a full GPU profiler.");
            Check(effects.MarksWritten == stopped, "Zero visual density suppresses emissions");
            await Frames(10);
            Check(batch.Submitted == 0 && _cars.All(car => car.GetChildren().OfType<TireFeedback>().Single().GetChildren()
                .OfType<MultiMeshInstance3D>().All(batch => batch.Multimesh.VisibleInstanceCount == 0)),
                "Expired tracks and wakes submit zero instances for all eight cars");
            await View("fade-expired", new Vector3(0,35,30), Vector3.Zero);
            effects.Density = 1;
            _car.ResetBody(Physics(new Vector3(0,.9f,15), new Vector3(0,0,-9)));
            await Frames(12);
            Check(effects.MarksWritten > stopped && batch.Submitted is > 0 and < 32,
                "Resumed tracks contain only new segments, without reviving the expired ring");
            await VerifyTuning(shell, settings, batch, pad);
            shell.QueueFree();
            _drive = false;
            _car = null;
            foreach (var car in _cars) { car.QueueFree(); }
            _cars.Clear();
            pad.QueueFree();
            await Frames(3);
            }
            var map = ActiveMap.Load();
            AddChild(map);
            await Frames(5);
            int mapNodes = Descendants(map);
            foreach (var preset in Enum.GetValues<EnvironmentPreset>())
            {
                environment.Apply(preset);
                await View("preset-" + preset, new Vector3(-125,13,57), new Vector3(-15,4,-25));
                await View("overview-" + preset, new Vector3(-185,105,140), Vector3.Zero);
                await MeasureRenderedMap(preset);
                Check(environment.Current == preset && Descendants(map) == mapNodes, "Preset applies without map reconstruction: " + preset);
            }
            for (int repeat = 0; repeat < 20; repeat++)
            {
                foreach (var preset in Enum.GetValues<EnvironmentPreset>()) { environment.Apply(preset); await Frames(1); }
            }
            Check(Descendants(map) == mapNodes, "100 repeated preset switches retain map topology");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output,"evidence.txt"), _evidence);
            GD.Print("Terrain effects integration passed: " + _evidence.Count + " observations.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            System.IO.File.WriteAllLines(System.IO.Path.Combine(_output,"evidence.txt"), _evidence.Append(exception.ToString()));
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static VehiclePhysicsState Physics(Vector3 position, Vector3 velocity = default) => new(VehicleBody.ToCore(position), N.Quaternion.Identity, VehicleBody.ToCore(velocity), N.Vector3.Zero);
    private async Task VerifyTuning(DevToolsShell shell, PlayerSettingsController settings, TireMarkBatch batch, StaticBody3D pad)
    {
        shell.Open(DevToolsTab.Configs);
        await Frames(20);
        LineEdit Editor(string key) => FindEditors(shell.Configs).Single(editor => editor.Name == key.Replace('.', '_'));
        void Set(string key, string value) => Editor(key).Text = value;
        float baselineWidth = batch.LastTransform.Basis.X.Length();
        Set("tire.width", "2"); Set("tire.intensity", "0.5"); Set("tire.lifetime", "4");
        Set("tire.dirt.duration", "0.5"); Set("tire.fade", "0.5"); Set("tire.budget", "256");
        Check(shell.Configs.HasUnappliedChanges && settings.Current.TireEffects["tire.width"] == 1, "Local Configs stages edits without runtime mutation");
        Check(shell.Configs.Apply(), "Local Configs Apply saves preferences without session authority");
        _car!.ResetBody(Physics(new Vector3(0,.9f,15), new Vector3(0,0,-9)));
        await Frames(30);
        if (DisplayServer.GetName() != "headless")
        {
            Check(Math.Abs(batch.LastTransform.Basis.X.Length() - baselineWidth * 2) < .01f, $"Width control changes native segment geometry: {baselineWidth:F3} -> {batch.LastTransform.Basis.X.Length():F3}");
            Check(Math.Abs(batch.LastColor.A - TireFeedback.Style(SurfaceIdentity.Dirt).Color.A * .5f) < .01f, "Intensity control changes native segment alpha");
            Check(Math.Abs(batch.LastData.G - 2) < .01f && Math.Abs(batch.LastData.B - 1) < .01f, "Global/per-surface duration and fade controls reach the shader");
        }
        else { _evidence.Add("UNVERIFIED in headless: dummy renderer has no MultiMesh buffer readback; width/alpha/shader checks require -Visual."); }
        foreach (var car in _cars) { car.GetChildren().OfType<TireFeedback>().Single().Density = 1; }
        for (int i = 0; i < 5; i++)
        {
            for (int slot = 0; slot < _cars.Count; slot++) { _cars[slot].ResetBody(Physics(new Vector3(-49+slot*14,.9f,15), new Vector3(0,0,-9))); }
            await Frames(100);
        }
        Check(batch.Budget == 256 && batch.Submitted == 256, "Lower runtime budget stays bounded under eight-car emission");
        Set("tire.quality", "0"); Check(shell.Configs.Apply(), "Zero quality applies through Configs");
        await Frames(10);
        long stopped = batch.Written;
        await Frames(130);
        Check(batch.Written == stopped && batch.Submitted == 0, "Quality zero stops all marks while tuned disturbance expires");
        Set("tire.budget", "999999"); Check(!shell.Configs.Apply() && batch.Budget == 256, "Unsafe budget is rejected without a runtime partial commit");
        shell.Configs.Cancel();
        Check(Editor("tire.budget").Text == "256" && !shell.Configs.HasUnappliedChanges, "Cancel restores accepted local graphics");
        var restored = PlayerSettingsJson.Deserialize(System.IO.File.ReadAllText(System.IO.Path.Combine(_output, "tire-settings.json")));
        Check(restored.TireEffects["tire.width"] == 2 && restored.TireEffects["tire.dirt.duration"] == .5f, "Local graphics persistence contains accepted surface tuning");
        Set("tire.quality", "1");
        foreach (var (surface, key) in new[] { (SurfaceIdentity.Asphalt, "asphalt"), (SurfaceIdentity.Concrete, "concrete"), (SurfaceIdentity.Dirt, "dirt"), (SurfaceIdentity.Grass, "grass"), (SurfaceIdentity.Mud, "mud"), (SurfaceIdentity.DeepMud, "deep_mud") })
        {
            Set($"tire.{key}.duration", "0.25");
            Check(shell.Configs.Apply(), $"{surface} duration applies through Configs");
            pad.SetMeta("surface_identity", surface.ToString());
            _hard = surface is SurfaceIdentity.Asphalt or SurfaceIdentity.Concrete;
            long before = batch.Written;
            _car.ResetBody(Physics(new Vector3(0,.9f,15), _hard ? new Vector3(7,0,-9) : new Vector3(0,0,-9)));
            await Frames(35);
            Check(batch.Written > before, $"{surface} emits with edited duration");
            if (DisplayServer.GetName() != "headless") { Check(Math.Abs(batch.LastData.G - 1) < .01f, $"{surface} edited duration reaches native shader data"); }
        }
        pad.SetMeta("surface_identity", "Dirt"); _hard = false;
        foreach (var car in _cars) { car.GetChildren().OfType<TireFeedback>().Single().Density = car == _car ? 1 : 0; }
        async Task<long> DriveSample()
        {
            _car.ResetBody(Physics(new Vector3(0,.9f,15), new Vector3(0,0,-9)));
            await Frames(3);
            long before = batch.Written;
            await Frames(60);
            return batch.Written - before;
        }
        long dense = await DriveSample();
        Set("tire.dirt.density", "0.2"); Check(shell.Configs.Apply(), "Surface density applies");
        long sparse = await DriveSample();
        Check(sparse > 0 && sparse < dense, $"Surface density reduces actual emission: dense={dense}, sparse={sparse}");
        Set("tire.speed", "30"); Check(shell.Configs.Apply(), "Speed threshold applies");
        Check(await DriveSample() == 0, "High minimum speed suppresses actual emission");
        Set("tire.speed", "0.8"); Set("tire.distance", "20"); Check(shell.Configs.Apply(), "Distance control applies");
        Check(await DriveSample() == 0, "Short visibility distance culls actual emission");
        Set("tire.distance", "100"); Check(shell.Configs.Apply(), "Distance restores");
        Check(await DriveSample() > 0, "Restored visibility distance resumes actual emission");
        if (DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            image.SavePng(System.IO.Path.Combine(_output, "tire-configs.png"));
        }
        shell.Close();
    }
    private static IEnumerable<LineEdit> FindEditors(Node node) => node.GetChildren().SelectMany(child => FindEditors(child)).Concat(node is LineEdit editor ? new[] { editor } : Array.Empty<LineEdit>());
    private static int Descendants(Node node) => 1 + node.GetChildren().Sum(Descendants);
    private async Task MeasureRenderedMap(EnvironmentPreset preset)
    {
        if (DisplayServer.GetName() == "headless") { return; }
        // Real frame intervals include renderer, scheduling and vsync. Do not label them GPU timings.
        var samples = new List<double>();
        ulong previous = Time.GetTicksUsec();
        for (int i = 0; i < 120; i++)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            ulong now = Time.GetTicksUsec();
            samples.Add((now - previous) / 1000.0);
            previous = now;
        }
        samples.Sort();
        _evidence.Add($"Rendered map {preset}: 120 overview frames, p50={samples[60]:F2} ms, p95={samples[114]:F2} ms, p99={samples[118]:F2} ms; " +
            $"draw calls={Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderTotalDrawCallsInFrame)}, " +
            $"primitives={Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderTotalPrimitivesInFrame)}, " +
            $"video memory={Godot.Performance.GetMonitor(Godot.Performance.Monitor.RenderVideoMemUsed)} bytes.");
    }
    private async Task Frames(int count) { for (int i=0;i<count;i++) { await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame); } }
    private async Task View(string name, Vector3 position, Vector3 target)
    {
        _camera.Position = position;
        _camera.LookAt(target);
        await Frames(3);
        if (DisplayServer.GetName() == "headless") { return; }
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(_output, name + ".png"));
    }
    private void Check(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
        _evidence.Add("PASS: " + message);
    }
}
