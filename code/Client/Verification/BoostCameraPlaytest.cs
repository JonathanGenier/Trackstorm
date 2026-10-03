using System.Text.Json;
using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Native driving on the production oval with bounded, repeatable Boost camera scenarios.</summary>
public sealed partial class BoostCameraPlaytest : Node3D
{
    private readonly List<VehicleBody> _cars = new();
    private readonly List<object> _trace = new();
    private Core.Simulation.Simulation _world = null!;
    private VehicleChaseCamera _camera = null!;
    private Label _label = null!;
    private int _frame;
    private bool _done;
    private int _boostFrames;
    private int _groundFrames;
    private int _airFrames;
    private string _output = "";
    private readonly List<BoostExhaust> _exhausts = new();
    private int _simultaneousFrames;
    private int _shortPulseFrames;
    private bool _interactive;
    private bool _manualBoost;
    private short _manualSteer;
    private int _rolloverFrames;
    private int _reframedFrames;
    private float _minimumRolloverDistance = float.MaxValue;
    private readonly List<double> _rolloverFollowMs = new();
    private static readonly string[] Names = ["enter-sustain-exit", "repeated-use", "steering-traffic", "high-speed-no-boost", "airborne-boost", "short-cancelled-pulses", "simultaneous-boost"];

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _interactive = OS.GetCmdlineUserArgs().Contains("--boost-interactive");
        _output = ProjectSettings.GlobalizePath("res://.godot/ts-227/playtest");
        System.IO.Directory.CreateDirectory(_output);
        AddChild(Arenas.ActiveMap.Load());
        AddChild(new Arenas.EnvironmentPresentation());
        _world = new(new(60));
        for (int i = 0; i < 3; i++)
        {
            var damage = new DamageConfiguration { MaxHP = 1000, CollisionScale = 5 };
            _world.AddVehicle((ulong)i + 1, new(), damage, Pose(i, false, 0));
            var body = new VehicleBody { VehicleId = (ulong)i + 1, DamageConfiguration = damage };
            body.Initialize(_world); AddChild(body); _cars.Add(body);
            _exhausts.Add(body.FindChildren("*", "", true, false).OfType<BoostExhaust>().Single());
        }
        _camera = new VehicleChaseCamera { Current = true, Fov = 65, Far = 1500 };
        AddChild(_camera);
        var layer = new CanvasLayer { Layer = 2 }; AddChild(layer);
        _label = new Label { Position = new(24, 24) }; layer.AddChild(_label);
        AddChild(new Hud.CombatHud { Vehicle = () => _cars[0].Snapshot });
    }

    private static VehiclePhysicsState Pose(int index, bool air, float speed)
    {
        var rotation = N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -MathF.PI / 2);
        return new(new(-50 + index * 30, air && index == 0 ? 10 : 1.3f, 96 + index * 3), rotation,
            new(speed, air && index == 0 ? 6 : 0, 0), N.Vector3.Zero);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_done) { return; }
        try
        {
            int phase = _frame / 360;
            int frame = _frame % 360;
            if (phase >= Names.Length) { Finish(); return; }
            bool active = _interactive ? _manualBoost : phase switch { 0 => frame is >= 60 and < 240,
                1 => frame is >= 30 and < 270 && (frame < 120 || frame % 48 < 40),
                2 => frame is >= 30 and < 240, 4 => frame is >= 30 and < 180,
                5 => frame is >= 60 and < 240 && frame % 24 < 8, 6 => frame is >= 60 and < 240, _ => false };
            if (frame == 0)
            {
                for (int i = 0; i < _cars.Count; i++)
                {
                    var spawn = Pose(i, phase == 4, i == 0 || phase == 6 ? phase == 3 ? 58 : 24 : 8);
                    // Give all three simultaneous boosters a full straight before the
                    // bank. A lead car crashing early does not exercise joint exhaust.
                    if (phase == 6) spawn = new(new(-110 + i * 15, 1.3f, 96 + i * 3), spawn.Orientation, spawn.LinearVelocity, spawn.AngularVelocity);
                    _cars[i].ResetBody(spawn);
                }
            }
            var requests = new List<VehicleStepRequest>();
            for (int i = 0; i < _cars.Count; i++)
            {
                short steer = i == 0 && _interactive ? _manualSteer : i == 0 && phase == 2 ? (short)(Math.Sin(frame / 70f) * 6500) : (short)0;
                bool boosting = active && (i == 0 || phase == 6);
                var input = new InputFrame(_world.State.Tick + 1, steer, phase == 4 ? (ushort)0 : ushort.MaxValue, 0, boosting ? InputButtons.UseItem : 0, 0, 0);
                var r = _cars[i].Capture(input);
                requests.Add(new(r.VehicleId, r.Input, r.Observation, r.Effects, r.Reset, nitro: boosting ? new(2, 18000, 1.4f, 1) : default));
            }
            var result = _world.Step(requests[0].Input, requests);
            for (int i = 0; i < _cars.Count; i++)
            {
                _cars[i].Apply(result[i]);
                var rack = _cars[i].FindChildren("*", "", true, false).OfType<CarRackPresentation>().Single();
                rack.Observe(_cars[i].Snapshot.LifeId, _cars[i].Snapshot.CanInteract,
                    new Core.Items.ItemSlot((ulong)i + 1, _cars[i].Snapshot.LifeId, (ulong)i + 1, Core.Items.HeldItem.Nitro), []);
            }
            _frame++;
            if (_interactive && _frame >= 359) { _frame = 1; }
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); _done = true; GetTree().Quit(1); }
    }

    public override void _Process(double delta)
    {
        if (_done || _frame == 0) { return; }
        var state = _cars[0].Snapshot;
        if (state.Movement.Nitro.Active) { _boostFrames++; }
        if (state.Movement.Grounded) { _groundFrames++; } else { _airFrames++; }
        long followStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _camera.Follow(_cars[0].GetGlobalTransformInterpolated(), state, (float)delta, _cars[0].GetRid());
        bool rolled = _cars[0].GetGlobalTransformInterpolated().Basis.Y.Y < .65f;
        if (rolled || _camera.RolloverFraming) _rolloverFollowMs.Add(System.Diagnostics.Stopwatch.GetElapsedTime(followStart).TotalMilliseconds);
        int phase = Math.Min(Names.Length - 1, (_frame - 1) / 360);
        _label.Text = $"TS-227 | {(_interactive ? "Interactive: Space Boost toggle, A/D steer, S center, R reset, Esc exit" : Names[phase])}\n{state.Speed * 3.6f:F0} km/h | Boost {state.Movement.Nitro.Active} | FOV {_camera.Fov:F1}";
        _trace.Add(new { frame = _frame, phase = Names[phase], speed = state.Speed, active = state.Movement.Nitro.Active,
            grounded = state.Movement.Grounded, fov = _camera.Fov, pullback = _camera.BoostMotion.PullBack,
            streaks = _camera.BoostMotion.StreakStrength, flame = _exhausts[0].FlameEnergy,
            smoke = _exhausts[0].SmokeEmitting, hp = state.Damage.CurrentHP,
            allFlames = _exhausts.Select(exhaust => exhaust.FlameVisible).ToArray(),
            allBoosting = _cars.Select(car => car.Snapshot.Movement.Nitro.Active).ToArray(),
            reframed = _camera.RolloverFraming,
            camera = new[] { _camera.GlobalPosition.X, _camera.GlobalPosition.Y, _camera.GlobalPosition.Z },
            position = new[] { _cars[0].GlobalPosition.X, _cars[0].GlobalPosition.Y, _cars[0].GlobalPosition.Z },
            up = new[] { _cars[0].GlobalBasis.Y.X, _cars[0].GlobalBasis.Y.Y, _cars[0].GlobalBasis.Y.Z } });
        int localFrame = (_frame - 1) % 360;
        if (!_interactive)
        {
            // The close rack boom can clear this bank without alternate framing.
            // Require real rolled poses and lens clearance, not a particular fallback activation.
            if (rolled || _camera.RolloverFraming)
            {
                _rolloverFrames++;
                if (_camera.RolloverFraming) _reframedFrames++;
                float distance = _camera.GlobalPosition.DistanceTo(_cars[0].GetGlobalTransformInterpolated() * VehicleBody.ToGodot(WeaponAim.Pivot));
                _minimumRolloverDistance = Math.Min(_minimumRolloverDistance, distance);
                if (_camera.RolloverFraming && distance < 5.4f) { Fail($"Rollover framing collapsed into the car: {distance}m."); return; }
            }
            // Check actual final lens volume against both the world and the followed
            // chassis; the production world sweep intentionally excludes that body.
            using var sphere = new SphereShape3D { Radius = .20f };
            using var query = new PhysicsShapeQueryParameters3D { Shape = sphere, Transform = new(Basis.Identity, _camera.GlobalPosition), CollisionMask = 1, Margin = 0 };
            if (GetWorld3D().DirectSpaceState.IntersectShape(query, 1).Count > 0)
            { Fail($"Boost camera intersects a solid at frame {_frame}."); return; }
            if (phase == 5 && localFrame > 60)
            {
                if (_exhausts[0].FlameVisible) { Fail("Cancelled short primes ignited a delayed flame."); return; }
                if (state.Movement.Nitro.Active) { _shortPulseFrames++; }
            }
            if (phase == 6 && localFrame is > 120 and < 230 && _exhausts.All(e => e.FlameVisible)) { _simultaneousFrames++; }
            // The repeated-use phase releases at frame 270: at least one second
            // permits <2 cm of the deliberately exponential camera recovery.
            if (localFrame > 330 && (_exhausts.Any(e => e.FlameVisible || e.SmokeEmitting || e.SparksEmitting) || _camera.BoostMotion.PullBack > .02f))
            { Fail($"Integrated cutoff failed in {Names[phase]} frame {localFrame}: pull-back {_camera.BoostMotion.PullBack}, flame {_exhausts[0].FlameEnergy}."); return; }
        }
        if (!float.IsFinite(_camera.Fov) || _camera.Fov < 65 || _camera.Fov > 73.01f || !_camera.GlobalTransform.IsFinite())
        { GD.PushError("Boost camera escaped finite presentation bounds."); _done = true; GetTree().Quit(1); }
        if (OS.GetCmdlineUserArgs().Contains("--boost-camera-captures") && _frame % 15 == 0) { Capture(_frame); }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!_interactive || @event is not InputEventKey { Pressed: true, Echo: false } key) { return; }
        switch (key.Keycode)
        {
            case Key.Space: _manualBoost = !_manualBoost; break;
            case Key.A: _manualSteer = -6500; break;
            case Key.D: _manualSteer = 6500; break;
            case Key.S: _manualSteer = 0; break;
            case Key.R: _frame = 0; _manualBoost = false; _manualSteer = 0; _camera.ResetFollow(); break;
            case Key.Escape: GetTree().Quit(); break;
        }
    }

    private void Fail(string message) { GD.PushError(message); _done = true; GetTree().Quit(1); }

    private async void Capture(int frame)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng(System.IO.Path.Combine(_output, $"frame-{frame:D4}.png"));
    }

    private void Finish()
    {
        _done = true;
        System.IO.File.WriteAllText(System.IO.Path.Combine(_output, "trace.json"), JsonSerializer.Serialize(_trace));
        if (_boostFrames < 300 || _groundFrames < 300 || _airFrames < 30 || _simultaneousFrames < 60 || _shortPulseFrames < 40 || _rolloverFrames < 30)
        { GD.PushError($"Fixture coverage missing: Boost {_boostFrames}, grounded {_groundFrames}, air {_airFrames}, simultaneous {_simultaneousFrames}, short pulse {_shortPulseFrames}, rolled {_rolloverFrames}"); GetTree().Quit(1); return; }
        var actual = _cars[0].Snapshot;
        var active = new VehicleSnapshot(actual.VehicleId, actual.LifeId, actual.Movement with { Nitro = new(60, 18000, 1.4f, 1) }, actual.Damage, actual.ObservedPhysics);
        Transform3D pose = _cars[0].GlobalTransform;
        for (int i = 0; i < 120; i++) { _camera.Follow(pose, active, 1f / 60, _cars[0].GetRid()); }
        if (_camera.Fov < 68) { throw new InvalidOperationException("Active Boost camera did not expand."); }
        _camera.ResetFollow();
        _camera.Follow(pose, active, 1f / 60, _cars[0].GetRid());
        if (_camera.Fov != 65 || _camera.BoostMotion.PullBack != 0) { throw new InvalidOperationException("Reseed retained historical Boost camera state."); }
        if (_cars[0].Snapshot != actual) { throw new InvalidOperationException("Camera changed authoritative vehicle state."); }
        GD.Print($"Boost camera playtest passed: native entry/sustain/exit, rapid re-engagement, cancelled short primes, three simultaneously boosting bodies, steering, unboosted speed, airborne launch; integrated cutoff, bounded camera, active reseed, unchanged snapshot. Boost frames {_boostFrames}, grounded {_groundFrames}, airborne {_airFrames}, simultaneous flame {_simultaneousFrames}, short pulse {_shortPulseFrames}. Visual quality requires observation.");
        GD.Print($"Rolled/recovery coverage: {_rolloverFrames} frames ({_reframedFrames} alternate framing); minimum lens-to-rack distance {_minimumRolloverDistance:F3}m; final lens volume clear of world and chassis throughout all scenarios.");
        _rolloverFollowMs.Sort();
        GD.Print($"Rollover camera Follow CPU p95={_rolloverFollowMs[(int)(_rolloverFollowMs.Count * .95)]:F3}ms, max={_rolloverFollowMs[^1]:F3}ms; excludes render/GPU work.");
        GetTree().Quit();
    }
}
