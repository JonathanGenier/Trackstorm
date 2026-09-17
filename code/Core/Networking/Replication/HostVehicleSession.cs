using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Input;
using Trackstorm.Core.Items;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Networking.Replication;

/// <summary>Owns gameplay peer assignment, validated inputs and the authoritative 60 Hz vehicle world.</summary>
public sealed class HostVehicleSession
{
    /// <summary>Shared fixed physics frequency.</summary>
    public const int TickRate = 60;
    /// <summary>Snapshots at 20 Hz, below the simulation frequency.</summary>
    public const int SnapshotInterval = 3;
    private readonly Dictionary<ulong, (ulong Vehicle, HostInputBuffer Inputs, int SpawnSlot)> _peers = new();
    private readonly Dictionary<ulong, (HostInputBuffer Inputs, int SpawnSlot)> _disconnected = new();
    private ulong _nextVehicle = 1;

    /// <summary>Starts one host vehicle in a caller-identified session.</summary>
    /// <param name="sessionId">Nonzero identity supplied by the outer session lifetime.</param>
    /// <param name="itemConfiguration">Optional authoritative item tuning.</param>
    /// <param name="respawnConfiguration">Optional host lifecycle tuning.</param>
    /// <param name="matchConfiguration">Optional match tuning; scoring always uses the shared simulation.</param>
    /// <param name="damageConfiguration">Vehicle health tuning for this arena.</param>
    /// <param name="configuration">Validated effective gameplay tuning.</param>
    /// <param name="hostPlayerId">Stable identity of the current authority.</param>
    /// <param name="configurationRevision">Session configuration revision retained across arena transitions.</param>
    public HostVehicleSession(ulong sessionId, ItemConfiguration? itemConfiguration = null, RespawnConfiguration? respawnConfiguration = null, Matches.MatchConfiguration? matchConfiguration = null, DamageConfiguration? damageConfiguration = null, GameplayConfiguration? configuration = null, ulong hostPlayerId = 1, ulong configurationRevision = 0)
    {
        ArgumentOutOfRangeException.ThrowIfZero(sessionId);
        SessionId = sessionId;
        ArgumentOutOfRangeException.ThrowIfZero(hostPlayerId);
        HostPlayerId = hostPlayerId;
        var effective = configuration ?? new GameplayConfiguration { Damage = damageConfiguration ?? new(), Items = itemConfiguration ?? new(), Respawn = respawnConfiguration ?? new(), Match = matchConfiguration ?? new() };
        Configuration = new GameplayConfigurationState(configurationRevision, effective);
        World = new Simulation.Simulation(new SimulationConfiguration(TickRate), effective.Respawn, match: effective.Match);
        Items = new ItemAuthority(effective.Items);
        World.AddVehicle(hostPlayerId, effective.Vehicle, effective.Damage, Spawn(0));
        _nextVehicle = hostPlayerId;
    }

    /// <summary>Caller-provided session generation.</summary>
    public ulong SessionId { get; }
    /// <summary>Stable local authority identity; independent of roster order and transport handles.</summary>
    public ulong HostPlayerId { get; }
    /// <summary>Sole host gameplay owner, using the existing aggregate simulation path.</summary>
    public Simulation.Simulation World { get; }

    /// <summary>Match-scoped item gameplay authority.</summary>
    public ItemAuthority Items { get; }

    /// <summary>Registered arena spawns, absent until a layout is attached.</summary>
    public ItemSpawnAuthority? Spawns { get; private set; }

    /// <summary>Effective validated gameplay configuration and monotonic revision.</summary>
    public GameplayConfigurationState Configuration { get; private set; }

    /// <summary>Constructs a complete replacement off to the side; no live authority is partially mutated.</summary>
    /// <param name="checkpoint">Existing validated complete resync state.</param>
    /// <param name="continuation">Authority-only continuation.</param>
    /// <param name="host">Elected player already present in the world.</param>
    /// <returns>Restored authority with neutral remote inputs awaiting authenticated rebind.</returns>
    public static HostVehicleSession Restore(ResumeCheckpoint checkpoint, HostRestoreState continuation, ulong host)
    {
        var world = checkpoint.Items.World;
        if (!world.Vehicles.Any(vehicle => vehicle.State.VehicleId == host) || continuation.NextVehicle < world.Vehicles.Max(vehicle => vehicle.State.VehicleId))
        {
            throw new ArgumentException("Invalid vehicle migration roster.");
        }

        var tuning = checkpoint.Configuration.Configuration;
        if (continuation.Damage != tuning.Damage || continuation.Items != tuning.Items || continuation.Respawn != tuning.Respawn ||
            continuation.Match != tuning.Match || (continuation.Spawns is not null && continuation.Spawns != tuning.Spawns))
        {
            throw new ArgumentException("Migration tuning does not match its resume boundary.");
        }

        var result = new HostVehicleSession(world.Session, configuration: tuning, hostPlayerId: host)
        {
            Configuration = checkpoint.Configuration,
        };
        int slot = 1;
        foreach (var vehicle in world.Vehicles.Where(vehicle => vehicle.State.VehicleId != host))
        {
            result.World.AddVehicle(vehicle.State.VehicleId, tuning.Vehicle, tuning.Damage, vehicle.State.ObservedPhysics);
            result._disconnected.Add(vehicle.State.VehicleId, (new HostInputBuffer(vehicle.AcknowledgedInput), slot++));
        }

        if (continuation.Spawns is not null)
        {
            result.RegisterSpawns(result.World.Arena, continuation.Spawns);
            result.Spawns!.Restore(checkpoint.Items, continuation.SpawnRevision, continuation.RandomState);
        }
        else if (checkpoint.Items.Spawns.Count != 0)
        {
            throw new ArgumentException("Missing pickup continuation.");
        }

        result.World.Restore(new SimulationState(world.Tick, new InputFrame(world.Tick, 0, 0, 0, 0, 0, 0), world.Vehicles.Select(vehicle => vehicle.State), checkpoint.Match));
        result.Items.Restore(checkpoint.Items, continuation.ItemRevision, continuation.Token);
        result._nextVehicle = continuation.NextVehicle;
        return result;
    }

    /// <summary>Captures authority-only continuation at the same boundary as Snapshot.</summary>
    /// <returns>Portable immutable tuning and sequence state.</returns>
    public HostRestoreState CaptureAuthority() => new()
    {
        Damage = Configuration.Configuration.Damage,
        Items = Items.Configuration,
        Respawn = World.Respawn!,
        Match = World.MatchRules!,
        Spawns = Spawns?.Configuration,
        Token = Items.TokenHighWater,
        ItemRevision = Items.Revision,
        SpawnRevision = Spawns?.Revision ?? 0,
        RandomState = Spawns?.RandomState ?? 0,
        NextVehicle = _nextVehicle,
    };

    /// <summary>Only trusted local host requests may commit an atomic gameplay tuning transaction.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    /// <param name="peer">Actual sender; zero denotes the trusted local host.</param>
    /// <param name="edits">Stable gameplay keys and requested values.</param>
    /// <param name="error">Safe validation feedback.</param>
    public bool TryConfigure(ulong peer, IReadOnlyDictionary<string, double> edits, out string error)
    {
        error = "Only the authoritative host may change gameplay tuning.";
        if (peer != 0 || !GameplayOptions.TryApply(Configuration.Configuration, edits, out var candidate, out error))
        {
            return false;
        }

        if (candidate == Configuration.Configuration)
        {
            return true;
        }

        try
        {
            var next = new GameplayConfigurationState(checked(Configuration.Revision + 1), candidate);
            World.ApplyConfiguration(candidate);
            Items.ApplyConfiguration(candidate.Items);
            Spawns?.ApplyConfiguration(candidate.Spawns);
            Configuration = next;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>Host-only non-persistent grant through the single-slot inventory authority.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    /// <param name="peer">Actual sender; zero denotes the trusted local host.</param>
    /// <param name="item">Implemented item to grant.</param>
    public bool GiveItem(ulong peer, HeldItem item) => peer == 0 && Items.Grant(World, HostPlayerId, item);

    /// <summary>Host-only non-persistent solo override; uses the normal authoritative countdown.</summary>
    /// <returns>Whether the operation was accepted.</returns>
    /// <param name="peer">Actual sender; zero denotes the trusted local host.</param>
    public bool ForceStart(ulong peer) => peer == 0 && World.ForceStart();

    /// <summary>Registers actual scene markers once before simulation.</summary>
    /// <param name="arena">Validated scene contract.</param>
    /// <param name="configuration">Optional host tuning.</param>
    /// <param name="selector">Optional deterministic selector.</param>
    public void RegisterSpawns(Arenas.ArenaConfiguration arena, ItemSpawnConfiguration? configuration = null, Func<HeldItem>? selector = null)
    {
        if (World.State.Tick != 0 || Spawns is not null)
        {
            throw new InvalidOperationException("Spawn registration must precede simulation and occur only once.");
        }

        if (configuration is not null)
        {
            Configuration = new GameplayConfigurationState(Configuration.Revision, Configuration.Configuration with { Spawns = configuration });
        }

        Spawns = new ItemSpawnAuthority(arena, Items, Configuration.Configuration.Spawns, selector);
    }

    /// <summary>Resolves a use request using actual sender ownership.</summary>
    /// <returns>Whether accepted for the next fixed step.</returns>
    /// <param name="peer">Transport sender; zero is the local host.</param>
    /// <param name="session">Arena generation.</param>
    /// <param name="life">Vehicle life.</param>
    /// <param name="token">Issued slot token.</param>
    public bool UseItem(ulong peer, ulong session, ulong life, ulong token)
    {
        ulong vehicle = peer == 0 ? HostPlayerId : _peers.TryGetValue(peer, out var entry) ? entry.Vehicle : 0;
        return session == SessionId && vehicle != 0 && Items.RequestUse(World, vehicle, life, token);
    }

    /// <summary>Assigns a unique gameplay identity only after the transport reports a connected peer.</summary>
    /// <returns>Assigned vehicle identity, or zero when the eight-player session is full.</returns>
    /// <param name="peer">Transport identity scoped to the caller's live gateway.</param>
    public ulong Join(ulong peer)
    {
        if (_peers.TryGetValue(peer, out var existing))
        {
            return existing.Vehicle;
        }

        if (_peers.Count + _disconnected.Count == 7 || World.State.Match!.Players.Count >= Matches.MatchState.MaximumPlayers)
        {
            return 0;
        }

        ulong id = checked(++_nextVehicle);
        int spawnSlot = Enumerable.Range(1, 7).First(slot => _peers.Values.All(entry => entry.SpawnSlot != slot) && _disconnected.Values.All(entry => entry.SpawnSlot != slot));
        World.JoinVehicle(id, Configuration.Configuration.Vehicle, Configuration.Configuration.Damage, Spawn(spawnSlot));
        _peers.Add(peer, (id, new HostInputBuffer(), spawnSlot));
        return id;
    }

    /// <summary>Uses a lobby-owned stable identity for an already admitted connected player.</summary>
    /// <param name="peer">Actual transport peer.</param>
    /// <param name="playerId">Stable session player identity.</param>
    public void JoinPlayer(ulong peer, ulong playerId)
    {
        if (peer == 0 || playerId == 0 || playerId == HostPlayerId || _peers.ContainsKey(peer) || World.State.Vehicles.Any(vehicle => vehicle.VehicleId == playerId) || _peers.Count + _disconnected.Count == 7)
        {
            throw new ArgumentException("Invalid lobby vehicle assignment.");
        }

        int slot = Enumerable.Range(1, 7).First(candidate => _peers.Values.All(entry => entry.SpawnSlot != candidate) && _disconnected.Values.All(entry => entry.SpawnSlot != candidate));
        World.JoinVehicle(playerId, Configuration.Configuration.Vehicle, Configuration.Configuration.Damage, Spawn(slot));
        _peers.Add(peer, (playerId, new HostInputBuffer(), slot));
        _nextVehicle = Math.Max(_nextVehicle, playerId);
    }

    /// <summary>Creates a disconnected lobby reservation in a newly started match without granting input ownership.</summary>
    /// <param name="playerId">Retained stable lobby identity.</param>
    public void ReservePlayer(ulong playerId)
    {
        if (playerId == 0 || playerId == HostPlayerId || World.State.Vehicles.Any(vehicle => vehicle.VehicleId == playerId) || _peers.Count + _disconnected.Count == 7)
        {
            throw new ArgumentException("Invalid reserved vehicle identity.");
        }

        int slot = Enumerable.Range(1, 7).First(candidate => _peers.Values.All(entry => entry.SpawnSlot != candidate) && _disconnected.Values.All(entry => entry.SpawnSlot != candidate));
        World.JoinVehicle(playerId, Configuration.Configuration.Vehicle, Configuration.Configuration.Damage, Spawn(slot));
        _disconnected.Add(playerId, (new HostInputBuffer(), slot));
        _nextVehicle = Math.Max(_nextVehicle, playerId);
    }

    /// <summary>Releases gameplay ownership; stale input can no longer target the departed vehicle.</summary>
    /// <param name="peer">Departed transport identity.</param>
    public void Leave(ulong peer)
    {
        if (_peers.Remove(peer, out var entry))
        {
            World.LeaveVehicle(entry.Vehicle);
            Items.RemovePlayer(entry.Vehicle);
        }
    }

    /// <summary>Retires input ownership while keeping the entire vehicle, inventory and match authority alive.</summary>
    /// <param name="peer">Lost transport peer.</param>
    public void Suspend(ulong peer)
    {
        if (_peers.Remove(peer, out var entry))
        {
            _disconnected.Add(entry.Vehicle, (new HostInputBuffer(entry.Inputs.LastAcknowledged), entry.SpawnSlot));
            Items.CancelPending(entry.Vehicle);
        }
    }

    /// <summary>Rebinds an existing suspended vehicle after lobby authorization, without respawning it.</summary>
    /// <returns>Whether the existing vehicle was rebound.</returns>
    /// <param name="peer">New transport peer.</param>
    /// <param name="player">Stable lobby player.</param>
    public bool ResumePlayer(ulong peer, ulong player)
    {
        if (peer == 0 || _peers.ContainsKey(peer) || !_disconnected.Remove(player, out var entry))
        {
            return false;
        }

        _peers.Add(peer, (player, entry.Inputs, entry.SpawnSlot));
        return true;
    }

    /// <summary>Finalizes an expired reservation once; score history remains owned by the match.</summary>
    /// <param name="player">Expired lobby identity.</param>
    public void ExpirePlayer(ulong player)
    {
        if (_disconnected.Remove(player))
        {
            World.LeaveVehicle(player);
            Items.RemovePlayer(player);
        }
    }

    /// <summary>Routes input solely by the established sender mapping, never a client-claimed player ID.</summary>
    /// <returns>Whether the input passed ownership and ordering validation.</returns>
    /// <param name="peer">Actual transport sender.</param>
    /// <param name="session">Negotiated session generation.</param>
    /// <param name="inputs">Bounded redundant command window.</param>
    /// <param name="life">Observed life generation; zero is reserved for trusted in-process callers.</param>
    public bool Receive(ulong peer, ulong session, IReadOnlyList<SequencedInput> inputs, ulong life = 0)
    {
        if (session != SessionId || !_peers.TryGetValue(peer, out var entry))
        {
            return false;
        }

        VehicleSnapshot state = World.GetVehicle(entry.Vehicle);
        return entry.Inputs.Receive(!state.CanInteract || (life != 0 && life != state.LifeId)
            ? inputs.Select(input => new SequencedInput(input.Sequence, default)).ToArray() : inputs);
    }

    /// <summary>Advances all active vehicles once using caller-supplied collision observations.</summary>
    /// <param name="local">Current host input.</param>
    /// <param name="observe">Native collision solver or deterministic test seam.</param>
    /// <param name="collide">Optional host projectile collision seam.</param>
    public void Step(InputFrame local, Func<VehicleSnapshot, VehicleObservation> observe, Func<MissileState, Vector3, float?>? collide = null)
    {
        ulong tick = checked(World.State.Tick + 1);
        var inputs = _peers.Values.ToDictionary(entry => entry.Vehicle, entry => entry.Inputs.Consume(tick));
        foreach (ulong player in _disconnected.Keys)
        {
            inputs.Add(player, new InputFrame(tick, 0, 0, 0, 0, 0, 0));
        }

        InputFrame hostInput = new SequencedInput(0, local).AtTick(tick);
        inputs.Add(HostPlayerId, hostInput);
        var previous = World.State.Vehicles.ToDictionary(state => state.VehicleId);
        Items.Step(World, hostInput, World.State.Vehicles.Select(state => new VehicleStepRequest(state.VehicleId, inputs[state.VehicleId], observe(state))).ToArray(), collide ?? ((_, _) => null));
        foreach (var peer in _peers.Values)
        {
            VehicleSnapshot state = World.GetVehicle(peer.Vehicle);
            if (state.LifeId != previous[peer.Vehicle].LifeId || state.Lifecycle != previous[peer.Vehicle].Lifecycle)
            {
                peer.Inputs.NeutralizePending();
            }
        }

        Spawns?.Advance(World);
    }

    /// <summary>Captures the complete active roster and per-owner input confirmations.</summary>
    /// <returns>Immutable authoritative snapshot.</returns>
    public WorldSnapshot Snapshot() => new(SessionId, World.State.Tick, World.State.Vehicles.Select(state => new ReplicatedVehicle(state, _peers.Values.FirstOrDefault(entry => entry.Vehicle == state.VehicleId).Inputs?.LastAcknowledged ?? _disconnected.GetValueOrDefault(state.VehicleId).Inputs?.LastAcknowledged ?? 0)), Configuration.Revision);

    private static VehiclePhysicsState Spawn(int slot) => Arenas.PrototypeArena.Configuration.Spawn(slot);
}
