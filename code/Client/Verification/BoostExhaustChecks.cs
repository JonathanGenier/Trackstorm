using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Vehicles;
using N = System.Numerics;

namespace Trackstorm.Client.Verification;

/// <summary>Rendered lifecycle/scaling fixture using production vehicle presentation and the active map.</summary>
public sealed partial class BoostExhaustChecks : Node3D
{
    private readonly List<NetworkVehicleBody> _cars = new();
    private readonly List<double> _frameTimes = new();
    private Camera3D _camera = null!;
    private Label _label = null!;
    private float _time;
    private int _frame;
    private bool _capturePending;
    private int _capture;
    private bool _reseeded;
    private readonly float[] _captureTimes = [.3f, .8f, 1.02f, 1.5f, 1.65f, 1.85f, 2.12f, 2.24f, 2.4f, 3, 4.5f, 6.04f, 6.20f, 6.36f, 6.6f, 6.76f, 6.94f, 7.15f, 7.85f, 8.2f, 9.9f, 10.2f, 10.6f, 13, 17, 21, 24.10f, 24.28f, 24.48f, 25];
    private readonly string[] _captureNames = ["idle", "rack-rise", "deployment", "ready", "mechanical-detail", "intake-front", "priming", "fumes", "ignition", "sustain-a", "sustain-b", "release", "release-middle", "release-end", "ready-after-release", "short-prime", "prime-cancelled", "retract", "switched", "cancelled", "reignite", "reprime-during-decay", "rapid-reignite", "chase", "eight", "thirty-two", "depletion-burst", "depletion-decay", "depletion-end", "cutoff"];
    private float _releaseEnergy = 1;
    private float _lastClipTime;
    private bool _clipPending;
    private float _readyFanAngle;

    public override void _Ready()
    {
        Engine.MaxFps = 60;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        AddChild(new Arenas.EnvironmentPresentation());
        AddChild(Networking.MatchResourceLoader.LoadResource<PackedScene>(Arenas.ActiveMap.ScenePath).Instantiate());
        for (int i = 0; i < 32; i++)
        {
            var car = new NetworkVehicleBody { VehicleId = (ulong)i + 1 };
            AddChild(car);
            _cars.Add(car);
        }
        _camera = new Camera3D { Fov = 65, Position = new Vector3(5, 4, 9) };
        AddChild(_camera);
        _camera.MakeCurrent();
        var layer = new CanvasLayer();
        AddChild(layer);
        _label = new Label { Position = new Vector2(18, 18) };
        _label.AddThemeFontSizeOverride("font_size", 22);
        layer.AddChild(_label);
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://.godot/ts225-vfx"));
    }

    public override void _Process(double delta)
    {
        try
        {
            _time += (float)delta;
            _frame++;
            bool active = _time is >= 2 and < 6 or >= 6.7f and < 6.82f or >= 8 and < 8.15f or >= 8.3f and < 8.5f or >= 9 and < 24;
            active &= _time is not (>= 10 and < 10.12f);
            int count = _time < 16 ? 1 : _time < 20 ? 8 : 32;
            float speed = _time is >= 4 and < 12 ? 6 : 0;
            var anchor = new Transform3D(new Basis(Vector3.Up, Mathf.Pi / 2), new Vector3(0, .9f, 96));
            anchor.Origin += anchor.Basis * new Vector3(0, 0, -Math.Clamp(_time - 4, 0, 8) * 6);
            for (int i = 0; i < _cars.Count; i++)
            {
                Vector3 position = anchor * new Vector3((i % 4) * 4, 0, -(i / 4) * 7);
                var rotation = anchor.Basis.GetRotationQuaternion();
                var physics = new VehiclePhysicsState(VehicleBody.ToCore(position), new N.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W), VehicleBody.ToCore(-anchor.Basis.Z * speed), N.Vector3.Zero);
                var movement = new VehicleState((ulong)_frame, physics, true, false, 0, 0, SurfaceType.Asphalt,
                    nitro: active && i < count ? new NitroState(60, 18000, 1.4f, 1) : default);
                bool alive = _time < 26.5f && !(i == 31 && _time >= 24.15f);
                var state = new VehicleSnapshot((ulong)i + 1, _time < 26 ? 1ul : 2ul, movement, new VehicleDamageState(1000, alive ? 1000 : 0, null, null), physics);
                _cars[i].Apply(state);
                _cars[i].PresentRemote(physics);
                _cars[i].Visible = i < count;
                var item = _time < .5f || _time >= 24 ? Core.Items.HeldItem.None :
                    _time is >= 7 and < 8 ? Core.Items.HeldItem.Missile : Core.Items.HeldItem.Nitro;
                _cars[i].Rack.Observe(state.LifeId, state.CanInteract,
                    new Core.Items.ItemSlot(state.VehicleId, state.LifeId, item == Core.Items.HeldItem.Missile ? 1ul : 2ul, item)
                    { NitroDeploymentTicks = item == Core.Items.HeldItem.Nitro ? Math.Clamp((int)MathF.Ceiling((.6f - (_time - (_time >= 8 ? 8 : .5f))) * 60), 0, Core.Items.ItemSlot.NitroDeploymentDurationTicks) : 0 }, []);
                if (i == 0 && _time > 22 && !_reseeded)
                {
                    _cars[i].Reseed(state);
                    var exhaust = FindExhaust(_cars[i]);
                    if (exhaust.FlameVisible || exhaust.SmokeEmitting || exhaust.Deployment != 0) { throw new InvalidOperationException("Reseed retained historical exhaust."); }
                    _reseeded = true;
                }
            }
            _camera.Position = anchor * (_time < 12 ? new Vector3(5, 3.5f, 8) : _time < 16 ? new Vector3(0, 4.4f, 10) : _time < 20 ? new Vector3(8, 12, 20) : new Vector3(15, 23, 25));
            _camera.LookAt(anchor * (_time < 16 ? new Vector3(0, .6f, .6f) : new Vector3(6, 0, -12)));
            if (_time >= 23.7f)
            {
                _camera.Position = anchor * new Vector3(5, 3.5f, 8);
                _camera.LookAt(anchor * new Vector3(0, .6f, .6f));
            }
            if (_time is >= 1.55f and < 1.75f)
            {
                _camera.Position = anchor * new Vector3(1.8f, 2.1f, 4.5f);
                _camera.LookAt(anchor * new Vector3(0, 1.15f, 1.55f));
            }
            else if (_time is >= 1.75f and < 1.95f)
            {
                _camera.Position = anchor * new Vector3(1.7f, 3.1f, -.5f);
                _camera.LookAt(anchor * new Vector3(0, 1.55f, 1.45f));
            }
            _label.Text = $"TS-225 · {(active ? "BOOST" : "RELEASE / IDLE")} · {count} vehicles · {speed:0} m/s\n{_time:0.00} s · {Engine.GetFramesPerSecond()} FPS · Space: restart sequence";
            if (_time is > 21 and < 24) { _frameTimes.Add(delta * 1000); }
            if (_capture < _captureTimes.Length && _time >= _captureTimes[_capture] && !_capturePending) { Capture(_capture++, active); }
            if (OS.GetCmdlineUserArgs().Contains("--boost-clips") && !_clipPending && _time - _lastClipTime >= .05f &&
                (_time is >= 5.7f and <= 6.65f or >= 23.7f and <= 25.2f))
            {
                _lastClipTime = _time;
                CaptureClip();
            }
            if (_time > 27)
            {
                foreach (var car in _cars)
                {
                    var exhaust = FindExhaust(car);
                    if (exhaust.FlameVisible || exhaust.SmokeEmitting || exhaust.Deployment != 0) { throw new InvalidOperationException("Cutoff/new-life left active exhaust."); }
                }
                CheckSlowFrameCutoff();
                _frameTimes.Sort();
                GD.Print($"Boost VFX checks passed: repeated release/reignite, 1/8/32 production network visuals, new life; 32-car frame p95={_frameTimes[(int)(_frameTimes.Count * .95)]:0.00} ms (60 FPS cap). Render captures in .godot/ts225-vfx.");
                if (OS.GetCmdlineUserArgs().Contains("--boost-loop")) { _time = 0; _capture = 0; _frameTimes.Clear(); _reseeded = false; _releaseEnergy = 1; _lastClipTime = 0; }
                else { GetTree().Quit(); }
            }
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }

    private void CheckSlowFrameCutoff()
    {
        // Exercise real production nodes with an elapsed-time hitch, not a simulated
        // smaller frame delta. Both normal and confirmed-depletion tails must expire.
        var car = _cars[0];
        var physics = new VehiclePhysicsState(N.Vector3.Zero, N.Quaternion.Identity, N.Vector3.Zero, N.Vector3.Zero);
        var damage = new VehicleDamageState(1000, 1000, null, null);
        var active = new VehicleSnapshot(1, 3, new VehicleState(1, physics, true, false, 0, 0,
            nitro: new NitroState(60, 18000, 1.4f, 1)), damage, physics);
        var idle = new VehicleSnapshot(1, 3, new VehicleState(2, physics, true, false, 0, 0), damage, physics);
        var exhaust = FindExhaust(car);
        foreach (bool depletion in new[] { false, true })
        {
            car.Apply(active);
            exhaust.Visible = true;
            exhaust._Process(0);
            exhaust.Deploy = true;
            for (int i = 0; i < 60; i++) { exhaust._Process(1.0 / 60); }
            if (!exhaust.FlameVisible) { throw new InvalidOperationException("Slow-frame fixture did not ignite."); }
            car.Apply(idle);
            if (depletion) { exhaust.Exhausted(); }
            exhaust._Process(1.0 / 60);
            if (!exhaust.FlameVisible) { throw new InvalidOperationException("Slow-frame fixture missed shutdown tail."); }
            exhaust._Process(.7);
            if (exhaust.FlameVisible || exhaust.SmokeEmitting || exhaust.SparksEmitting || exhaust.DepletionBurst)
            { throw new InvalidOperationException("Elapsed-time hitch prolonged Boost combustion after shutdown."); }
            exhaust.Reset();
        }
        GD.Print("Boost slow-frame cutoff passed: release and depletion expire across a 700 ms rendering hitch.");
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Space }) { _time = 0; _capture = 0; _reseeded = false; _releaseEnergy = 1; _lastClipTime = 0; }
    }

    private async void CaptureClip()
    {
        _clipPending = true;
        string name = _time < 10 ? "release" : "depletion";
        int milliseconds = (int)(_time * 1000);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng($"res://.godot/ts225-vfx/clip-{name}-{milliseconds}.png");
        _clipPending = false;
    }

    private async void Capture(int index, bool active)
    {
        _capturePending = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var exhaust = FindExhaust(_cars[0]);
        string name = _captureNames[index];
        bool release = name is "release" or "release-middle" or "release-end";
        bool depleted = name.StartsWith("depletion-", StringComparison.Ordinal);
        if (name == "depletion-decay")
        {
            var dead = FindExhaust(_cars[31]);
            if (dead.FlameVisible || dead.SmokeEmitting || dead.DepletionBurst)
            { throw new InvalidOperationException("Death retained the depletion tail."); }
        }
        bool expectedFlame = release || depleted || (active && name is not ("priming" or "fumes" or "short-prime"));
        if (release)
        {
            if (exhaust.FlameEnergy <= 0 || exhaust.FlameEnergy > _releaseEnergy || exhaust.DepletionBurst)
            { throw new InvalidOperationException("Release failed to decay monotonically without a depletion burst."); }
            _releaseEnergy = exhaust.FlameEnergy;
        }
        if (_captureNames[index] == "ready") { _readyFanAngle = exhaust.FanAngle; }
        bool expectedSparks = (active || name is "depletion-burst" or "depletion-decay") && _camera.GlobalPosition.DistanceTo(exhaust.GlobalPosition) < 35;
        if (exhaust.FlameVisible != expectedFlame || exhaust.SmokeEmitting != (active || release || depleted) || exhaust.SparksEmitting != expectedSparks ||
            (depleted && (!exhaust.DepletionBurst || exhaust.Deployment < .999f || _cars[0].Rack.Progress < .999f)) ||
            (name == "depletion-burst" && exhaust.FlameEnergy <= 1) ||
            (name == "reprime-during-decay" && (exhaust.FlameEnergy >= 1 || exhaust.DepletionBurst)) ||
            (_captureNames[index] is "mechanical-detail" or "priming" && Mathf.IsEqualApprox(_readyFanAngle, exhaust.FanAngle)) ||
            (_captureNames[index] == "deployment" && (exhaust.Deployment <= 0 || exhaust.Deployment >= 1)) ||
            (_captureNames[index] is "ready" or "ready-after-release" && (exhaust.Deployment < .999f || _cars[0].Rack.Progress < .999f)) ||
            (_captureNames[index] == "idle" && exhaust.IsVisibleInTree()) ||
            (_captureNames[index] == "switched" && exhaust.IsVisibleInTree()))
        {
            GD.PushError($"Boost presentation mismatch at {_captureNames[index]}.");
            GetTree().Quit(1);
            return;
        }
        using var image = GetViewport().GetTexture().GetImage();
        image.SavePng($"res://.godot/ts225-vfx/{_captureNames[index]}.png");
        _capturePending = false;
    }

    private static BoostExhaust FindExhaust(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is BoostExhaust exhaust) { return exhaust; }
            if (child is Node3D && child.GetChildCount() > 0)
            {
                var found = child.FindChild("BoostExhaust", true, false) as BoostExhaust;
                if (found is not null) { return found; }
            }
        }
        throw new InvalidOperationException("Production vehicle missing Boost exhaust.");
    }
}
