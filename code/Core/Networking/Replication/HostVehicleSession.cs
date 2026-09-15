using System.Numerics;
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
    private ulong _nextVehicle = 1;

    /// <summary>Starts one host vehicle in a caller-identified session.</summary>
    /// <param name="sessionId">Nonzero identity supplied by the outer session lifetime.</param>
    /// <param name="itemConfiguration">Optional authoritative item tuning.</param>
    /// <param name="respawnConfiguration">Optional host lifecycle tuning.</param>
    public HostVehicleSession(ulong sessionId, ItemConfiguration? itemConfiguration = null, RespawnConfiguration? respawnConfiguration = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(sessionId);
        SessionId = sessionId;
        World = new Simulation.Simulation(new SimulationConfiguration(TickRate), respawnConfiguration ?? new());
        Items = new ItemAuthority(itemConfiguration);
        World.AddVehicle(1, new(), new(), Spawn(0));
    }

    /// <summary>Caller-provided session generation.</summary>
    public ulong SessionId { get; }
    /// <summary>Sole host gameplay owner, using the existing aggregate simulation path.</summary>
    public Simulation.Simulation World { get; }

    /// <summary>Match-scoped item gameplay authority.</summary>
    public ItemAuthority Items { get; }

    /// <summary>Registered arena spawns, absent until a layout is attached.</summary>
    public ItemSpawnAuthority? Spawns { get; private set; }

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

        Spawns = new ItemSpawnAuthority(arena, Items, configuration, selector);
    }

    /// <summary>Resolves a use request using actual sender ownership.</summary>
    /// <param name="peer">Transport sender; zero is the local host.</param>
    /// <param name="session">Arena generation.</param>
    /// <param name="life">Vehicle life.</param>
    /// <param name="token">Issued slot token.</param>
    /// <returns>Whether accepted for the next fixed step.</returns>
    public bool UseItem(ulong peer, ulong session, ulong life, ulong token)
    {
        ulong vehicle = peer == 0 ? 1 : _peers.TryGetValue(peer, out var entry) ? entry.Vehicle : 0;
        return session == SessionId && vehicle != 0 && Items.RequestUse(World, vehicle, life, token);
    }

    /// <summary>Assigns a unique gameplay identity only after the transport reports a connected peer.</summary>
    /// <param name="peer">Transport identity scoped to the caller's live gateway.</param>
    /// <returns>Assigned vehicle identity, or zero when the eight-player session is full.</returns>
    public ulong Join(ulong peer)
    {
        if (_peers.TryGetValue(peer, out var existing))
        {
            return existing.Vehicle;
        }

        if (_peers.Count == 7)
        {
            return 0;
        }

        ulong id = checked(++_nextVehicle);
        int spawnSlot = Enumerable.Range(1, 7).First(slot => _peers.Values.All(entry => entry.SpawnSlot != slot));
        World.JoinVehicle(id, new(), new(), Spawn(spawnSlot));
        _peers.Add(peer, (id, new HostInputBuffer(), spawnSlot));
        return id;
    }

    /// <summary>Uses a lobby-owned stable identity for an already admitted connected player.</summary>
    /// <param name="peer">Actual transport peer.</param>
    /// <param name="playerId">Stable session player identity.</param>
    public void JoinPlayer(ulong peer, ulong playerId)
    {
        if (peer == 0 || playerId <= 1 || _peers.ContainsKey(peer) || World.State.Vehicles.Any(vehicle => vehicle.VehicleId == playerId) || _peers.Count == 7)
        {
            throw new ArgumentException("Invalid lobby vehicle assignment.");
        }

        int slot = Enumerable.Range(1, 7).First(candidate => _peers.Values.All(entry => entry.SpawnSlot != candidate));
        World.JoinVehicle(playerId, new(), new(), Spawn(slot));
        _peers.Add(peer, (playerId, new HostInputBuffer(), slot));
        _nextVehicle = Math.Max(_nextVehicle, playerId);
    }

    /// <summary>Releases gameplay ownership; stale input can no longer target the departed vehicle.</summary>
    /// <param name="peer">Departed transport identity.</param>
    public void Leave(ulong peer)
    {
        if (_peers.Remove(peer, out var entry))
        {
            World.LeaveVehicle(entry.Vehicle);
        }
    }

    /// <summary>Routes input solely by the established sender mapping, never a client-claimed player ID.</summary>
    /// <param name="peer">Actual transport sender.</param>
    /// <param name="session">Negotiated session generation.</param>
    /// <param name="inputs">Bounded redundant command window.</param>
    /// <returns>Whether the input passed ownership and ordering validation.</returns>
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
        InputFrame hostInput = new SequencedInput(0, local).AtTick(tick);
        inputs.Add(1, hostInput);
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
    public WorldSnapshot Snapshot() => new(SessionId, World.State.Tick, World.State.Vehicles.Select(state => new ReplicatedVehicle(state, _peers.Values.FirstOrDefault(entry => entry.Vehicle == state.VehicleId).Inputs?.LastAcknowledged ?? 0)));

    private static VehiclePhysicsState Spawn(int slot) => Arenas.PrototypeArena.Configuration.Spawn(slot);
}
