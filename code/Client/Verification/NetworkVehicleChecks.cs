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
    private readonly List<object> _largeCorrectionDetails = new();
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
    private bool _started;
    private ulong _startupMilliseconds;
    private bool _propsLaunched;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        _startupMilliseconds = Time.GetTicksMsec();
        Engine.MaxPhysicsStepsPerFrame = 64;
        Engine.MaxFps = 60;
        OS.LowProcessorUsageMode = false;
        if (DisplayServer.GetName() != "headless")
        {
            DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        }

        string[] args = OS.GetCmdlineUserArgs();
        string Value(string key, string fallback = "") => args.FirstOrDefault(argument => argument.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? fallback;
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

        _arena = new NetworkVehicleArena();
        _arena.Initialize(_gateway, host.Length > 0 ? 12345ul : 0, peer);
        _arena.Driver.LocalCorrected += state =>
        {
            var prediction = _arena.Driver.Prediction!;
            _errors.Add(prediction.PredictionError);
            if (prediction.PredictionError >= 1 && _largeCorrectionDetails.Count < 128)
            {
                _largeCorrectionDetails.Add(new { Seconds = _seconds, Error = prediction.PredictionError, Tick = state.Movement.Tick, Ack = prediction.History.LastAcknowledged, Pending = prediction.History.Pending.Count, HP = state.Damage.CurrentHP, Position = state.Movement.Physics.Position.ToString(), Speed = state.Speed });
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
            _started = driver.Host is not null ? _largestRoster == _players : driver.LocalState is not null;
            if (Time.GetTicksMsec() - _startupMilliseconds > 60000)
            {
                _done = true;
                GD.PushError("Network vehicle participants did not become ready within 60 seconds.");
                GetTree().Quit(1);
            }

            return;
        }

        _seconds += delta;
        if (!_propsLaunched && driver.Host is not null && _seconds > 7)
        {
            _propsLaunched = true;
            _arena.Layout.Explode(_arena.Layout.Props[0].GlobalPosition + new Vector3(-2, 0, 0));
        }

        uint? ack = driver.Prediction?.History.LastAcknowledged;
        int? pending = driver.Prediction?.History.Pending.Count;
        short steering = _seconds is > 1 and < 6 ? (short)18000 : (short)0;
        InputButtons drift = _seconds is > 2 and < 3 ? InputButtons.Drift : 0;
        ushort throttle = _seconds < 6 ? ushort.MaxValue : (ushort)0;
        ushort brake = _seconds >= 6 && driver.LocalState?.Speed > 0.5f ? ushort.MaxValue : (ushort)0;
        var input = new InputFrame(0, steering, throttle, brake, drift, 0, 0);
        try
        {
            _arena.Advance(input);
            _largestRoster = Math.Max(_largestRoster, driver.Latest?.Vehicles.Count ?? 0);
            if (ack.HasValue && ack == driver.Prediction!.History.LastAcknowledged && driver.Prediction.History.Pending.Count > pending)
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

            if (driver.Failure.Length > 0 && _seconds < _duration - 2)
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
        if (!_captured && _seconds > 3.5 && DisplayServer.GetName() != "headless" && _output.Length > 0)
        {
            _captured = true;
            GetViewport().GetTexture().GetImage().SavePng(_output + ".png");
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
        float propReplicaError = props is null ? float.PositiveInfinity : props.Bodies.Select((body, index) => _arena.Layout.Props[index].GlobalPosition.DistanceTo(Vehicles.VehicleBody.ToGodot(body.Position))).Max();
        var report = new
        {
            Host = driver.Host is not null,
            LargestRoster = _largestRoster,
            LocalVehicle = driver.LocalVehicleId,
            Tick = driver.LocalState.Movement.Tick,
            Snapshots = driver.ReceivedSnapshots,
            ImmediateFrames = _immediate,
            DriftFrames = _driftFrames,
            HandbrakeFrames = _handbrakeFrames,
            ErrorP99 = p99,
            ErrorMaximum = _errors.Count > 0 ? _errors.Max() : 0,
            InterpolationDelay = _arena.InterpolationDelay,
            LargeCorrections = _errors.Count(error => error >= 3),
            CorrectionDetails = _largeCorrectionDetails,
            LastAcknowledged = driver.Prediction?.History.LastAcknowledged ?? 0,
            Position = new[] { position.X, position.Y, position.Z },
            HP = driver.LocalState.Damage.CurrentHP,
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
        if (props is null || propTravel < 0.5f || (driver.Host is null && propReplicaError > 0.01f) || _largestRoster != _players || driver.LocalState.Movement.Tick < 600 || (driver.Host is null && (driver.ReceivedSnapshots < 100 || _immediate < 100 || p99 >= 3)))
        {
            throw new InvalidOperationException("Network vehicle runtime acceptance checks failed; inspect the recorded metrics.");
        }

        _captured = true;
        CallDeferred(MethodName.Complete);
    }

}
