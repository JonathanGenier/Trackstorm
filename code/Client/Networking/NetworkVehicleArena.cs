using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Networking;

/// <summary>Playable host/client arena composing Core replication with synchronous native collision proxies.</summary>
internal sealed partial class NetworkVehicleArena : Node3D
{
    private readonly Dictionary<ulong, NetworkVehicleBody> _bodies = new();
    private readonly VehicleChaseCamera _camera = new() { Name = "ChaseCamera", Current = true, Fov = 65 };
    private readonly RemoteInterpolation _interpolation = new();
    private readonly Items.ItemPresentation _items = new();
    private readonly VehicleDestructionEffects _destruction = new();
    private readonly Audio.ArenaAudio _audio = new();
    private readonly Items.ItemSpawnPresentation _pickups = new();
    private readonly Label _matchLabel = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private string _developerDiagnostics = string.Empty;
    private VehicleNetworkDriver _driver = null!;
    private LobbyNetworkDriver? _lobby;
    private Arenas.CombatArena? _layout;
    private ulong _collisionLife;
    private ulong _collisionTick;
    internal Input.PlayerInputAdapter? CameraInput { get => _camera.InputSource; set => _camera.InputSource = value; }
    /// <summary>Local camera preferences, independent of replicated gameplay configuration.</summary>
    internal Settings.PlayerSettingsController? CameraSettings { get => _camera.SettingsSource; set => _camera.SettingsSource = value; }

    /// <summary>Host pickup tuning supplied before scene entry.</summary>
    internal ItemSpawnConfiguration? SpawnConfiguration { get; init; }
    /// <summary>Explicit old-map fixture for prop/pickup regressions; never enabled by application loading.</summary>
    internal bool PrototypeMapForVerification { get; init; }
    /// <summary>Selected scene retained by the dedicated match loader.</summary>
    internal PackedScene? PreparedMap { get; init; }
    /// <summary>Application sessions require complete synchronization before simulation.</summary>
    internal bool ApplicationEntry { get; init; }
    /// <summary>Actual loaded map, independent of session systems.</summary>
    internal Node3D Map { get; private set; } = null!;
    /// <summary>Scene-derived marker contract used for initial spawns, respawns and migration.</summary>
    internal Core.Arenas.ArenaConfiguration MapConfiguration { get; private set; } = null!;
    /// <summary>Replicated pickup presentation for runtime verification.</summary>
    internal Items.ItemSpawnPresentation Pickups => _pickups;

    /// <summary>Shared authored layout for runtime verification.</summary>
    internal Arenas.CombatArena Layout => _layout ?? throw new InvalidOperationException("The active oval has no prototype arena content.");

    /// <summary>Production session driver exposed to the runtime verification harness.</summary>
    internal VehicleNetworkDriver Driver => _driver;
    /// <summary>Presentation diagnostics for native lifecycle checks.</summary>
    internal VehicleDestructionEffects Destruction => _destruction;
    /// <summary>Arena-owned audio diagnostics for integrated native checks.</summary>
    internal Audio.ArenaAudio Audio => _audio;
    /// <summary>Native bodies for participation/reset integration checks.</summary>
    internal IReadOnlyDictionary<ulong, NetworkVehicleBody> Bodies => _bodies;
    /// <summary>Current local gameplay snapshot for settings telemetry.</summary>
    internal VehicleSnapshot? LocalState => _driver.LocalState;
    /// <summary>Measured render-buffer delay for diagnostics and runtime checks.</summary>
    internal double InterpolationDelay => _interpolation.DelayMilliseconds;
    /// <summary>Existing runtime diagnostics presented by DevTools Stats.</summary>
    internal string DeveloperDiagnostics => _developerDiagnostics;

    private IReadOnlyList<RigidBody3D> Props => _layout?.Props ?? Array.Empty<RigidBody3D>();

    /// <inheritdoc/>
    public override void _ExitTree() => _driver?.Dispose();

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Network visuals already interpolate and smooth corrections explicitly.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        AddChild(new WorldEnvironment
        {
            Environment = _layout is null ? GD.Load<Godot.Environment>("res://assets/maps/oval/Daylight.tres") : new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("172235"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b9d6ed"),
                AmbientLightEnergy = 0.65f,
            }
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = 1.4f, ShadowEnabled = true });
        if (_layout is not null)
        {
            _layout.Replica = _driver.Host is null;
        }

        AddChild(Map);
        foreach (var prop in Props)
        {
            prop.Freeze = ApplicationEntry || _driver.Host is null;
        }

        AddChild(_items);
        AddChild(_destruction);
        AddChild(_audio);
        _driver.LifecycleReceived += snapshot => _audio.ApplyVehicles(snapshot.Vehicles.Select(vehicle => vehicle.State));
        _driver.MatchReceived += state => _audio.ApplyMatch(state);
        _driver.LifecycleReceived += snapshot => _destruction.Apply(snapshot.Vehicles.Select(vehicle => vehicle.State));
        var markers = MapConfiguration;
        _driver.Host?.RegisterSpawns(markers, SpawnConfiguration);
        AddChild(_pickups);
        _pickups.Initialize(markers);
        _driver.ObservePickups = () =>
        {
            var contacts = new List<(string Spawn, ulong Vehicle)>();
            foreach (var marker in Map.GetNodeOrNull<Node3D>("ItemSpawns")?.GetChildren().OfType<Marker3D>() ?? Enumerable.Empty<Marker3D>())
            {
                foreach (var pair in _bodies)
                {
                    float radius = _driver.Configuration.Configuration.Spawns.PickupRadius;
                    if (pair.Value.GlobalPosition.DistanceSquaredTo(marker.GlobalPosition) <= radius * radius)
                    {
                        contacts.Add((marker.Name.ToString(), pair.Key));
                    }
                }
            }

            return contacts;
        };
        _driver.CollideMissile = CollideMissile;
        _driver.PlaceOil = PlaceOil;
        _driver.ItemsReceived += publication =>
        {
            _items.Apply(publication);
            _audio.ApplyVehicles(publication.World.Vehicles.Select(vehicle => vehicle.State));
            _audio.ApplyItems(publication);
            _pickups.Apply(publication);
            if (_driver.Host is not null)
            {
                foreach (var impact in publication.Events.Where(outcome => outcome.Impact))
                {
                    foreach (var prop in Props)
                    {
                        var effect = _driver.Host.Items.Explosion(impact.Position, VehicleBody.ToCore(prop.GlobalPosition));
                        prop.ApplyCentralImpulse(VehicleBody.ToGodot(effect.Impulse));
                    }
                }
            }
        };
        if (Props.Count > 0)
        {
            _driver.ObserveProps = () => Props.Select(prop => new VehiclePhysicsState(VehicleBody.ToCore(prop.GlobalPosition), new System.Numerics.Quaternion(prop.Quaternion.X, prop.Quaternion.Y, prop.Quaternion.Z, prop.Quaternion.W), VehicleBody.ToCore(prop.LinearVelocity), VehicleBody.ToCore(prop.AngularVelocity))).ToArray();
        }

        _driver.PropsReceived += snapshot =>
        {
            for (int index = 0; index < snapshot.Bodies.Count; index++)
            {
                VehiclePhysicsState state = snapshot.Bodies[index];
                Props[index].GlobalTransform = new Transform3D(new Basis(VehicleBody.ToGodot(state.Orientation)), VehicleBody.ToGodot(state.Position));
                Props[index].LinearVelocity = VehicleBody.ToGodot(state.LinearVelocity);
                Props[index].AngularVelocity = VehicleBody.ToGodot(state.AngularVelocity);
            }
        };
        AddChild(_camera);
        _camera.Position = new Vector3(0, 24, 38);
        _camera.LookAt(Vector3.Zero);
        var layer = new CanvasLayer();
        AddChild(layer);
        var matchPanel = new PanelContainer { AnchorRight = 1, OffsetLeft = 24, OffsetRight = -24, OffsetTop = 90, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(matchPanel);
        matchPanel.AddChild(_matchLabel);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (ApplicationEntry && !_driver.EntryReady)
        {
            return;
        }

        if (_driver.Latest is { } world)
        {
            _audio.Follow(world.Vehicles.Select(vehicle => vehicle.State), id => _bodies[id].VisualPosition, world.Tick);
        }

        if (_driver.Match is { } match)
        {
            string phase = match.Phase == Core.Matches.MatchPhase.Finished ? $"FINISHED — Player {match.Winner} wins!"
                : match.Phase == Core.Matches.MatchPhase.Countdown ? $"COUNTDOWN — {Math.Ceiling(Math.Max(0, (double)match.CountdownAtTick!.Value - (_driver.Latest?.Tick ?? 0)) / HostVehicleSession.TickRate):0}"
                : match.Phase == Core.Matches.MatchPhase.Waiting ? "WAITING FOR PLAYERS" : $"FIRST TO {match.KillTarget}";
            _matchLabel.Text = phase;
        }

        if (_driver.History is not null && _driver.SnapshotAge is double clientSnapshotAge)
        {
            _interpolation.Advance(_driver.History, delta, clientSnapshotAge);
        }

        foreach (var pair in _bodies)
        {
            if (_driver.Host is not null || pair.Key == _driver.LocalVehicleId)
            {
                pair.Value.PresentLocal((float)delta);
            }
            else if (_driver.History is not null && RemoteInterpolation.Sample(_driver.History, pair.Key, _interpolation.RenderTick, _driver.Latest?.Vehicles.Single(vehicle => vehicle.State.VehicleId == pair.Key).State.LifeId) is VehiclePhysicsState remote)
            {
                pair.Value.PresentRemote(remote);
            }
        }

        if (_bodies.TryGetValue(_driver.LocalVehicleId, out var local))
        {
            if (_driver.LocalState is VehicleSnapshot cameraState)
            {
                _camera.Follow(local.VisualTransform, cameraState, (float)delta, local.GetRid());
            }
        }

        foreach (var pair in _bodies)
        {
            if (pair.Value.GetNodeOrNull<RemoteVehicleTag>("PlayerTag") is { } tag)
            {
                var player = _lobby?.State?.Players.SingleOrDefault(player => player.Id == pair.Key);
                tag.Present(
                    _driver.IsActive && player?.Connected == true ? player.Name : null,
                    _driver.Latest?.Vehicles.SingleOrDefault(vehicle => vehicle.State.VehicleId == pair.Key)?.State,
                    pair.Value.VisualPosition,
                    _camera);
            }
        }

        string role = _driver.Host is null ? "CLIENT" : "HOST";
        string status = _driver.Failure.Length > 0 ? "Replication failed" : _driver.LocalState is null ? "Connecting…" : $"HP {_driver.LocalState.Damage.CurrentHP:0} / {_driver.LocalState.Damage.MaxHP:0}   {_driver.LocalState.Lifecycle}   {_driver.LocalState.Movement.CurrentSurface}   {(_driver.LocalState.Movement.Handbrake > 0 ? "HANDBRAKE" : _driver.LocalState.Movement.Drifting ? "SLIDING" : _driver.LocalState.Movement.Grounded ? "GROUNDED" : "AIRBORNE")}";
        string formattedSnapshotAge = FormatSnapshotAge(_driver.SnapshotAge);
        _developerDiagnostics = $"{role}   {_bodies.Count}/8 vehicles   {status}\nPrediction error  {_driver.Prediction?.PredictionError ?? 0:0.000} m   Snapshot age  {formattedSnapshotAge}   Interpolation  {InterpolationDelay:0} ms\nLast acknowledged input  {_driver.Prediction?.History.LastAcknowledged ?? 0}   Corrections ≥3m  {local?.Smoothing.HardSnaps ?? 0}";
    }

    /// <summary>Formats client authority freshness without assigning that metric to a host.</summary>
    /// <returns>HUD-ready diagnostic value.</returns>
    /// <param name="seconds">Elapsed client snapshot time, or null when not applicable.</param>
    internal static string FormatSnapshotAge(double? seconds) => seconds is double age ? $"{age * 1000:0} ms" : "N/A";

    /// <summary>Binds a caller-owned transport before scene entry.</summary>
    /// <param name="gateway">Active transport.</param>
    /// <param name="session">Nonzero host generation, or zero for a joining client.</param>
    /// <param name="serverPeer">Client's actual transport server identity.</param>
    /// <param name="lobby">Optional admitted development lobby.</param>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    internal void Initialize(ITransportGateway gateway, ulong session, ulong serverPeer, LobbyNetworkDriver? lobby = null, Core.Development.GameplayConfiguration? configuration = null)
    {
        _lobby = lobby;
        if (PrototypeMapForVerification || lobby?.State?.Map == Core.Sessions.MatchMap.OldMap)
        {
            _layout = new Arenas.CombatArena { Name = "PrototypeArena" };
            Map = _layout;
            MapConfiguration = Core.Arenas.PrototypeArena.Configuration;
        }
        else
        {
            Map = PreparedMap?.Instantiate<Node3D>() ?? Arenas.ActiveMap.Load();
            MapConfiguration = Arenas.ActiveMap.ReadConfiguration(Map);
        }

        _driver = new VehicleNetworkDriver(gateway, session, serverPeer, lobby, configuration: configuration ?? Core.Development.GameplayConfiguration.HostedDefaults, arena: MapConfiguration, applicationEntry: ApplicationEntry);
        _driver.ConfigurationChanged += accepted =>
        {
            foreach (var body in _bodies.Values)
            {
                body.ApplyConfiguration(accepted.Vehicle);
            }
        };
        _driver.RosterChanged += SynchronizeBodies;
        _driver.LocalCorrected += state => _bodies[state.VehicleId].Apply(state, true);
        _driver.Resynchronized += snapshot =>
        {
            _camera.ResetFollow();
            _audio.ApplyVehicles(snapshot.Vehicles.Select(vehicle => vehicle.State), true);
            _audio.ApplyItems(_driver.ItemState!, true);
            _audio.ApplyMatch(_driver.Match!, true);
            _interpolation.Reset();
            if (_layout is not null)
            {
                _layout.Replica = _driver.Host is null;
            }

            foreach (var body in _bodies.Values)
            {
                body.PushProps = _driver.Host is not null;
            }

            _destruction.Reseed(snapshot.Vehicles.Select(vehicle => vehicle.State));
            foreach (var vehicle in snapshot.Vehicles)
            {
                _bodies[vehicle.State.VehicleId].Reseed(vehicle.State);
            }

            _collisionLife = _driver.LocalState!.LifeId;
            _collisionTick = snapshot.Tick;
        };
    }

    /// <summary>Advances production networking and simulation once per captured physics input.</summary>
    /// <param name="input">Immediately captured local logical input.</param>
    internal void Advance(InputFrame input)
    {
        _audio.Initialize(_driver.LocalVehicleId);
        _driver.Advance(input, state =>
        {
            VehicleObservation observation = _bodies[state.VehicleId].Observe(state);
            // Prediction replay must not replay already presented contact impulses.
            if (state.VehicleId == _driver.LocalVehicleId && (state.LifeId != _collisionLife || state.Movement.Tick > _collisionTick))
            {
                _collisionLife = state.LifeId;
                _collisionTick = state.Movement.Tick;
                _camera.ObserveCollision(observation, _driver.Configuration.Configuration.Vehicle.Mass);
            }

            return observation;
        });
        foreach (var prop in Props)
        {
            prop.Freeze = _driver.Host is null || !_driver.IsActive;
        }

        if (!_driver.IsActive)
        {
            return;
        }

        if (_driver.Latest is { } audioWorld)
        {
            _audio.Initialize(_driver.LocalVehicleId);
            _audio.ApplyVehicles(audioWorld.Vehicles.Select(vehicle => vehicle.State));
        }

        if (_driver.Host is not null)
        {
            foreach (VehicleSnapshot state in _driver.Host.World.State.Vehicles)
            {
                _bodies[state.VehicleId].Apply(state);
            }
        }
        else if (_driver.LocalState is VehicleSnapshot state)
        {
            _bodies[state.VehicleId].Apply(state);
        }
    }

    /// <summary>Development acquisition seam; only the host may fill empty living slots.</summary>
    /// <param name="item">One of the two supported items.</param>
    internal void GrantItems(HeldItem item)
    {
        if (_driver.Host is not null)
        {
            foreach (var vehicle in _driver.Host.World.State.Vehicles)
            {
                _driver.Host.Items.Grant(_driver.Host.World, vehicle.VehicleId, item);
            }
        }
    }

    private OilPatch? PlaceOil(ItemSlot slot, VehiclePhysicsState pose)
    {
        Vector3 up = VehicleBody.ToGodot(System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitY, pose.Orientation));
        Vector3 behind = VehicleBody.ToGodot(pose.Position + System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ * 4, pose.Orientation));
        using var ray = PhysicsRayQueryParameters3D.Create(behind + up, behind - up * 5, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        if (hit.Count == 0 || hit["collider"].AsGodotObject() is not StaticBody3D) { return null; }
        Vector3 center = hit["position"].AsVector3();
        Vector3 normal = hit["normal"].AsVector3().Normalized();
        if (normal.Y < 0.55f) { return null; }
        Vector3 tangent = normal.Cross(Vector3.Forward).Normalized();
        Vector3 bitangent = normal.Cross(tangent);
        // Refuse ledges, obstacles and surfaces that cannot support the entire planar footprint.
        for (int i = 0; i < 16; i++)
        {
            float angle = i * Mathf.Tau / 16;
            Vector3 point = center + 3 * (tangent * Mathf.Cos(angle) + bitangent * Mathf.Sin(angle));
            ray.From = point + normal * 0.5f;
            ray.To = point - normal * 0.5f;
            var sample = GetWorld3D().DirectSpaceState.IntersectRay(ray);
            if (sample.Count == 0 || sample["collider"].AsGodotObject() is not StaticBody3D ||
                sample["normal"].AsVector3().Dot(normal) < 0.98f ||
                Mathf.Abs((sample["position"].AsVector3() - point).Dot(normal)) > 0.06f)
            {
                return null;
            }
        }
        return new OilPatch(slot.Token, slot.Vehicle, new System.Numerics.Vector3(center.X, center.Y, center.Z), new System.Numerics.Vector3(normal.X, normal.Y, normal.Z), 3);
    }

    private float? CollideMissile(MissileState missile, System.Numerics.Vector3 end)
    {
        var exclude = new Godot.Collections.Array<Rid>();
        if (_bodies.TryGetValue(missile.Owner, out var owner))
        {
            exclude.Add(owner.GetRid());
        }

        Vector3 start = VehicleBody.ToGodot(missile.Position);
        Vector3 finish = VehicleBody.ToGodot(end);
        using var ray = PhysicsRayQueryParameters3D.Create(start, finish, 3, exclude);
        ray.HitFromInside = true;
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(ray);
        return hit.Count == 0 ? null : Math.Clamp(start.DistanceTo(hit["position"].AsVector3()) / start.DistanceTo(finish), 0, 1);
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
                body = new NetworkVehicleBody { Name = $"Vehicle{id}", VehicleId = id, PushProps = _driver.Host is not null };
                body.ApplyConfiguration(_driver.Configuration.Configuration.Vehicle);
                AddChild(body);
                if (id != _driver.LocalVehicleId && _lobby is not null)
                {
                    body.AddChild(new RemoteVehicleTag { Name = "PlayerTag", Visible = false });
                }

                _bodies.Add(id, body);
                body.Apply(vehicle.State);
            }
            else if (_driver.Host is not null || id != _driver.LocalVehicleId)
            {
                body.Apply(vehicle.State);
            }

            body.SynchronizeLifecycle(vehicle.State);
        }
    }

}
