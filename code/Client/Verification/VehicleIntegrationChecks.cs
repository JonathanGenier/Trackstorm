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
    private float _shakePeak;

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_arena is not null && _settings is not null)
        {
            var camera = _arena.GetNode<VehicleChaseCamera>("ChaseCamera");
            _shakePeak = Math.Max(_shakePeak, Math.Abs(camera.Motion.ShakeOffset * camera.MaximumShakeMetres * (float)_settings.Current.CameraShakeIntensity));
        }
    }

    /// <inheritdoc/>
    public override void _Ready() => CallDeferred(MethodName.Run);

    /// <summary>Runs vehicle integration scenarios and returns nonzero on any failure.</summary>
    public async void Run()
    {
        try
        {
            _output = OS.GetCmdlineUserArgs().Single(value => value.StartsWith("--vehicle-output=", StringComparison.Ordinal))[17..];
            _visual = DisplayServer.GetName() != "headless";
            _arena = new VehicleArena { LegacyTestLayout = true };
            AddChild(_arena);
            _input = new Trackstorm.Client.Input.PlayerInput();
            AddChild(_input);
            _settings = new PlayerSettingsController();
            _settings.Initialize(_input.Adapter, _output + ".settings.json");
            AddChild(_settings);
            _arena.CameraSettings = _settings;
            _panel = new SettingsPanel();
            _panel.Initialize(_settings, _input.Adapter);
            _settings.AddChild(_panel);
            _hud = Descendants(_panel).OfType<SettingsHud>().Single();
            _input.FrameCaptured += Advance;
            await Settle();
            VerifyVisualBinding();
            await VerifyDrivingAndRamp();
            await VerifyReverseAndDrift();
            await VerifyBrakingAndInvalidDrift();
            await VerifyCorneringAndRecovery();
            await VerifyResponsiveHandbrake();
            await VerifyPowerThroughSlide();
            await VerifyPhysicalInteractions();
            await VerifyDamageAndExplosions();
            await VerifyAdjustableShake();
            await VerifySpeedTelemetry();
            await VerifyNativeInput();
            await VerifySurfaces();
            await CameraPlaytest.Run(this, _arena.GetNode<VehicleChaseCamera>("ChaseCamera"), _input,
                () => _arena.Player.GetGlobalTransformInterpolated(), _output,
                () => _arena.Player.ResetBody(_arena.Player.Snapshot.Movement.Physics));
            _input.FrameCaptured -= Advance;
            SetProcess(false);
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

    private void VerifyVisualBinding()
    {
        VehicleSnapshot authority = _arena.Player.Snapshot;
        Node3D localModel = _arena.Player.GetNode<Node3D>("WastelandVehicle");
        Check(localModel.Transform.IsEqualApprox(Transform3D.Identity), "Static model is bound at the physics origin.");
        Check(!Descendants(localModel).Any(node => node is CollisionObject3D or CollisionShape3D or AnimationPlayer or AnimationTree), "Visual asset has no collision or animation layer.");
        var meshes = Descendants(localModel).OfType<MeshInstance3D>().ToArray();
        Check(meshes.Length > 10 && meshes.All(mesh => mesh.Mesh is not null), "Vehicle asset resolves all base and conversion meshes.");
        Aabb bounds = meshes.Select(mesh => (localModel.GlobalTransform.AffineInverse() * mesh.GlobalTransform) * mesh.GetAabb()).Aggregate((left, right) => left.Merge(right));
        Check(Math.Abs(bounds.Size.X - VehicleDimensions.Width) < 0.001f && Math.Abs(bounds.Size.Z - VehicleDimensions.Length) < 0.001f, "Authored silhouette has the canonical real-world dimensions.");
        Check(Math.Abs(bounds.Position.Y + VehicleDimensions.RideHeight) < 0.01f, "Static tires match the settled suspension ride height.");

        var network = new Networking.NetworkVehicleBody { VehicleId = 999 };
        AddChild(network);
        network.Apply(authority);
        Node3D remoteModel = Descendants(network).OfType<Node3D>().Single(node => node.Name == "WastelandVehicle");
        var parts = Descendants(remoteModel).OfType<Node3D>().ToDictionary(node => node, node => node.Transform);
        Transform3D collision = network.GlobalTransform;
        var pose = authority.Movement.Physics;
        var launched = new VehiclePhysicsState(pose.Position + new Numerics.Vector3(3, 5, -2), Numerics.Quaternion.CreateFromYawPitchRoll(0.7f, 1.2f, 2.5f), pose.LinearVelocity, pose.AngularVelocity);
        network.PresentRemote(launched);
        Check(remoteModel.GlobalTransform.IsEqualApprox(network.VisualTransform), "Complete remote model follows the interpolated launch/flip pose.");
        Check(network.GlobalTransform.IsEqualApprox(collision), "Remote rendering does not move the authoritative collision proxy.");
        network.Apply(launched, true);
        network.PresentLocal(1f / 60);
        Check(remoteModel.GlobalTransform.IsEqualApprox(network.VisualTransform), "Complete local model follows prediction correction smoothing.");
        Check(parts.All(part => part.Key.Transform.IsEqualApprox(part.Value)), "Reconciliation and interpolation do not articulate or detach model parts.");
        Check(ReferenceEquals(authority, _arena.Player.Snapshot), "Visual binding leaves committed authority unchanged.");
        network.QueueFree();
    }

    private void Press(string text) => Descendants(_panel).OfType<Button>().Single(button => button.Text == text && button.IsVisibleInTree()).EmitSignal(BaseButton.SignalName.Pressed);

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
        float[] trace = states.SelectMany(sample => new[] { sample.Physics.Position.X, sample.Physics.Position.Y, sample.Physics.Position.Z, sample.Physics.LinearVelocity.X, sample.Physics.LinearVelocity.Y, sample.Physics.LinearVelocity.Z, sample.Physics.Orientation.X, sample.Physics.Orientation.Y, sample.Physics.Orientation.Z, sample.Physics.Orientation.W, sample.Physics.AngularVelocity.X, sample.Physics.AngularVelocity.Y, sample.Physics.AngularVelocity.Z, sample.Grounded ? 1f : 0f, sample.SteeringAngle, sample.Handbrake }).ToArray();
        File.WriteAllText(_output + ".replay.json", System.Text.Json.JsonSerializer.Serialize(trace));
        await Screenshot("drive");
    }

    private async Task VerifyReverseAndDrift()
    {
        List<VehicleState> reverse = await RunDrive(new Vector3(22, 1, 15), Vector3.Zero, 120, tick => Frame(tick, brake: 65535));
        Check(reverse.Last().Physics.Position.Z > 20 && reverse.Max(state => state.Physics.LinearVelocity.Z) > 5, "vehicle reverses with brake input");
        List<VehicleState> drift = await RunDrive(new Vector3(-20, 1, 28), new Vector3(0, 0, -16), 145, tick => Frame(tick, throttle: 65535, steering: 17000, drift: tick <= 90));
        GD.Print($"Drift check: max handbrake={drift.Max(state => state.Handbrake)}, supported={drift.Count(state => state.Grounded)}, drift ticks={drift.Count(state => state.Drifting)}, end={drift.Last().Physics.Position}");
        File.WriteAllLines(_output + ".drift.csv", drift.Select(state => $"{state.Tick},{state.Grounded},{state.SteeringAngle},{state.Physics.Position.Y},{state.Physics.LinearVelocity.Y},{state.CommandSpeed}"));
        Check(drift.Any(state => state.Handbrake == 1 && state.RearSlip > 0.5f), "native handbrake reduces rear traction");
        Check(drift.Skip(90).Any(state => state.Handbrake > 0 && state.Handbrake < 1) && drift.Last().Handbrake == 0, "handbrake releases progressively without a boost");
        Check(drift.Any(state => Math.Abs(state.Physics.Orientation.Y) > 0.2f), "steering changes vehicle heading");
        Check(drift.All(state => VehiclePhysicsState.IsFinite(state.Physics.Position) && state.CommandSpeed <= 65.001f), "handbrake turns remain stable");
        await Screenshot("drift");
    }

    private async Task VerifyCorneringAndRecovery()
    {
        List<VehicleState> low = await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 25), new Vector3(0, 0, -8), 45, tick => Frame(tick, steering: 24000));
        List<VehicleState> fast = await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 25), new Vector3(0, 0, -24), 45, tick => Frame(tick, steering: 24000));
        float lowYaw = Math.Abs(2 * MathF.Atan2(low.Last().Physics.Orientation.Y, low.Last().Physics.Orientation.W));
        float fastYaw = Math.Abs(2 * MathF.Atan2(fast.Last().Physics.Orientation.Y, fast.Last().Physics.Orientation.W));
        float lowRadius = Numerics.Vector3.Distance(low.First().Physics.Position, low.Last().Physics.Position) / Math.Max(0.001f, lowYaw);
        float fastRadius = Numerics.Vector3.Distance(fast.First().Physics.Position, fast.Last().Physics.Position) / Math.Max(0.001f, fastYaw);
        GD.Print($"Cornering: low radius={lowRadius:F2}m; fast radius={fastRadius:F2}m; front slip={fast.Max(state => state.FrontSlip):F2}");
        GD.Print($"Steering onset: first wheel={low[0].SteeringAngle:F3}rad; yaw at 100ms={low[5].Physics.AngularVelocity.Y:F3}rad/s");
        Check(low[0].SteeringAngle > 0.08f && Math.Abs(low[5].Physics.AngularVelocity.Y) > 0.1f, "steering starts on the first fixed tick and produces physical yaw within 100ms");
        Check(lowYaw > 0.15f && fastRadius > lowRadius * 1.5f, "fast entry runs a wider line than low-speed steering");
        Check(fast.Max(state => state.FrontSlip) > 0.1f, "high-speed steering has measurable front traction saturation");
        List<VehicleState> lane = await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 25), new Vector3(0, 0, -20), 60, tick => Frame(tick, steering: tick <= 30 ? (short)10000 : (short)-10000));
        Check(lane.All(state => Math.Abs(state.Physics.AngularVelocity.Y) < 1.5f) && Math.Abs(lane.Last().Physics.Position.X + 20) < 5, "high-speed lane change stays controlled");
        List<VehicleState> slide = await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 25), new Vector3(4, 0, -14), 100, tick => Frame(tick, steering: tick < 25 ? (short)-7000 : (short)0));
        float finalSide = Math.Abs(Numerics.Vector3.Dot(slide.Last().Physics.LinearVelocity, Numerics.Vector3.Transform(Numerics.Vector3.UnitX, slide.Last().Physics.Orientation)));
        GD.Print($"Recovery: initial side speed=4m/s; final={finalSide:F3}m/s");
        Check(finalSide < 1, "throttle reduction and mild countersteering recover a moderate slide");
        List<VehicleState> handbrake = await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 25), new Vector3(0, 0, -14), 90, tick => Frame(tick, steering: 12000, drift: true));
        Check(handbrake.Last().CommandSpeed < 10 && handbrake.Any(state => Math.Abs(state.Physics.AngularVelocity.Y) > 0.3f), "prolonged turning handbrake scrubs speed and rotates the rear");
        Check(slide.All(state => state.CommandSpeed < 20), "recovery does not add a drift boost");
        await Screenshot("cornering");
    }

    private async Task VerifyResponsiveHandbrake()
    {
        foreach (float speed in new[] { 6f, 16f })
        {
            List<VehicleState> straight = await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 25), new Vector3(0, 0, -speed), 90, tick => Frame(tick, throttle: 65535, drift: true));
            Check(straight.Last().CommandSpeed < speed * 0.7f && straight.All(state => Math.Abs(state.Physics.AngularVelocity.Y) < 0.1f), "straight handbrake slows without manufacturing rotation, even with throttle");
            foreach (ulong heldTicks in new[] { 6ul, 45ul })
            {
                List<VehicleState> turn = await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 25), new Vector3(0, 0, -speed), 180, tick => Frame(tick, throttle: tick > heldTicks + 30 ? (ushort)15000 : (ushort)0, steering: tick <= heldTicks ? (short)12000 : tick <= heldTicks + 20 ? (short)-7000 : (short)0, drift: tick <= heldTicks));
                float side = Math.Abs(Numerics.Vector3.Dot(turn.Last().Physics.LinearVelocity, Numerics.Vector3.Transform(Numerics.Vector3.UnitX, turn.Last().Physics.Orientation)));
                float peakYaw = turn.Max(state => Math.Abs(state.Physics.AngularVelocity.Y));
                File.WriteAllLines($"{_output}.handbrake-{speed}-{heldTicks}.csv", turn.Select((state, index) => $"{index},{state.Physics.Position},{state.Physics.LinearVelocity},{state.Physics.AngularVelocity.Y},{state.Handbrake},{state.FrontSlip},{state.RearSlip}"));
                GD.Print($"Handbrake recovery: entry={speed}, held={heldTicks}, peak yaw={peakYaw:F2}, final side={side:F3}, speed={turn.Last().CommandSpeed:F2}");
                Check(peakYaw is > 0.1f and < 2 && side < 1 && turn.Last().Handbrake == 0, "tap/sustained turning handbrake retains control and settles after release with countersteering/throttle");
                Check(turn.Skip((int)heldTicks).Any(state => state.Handbrake > 0 && state.Handbrake < 1), "handbrake recovery remains progressive");
            }
        }

        await Screenshot("handbrake-recovery");
    }

    private async Task VerifyPowerThroughSlide()
    {
        foreach (float speed in new[] { 6f, 16f })
        {
            foreach (int heldTicks in new[] { 6, 45 })
            {
                foreach (int throttleDelay in new[] { -6, 0, 6 })
                {
                    int release = 12 + heldTicks;
                    int powered = release + Math.Max(0, throttleDelay);
                    List<VehicleState> states = await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 25), new Vector3(0, 0, -speed), heldTicks == 45 ? 180 : 90, tick => Frame(
                        tick,
                        throttle: (int)tick > release + throttleDelay ? (ushort)65535 : (ushort)0,
                        steering: (int)tick <= release ? (short)12000 : (int)tick <= release + 20 ? (short)-7000 : (short)0,
                        drift: tick > 12 && (int)tick <= release));
                    VehicleState first = states[powered];
                    float side = Math.Abs(Numerics.Vector3.Dot(first.Physics.LinearVelocity, Numerics.Vector3.Transform(Numerics.Vector3.UnitX, first.Physics.Orientation)));
                    float yaw = Math.Abs(first.Physics.AngularVelocity.Y);
                    float finalSide = Math.Abs(Numerics.Vector3.Dot(states.Last().Physics.LinearVelocity, Numerics.Vector3.Transform(Numerics.Vector3.UnitX, states.Last().Physics.Orientation)));
                    GD.Print($"Power out: entry={speed}, held={heldTicks}, throttle delay={throttleDelay}, acceleration={first.LongitudinalAcceleration:F3}, side={side:F3}, yaw={yaw:F3}, recovery={first.Handbrake:F3}, final side={finalSide:F3}, peak speed={states.Max(state => state.CommandSpeed):F3}, peak yaw={states.Max(state => Math.Abs(state.Physics.AngularVelocity.Y)):F3}");
                    Check(first.Grounded && first.LongitudinalAcceleration > 2 && first.Handbrake is > 0 and < 1, "first available powered tick accelerates during progressive handbrake recovery");
                    VehicleState before = states[powered - 1];
                    Check(Numerics.Vector3.Distance(first.Physics.LinearVelocity, before.Physics.LinearVelocity) < 0.5f && Math.Abs(first.Physics.AngularVelocity.Y - before.Physics.AngularVelocity.Y) < 0.3f, "propulsion changes momentum and yaw progressively without a snap");
                    if (speed == 16 && heldTicks == 45 && throttleDelay <= 0)
                    {
                        Check(first.Drifting && side > 1 && yaw > 0.1f, "forward propulsion starts while the faster sustained slide remains active");
                    }

                    Check(states.Last().Handbrake == 0 && finalSide < 1 && states.All(state => state.CommandSpeed < 30 && Math.Abs(state.Physics.AngularVelocity.Y) < 2), "powered recovery remains controlled and returns progressively to grip");
                    File.WriteAllLines($"{_output}.power-{speed}-{heldTicks}-{throttleDelay}.csv", states.Select((state, index) => $"{index},{state.Physics.Position},{state.Physics.LinearVelocity},{state.Physics.AngularVelocity.Y},{state.Handbrake},{state.LongitudinalAcceleration},{state.RearSlip}"));
                }
            }
        }

        foreach (float speed in new[] { 0f, 4f, 12f })
        {
            List<VehicleState> acceleration = await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 25), new Vector3(0, 0, -speed), 90, tick => Frame(tick, throttle: 65535));
            GD.Print($"Acceleration: entry={speed}, final={acceleration.Last().CommandSpeed:F3}");
            Check(acceleration.Take(6).Any(state => state.Grounded && state.LongitudinalAcceleration > 4) && acceleration.Last().CommandSpeed > speed + 8, "standing/low/cruising speed throttle produces immediate sustained acceleration");
        }

        List<VehicleState> braking = await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 25), new Vector3(0, 0, -12), 90, tick => Frame(tick, brake: tick <= 30 ? (ushort)65535 : (ushort)0, throttle: tick > 30 ? (ushort)65535 : (ushort)0));
        GD.Print($"Brake recovery: first drive={braking[30].LongitudinalAcceleration:F3}, before={braking[29].CommandSpeed:F3}, after={braking.Last().CommandSpeed:F3}");
        Check(braking[30].LongitudinalAcceleration > 2 && braking.Last().CommandSpeed > braking[29].CommandSpeed + 5, "throttle immediately rebuilds speed after service braking");
        await Screenshot("power-recovery");
    }

    private async Task VerifyPhysicalInteractions()
    {
        _arena.Target.ResetBody(new VehiclePhysicsState(new Numerics.Vector3(22, 1, -5), Numerics.Quaternion.Identity, Numerics.Vector3.Zero, Numerics.Vector3.Zero));
        await RunDrive(new Vector3(22, 1, 10), new Vector3(0, 0, -22), 90, tick => Frame(tick));
        Check(_arena.Target.GlobalPosition.Z < -6, "vehicle contact transfers momentum to the target");
        GD.Print($"Collision HP: player={_arena.Player.DamageState.CurrentHP:F2}, target={_arena.Target.DamageState.CurrentHP:F2}");
        Check(_arena.Player.DamageState.CurrentHP < 100 && _arena.Target.DamageState.CurrentHP < 100, "hard vehicle contact damages both vehicles");
        Check(_arena.Player.DamageState.LastDamage?.Attribution.InstigatorId == 2 && _arena.Target.DamageState.LastDamage?.Attribution.InstigatorId == 1, "both collision victims retain other vehicle attribution");
        float postCollisionSpeed = _arena.Player.Snapshot.Speed;
        _arena.Player.InputSource = tick => Frame(tick, throttle: 65535);
        List<VehicleState> collisionRecovery = await ObserveTicks(45);
        Check(collisionRecovery.Any(state => state.LongitudinalAcceleration > 2) && _arena.Player.Snapshot.Speed > postCollisionSpeed + 1, "throttle rebuilds speed after a real vehicle collision");
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
        List<VehicleState> braking = await RunDrive(new Vector3(-25, VehicleDimensions.RideHeight, 20), new Vector3(0, 0, -12), 180, tick => Frame(tick, brake: 65535));
        GD.Print($"Brake check: end velocity={braking.Last().Physics.LinearVelocity}, distance={20 - braking.Min(state => state.Physics.Position.Z):F2}m");
        Check(braking.First().Physics.LinearVelocity.Z < -10 && braking.Any(state => Math.Abs(state.Physics.LinearVelocity.Z) < 0.5f) && braking.Last().Physics.LinearVelocity.Z > 2, "native braking slows forward travel through rest before reversing");
        List<VehicleState> stationary = await RunDrive(new Vector3(-25, VehicleDimensions.RideHeight, 20), Vector3.Zero, 60, tick => Frame(tick, steering: 32767, drift: tick < 50));
        Check(stationary.All(state => !state.Drifting), "stationary handbrake cannot manufacture sliding");
        List<VehicleState> airborne = await RunDrive(new Vector3(-25, 8, 20), new Vector3(0, 15, -15), 30, tick => Frame(tick, steering: 32767, drift: tick < 20));
        Check(airborne.All(state => !state.Grounded && !state.Drifting && state.CommandSpeed < 65), "airborne handbrake cannot manufacture sliding and remains bounded");
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
        await RunDrive(new Vector3(25, VehicleDimensions.RideHeight, -1.3f), new Vector3(0, 0, -1), 180, tick => Frame(tick, throttle: 14000));
        _arena.Player.Advanced -= ObserveBrush;
        GD.Print($"Brush check: contacts={brushingTicks}, player HP={_arena.Player.DamageState.CurrentHP:F2}, target HP={_arena.Target.DamageState.CurrentHP:F2}");
        Check(brushingTicks > 30, "minor brushing scenario makes sustained real vehicle contact");
        Check(_arena.Player.DamageState.CurrentHP == 100 && _arena.Target.DamageState.CurrentHP == 100, "sustained minor contact does not drain either vehicle HP");

        await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 20), Vector3.Zero, 30, tick => Frame(tick));
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
        Check(_arena.Player.DamageState.LastDamage == death && _arena.Player.State.Handbrake == 0 && !_arena.Player.State.Drifting, "wreck cannot repeat its death transition or apply handbrake intent");
        await Screenshot("destroyed");
        ulong life = _arena.Player.Snapshot.LifeId;
        ulong globalTick = _arena.Simulation.State.Tick;
        await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 20), Vector3.Zero, 60, tick => Frame(tick, throttle: 65535));
        Check(!_arena.Player.DamageState.Destroyed && _arena.Player.DamageState.CurrentHP == 100 && _arena.Player.State.CommandSpeed > 5, "explicit reset restores health and driving");
        Check(_arena.Player.Snapshot.LifeId == life + 1 && _arena.Simulation.State.Tick == globalTick + 60 && _arena.Simulation.State.Vehicles.All(vehicle => vehicle.Movement.Tick == globalTick + 60), "reset starts one new Core life while both vehicles retain the global clock");
    }

    private async Task VerifySurfaces()
    {
        List<VehicleState> baseline = await RunDrive(new Vector3(-22, VehicleDimensions.RideHeight, 7), Vector3.Zero, 90, tick => Frame(tick, throttle: 65535));
        List<VehicleState> mud = await RunDrive(new Vector3(-32, VehicleDimensions.RideHeight, 7), Vector3.Zero, 90, tick => Frame(tick, throttle: 65535));
        Check(mud.Skip(3).All(state => state.CurrentSurface == SurfaceType.Mud), "native support identifies the mud tile");
        Check(mud.Last().CommandSpeed < baseline.Last().CommandSpeed * 0.7f, "mud acceleration and resistance clearly reduce native speed");
        GD.Print($"Surface speed after 90 ticks: Concrete={baseline.Last().CommandSpeed:F2}, Mud={mud.Last().CommandSpeed:F2}");
        await Screenshot("mud");

        bool reversing = false;
        List<VehicleState> crossings = await RunDrive(new Vector3(-32, VehicleDimensions.RideHeight, 20), Vector3.Zero, 1200, tick =>
        {
            if (_arena.Player.Position.Z < -18)
            {
                reversing = true;
            }

            if (_arena.Player.Position.Z > 18)
            {
                reversing = false;
            }

            return reversing ? Frame(tick, brake: 65535) : Frame(tick, throttle: 65535);
        });
        int transitions = crossings.Zip(crossings.Skip(1)).Count(pair => pair.First.CurrentSurface != pair.Second.CurrentSurface);
        Check(transitions >= 4, "repeated native driving crosses Concrete and Mud in both directions");
        Check(crossings.Any(state => state.CurrentSurface == SurfaceType.Concrete && state.Physics.Position.Z < -12 && -state.Physics.LinearVelocity.Z > 13), "leaving mud restores baseline acceleration");
        Check(crossings.All(state => VehiclePhysicsState.IsFinite(state.Physics.Position) && state.CommandSpeed <= 65.001f && state.Physics.AngularVelocity.Length() <= 8.001f && state.Physics.Position.Y is > 0.7f and < 1.1f), "coplanar repeated transitions remain supported and bounded without launches");
        Check(crossings.Zip(crossings.Skip(1)).Where(pair => pair.First.CurrentSurface != pair.Second.CurrentSurface).All(pair => Numerics.Vector3.Distance(pair.First.Physics.LinearVelocity, pair.Second.Physics.LinearVelocity) < 1), "surface selection introduces no velocity impulse");
        File.WriteAllText(_output + ".surfaces.json", System.Text.Json.JsonSerializer.Serialize(crossings.SelectMany(state => new[] { state.Physics.Position.X, state.Physics.Position.Y, state.Physics.Position.Z, state.Physics.LinearVelocity.X, state.Physics.LinearVelocity.Y, state.Physics.LinearVelocity.Z, (float)state.CurrentSurface }).ToArray()));
        GD.Print($"Surface crossings: {transitions}; peak speed={crossings.Max(state => state.CommandSpeed):F2}");

        List<VehicleState> drift = await RunDrive(new Vector3(-32, VehicleDimensions.RideHeight, 12), new Vector3(0, 0, -18), 110, tick => Frame(tick, throttle: 65535, steering: 8000, drift: tick <= 70));
        Check(drift.Any(state => state.Drifting && state.CurrentSurface == SurfaceType.Mud), "drift continues when entering native mud support");
        Check(drift.Any(state => state.CurrentSurface == SurfaceType.Concrete && state.Handbrake > 0), "handbrake state remains continuous through surface crossings");
        Check(drift.All(state => state.CommandSpeed <= 65.001f && VehiclePhysicsState.IsFinite(state.Physics.Position)), "drift across surfaces remains bounded");
        await Screenshot("surface-drift");

        List<VehicleState> landing = await RunDrive(new Vector3(-32, 4, 0), Vector3.Zero, 120, tick => Frame(tick, drift: true, steering: 8000));
        Check(landing.Take(15).All(state => !state.Grounded && !state.Drifting), "air above mud does not acquire surface grip or drift support");
        Check(landing.TakeLast(30).All(state => state.Grounded && state.CurrentSurface == SurfaceType.Mud), "landing selects mud on the actual supporting collider");
        await Screenshot("mud-landing");
    }

    private async Task VerifyAdjustableShake()
    {
        var camera = _arena.GetNode<VehicleChaseCamera>("ChaseCamera");
        var evidence = new List<object>();
        foreach (double intensity in new[] { 0d, 0.25d, 0.5d, 1d, 1d })
        {
            _settings.UpdateSettings(_settings.Current with { CameraShakeIntensity = intensity });
            _shakePeak = 0;
            await RunDrive(new Vector3(28, 1, -28), new Vector3(0, 0, -23), 90, tick => Frame(tick));
            float hp = _arena.Player.DamageState.CurrentHP;
            Check(hp < 100, "real wall impact still damages the vehicle at every local shake setting");
            Check(intensity == 0 ? _shakePeak == 0 : _shakePeak > 0.0001f, "real impact obeys the local shake intensity");
            Check(_shakePeak <= camera.MaximumShakeMetres * intensity, "repeated native impacts stay within the configured presentation bound");
            evidence.Add(new { intensity, peakMetres = _shakePeak, hp, life = _arena.Player.Snapshot.LifeId });
            GD.Print($"Collision shake: intensity={intensity:P0}, peak={_shakePeak:F6}m, HP={hp:F3}, life={_arena.Player.Snapshot.LifeId}");
        }

        await RunDrive(new Vector3(-20, VehicleDimensions.RideHeight, 20), Vector3.Zero, 60, tick => Frame(tick));
        Check(camera.Motion.Shake == 0, "native new-life reset clears shake");
        _settings.UpdateSettings(_settings.Current with { CameraShakeIntensity = 0 });
        _arena.Player.ApplyEffect(new DamageEffect(20, Numerics.Vector3.Zero, Numerics.Vector3.Zero), new DamageContext("missile", 99, "disabled-shake"));
        await ObserveTicks(3);
        Check(_arena.Player.DamageState.CurrentHP == 80 && camera.Motion.Shake == 0, "zero disables real damage feedback without disabling damage");
        _settings.UpdateSettings(_settings.Current with { CameraShakeIntensity = 1 });
        await ObserveTicks(3);
        Check(camera.Motion.Shake == 0, "enabling does not replay an already consumed damage event");
        File.WriteAllText(_output + ".shake.json", System.Text.Json.JsonSerializer.Serialize(evidence));
    }

    private async Task VerifySpeedTelemetry()
    {
        await RunDrive(new Vector3(-25, VehicleDimensions.RideHeight, 25), Vector3.Zero, 90, tick => Frame(tick));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_arena.Player.Snapshot.Speed < 0.01f && _arena.Player.State.CommandSpeed < 0.01f, "stationary suspension balances gravity without road-speed creep");
        Check(_hud.SpeedText == "Speed  0.0 km/h", "stationary HUD reads zero in km/h");
        GD.Print($"Stationary speed: observed={_arena.Player.Snapshot.Speed:F4} m/s, command={_arena.Player.State.CommandSpeed:F4} m/s, HUD={_hud.SpeedText}");
        _settings.UpdateSettings(_settings.Current with { SpeedUnit = SpeedUnit.MilesPerHour });
        Check(_hud.SpeedText == "Speed  0.0 mph", "stationary HUD reads zero in mph");
        await Screenshot("stationary-speed");
        _settings.UpdateSettings(_settings.Current with { SpeedUnit = SpeedUnit.KilometresPerHour });

        await RunDrive(new Vector3(-25, VehicleDimensions.RideHeight, 25), Vector3.Zero, 60, tick => Frame(tick, throttle: 65535));
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(_arena.Player.Snapshot.Speed > 3, "solved forward travel produces positive HUD speed");
        VerifyHudConversion(3.6, "km/h");
        _settings.UpdateSettings(_settings.Current with { SpeedUnit = SpeedUnit.MilesPerHour });
        VerifyHudConversion(2.2369362920544, "mph");
        _settings.UpdateSettings(_settings.Current with { SpeedUnit = SpeedUnit.KilometresPerHour });

        await RunDrive(new Vector3(-25, VehicleDimensions.RideHeight, 15), Vector3.Zero, 60, tick => Frame(tick, brake: 65535));
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
        await RunDrive(new Vector3(-25, VehicleDimensions.RideHeight, 25), Vector3.Zero, 2, tick => Frame(tick));
        _arena.Player.InputSource = null;
        Trackstorm.Client.Input.PlayerInput input = _input;
        using var press = new InputEventKey { PhysicalKeycode = Key.W, Pressed = true };
        using var release = new InputEventKey { PhysicalKeycode = Key.W, Pressed = false };
        try
        {
            Godot.Input.ParseInputEvent(press);
            Godot.Input.FlushBufferedEvents();
            await ObserveTicks(90);
            Check(_arena.Player.State.CommandSpeed > 5 && _arena.Player.GlobalPosition.Z < 20, "native W input flows through production capture into vehicle movement");
            Press("Settings");
            await ObserveTicks(3);
            Check(input.Adapter.GameplaySuppressed && input.LatestFrame.Accelerate == 0, "opening the production settings panel neutralizes held driving input");
            Press("Back");
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
