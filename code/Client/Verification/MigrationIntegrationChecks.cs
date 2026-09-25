using System.Net;
using System.Net.Sockets;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

/// <summary>Two or three real UDP peers and isolated Godot worlds; identity is an explicit local test seam.</summary>
public sealed partial class MigrationIntegrationChecks : Node
{
    private readonly Dictionary<ulong, IReadOnlyList<Core.Items.MissileState>> _salvoBoundaries = new();
    private OilPatch? _oil;
    private readonly DevelopmentSession?[] _presentations = new DevelopmentSession?[3];
    private readonly Dictionary<ulong, (Node3D Root, int Slot)>[] _showcases = [new(), new(), new()];
    private readonly GameNetworkingSocketsTransport[] _gateways = new GameNetworkingSocketsTransport[3];
    private readonly LobbyNetworkDriver?[] _drivers = new LobbyNetworkDriver?[3];
    private readonly NetworkVehicleArena?[] _arenas = new NetworkVehicleArena?[3];
    private readonly SubViewport[] _views = new SubViewport[3];
    private readonly Dictionary<ulong, string>[] _subjects = Enumerable.Range(0, 3).Select(_ => new Dictionary<ulong, string>()).ToArray();
    private readonly Queue<string>[] _connecting = Enumerable.Range(0, 3).Select(_ => new Queue<string>()).ToArray();
    private readonly string[] _endpoints = new string[3];
    private readonly long?[] _retiredAt = new long?[3];
    private int _stage;
    private int _frames;
    private int _boundary;
    private int _players = 3;
    private bool _finished;
    private PackedScene _preparedMap = null!;
    private NetworkVehicleBody? _retainedBody;
    private Core.Development.GameplayConfigurationState? _configuration;
    private string _categoryHistory = string.Empty;
    private ulong _randomState;
    private ulong _nitroOwner;
    private Core.Matches.MatchPhase _matchPhase;
    private ulong? _countdownAtTick;
    private readonly Dictionary<ulong, Core.Matches.MatchState> _circusBoundaries = new();

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
        _players = OS.GetCmdlineUserArgs().Contains("--migration-players=2") ? 2 : 3;
        // Production MatchResourceLoader prepares resources before arena entry.
        // Do not block three live peer heartbeats on cold resource loading in this fixture.
        _preparedMap = GD.Load<PackedScene>(Arenas.ActiveMap.ScenePath);
        for (int i = 0; i < _players; i++)
        {
            int index = i;
            using var reservation = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _endpoints[i] = $"127.0.0.1:{((IPEndPoint)reservation.Client.LocalEndPoint!).Port}";
            _gateways[i] = new GameNetworkingSocketsTransport();
            _gateways[i].ConnectionChanged += change =>
            {
                if (change.State == TransportConnectionState.Connected && _connecting[index].TryDequeue(out string? subject))
                {
                    _subjects[index][change.RemotePeerId] = subject;
                }
            };
            _views[i] = new SubViewport { Size = new Vector2I(640, 360), OwnWorld3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
            AddChild(_views[i]);
        }

        _gateways[0].Listen(TransportEndpoint.DirectIp(_endpoints[0]));
        CreateDriver(0, 0, true);
        Require(_drivers[0]!.Authority!.TryConfigure(0, new Dictionary<string, double> { ["environment.preset"] = (int)Core.Development.EnvironmentPreset.Night, ["vehicle.acceleration"] = 7 }, out _), "Original lobby host configures the session.");
        _configuration = _drivers[0]!.Authority!.Configuration;
        CreateDriver(1, Connect(1, 0), false);
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_finished)
        {
            if (++_frames > _boundary + 4)
            {
                GD.Print($"Migration integration passed: {_players} native UDP peers; intentional lobby host leave; former-host fresh join; active native match authority loss; sequential epochs; retained vehicles; prediction reset; item/spawn/match restore. Identity is a trusted test seam, not real EOS.");
                GetTree().Quit();
            }

            return;
        }

        try
        {
            Require(++_frames < 3000, $"Migration stage {_stage} timed out: {string.Join("; ", _drivers.Select(driver => driver?.Failure))}");
            for (int i = 0; i < _players; i++)
            {
                if (_drivers[i] is not null)
                {
                    if (_arenas[i] is { } arena)
                    {
                        arena.Advance(default);
                    }
                    else
                    {
                        _drivers[i]!.Pump(delta);
                    }
                }
            }

            Scenario();
        }
        catch (Exception exception)
        {
            GD.PrintErr(exception);
            Cleanup();
            GetTree().Quit(1);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private ulong Connect(int client, int host)
    {
        _connecting[host].Enqueue("native-" + client);
        return _gateways[client].Connect(TransportEndpoint.DirectIp(_endpoints[host]));
    }

    private void CreateDriver(int index, ulong server, bool host, ulong player = 0, ulong epoch = 1)
    {
        var driver = new LobbyNetworkDriver(_gateways[index], host ? 900UL : 0, server, "Player" + index, _ => true, 900, peer => _subjects[index].GetValueOrDefault(peer), epoch);
        driver.Reconnect = () => throw new InvalidOperationException("Host is unavailable in this controlled loss scenario.");
        driver.Migration = new SessionMigration(driver, _gateways[index], "native-" + index, peer => _subjects[index].GetValueOrDefault(peer), (subject, listen) =>
        {
            _gateways[index].Stop();
            _subjects[index].Clear();
            int target = int.Parse(subject.AsSpan(7), System.Globalization.CultureInfo.InvariantCulture);
            if (listen)
            {
                _gateways[index].Listen(TransportEndpoint.DirectIp(_endpoints[index]));
                return 0;
            }

            return Connect(index, target);
        });
        // This local harness directly observes destruction of the old authority's transport.
        driver.Migration.RetirementConfirmedAt = checkpoint =>
        {
            int host = int.Parse(checkpoint.Lobby.Subjects[checkpoint.Lobby.State.CurrentHostId].AsSpan(7), System.Globalization.CultureInfo.InvariantCulture);
            return !_gateways[host].IsListening ? _retiredAt[host] : null;
        };
        if (player != 0)
        {
            driver.BeginResume(player, 1);
        }

        _drivers[index] = driver;
        if (_presentations[index] is null)
        {
            _presentations[index] = new DevelopmentSession();
            _views[index].AddChild(_presentations[index]);
        }
        _presentations[index]!.BindLobby(_gateways[index], driver);
    }

    private void Scenario()
    {
        if (_stage == 0 && _drivers[1]!.State?.Players.Count == 2)
        {
            if (_players == 3)
            {
                CreateDriver(2, Connect(2, 0), false);
            }

            _stage = 1;
        }
        else if (_stage == 1 && _drivers.Take(_players).All(driver => driver?.Migration?.Subjects?.Count == _players))
        {
            var authority = _drivers[0]!.Authority!;
            authority.SelectMap(0, MatchMap.OldMap);
            foreach (ulong peer in authority.Peers.Keys) authority.SetReady(peer, true);
            _stage = 101;
        }
        else if (_stage == 101 && _drivers.Take(_players).All(driver => driver!.Migration!.LobbyRevision == driver.State!.Revision && driver.State.Map == MatchMap.OldMap && driver.State.Players.Where(player => player.Id != 1).All(player => player.Ready)))
        {
            for (int i = 0; i < _players; i++)
            {
                _presentations[i]!.JoinedLobby.Refresh();
                foreach (var pair in _presentations[i]!.JoinedLobby.Cars) _showcases[i][pair.Key] = (pair.Value.Root, pair.Value.Slot);
            }
            var migration = _drivers[0]!.Migration!;
            migration.AuthorityAvailable = () => false;
            migration.Advance(0);
            _presentations[0]!.JoinedLobby.Refresh();
            Require(migration.Frozen, "Authority is explicitly frozen for local-navigation verification.");
            foreach (string label in new[] { "Settings", "Quit to Main Menu" })
                Require(!_presentations[0]!.JoinedLobby.FindChildren("*", "Button", true, false).Cast<Button>().Single(button => button.Text == label).Disabled, "Personal Settings and cleanup remain available during lobby migration freeze.");
            migration.AuthorityAvailable = null;
            migration.Advance(0);
            Require(_drivers[0]!.BeginLeave(), "Host drain begins.");
            _stage = 2;
        }
        else if (_stage == 2 && _drivers[0]!.LeaveComplete)
        {
            _gateways[0].Stop();
            _retiredAt[0] ??= TimeProvider.System.GetTimestamp();
            _drivers[0] = null;
            _stage = 3;
        }
        else if (_stage == 3 && _drivers[1]!.State?.AuthorityEpoch == 2 && (_players == 2 || (_drivers[2]!.State?.AuthorityEpoch == 2 && !_drivers[2]!.Reconnecting)))
        {
            Require(_drivers[1]!.State!.Players.All(player => player.Ready), "Migration preserves survivor Ready.");
            for (int i = 1; i < _players; i++)
            {
                var scene = _presentations[i]!.JoinedLobby;
                scene.Refresh();
                Require(_drivers[i]!.State!.Map == MatchMap.OldMap, "Migration preserves map selection.");
                Require(scene.Cars.All(pair => _showcases[i][pair.Key] == (pair.Value.Root, pair.Value.Slot)), "Migration retains survivor vehicle nodes and slots.");
                foreach (string label in new[] { "Start", "Map Select", "Lobby Settings" })
                    Require(scene.FindChildren("*", "Button", true, false).Cast<Button>().Single(button => button.Text == label).Visible == (i == 1), "Authoritative migration transfers host controls.");
            }
            Require(_drivers[1]!.Authority!.Configuration == _configuration, "Lobby migration retains the original host's tuning.");
            Require(_drivers[1]!.State!.Players.All(player => player.Id != 1), "Lobby migration removes the former host without a reservation.");
            CreateDriver(0, Connect(0, 1), false, epoch: 2);
            _stage = 4;
        }
        else if (_stage == 4 && _drivers[0]!.State?.AuthorityEpoch == 2 && !_drivers[0]!.Reconnecting)
        {
            Require(_drivers[0]!.Authority is null && _drivers[0]!.LocalPlayerId > (ulong)_players, "Former lobby host returns through fresh admission with a new PlayerId.");
            _presentations[0]!.JoinedLobby.Refresh();
            Require(!_presentations[0]!.JoinedLobby.FindChildren("*", "Button", true, false).Cast<Button>().Any(button => button.Text is "Start" or "Map Select" or "Lobby Settings" && button.Visible), "Former host has no host-only controls after fresh admission.");
            foreach (var driver in _drivers.Take(_players))
            {
                driver!.Request(LobbyCommand.Ready, true);
            }

            _stage = 5;
        }
        else if (_stage == 5 && _drivers[1]!.State!.CanStart)
        {
            Require(_drivers[1]!.SelectMap(MatchMap.NewMap), "Replacement controls the authoritative map.");
            Require(_drivers[1]!.Request(LobbyCommand.Start), "Replacement can start normally.");
            _stage = 6;
        }
        else if (_stage == 6 && _drivers.Take(_players).All(driver => driver!.State!.Phase == SessionPhase.Arena))
        {
            for (int i = 0; i < _players; i++)
            {
                var arena = new NetworkVehicleArena { ApplicationEntry = true, PreparedMap = _preparedMap };
                arena.Initialize(_gateways[i], i == 1 ? _drivers[i]!.State!.Match : 0, _drivers[i]!.ServerPeer, _drivers[i], i == 1 ? _drivers[i]!.Authority!.Configuration.Configuration : new() { Vehicle = new() { Acceleration = 80 } });
                _views[i].AddChild(arena);
                _arenas[i] = arena;
            }

            _stage = 61;
        }
        else if (_stage == 61 && _arenas.Take(_players).All(arena => arena!.Driver.EntryReady &&
            arena.Driver.Match?.Phase == (_players == 2 ? Core.Matches.MatchPhase.Countdown : Core.Matches.MatchPhase.Active)))
        {
            int nextHost = _players == 2 ? 0 : 2;
            _categoryHistory = CategoryBalanceRecoveryFixture.Seed(_arenas[1]!);
            GD.Print("Category pickup history before migration: " + _categoryHistory);
            _arenas[1]!.Driver.Host!.Items.Grant(_arenas[1]!.Driver.Host!.World, _drivers[nextHost]!.LocalPlayerId, HeldItem.Wrench);
            _arenas[1]!.Driver.Host!.Items.Grant(_arenas[1]!.Driver.Host!.World, _drivers[nextHost]!.LocalPlayerId, HeldItem.Oil);
            Require(_arenas[1]!.Driver.Host!.Items.Switch(_arenas[1]!.Driver.Host!.World, _drivers[nextHost]!.LocalPlayerId, _arenas[1]!.Driver.Host!.World.GetVehicle(_drivers[nextHost]!.LocalPlayerId).LifeId, 1), "Select second held slot before migration.");
            Require(_arenas[1]!.Driver.TryConfigure(new Dictionary<string, double> { ["environment.preset"] = (int)Core.Development.EnvironmentPreset.NeonSunset, ["vehicle.acceleration"] = 9, ["spawns.seed"] = 42, ["match.countdown_ticks"] = 600 }, out _), "First replacement configures normal gameplay owners.");
            _oil = OilRecoveryFixture.Seed(_arenas[1]!);
            _nitroOwner = _drivers[1]!.LocalPlayerId;
            Require(_arenas[1]!.Driver.Host!.Items.Grant(_arenas[1]!.Driver.Host!.World, _nitroOwner, HeldItem.Nitro), "Nitro uses the same authority before host loss.");
            _configuration = _arenas[1]!.Driver.Configuration;
            _randomState = _arenas[1]!.Driver.Host!.Spawns!.RandomState;
            if (_players == 3)
            {
                var arena = _arenas[1]!;
                var world = arena.Driver.Host!.World;
                var boundary = world.State;
                var vehicles = boundary.Vehicles.Select(vehicle =>
                {
                    var pose = new Core.Vehicles.VehiclePhysicsState(vehicle.ObservedPhysics.Position + new System.Numerics.Vector3(0, 500, 0),
                        System.Numerics.Quaternion.Identity, new System.Numerics.Vector3(0, 0, -10), System.Numerics.Vector3.Zero);
                    arena.Bodies[vehicle.VehicleId].Apply(pose);
                    return new Core.Vehicles.VehicleSnapshot(vehicle.VehicleId, vehicle.LifeId,
                        new Core.Vehicles.VehicleState(boundary.Tick, pose, false, false, 0, 0), vehicle.Damage, pose);
                }).ToArray();
                // Seed earned statistics and an already armed flight; native falling continues during checkpoint publication.
                var match = new Core.Matches.MatchState(boundary.Tick, boundary.Match!.Revision + 1, boundary.Match.KillTarget,
                    Core.Matches.MatchPhase.Active, null, null, boundary.Match.Players.Select(row => row with
                    {
                        Kills = 1, Deaths = 1, ProcessedLife = 1, CircusScore = 125.25 + row.Player, KillStreak = 1,
                        Stunts = new Core.Matches.StuntState { Life = world.GetVehicle(row.Player).LifeId, Tick = boundary.Tick,
                            Airtime = new(1, 0.5), JumpOrigin = world.GetVehicle(row.Player).ObservedPhysics.Position },
                    }));
                world.Restore(new Core.Simulation.SimulationState(boundary.Tick, boundary.LastInput, vehicles, match));
                NitroRecoveryFixture.Seed(arena, _nitroOwner);
                _circusBoundaries[match.Revision] = match;
                arena.Driver.MatchReceived += state => _circusBoundaries[state.Revision] = state;
            }
            SalvoRecoveryFixture.Seed(_arenas[1]!);
            _arenas[1]!.Driver.ItemsReceived += state => _salvoBoundaries[state.World.Tick] = state.Missiles;
            _boundary = _frames;
            _stage = 7;
        }
        else if (_stage == 7 && _frames - _boundary > 150)
        {
            int survivor = _players == 2 ? 0 : 2;
            _retainedBody = _arenas[survivor]!.Bodies[_drivers[survivor]!.LocalPlayerId];
            _arenas[survivor]!.Driver.Resynchronized += _ =>
            {
                SalvoRecoveryFixture.Verify(_arenas[survivor]!.Driver.ItemState!, _salvoBoundaries);
                Require(_arenas[survivor]!.Driver.Inputs!.Pending.Count == 0, "No old pending input.");
                Require(_arenas[survivor]!.Driver.History!.Snapshots.Count == 1, "Interpolation reseeded at one boundary.");
                if (_players == 3)
                {
                    var restored = _arenas[survivor]!.Driver.Match!;
                    Require(_circusBoundaries.TryGetValue(restored.Revision, out var original) && restored.Players.SequenceEqual(original.Players), "Selected checkpoint restores exact Circus score, K/D, streak and pending flight state.");
                    Require(restored.Mode == Core.Matches.MatchMode.Circus && restored.Players.All(row => row.CircusScore > 0 && row.Stunts is not null), "Migration retains the configured Circus mode and unbanked stunts.");
                    Require(_arenas[survivor]!.Driver.LocalState is not null &&
                        _arenas[survivor]!.Driver.ItemState!.Slots.Single(s => s.Vehicle == _nitroOwner).NitroCharge == 37.5,
                        "Partial Nitro charge survives selected checkpoint installation.");
                    Require(restored.Awards.Count == 0 && restored.Changes.Count == 0, "Migration does not replay prior Circus awards.");
                }
            };
            _retiredAt[1] = TimeProvider.System.GetTimestamp();
            _matchPhase = _arenas[1]!.Driver.Match!.Phase;
            _countdownAtTick = _arenas[1]!.Driver.Match!.CountdownAtTick;
            _gateways[1].Stop();
            _drivers[1] = null;
            _arenas[1]!.QueueFree();
            _arenas[1] = null;
            _stage = 8;
        }
        else if (_stage == 8 && _drivers[_players == 2 ? 0 : 2]!.State?.AuthorityEpoch == 3 && _arenas[_players == 2 ? 0 : 2]!.Driver.IsActive &&
            _drivers.Take(_players).Where((_, index) => index != 1).All(driver => driver!.State?.AuthorityEpoch == 3))
        {
            int survivor = _players == 2 ? 0 : 2;
            var arena = _arenas[survivor]!;
            ulong successor = _drivers[survivor]!.LocalPlayerId;
            Require(_drivers.Take(_players).Where((_, index) => index != 1).All(driver => driver!.State!.CurrentHostId == successor), "Second election converges on the lowest eligible stable ID.");
            Require(_arenas[0]!.Bodies.Count == _players && arena.Bodies.Count == _players && arena.Bodies[_drivers[survivor]!.LocalPlayerId] == _retainedBody, "No duplicate or replaced surviving vehicles.");
            Require(arena.Driver.LocalItem?.Item == HeldItem.Wrench && arena.Driver.ItemState!.Spawns.Count == 27 && arena.Driver.Match!.Players.Count == _players, "Complete gameplay continuation with twenty-seven map pickups.");
            Require(arena.Driver.LocalItem is { SecondItem: HeldItem.Oil, ActiveSlot: 1, SelectionRevision: 1 }, "Both held slots and selected second slot restore through host migration.");
            Require(arena.Driver.ItemState!.Slots.Single(slot => slot.Vehicle == _nitroOwner).Item == HeldItem.Nitro, "Nitro is retained on the disconnected former host across migration.");
            Require(arena.Driver.ItemState!.Patches.Count == 1 && arena.Driver.ItemState.Patches.Single() == _oil && arena.Driver.Host!.Items.Patches.Single() == _oil, "Persistent Oil survives host replacement exactly once, including departed owner.");
            if (_players == 3)
            {
                Require(!arena.Driver.Host!.World.GetVehicle(_nitroOwner).Movement.Nitro.Active && arena.Driver.Host.Items.Slots.Single(s => s.Vehicle == _nitroOwner).NitroCharge == 37.5, "Nitro charge remains intact and inactive without held input after migration.");
                GD.Print("Nitro migration verified: partial charge retained without unwanted activation; Circus totals restore without historical awards.");
            }
            OvalGameplayAssertions.Verify(arena);
            Require(_arenas[0]!.Driver.Configuration == _configuration && arena.Driver.Configuration == _configuration, "Successive hosts retain configuration revision and ignore successor-local presets.");
            Require(CategoryBalanceRecoveryFixture.Signature(arena.Driver.Host!.Spawns!.Balances) == _categoryHistory, "Host migration retains exact per-player category history.");
            GD.Print("Category history verified after authority migration: " + _categoryHistory);
            Require(arena.Driver.Host!.Spawns!.RandomState == _randomState, "Migrated RNG continuation.");
            Require(arena.Driver.Match!.Phase == _matchPhase && arena.Driver.Match.CountdownAtTick == _countdownAtTick, "Migration preserves the current phase and absolute countdown deadline.");
            Require(!arena.Driver.ForceDeveloperStart(), "Replacement cannot restart an existing Countdown or Active phase.");
            Require(arena.Driver.TryConfigure(new Dictionary<string, double> { ["vehicle.acceleration"] = 11 }, out _), "Second replacement can edit live tuning.");
            if (_players == 3)
            {
                Require(!_arenas[0]!.Driver.TryConfigure(new Dictionary<string, double> { ["vehicle.acceleration"] = 13 }, out _) && !_arenas[0]!.Driver.GiveDeveloperItem(HeldItem.Missile) && !_arenas[0]!.Driver.ForceDeveloperStart(), "Ordinary survivor cannot invoke developer authority.");
            }

            _stage = 9;
        }
        else if (_stage == 9)
        {
            int host = _players == 2 ? 0 : 2;
            if (!_arenas[host]!.Driver.AllowsParticipation)
            {
                Require(!_arenas[host]!.Driver.RequestItemUse(), "Replacement cannot use its retained item before authoritative Active.");
                return;
            }

            if (_arenas[host]!.Driver.LocalItem?.Item == HeldItem.Wrench)
            {
                if (_arenas[host]!.Driver.LocalItem!.ActiveSlot == 1) { Require(_arenas[host]!.Driver.RequestItemSwitch(), "Replacement switches normally after recovery."); return; }
                Require(_arenas[host]!.Driver.RequestItemUse(), "Normal use clears the replacement slot.");
                return;
            }

            Require(_arenas[host]!.Driver.GiveDeveloperItem(HeldItem.Missile), "Give Item targets the second replacement's stable player ID.");
            Cleanup();
            _finished = true;
            _boundary = _frames;
        }
    }

    private void Cleanup()
    {
        foreach (var arena in _arenas)
        {
            arena?.QueueFree();
        }

        foreach (var gateway in _gateways)
        {
            gateway?.Dispose();
        }
    }
}
