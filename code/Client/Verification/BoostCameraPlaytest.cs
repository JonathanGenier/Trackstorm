using System.Text.Json;
using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
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
    private static readonly string[] Names = ["enter-sustain-exit", "repeated-use", "steering-traffic", "high-speed-no-boost", "airborne-boost"];

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _output = ProjectSettings.GlobalizePath("res://.godot/ts-226/playtest");
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
            bool active = phase switch { 0 => frame is >= 60 and < 240, 1 => frame >= 30 && frame < 270 && frame % 60 < 35,
                2 => frame is >= 30 and < 240, 4 => frame is >= 30 and < 180, _ => false };
            if (frame == 0)
            {
                for (int i = 0; i < _cars.Count; i++) { _cars[i].ResetBody(Pose(i, phase == 4, i == 0 ? phase == 3 ? 58 : 24 : 8)); }
            }
            var requests = new List<VehicleStepRequest>();
            for (int i = 0; i < _cars.Count; i++)
            {
                short steer = i == 0 && phase == 2 ? (short)(Math.Sin(frame / 70f) * 6500) : (short)0;
                var input = new InputFrame(_world.State.Tick + 1, steer, phase == 4 ? (ushort)0 : ushort.MaxValue, 0, i == 0 && active ? InputButtons.UseItem : 0, 0, 0);
                var r = _cars[i].Capture(input);
                requests.Add(new(r.VehicleId, r.Input, r.Observation, r.Effects, r.Reset, nitro: i == 0 && active ? new(2, 18000, 1.4f, 1) : default));
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
        }
        catch (Exception ex) { GD.PushError(ex.ToString()); _done = true; GetTree().Quit(1); }
    }

    public override void _Process(double delta)
    {
        if (_done || _frame == 0) { return; }
        var state = _cars[0].Snapshot;
        if (state.Movement.Nitro.Active) { _boostFrames++; }
        if (state.Movement.Grounded) { _groundFrames++; } else { _airFrames++; }
        _camera.Follow(_cars[0].GetGlobalTransformInterpolated(), state, (float)delta, _cars[0].GetRid());
        int phase = Math.Min(Names.Length - 1, (_frame - 1) / 360);
        _label.Text = $"TS-226 | {Names[phase]} | {state.Speed * 3.6f:F0} km/h | Boost {state.Movement.Nitro.Active} | FOV {_camera.Fov:F1}";
        _trace.Add(new { frame = _frame, phase = Names[phase], speed = state.Speed, active = state.Movement.Nitro.Active,
            grounded = state.Movement.Grounded, fov = _camera.Fov, pullback = _camera.BoostMotion.PullBack,
            streaks = _camera.BoostMotion.StreakStrength, hp = state.Damage.CurrentHP });
        if (!float.IsFinite(_camera.Fov) || _camera.Fov < 65 || _camera.Fov > 73.01f || !_camera.GlobalTransform.IsFinite())
        { GD.PushError("Boost camera escaped finite presentation bounds."); _done = true; GetTree().Quit(1); }
        if (OS.GetCmdlineUserArgs().Contains("--boost-camera-captures") && _frame % 15 == 0) { Capture(_frame); }
    }

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
        if (_boostFrames < 300 || _groundFrames < 300 || _airFrames < 30)
        { GD.PushError($"Fixture coverage missing: Boost {_boostFrames}, grounded {_groundFrames}, air {_airFrames}"); GetTree().Quit(1); return; }
        var actual = _cars[0].Snapshot;
        var active = new VehicleSnapshot(actual.VehicleId, actual.LifeId, actual.Movement with { Nitro = new(60, 18000, 1.4f, 1) }, actual.Damage, actual.ObservedPhysics);
        Transform3D pose = _cars[0].GlobalTransform;
        for (int i = 0; i < 120; i++) { _camera.Follow(pose, active, 1f / 60, _cars[0].GetRid()); }
        if (_camera.Fov < 68) { throw new InvalidOperationException("Active Boost camera did not expand."); }
        _camera.ResetFollow();
        _camera.Follow(pose, active, 1f / 60, _cars[0].GetRid());
        if (_camera.Fov != 65 || _camera.BoostMotion.PullBack != 0) { throw new InvalidOperationException("Reseed retained historical Boost camera state."); }
        if (_cars[0].Snapshot != actual) { throw new InvalidOperationException("Camera changed authoritative vehicle state."); }
        GD.Print($"Boost camera playtest passed: native entry/sustain/exit, repeated use, steering with three bodies, unboosted speed, airborne launch; bounded camera output, active reseed, unchanged vehicle snapshot. Boost frames {_boostFrames}, grounded {_groundFrames}, airborne {_airFrames}. Visual quality requires observation.");
        GetTree().Quit();
    }
}
