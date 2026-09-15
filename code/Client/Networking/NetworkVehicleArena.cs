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
    private readonly Label _diagnostics = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly RemoteInterpolation _interpolation = new();
    private readonly Items.ItemPresentation _items = new();
    private readonly VehicleDestructionEffects _destruction = new();
    private readonly Items.ItemSpawnPresentation _pickups = new();
    private readonly Label _itemLabel = new();
    private readonly Label _matchLabel = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private VehicleNetworkDriver _driver = null!;
    private Arenas.CombatArena _layout = null!;
    private ulong _collisionLife;
    private ulong _collisionTick;

    /// <summary>Host pickup tuning supplied before scene entry.</summary>
    internal ItemSpawnConfiguration SpawnConfiguration { get; init; } = new();
    /// <summary>Replicated pickup presentation for runtime verification.</summary>
    internal Items.ItemSpawnPresentation Pickups => _pickups;

    /// <summary>Shared authored layout for runtime verification.</summary>
    internal Arenas.CombatArena Layout => _layout;

    /// <summary>Production session driver exposed to the runtime verification harness.</summary>
    internal VehicleNetworkDriver Driver => _driver;
    /// <summary>Presentation diagnostics for native lifecycle checks.</summary>
    internal VehicleDestructionEffects Destruction => _destruction;
    /// <summary>Native bodies for participation/reset integration checks.</summary>
    internal IReadOnlyDictionary<ulong, NetworkVehicleBody> Bodies => _bodies;
    /// <summary>Current local gameplay snapshot for settings telemetry.</summary>
    internal VehicleSnapshot? LocalState => _driver.LocalState;
    /// <summary>Measured render-buffer delay for diagnostics and runtime checks.</summary>
    internal double InterpolationDelay => _interpolation.DelayMilliseconds;

    /// <inheritdoc/>
    public override void _Ready()
    {
        // Network visuals already interpolate and smooth corrections explicitly.
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
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
        _layout = new Arenas.CombatArena { Name = "PrototypeArena", Replica = _driver.Host is null };
        AddChild(_layout);
        AddChild(_items);
        AddChild(_destruction);
        _driver.LifecycleReceived += snapshot => _destruction.Apply(snapshot.Vehicles.Select(vehicle => vehicle.State));
        var markers = _layout.ValidateScene();
        _driver.Host?.RegisterSpawns(markers, SpawnConfiguration);
        AddChild(_pickups);
        _pickups.Initialize(markers);
        _driver.ObservePickups = () =>
        {
            var contacts = new List<(string Spawn, ulong Vehicle)>();
            foreach (var marker in _layout.GetNode<Node3D>("ItemSpawns").GetChildren().OfType<Marker3D>())
            {
                foreach (var pair in _bodies)
                {
                    if (pair.Value.GlobalPosition.DistanceSquaredTo(marker.GlobalPosition) <= SpawnConfiguration.PickupRadius * SpawnConfiguration.PickupRadius)
                    {
                        contacts.Add((marker.Name.ToString(), pair.Key));
                    }
                }
            }

            return contacts;
        };
        _driver.CollideMissile = CollideMissile;
        _driver.ItemsReceived += publication =>
        {
            _items.Apply(publication);
            _pickups.Apply(publication);
            if (_driver.Host is not null)
            {
                foreach (var impact in publication.Events.Where(outcome => outcome.Impact))
                {
                    foreach (var prop in _layout.Props)
                    {
                        var effect = _driver.Host.Items.Explosion(impact.Position, VehicleBody.ToCore(prop.GlobalPosition));
                        prop.ApplyCentralImpulse(VehicleBody.ToGodot(effect.Impulse));
                    }
                }
            }
        };
        _driver.ObserveProps = () => _layout.Props.Select(prop => new VehiclePhysicsState(VehicleBody.ToCore(prop.GlobalPosition), new System.Numerics.Quaternion(prop.Quaternion.X, prop.Quaternion.Y, prop.Quaternion.Z, prop.Quaternion.W), VehicleBody.ToCore(prop.LinearVelocity), VehicleBody.ToCore(prop.AngularVelocity))).ToArray();
        _driver.PropsReceived += snapshot =>
        {
            for (int index = 0; index < snapshot.Bodies.Count; index++)
            {
                VehiclePhysicsState state = snapshot.Bodies[index];
                _layout.Props[index].GlobalTransform = new Transform3D(new Basis(VehicleBody.ToGodot(state.Orientation)), VehicleBody.ToGodot(state.Position));
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
        var tools = new VBoxContainer { Position = new Vector2(24, 190) };
        layer.AddChild(tools);
        var toggle = new Button { Text = "Arena tools", ToggleMode = true };
        tools.AddChild(toggle);
        var content = new VBoxContainer { Visible = false };
        tools.AddChild(content);
        toggle.Toggled += visible => content.Visible = visible;
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(360, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("172235"), ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8 });
        content.AddChild(panel);
        panel.AddChild(_diagnostics);
        var itemPanel = new VBoxContainer();
        content.AddChild(itemPanel);
        itemPanel.AddChild(_itemLabel);
        var use = new Button { Text = "Use held item" };
        use.Pressed += () => _driver.RequestItemUse();
        tools.AddChild(use);
        if (_driver.Host is not null)
        {
            var grants = new HBoxContainer();
            itemPanel.AddChild(grants);
            foreach (HeldItem item in new[] { HeldItem.Wrench, HeldItem.Missile })
            {
                var grant = new Button { Text = $"Dev: give {item} to empty slots" };
                grant.Pressed += () => GrantItems(item);
                grants.AddChild(grant);
            }
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_driver.Match is { } match)
        {
            string phase = match.Phase == Core.Matches.MatchPhase.Finished ? $"FINISHED — Player {match.Winner} wins!"
                : match.Phase == Core.Matches.MatchPhase.Countdown ? $"COUNTDOWN — {Math.Ceiling(Math.Max(0, (double)match.CountdownAtTick!.Value - (_driver.Latest?.Tick ?? 0)) / HostVehicleSession.TickRate):0}"
                : match.Phase == Core.Matches.MatchPhase.Waiting ? "WAITING FOR PLAYERS" : $"FIRST TO {match.KillTarget}";
            _matchLabel.Text = phase + "\n" + string.Join("   ", match.Players.Where(player => _bodies.ContainsKey(player.Player) || player.Player == match.Winner).Select(player => $"P{player.Player}: {player.Kills} K / {player.Deaths} D"));
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
                _camera.Follow(local.VisualTransform, cameraState, (float)delta);
            }
        }

        foreach (var pair in _bodies)
        {
            _items.Follow(pair.Key, pair.Value.VisualPosition, _driver.Latest?.Vehicles.SingleOrDefault(vehicle => vehicle.State.VehicleId == pair.Key)?.State.CanInteract == true);
        }

        _itemLabel.Text = $"HELD ITEM: {(_driver.LocalState?.CanInteract == true ? _driver.LocalItem?.Item ?? HeldItem.None : HeldItem.None)}";
        string role = _driver.Host is null ? "CLIENT" : "HOST";
        string status = _driver.Failure.Length > 0 ? _driver.Failure : _driver.LocalState is null ? "Connecting…" : $"HP {_driver.LocalState.Damage.CurrentHP:0} / {_driver.LocalState.Damage.MaxHP:0}   {_driver.LocalState.Lifecycle}   {_driver.LocalState.Movement.CurrentSurface}   {(_driver.LocalState.Movement.Handbrake > 0 ? "HANDBRAKE" : _driver.LocalState.Movement.Drifting ? "SLIDING" : _driver.LocalState.Movement.Grounded ? "GROUNDED" : "AIRBORNE")}";
        string formattedSnapshotAge = FormatSnapshotAge(_driver.SnapshotAge);
        _diagnostics.Text = $"{role}   {_bodies.Count}/8 vehicles   {status}\nPrediction error  {_driver.Prediction?.PredictionError ?? 0:0.000} m   Snapshot age  {formattedSnapshotAge}   Interpolation  {InterpolationDelay:0} ms\nLast acknowledged input  {_driver.Prediction?.History.LastAcknowledged ?? 0}   Corrections ≥3m  {local?.Smoothing.HardSnaps ?? 0}";
    }

    /// <summary>Formats client authority freshness without assigning that metric to a host.</summary>
    /// <param name="seconds">Elapsed client snapshot time, or null when not applicable.</param>
    /// <returns>HUD-ready diagnostic value.</returns>
    internal static string FormatSnapshotAge(double? seconds) => seconds is double age ? $"{age * 1000:0} ms" : "N/A";

    /// <summary>Binds a caller-owned transport before scene entry.</summary>
    /// <param name="gateway">Active transport.</param>
    /// <param name="session">Nonzero host generation, or zero for a joining client.</param>
    /// <param name="serverPeer">Client's actual transport server identity.</param>
    /// <param name="lobby">Optional admitted development lobby.</param>
    internal void Initialize(ITransportGateway gateway, ulong session, ulong serverPeer, LobbyNetworkDriver? lobby = null)
    {
        _driver = new VehicleNetworkDriver(gateway, session, serverPeer, lobby, new DamageConfiguration { MaxHP = 1000 });
        _driver.RosterChanged += SynchronizeBodies;
        _driver.LocalCorrected += state => _bodies[state.VehicleId].Apply(state, true);
    }

    /// <summary>Advances production networking and simulation once per captured physics input.</summary>
    /// <param name="input">Immediately captured local logical input.</param>
    internal void Advance(InputFrame input)
    {
        _driver.Advance(input, state =>
        {
            VehicleObservation observation = _bodies[state.VehicleId].Observe(state);
            // Prediction replay must not replay already presented contact impulses.
            if (state.VehicleId == _driver.LocalVehicleId && (state.LifeId != _collisionLife || state.Movement.Tick > _collisionTick))
            {
                _collisionLife = state.LifeId;
                _collisionTick = state.Movement.Tick;
                _camera.ObserveCollision(observation, 900);
            }

            return observation;
        });
        if (!_driver.IsActive)
        {
            return;
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
                AddChild(body);
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
