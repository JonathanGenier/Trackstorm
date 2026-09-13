using Godot;
using Trackstorm.Client.Settings;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Settings;
using Trackstorm.Core.Vehicles;
using Numerics = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises real fixed-step bodies against an arena with optional rendered evidence.</summary>
public sealed partial class VehicleIntegrationChecks : Node
{
    private VehicleArena _arena = null!;
    private int _assertions;
    private string _output = string.Empty;
    private bool _visual;
    private Trackstorm.Client.Input.PlayerInput _input = null!;
    private PlayerSettingsController _settings = null!;
    private SettingsPanel _panel = null!;
    private SettingsHud _hud = null!;

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <summary>Runs vehicle integration scenarios and returns nonzero on any failure.</summary>
    public async void Run()
    {
        try
        {
            _output = OS.GetCmdlineUserArgs().Single(value => value.StartsWith("--vehicle-output=", StringComparison.Ordinal))[17..];
            _visual = DisplayServer.GetName() != "headless";
            _arena = new VehicleArena();
            AddChild(_arena);
            _input = new Trackstorm.Client.Input.PlayerInput();
            AddChild(_input);
            _settings = new PlayerSettingsController();
            _settings.Initialize(_input.Adapter, _output + ".settings.json");
            AddChild(_settings);
            _panel = new SettingsPanel();
            _panel.Initialize(_settings, _input.Adapter);
            _settings.AddChild(_panel);
            _hud = Descendants(_panel).OfType<SettingsHud>().Single();
            _input.FrameCaptured += Advance;
            await Settle();
            await VerifyDrivingAndRamp();
            await VerifyReverseAndDrift();
            await VerifyBrakingAndInvalidDrift();
            await VerifyPhysicalInteractions();
            await VerifyDamageAndExplosions();
            await VerifySpeedTelemetry();
            await VerifyNativeInput();
            _input.FrameCaptured -= Advance;
            _settings.QueueFree();
            _input.QueueFree();
            _arena.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            // Fixed-FPS test runs can outpace the native audio mixer. Let it release stopped playbacks before shutdown.
            await Task.Delay(100);
            GD.Print($"Vehicle integration passed: {_assertions} assertions.");
            GetTree().Quit();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            GetTree().Quit(1);
        }
    }

    private static InputFrame Frame(ulong tick, ushort throttle = 0, ushort brake = 0, short steering = 0, bool drift = false) => new(tick, steering, throttle, brake, drift ? InputButtons.Drift : InputButtons.None, InputButtons.None, InputButtons.None);

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private void Advance(InputFrame input)
    {
        _arena.Advance(input);
        _panel.SetVehicleTelemetry(_arena.Player.Snapshot.Speed);
    }

    private void Press(string text) => Descendants(_panel).OfType<Button>().Single(button => button.Text == text).EmitSignal(BaseButton.SignalName.Pressed);

    private async Task Settle()
    {
        for (int index = 0; index < 3; index++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
    }

    private async Task<List<VehicleState>> RunDrive(Vector3 position, Vector3 velocity, int ticks, Func<ulong, InputFrame> source)
    {
        VehicleBody body = _arena.Player;
        ulong startTick = _arena.Simulation.State.Tick;
        body.InputSource = tick =>
        {
            InputFrame local = source(tick - startTick);
            return new InputFrame(tick, local.Steering, local.Accelerate, local.Brake, local.Held, local.Pressed, local.Released);
        };
        var states = new List<VehicleState>();
        var completion = new TaskCompletionSource();
        void Observe(VehicleState state)
        {
            states.Add(state);
            if (state.Tick == startTick + (ulong)ticks)
            {
                completion.TrySetResult();
            }
        }

        body.Advanced += Observe;
        body.ResetBody(new VehiclePhysicsState(VehicleBody.ToCore(position), Numerics.Quaternion.Identity, VehicleBody.ToCore(velocity), Numerics.Vector3.Zero));
        await completion.Task;
        body.Advanced -= Observe;
        return states;
    }

    private async Task VerifyDrivingAndRamp()
    {
        List<VehicleState> states = await RunDrive(new Vector3(0, 1, 20), Vector3.Zero, 240, tick => Frame(tick, throttle: 65535));
        GD.Print($"Drive check: end={states.Last().Physics.Position}, peak speed={states.Max(state => state.CommandSpeed):F2}, peak height={states.Max(state => state.Physics.Position.Y):F2}, supported ticks={states.Count(state => state.Grounded)}");
        Check(states.Max(state => state.CommandSpeed) > 15, "vehicle accelerates to usable arena speed");
        Check(states.Last().Physics.Position.Z < -15, "vehicle crosses the ramp lane");
        Check(states.Skip(30).Any(state => !state.Grounded && state.Physics.Position.Y > 2), "ramp launches vehicle into airborne state");
        Check(states.Skip(160).Any(state => state.Grounded), "vehicle lands after ramp launch");
        Check(states.All(state => VehiclePhysicsState.IsFinite(state.Physics.Position) && state.CommandSpeed <= 65.001f && state.Physics.AngularVelocity.Length() <= 8.001f), "drive and landing remain bounded");
        Check(states.All(state => state.Physics.Position.Z > -41), "continuous collision detection prevents wall tunnelling");
        float[] trace = states.SelectMany(sample => new[] { sample.Physics.Position.X, sample.Physics.Position.Y, sample.Physics.Position.Z, sample.Physics.LinearVelocity.X, sample.Physics.LinearVelocity.Y, sample.Physics.LinearVelocity.Z, sample.Physics.Orientation.X, sample.Physics.Orientation.Y, sample.Physics.Orientation.Z, sample.Physics.Orientation.W, sample.Physics.AngularVelocity.X, sample.Physics.AngularVelocity.Y, sample.Physics.AngularVelocity.Z, sample.Grounded ? 1f : 0f, sample.DriftTicks, sample.BoostTicks }).ToArray();
        File.WriteAllText(_output + ".replay.json", System.Text.Json.JsonSerializer.Serialize(trace));
        await Screenshot("drive");
    }

    private async Task VerifyReverseAndDrift()
    {
        List<VehicleState> reverse = await RunDrive(new Vector3(22, 1, 15), Vector3.Zero, 120, tick => Frame(tick, brake: 65535));
        Check(reverse.Last().Physics.Position.Z > 20 && reverse.Max(state => state.Physics.LinearVelocity.Z) > 5, "vehicle reverses with brake input");
        List<VehicleState> drift = await RunDrive(new Vector3(-20, 1, 28), new Vector3(0, 0, -16), 145, tick => Frame(tick, throttle: 65535, steering: 17000, drift: tick <= 90));
        GD.Print($"Drift check: max charge={drift.Max(state => state.DriftTicks)}, supported={drift.Count(state => state.Grounded)}, drift ticks={drift.Count(state => state.Drifting)}, end={drift.Last().Physics.Position}");
        File.WriteAllLines(_output + ".drift.csv", drift.Select(state => $"{state.Tick},{state.Grounded},{state.DriftTicks},{state.Physics.Position.Y},{state.Physics.LinearVelocity.Y},{state.CommandSpeed}"));
        Check(drift.Any(state => state.Drifting && state.DriftTicks >= 39), "native vehicle maintains a charged drift");
        Check(drift.Any(state => state.BoostTicks > 0), "charged drift release produces boost");
        Check(drift.Any(state => Math.Abs(state.Physics.Orientation.Y) > 0.2f), "steering changes vehicle heading");
        Check(drift.All(state => VehiclePhysicsState.IsFinite(state.Physics.Position) && state.CommandSpeed <= 65.001f), "drift and boost remain stable");
        await Screenshot("drift");
    }

    private async Task VerifyPhysicalInteractions()
    {
        _arena.Target.ResetBody(new VehiclePhysicsState(new Numerics.Vector3(22, 1, -5), Numerics.Quaternion.Identity, Numerics.Vector3.Zero, Numerics.Vector3.Zero));
        await RunDrive(new Vector3(22, 1, 10), new Vector3(0, 0, -22), 90, tick => Frame(tick));
        Check(_arena.Target.GlobalPosition.Z < -6, "vehicle contact transfers momentum to the target");
        GD.Print($"Collision HP: player={_arena.Player.DamageState.CurrentHP:F2}, target={_arena.Target.DamageState.CurrentHP:F2}");
        Check(_arena.Player.DamageState.CurrentHP < 100 && _arena.Target.DamageState.CurrentHP < 100, "hard vehicle contact damages both vehicles");
        Check(_arena.Player.DamageState.LastDamage?.Attribution.InstigatorId == 2 && _arena.Target.DamageState.LastDamage?.Attribution.InstigatorId == 1, "both collision victims retain other vehicle attribution");
        _arena.Crate.Position = new Vector3(12, 1, 5);
        _arena.Crate.LinearVelocity = Vector3.Zero;
        await RunDrive(new Vector3(12, 1, 15), new Vector3(0, 0, -20), 90, tick => Frame(tick));
        Check(_arena.Crate.GlobalPosition.DistanceTo(new Vector3(12, 1, 5)) > 2, "vehicle physically pushes movable props");
        List<VehicleState> wall = await RunDrive(new Vector3(28, 1, -28), new Vector3(0, 0, -55), 60, tick => Frame(tick));
        Check(wall.All(state => state.Physics.Position.Z > -40.5f) && wall.Last().CommandSpeed < 20, "high-speed impact stops at static wall");
        await Screenshot("collision");
    }

    private async Task VerifyBrakingAndInvalidDrift()
    {
        List<VehicleState> braking = await RunDrive(new Vector3(-25, 0.5f, 20), new Vector3(0, 0, -12), 90, tick => Frame(tick, brake: 65535));
        Check(braking.First().Physics.LinearVelocity.Z < -10 && braking.Any(state => Math.Abs(state.Physics.LinearVelocity.Z) < 0.5f) && braking.Last().Physics.LinearVelocity.Z > 2, "native braking slows forward travel through rest before reversing");
        List<VehicleState> stationary = await RunDrive(new Vector3(-25, 0.5f, 20), Vector3.Zero, 60, tick => Frame(tick, steering: 32767, drift: tick < 50));
        Check(stationary.All(state => !state.Drifting && state.DriftTicks == 0 && state.BoostTicks == 0), "stationary native vehicle cannot charge or release a drift boost");
        List<VehicleState> airborne = await RunDrive(new Vector3(-25, 8, 20), new Vector3(0, 15, -15), 30, tick => Frame(tick, steering: 32767, drift: tick < 20));
        Check(airborne.All(state => !state.Grounded && !state.Drifting && state.DriftTicks == 0 && state.BoostTicks == 0 && state.CommandSpeed < 65), "airborne native turning cannot charge or release boost and remains bounded");
    }

    private async Task VerifyDamageAndExplosions()
    {
        _arena.Target.ResetBody(new VehiclePhysicsState(new Numerics.Vector3(25, 0.5f, -5), Numerics.Quaternion.Identity, Numerics.Vector3.Zero, Numerics.Vector3.Zero));
        int brushingTicks = 0;
        void ObserveBrush(VehicleState state)
        {
            if (_arena.Player.GetCollidingBodies().Contains(_arena.Target))
            {
                brushingTicks++;
            }
        }

        _arena.Player.Advanced += ObserveBrush;
        await RunDrive(new Vector3(25, 0.5f, -1.3f), new Vector3(0, 0, -1), 180, tick => Frame(tick, throttle: 14000));
        _arena.Player.Advanced -= ObserveBrush;
        GD.Print($"Brush check: contacts={brushingTicks}, player HP={_arena.Player.DamageState.CurrentHP:F2}, target HP={_arena.Target.DamageState.CurrentHP:F2}");
        Check(brushingTicks > 30, "minor brushing scenario makes sustained real vehicle contact");
        Check(_arena.Player.DamageState.CurrentHP == 100 && _arena.Target.DamageState.CurrentHP == 100, "sustained minor contact does not drain either vehicle HP");

        await RunDrive(new Vector3(-20, 0.5f, 20), Vector3.Zero, 30, tick => Frame(tick));
        Vector3 start = _arena.Player.GlobalPosition;
        int cues = _arena.Player.FeedbackCueCount;
        _arena.Explode(start + new Vector3(-2, -0.2f, 0.5f));
        List<VehicleState> blast = await ObserveTicks(12);
        Check(_arena.Player.Snapshot.Speed > 1, "horizontal external impulse contributes to observed travel speed");
        await Screenshot("explosion");
        blast.AddRange(await ObserveTicks(168));
        GD.Print($"Blast check: max height={blast.Max(state => state.Physics.Position.Y):F2}, peak angular={blast.Max(state => state.Physics.AngularVelocity.Length()):F2}, end={blast.Last().Physics.Position}, HP={_arena.Player.DamageState.CurrentHP:F2}");
        Check(_arena.Player.GlobalPosition.DistanceTo(start) > 3, "explosion translates the vehicle");
        Check(blast.Any(state => state.Physics.AngularVelocity.Length() > 1 && Math.Abs(state.Physics.Orientation.X) + Math.Abs(state.Physics.Orientation.Z) > 0.1f), "off-center explosion visibly rotates the vehicle");
        Check(blast.All(state => VehiclePhysicsState.IsFinite(state.Physics.Position) && state.CommandSpeed <= 65.001f && state.Physics.AngularVelocity.Length() <= 8.001f), "explosion response stays finite and bounded");
        Check(blast.TakeLast(30).Any(state => state.Grounded), "vehicle returns to supported movement after explosion");
        Check(_arena.Player.DamageState.CurrentHP is > 0 and < 100 && _arena.Player.FeedbackCueCount > cues, "explosion applies HP damage and submits visual/audio feedback");
        await Screenshot("recovery");

        var source = new DamageContext("missile", 99, "integration-generic-hit");
        _arena.Player.ApplyEffect(new DamageEffect(1000, new Numerics.Vector3(900, 0, 0), new Numerics.Vector3(0, 0, 1)), source);
        await ObserveTicks(2);
        DamageEvent death = _arena.Player.DamageState.LastDamage!;
        Check(_arena.Player.DamageState.Destroyed && death.DestroyedTransition && death.Attribution == source, "generic combat hit destroys local vehicle with retained attribution");
        _arena.Player.ApplyEffect(new DamageEffect(1000, Numerics.Vector3.Zero, Numerics.Vector3.Zero), source);
        _arena.Player.InputSource = tick => Frame(tick, throttle: 65535, steering: 32767, drift: true);
        await ObserveTicks(90);
        Check(_arena.Player.DamageState.LastDamage == death && _arena.Player.State.BoostTicks == 0 && !_arena.Player.State.Drifting, "wreck cannot repeat its death transition or charge drive boost");
        await Screenshot("destroyed");
        ulong life = _arena.Player.Snapshot.LifeId;
        ulong globalTick = _arena.Simulation.State.Tick;
        await RunDrive(new Vector3(-20, 0.5f, 20), Vector3.Zero, 60, tick => Frame(tick, throttle: 65535));
        Check(!_arena.Player.DamageState.Destroyed && _arena.Player.DamageState.CurrentHP == 100 && _arena.Player.State.CommandSpeed > 5, "explicit reset restores health and driving");
        Check(_arena.Player.Snapshot.LifeId == life + 1 && _arena.Simulation.State.Tick == globalTick + 60 && _arena.Simulation.State.Vehicles.All(vehicle => vehicle.Movement.Tick == globalTick + 60), "reset starts one new Core life while both vehicles retain the global clock");
    }

    private async Task VerifySpeedTelemetry()
    {
        await RunDrive(new Vector3(-25, 0.5f, 25), Vector3.Zero, 90, tick => Frame(tick));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_arena.Player.Snapshot.Speed < 0.01f && _arena.Player.State.CommandSpeed > 0.3f, "stationary solved road speed excludes the downward gravity command");
        Check(_hud.SpeedText == "Speed  0.0 km/h", "stationary HUD reads zero in km/h");
        GD.Print($"Stationary speed: observed={_arena.Player.Snapshot.Speed:F4} m/s, command={_arena.Player.State.CommandSpeed:F4} m/s, HUD={_hud.SpeedText}");
        _settings.UpdateSettings(_settings.Current with { SpeedUnit = SpeedUnit.MilesPerHour });
        Check(_hud.SpeedText == "Speed  0.0 mph", "stationary HUD reads zero in mph");
        await Screenshot("stationary-speed");
        _settings.UpdateSettings(_settings.Current with { SpeedUnit = SpeedUnit.KilometresPerHour });

        await RunDrive(new Vector3(-25, 0.5f, 25), Vector3.Zero, 60, tick => Frame(tick, throttle: 65535));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_arena.Player.Snapshot.Speed > 8, "solved forward travel produces positive HUD speed");
        VerifyHudConversion(3.6, "km/h");
        _settings.UpdateSettings(_settings.Current with { SpeedUnit = SpeedUnit.MilesPerHour });
        VerifyHudConversion(2.2369362920544, "mph");
        _settings.UpdateSettings(_settings.Current with { SpeedUnit = SpeedUnit.KilometresPerHour });

        await RunDrive(new Vector3(-25, 0.5f, 15), Vector3.Zero, 60, tick => Frame(tick, brake: 65535));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_arena.Player.Snapshot.ObservedPhysics.LinearVelocity.Z > 3 && _arena.Player.Snapshot.Speed > 3, "reverse travel displays a nonnegative magnitude");
        GD.Print($"Reverse speed: observed={_arena.Player.Snapshot.Speed:F2} m/s, HUD={_hud.SpeedText}");
        VerifyHudConversion(3.6, "km/h");

        await RunDrive(new Vector3(-25, 5, 25), new Vector3(0, 15, 0), 4, tick => Frame(tick));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_arena.Player.Snapshot.ObservedPhysics.LinearVelocity.Y > 10 && _hud.SpeedText == "Speed  0.0 km/h", "pure vertical launch does not inflate the road-speed HUD");
        await RunDrive(new Vector3(-25, 5, 25), new Vector3(0, 15, -10), 4, tick => Frame(tick));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_arena.Player.Snapshot.ObservedPhysics.LinearVelocity.Y > 10 && Math.Abs(_arena.Player.Snapshot.Speed - 10) < 0.01f, "vertical launch preserves horizontal road speed");
        VerifyHudConversion(3.6, "km/h");
    }

    private void VerifyHudConversion(double factor, string unit)
    {
        string value = (_arena.Player.Snapshot.Speed * factor).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        Check(_hud.SpeedText == $"Speed  {value} {unit}", "production HUD converts the committed observed speed into preferred units");
    }

    private async Task<List<VehicleState>> ObserveTicks(int count)
    {
        var states = new List<VehicleState>();
        var completion = new TaskCompletionSource();
        void Observe(VehicleState state)
        {
            states.Add(state);
            if (states.Count == count)
            {
                completion.TrySetResult();
            }
        }

        _arena.Player.Advanced += Observe;
        await completion.Task;
        _arena.Player.Advanced -= Observe;
        return states;
    }

    private async Task VerifyNativeInput()
    {
        await RunDrive(new Vector3(-25, 0.5f, 25), Vector3.Zero, 2, tick => Frame(tick));
        _arena.Player.InputSource = null;
        Trackstorm.Client.Input.PlayerInput input = _input;
        using var press = new InputEventKey { PhysicalKeycode = Key.W, Pressed = true };
        using var release = new InputEventKey { PhysicalKeycode = Key.W, Pressed = false };
        try
        {
            Godot.Input.ParseInputEvent(press);
            Godot.Input.FlushBufferedEvents();
            await ObserveTicks(90);
            Check(_arena.Player.State.CommandSpeed > 10 && _arena.Player.GlobalPosition.Z < 20, "native W input flows through production capture into vehicle movement");
            Press("Settings");
            await ObserveTicks(3);
            Check(input.Adapter.GameplaySuppressed && input.LatestFrame.Accelerate == 0, "opening the production settings panel neutralizes held driving input");
            Press("Done");
            Check(!input.Adapter.GameplaySuppressed, "closing settings resumes the gameplay input path");
            await Screenshot("input");
            if (_visual)
            {
                Vector2I previous = DisplayServer.WindowGetSize();
                DisplayServer.WindowSetSize(new Vector2I(640, 360));
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await Screenshot("compact-hud");
                DisplayServer.WindowSetSize(previous);
            }
        }
        finally
        {
            Godot.Input.ParseInputEvent(release);
            Godot.Input.FlushBufferedEvents();
            input.Adapter.GameplaySuppressed = false;
        }
    }

    private async Task Screenshot(string name)
    {
        if (_visual)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image image = GetViewport().GetTexture().GetImage();
            Check(image.SavePng(_output + "." + name + ".png") == Error.Ok, "rendered vehicle evidence saved");
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }

        _assertions++;
    }
}
