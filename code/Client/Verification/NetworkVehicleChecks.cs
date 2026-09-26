using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Transport;

namespace Trackstorm.Client.Verification;

/// <summary>Separate-process Godot vehicle replication checks using production native collision and transport paths.</summary>
public sealed partial class NetworkVehicleChecks : Node
{
    private readonly List<float> _errors = new();
    private readonly List<double> _startupErrors = new();
    private readonly List<double> _steadyErrors = new();
    private readonly List<double> _snapshotAges = new();
    private readonly List<double> _interpolationDelays = new();
    private readonly List<double> _pings = new();
    private readonly List<double> _timelineDelays = new();
    private int _maximumPending;
    private int _limitedFrames;
    private int _steadyLimitedFrames;
    private int _startupHardSnaps;
    private ulong _serverPeer;
    private readonly List<object> _largeCorrectionDetails = new();
    private readonly List<double> _frameMilliseconds = new();
    private readonly List<double> _stepMilliseconds = new();
    private readonly List<double> _stepAllocatedBytes = new();
    private readonly List<object> _frameStalls = new();
    private ulong _lastFrameMicroseconds;
    private GameNetworkingSocketsTransport _gateway = null!;
    private NetworkVehicleArena _arena = null!;
    private double _seconds;
    private double _duration;
    private int _players;
    private int _largestRoster;
    private int _immediate;
    private int _driftFrames;
    private int _handbrakeFrames;
    private string _output = string.Empty;
    private bool _done;
    private bool _captured;
    private bool _captureDuringDriving;
    private double _captureMilliseconds;
    private bool _started;
    private ulong _startupMilliseconds;
    private bool _propsLaunched;
    private bool _prototype;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        _startupMilliseconds = Time.GetTicksMsec();
        Engine.MaxFps = 60;
        OS.LowProcessorUsageMode = false;
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        }

        string[] args = OS.GetCmdlineUserArgs();
        _captureDuringDriving = args.Contains("--network-check-capture-during-driving", StringComparer.Ordinal);
        string Value(string key, string fallback = "") => args.FirstOrDefault(argument => argument.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? fallback;
        // Match application scheduling by default. The historical 64-step overload remains
        // an explicit diagnostic, not the ordinary-WAN correction-quality scenario.
        Engine.MaxPhysicsStepsPerFrame = int.Parse(Value("--network-check-catch-up", "8"), System.Globalization.CultureInfo.InvariantCulture);
        _duration = double.Parse(Value("--network-check-seconds", "16"), System.Globalization.CultureInfo.InvariantCulture);
        _players = int.Parse(Value("--network-check-players", "2"), System.Globalization.CultureInfo.InvariantCulture);
        _output = Value("--network-check-output");
        string host = Value("--network-check-host");
        _gateway = new GameNetworkingSocketsTransport();
        _gateway.ConfigureSimulation(new NetworkSimulation(int.Parse(Value("--network-check-latency", "0"), System.Globalization.CultureInfo.InvariantCulture), int.Parse(Value("--network-check-jitter", "0"), System.Globalization.CultureInfo.InvariantCulture), float.Parse(Value("--network-check-loss", "0"), System.Globalization.CultureInfo.InvariantCulture), 0, 0));
        ulong peer = 0;
        if (host.Length > 0)
        {
            _gateway.Listen(TransportEndpoint.DirectIp(host));
        }
        else
        {
            peer = _gateway.Connect(TransportEndpoint.DirectIp(Value("--network-check-client")));
        }

        _prototype = args.Contains("--network-check-prototype", StringComparer.Ordinal);
        _serverPeer = peer;
        _arena = new NetworkVehicleArena { PrototypeMapForVerification = _prototype };
        _arena.Initialize(_gateway, host.Length > 0 ? 12345ul : 0, peer);
        _arena.Driver.LocalCorrected += state =>
        {
            var prediction = _arena.Driver.Prediction!;
            _errors.Add(prediction.PredictionError);
            (_seconds < 2 ? _startupErrors : _steadyErrors).Add(prediction.PredictionError);
            if (_seconds < 2) { _startupHardSnaps = _arena.Bodies[state.VehicleId].Smoothing.HardSnaps; }
            if (prediction.PredictionError >= 0.1 && _largeCorrectionDetails.Count < 128)
            {
                var authority = _arena.Driver.Latest!.Vehicles.Single(vehicle => vehicle.State.VehicleId == state.VehicleId).State;
                _largeCorrectionDetails.Add(new { Seconds = _seconds, Error = prediction.PredictionError, Tick = state.Movement.Tick, AuthorityTick = authority.Movement.Tick, Life = state.LifeId, Lifecycle = state.Lifecycle.ToString(), Effects = authority.Effects.Count, DamageTick = authority.Damage.LastDamage?.Tick, Ack = prediction.History.LastAcknowledged, Pending = prediction.History.Pending.Count, SnapshotAge = _arena.Driver.SnapshotAge, FrameMilliseconds = _frameMilliseconds.LastOrDefault(), HP = state.Damage.CurrentHP, Position = state.Movement.Physics.Position.ToString(), Speed = state.Speed });
            }
        };
        AddChild(_arena);
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_done)
        {
            return;
        }

        var driver = _arena.Driver;
        if (!_started)
        {
            _arena.Advance(default);
            _largestRoster = Math.Max(_largestRoster, driver.Latest?.Vehicles.Count ?? 0);
            _started = _largestRoster == _players && driver.LocalState is not null;
            if (_started && !_prototype)
            {
                var spawn = _arena.MapConfiguration.Spawn((int)driver.LocalVehicleId - 1).Position;
                var position = driver.LocalState!.Movement.Physics.Position;
                if (new System.Numerics.Vector2(position.X - spawn.X, position.Z - spawn.Z).Length() > 0.2f)
                {
                    GD.PushError("Initial network vehicle did not settle on its authored oval grid slot.");
                    GetTree().Quit(1);
                }
            }

            if (Time.GetTicksMsec() - _startupMilliseconds > 60000)
            {
                _done = true;
                GD.PushError("Network vehicle participants did not become ready within 60 seconds.");
                GetTree().Quit(1);
            }

            return;
        }

        _seconds += delta;
        if (_prototype && !_propsLaunched && driver.Host is not null && _seconds > 7)
        {
            _propsLaunched = true;
            _arena.Layout.Explode(_arena.Layout.Props[0].GlobalPosition + new Vector3(-2, 0, 0));
        }

        uint? ack = driver.Prediction?.History.LastAcknowledged;
        int? pending = driver.Prediction?.History.Pending.Count;
        ulong? predictedTick = driver.LocalState?.Movement.Tick;
        short steering = _seconds is > 1 and < 6 ? (short)18000 : (short)0;
        if (!_prototype)
        {
            // Turn inward from the +X grid into the flat infield; the foundation has no perimeter walls.
            Vector3 forward = Vehicles.VehicleBody.ToGodot(System.Numerics.Vector3.Transform(-System.Numerics.Vector3.UnitZ, driver.LocalState!.Movement.Physics.Orientation));
            float angle = new Vector3(forward.X, 0, forward.Z).SignedAngleTo(new Vector3(1, 0, -0.8f), Vector3.Up);
            steering = (short)(Math.Clamp(-angle * 3, -1, 1) * short.MaxValue);
        }

        InputButtons drift = _seconds is > 2 and < 3 ? InputButtons.Drift : 0;
        ushort throttle = _seconds < 6 ? ushort.MaxValue : (ushort)0;
        if (!_prototype && driver.LocalState!.Speed > 12)
        {
            throttle = 0;
        }

        ushort brake = _seconds >= 6 && driver.LocalState?.Speed > 0.5f ? ushort.MaxValue : (ushort)0;
        var input = new InputFrame(0, steering, throttle, brake, drift, 0, 0);
        try
        {
            ulong stepStarted = Time.GetTicksUsec();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            _arena.Advance(input);
            _stepAllocatedBytes.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
            _stepMilliseconds.Add((Time.GetTicksUsec() - stepStarted) / 1000.0);
            _maximumPending = Math.Max(_maximumPending, driver.Inputs?.Pending.Count ?? 0);
            if (driver.Prediction?.IsPredictionLimited == true)
            {
                _limitedFrames++;
                if (_seconds >= 2) { _steadyLimitedFrames++; }
            }
            if (driver.SnapshotAge is double age) { _snapshotAges.Add(age * 1000); }
            _interpolationDelays.Add(_arena.InterpolationDelay);
            _timelineDelays.Add(_arena.InterpolationTimelineDelay);
            if (_serverPeer != 0 && _gateway.GetStatistics(_serverPeer).PingMilliseconds is int ping) { _pings.Add(ping); }
            _largestRoster = Math.Max(_largestRoster, driver.Latest?.Vehicles.Count ?? 0);
            if (ack.HasValue && ack == driver.Prediction!.History.LastAcknowledged && driver.Prediction.History.Pending.Count > pending && driver.LocalState!.Movement.Tick > predictedTick)
            {
                _immediate++;
            }

            if (driver.LocalState?.Movement.Drifting == true)
            {
                _driftFrames++;
            }

            if (driver.LocalState?.Movement.Handbrake > 0)
            {
                _handbrakeFrames++;
            }

            if (driver.Failure.Length > 0 && _seconds < _duration - 4)
            {
                throw new InvalidOperationException(driver.Failure);
            }

            if (_seconds >= _duration)
            {
                Finish();
            }
        }
        catch (Exception exception)
        {
            _done = true;
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        ulong now = Time.GetTicksUsec();
        if (_started && !_done && _lastFrameMicroseconds != 0)
        {
            double milliseconds = (now - _lastFrameMicroseconds) / 1000.0;
            _frameMilliseconds.Add(milliseconds);
            if (milliseconds >= 100 && _frameStalls.Count < 128)
            {
                _frameStalls.Add(new { Seconds = _seconds, Milliseconds = milliseconds, Tick = _arena.Driver.LocalState?.Movement.Tick, SnapshotAge = _arena.Driver.SnapshotAge });
            }
        }

        _lastFrameMicroseconds = now;
        if (_captureDuringDriving && !_captured && _seconds > 3.5 && DisplayServer.GetName() != "headless" && _output.Length > 0)
        {
            _captured = true;
            ulong captureStarted = Time.GetTicksUsec();
            GetViewport().GetTexture().GetImage().SavePng(_output + ".png");
            _captureMilliseconds = (Time.GetTicksUsec() - captureStarted) / 1000.0;
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _gateway.ConfigureSimulation(new());
        _gateway.Dispose();
    }

    /// <summary>Releases the arena while native audio can still process queued stop commands before process shutdown.</summary>
    public async void Complete()
    {
        try
        {
            if (!_captureDuringDriving && DisplayServer.GetName() != "headless" && _output.Length > 0)
            {
                // GPU readback and PNG encoding block this thread. Capture only after
                // correction/frame measurements finish, unless explicitly diagnosing it.
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(_output + ".png");
            }
            var input = new Input.PlayerInput();
            AddChild(input);
            input.SetPhysicsProcess(false);
            await CameraPlaytest.Run(this, _arena.GetNode<Vehicles.VehicleChaseCamera>("ChaseCamera"), input,
                () => _arena.Bodies[_arena.Driver.LocalVehicleId].VisualTransform, _output);
            input.QueueFree();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
            return;
        }

        _arena.QueueFree();
        for (int i = 0; i < 6; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        // Match the vehicle harness: deferred node frees can finish before the native mixer releases playbacks.
        await Task.Delay(100);
        GD.Print("Network vehicle integration passed.");
        GetTree().Quit();
    }

    private void Finish()
    {
        _done = true;
        var driver = _arena.Driver;
        _errors.Sort();
        float p99 = _errors.Count > 0 ? _errors[(int)((_errors.Count - 1) * 0.99)] : 0;
        var position = driver.LocalState!.Movement.Physics.Position;
        var props = driver.PropSnapshot;
        float propTravel = props is null ? 0 : System.Numerics.Vector3.Distance(props.Bodies[0].Position, new System.Numerics.Vector3(-8, 1, -6));
        float propReplicaError = props is null ? 0 : props.Bodies.Select((body, index) => _arena.Layout.Props[index].GlobalPosition.DistanceTo(Vehicles.VehicleBody.ToGodot(body.Position))).Max();
        if (!_prototype)
        {
            OvalGameplayAssertions.Verify(_arena);
        }

        var report = new
        {
            Rendered = DisplayServer.GetName() != "headless",
            CaptureDuringDriving = _captureDuringDriving,
            InDriveCaptureMilliseconds = _captureMilliseconds,
            FrameTime = Timing(_frameMilliseconds),
            SimulationStepTime = Timing(_stepMilliseconds),
            SimulationStepAllocatedBytes = Timing(_stepAllocatedBytes),
            FrameStalls = _frameStalls,
            MaximumCatchUpSteps = Engine.MaxPhysicsStepsPerFrame,
            Host = driver.Host is not null,
            Map = _arena.Map.SceneFilePath,
            LargestRoster = _largestRoster,
            LocalVehicle = driver.LocalVehicleId,
            Tick = driver.LocalState.Movement.Tick,
            Snapshots = driver.ReceivedSnapshots,
            ImmediateFrames = _immediate,
            DriftFrames = _driftFrames,
            HandbrakeFrames = _handbrakeFrames,
            ErrorP99 = p99,
            ErrorMaximum = _errors.Count > 0 ? _errors.Max() : 0,
            Startup = Corrections(_startupErrors, Math.Min(2, _seconds)),
            Steady = Corrections(_steadyErrors, Math.Max(0, _seconds - 2)),
            SnapshotAgeMilliseconds = Timing(_snapshotAges),
            BufferedDelayMilliseconds = Timing(_interpolationDelays),
            ReceivedTimelineDelayMilliseconds = Timing(_timelineDelays),
            InterpolationRecoveries = _arena.InterpolationRecoveries,
            PingMilliseconds = Timing(_pings),
            MaximumPending = _maximumPending,
            PredictionLimitedFrames = _limitedFrames,
            SteadyPredictionLimitedFrames = _steadyLimitedFrames,
            FinalPending = driver.Inputs?.Pending.Count,
            HardSnaps = _arena.Bodies[driver.LocalVehicleId].Smoothing.HardSnaps,
            StartupHardSnaps = _startupHardSnaps,
            SteadyHardSnaps = _arena.Bodies[driver.LocalVehicleId].Smoothing.HardSnaps - _startupHardSnaps,
            ExactPredictionConfirmations = driver.Prediction?.ConfirmedPredictions,
            InterpolationDelay = _arena.InterpolationDelay,
            LargeCorrections = _errors.Count(error => error >= 3),
            CorrectionDetails = _largeCorrectionDetails,
            LastAcknowledged = driver.Prediction?.History.LastAcknowledged ?? 0,
            Position = new[] { position.X, position.Y, position.Z },
            HP = driver.LocalState.Damage.CurrentHP,
            Grounded = driver.LocalState.Movement.Grounded,
            PropTick = props?.Tick,
            PropTravel = propTravel,
            PropReplicaError = propReplicaError,
            PropPositions = props?.Bodies.Select(body => new[] { body.Position.X, body.Position.Y, body.Position.Z }).ToArray(),
        };
        string json = JsonSerializer.Serialize(report);
        if (_output.Length > 0)
        {
            System.IO.File.WriteAllText(_output + ".json", json);
        }

        GD.Print(json);
        bool invalidProps = _prototype ? props is null || propTravel < 0.5f || (driver.Host is null && propReplicaError > 0.01f) : props is not null;
        bool unsupported = !_prototype && (!driver.LocalState.Movement.Grounded || position.Y < 0);
        double steadyP99 = _steadyErrors.Order().ElementAtOrDefault(Math.Min(_steadyErrors.Count - 1, (int)(_steadyErrors.Count * 0.99)));
        bool poorCorrections = driver.Host is null && !_prototype && Engine.MaxPhysicsStepsPerFrame == 8 &&
            (_startupErrors.DefaultIfEmpty().Max() >= 1 || steadyP99 >= 0.25 ||
             _steadyErrors.DefaultIfEmpty().Max() >= 1 || _arena.Bodies[driver.LocalVehicleId].Smoothing.HardSnaps > 0 ||
             _steadyLimitedFrames > (_players == 2 ? 0 : 12));
        if (invalidProps || unsupported || poorCorrections || _largestRoster != _players || driver.LocalState.Movement.Tick < 600 || (driver.Host is null && (driver.ReceivedSnapshots < 100 || _immediate < 100 || p99 >= 3)))
        {
            throw new InvalidOperationException("Network vehicle runtime acceptance checks failed; inspect the recorded metrics.");
        }

        _captured = true;
        CallDeferred(MethodName.Complete);
    }

    private static object Timing(List<double> samples)
    {
        double[] ordered = samples.Order().ToArray();
        double Percentile(double fraction) => ordered.Length == 0 ? 0 : ordered[Math.Min(ordered.Length - 1, (int)(ordered.Length * fraction))];
        return new { Samples = ordered.Length, Mean = ordered.Length == 0 ? 0 : ordered.Average(), P50 = Percentile(0.5), P95 = Percentile(0.95), P99 = Percentile(0.99), Maximum = ordered.LastOrDefault() };
    }

    private static object Corrections(List<double> samples, double seconds) => new
    {
        Distribution = Timing(samples),
        AboveCentimetrePerSecond = seconds > 0 ? samples.Count(value => value > 0.01) / seconds : 0,
        UnderCentimetre = samples.Count(value => value <= 0.01),
        CentimetreToDecimetre = samples.Count(value => value > 0.01 && value < 0.1),
        DecimetreToMetre = samples.Count(value => value >= 0.1 && value < 1),
        MetreToThree = samples.Count(value => value >= 1 && value < 3),
        AtLeastThree = samples.Count(value => value >= 3),
    };

}
