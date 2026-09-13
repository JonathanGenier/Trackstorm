using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Networking;

/// <summary>Playable host/client arena composing Core replication with synchronous native collision proxies.</summary>
internal sealed partial class NetworkVehicleArena : Node3D
{
    private readonly Dictionary<ulong, NetworkVehicleBody> _bodies = new();
    private readonly Camera3D _camera = new() { Current = true, Fov = 65 };
    private readonly Label _diagnostics = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly RemoteInterpolation _interpolation = new();
    private VehicleNetworkDriver _driver = null!;

    /// <summary>Production session driver exposed to the runtime verification harness.</summary>
    internal VehicleNetworkDriver Driver => _driver;
    /// <summary>Current local gameplay snapshot for settings telemetry.</summary>
    internal VehicleSnapshot? LocalState => _driver.LocalState;
    /// <summary>Measured render-buffer delay for diagnostics and runtime checks.</summary>
    internal double InterpolationDelay => _interpolation.DelayMilliseconds;

    /// <inheritdoc/>
    public override void _Ready()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("172235"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b9d6ed"),
                AmbientLightEnergy = 0.65f,
            }
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = 1.4f, ShadowEnabled = true });
        AddStatic(new Vector3(80, 1, 80), new Vector3(0, -0.5f, 0), new Color("303b4b"));
        AddStatic(new Vector3(1, 4, 80), new Vector3(-40, 1.5f, 0), new Color("bf6d39"));
        AddStatic(new Vector3(1, 4, 80), new Vector3(40, 1.5f, 0), new Color("bf6d39"));
        AddStatic(new Vector3(80, 4, 1), new Vector3(0, 1.5f, -40), new Color("bf6d39"));
        AddStatic(new Vector3(80, 4, 1), new Vector3(0, 1.5f, 40), new Color("bf6d39"));
        StaticBody3D ramp = AddStatic(new Vector3(8, 0.4f, 12), new Vector3(0, 0.95f, -5), new Color("607789"));
        ramp.Rotation = new Vector3(0.2f, 0, 0);
        AddStatic(new Vector3(5, 2, 5), new Vector3(-15, 1, -10), new Color("7b879a"));
        AddChild(_camera);
        _camera.Position = new Vector3(0, 24, 38);
        _camera.LookAt(Vector3.Zero);
        var layer = new CanvasLayer();
        AddChild(layer);
        var panel = new PanelContainer { AnchorRight = 1, OffsetLeft = 190, OffsetTop = 24, OffsetRight = -24, OffsetBottom = 24, GrowVertical = Control.GrowDirection.End, MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("172235"), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8 });
        layer.AddChild(panel);
        panel.AddChild(_diagnostics);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_driver.History is not null)
        {
            _interpolation.Advance(_driver.History, delta, _driver.SnapshotAge);
        }

        foreach (var pair in _bodies)
        {
            if (_driver.Host is not null || pair.Key == _driver.LocalVehicleId)
            {
                pair.Value.PresentLocal((float)delta);
            }
            else if (_driver.History is not null && RemoteInterpolation.Sample(_driver.History, pair.Key, _interpolation.RenderTick) is VehiclePhysicsState remote)
            {
                pair.Value.PresentRemote(remote);
            }
        }

        if (_bodies.TryGetValue(_driver.LocalVehicleId, out var local))
        {
            Vector3 target = local.VisualPosition;
            Vector3 desired = target + new Vector3(0, 8, 13);
            desired.X = Math.Clamp(desired.X, -34, 34);
            desired.Z = Math.Clamp(desired.Z, -34, 34);
            _camera.GlobalPosition = _camera.GlobalPosition.Lerp(desired, 1 - MathF.Exp(-6 * (float)delta));
            _camera.LookAt(target);
        }

        string role = _driver.Host is null ? "CLIENT" : "HOST";
        string status = _driver.Failure.Length > 0 ? _driver.Failure : _driver.LocalState is null ? "Connecting…" : $"HP {_driver.LocalState.Damage.CurrentHP:0} / {_driver.LocalState.Damage.MaxHP:0}   {(_driver.LocalState.Movement.BoostTicks > 0 ? "BOOST" : _driver.LocalState.Movement.Drifting ? "DRIFT" : _driver.LocalState.Movement.Grounded ? "GROUNDED" : "AIRBORNE")}";
        _diagnostics.Text = $"{role}   {_bodies.Count}/8 vehicles   {status}\nPrediction error  {_driver.Prediction?.PredictionError ?? 0:0.000} m   Snapshot age  {_driver.SnapshotAge * 1000:0} ms   Interpolation  {InterpolationDelay:0} ms\nLast acknowledged input  {_driver.Prediction?.History.LastAcknowledged ?? 0}   Corrections ≥3m  {local?.Smoothing.HardSnaps ?? 0}";
    }

    /// <summary>Binds a caller-owned transport before scene entry.</summary>
    /// <param name="gateway">Active transport.</param>
    /// <param name="session">Nonzero host generation, or zero for a joining client.</param>
    /// <param name="serverPeer">Client's actual transport server identity.</param>
    internal void Initialize(ITransportGateway gateway, ulong session, ulong serverPeer)
    {
        _driver = new VehicleNetworkDriver(gateway, session, serverPeer);
        _driver.RosterChanged += SynchronizeBodies;
        _driver.LocalCorrected += state => _bodies[state.VehicleId].Apply(state.Movement.Physics, true);
    }

    /// <summary>Advances production networking and simulation once per captured physics input.</summary>
    /// <param name="input">Immediately captured local logical input.</param>
    internal void Advance(InputFrame input)
    {
        _driver.Advance(input, state => _bodies[state.VehicleId].Observe(state));
        if (_driver.Host is not null)
        {
            foreach (VehicleSnapshot state in _driver.Host.World.State.Vehicles)
            {
                _bodies[state.VehicleId].Apply(state.Movement.Physics);
            }
        }
        else if (_driver.LocalState is VehicleSnapshot state)
        {
            _bodies[state.VehicleId].Apply(state.Movement.Physics);
        }
    }

    private void SynchronizeBodies(WorldSnapshot snapshot)
    {
        var active = snapshot.Vehicles.Select(vehicle => vehicle.State.VehicleId).ToHashSet();
        foreach (ulong id in _bodies.Keys.Except(active).ToArray())
        {
            _bodies[id].CollisionLayer = 0;
            _bodies[id].QueueFree();
            _bodies.Remove(id);
        }

        foreach (ReplicatedVehicle vehicle in snapshot.Vehicles)
        {
            ulong id = vehicle.State.VehicleId;
            if (!_bodies.TryGetValue(id, out var body))
            {
                body = new NetworkVehicleBody { Name = $"Vehicle{id}", VehicleId = id };
                AddChild(body);
                _bodies.Add(id, body);
                body.Apply(vehicle.State.Movement.Physics);
            }
            else if (_driver.Host is not null || id != _driver.LocalVehicleId)
            {
                body.Apply(vehicle.State.Movement.Physics);
            }
        }
    }

    private StaticBody3D AddStatic(Vector3 size, Vector3 position, Color color)
    {
        var body = new StaticBody3D { Position = position };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(VehicleBody.Box(size, Vector3.Zero, color));
        AddChild(body);
        return body;
    }
}
