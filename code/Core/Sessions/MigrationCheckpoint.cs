using Trackstorm.Core.Networking.Replication;

namespace Trackstorm.Core.Sessions;

/// <summary>One recoverable session boundary, composing existing resync contracts with host continuation.</summary>
public sealed class MigrationCheckpoint
{
    /// <summary>Validates phase, roster and complete authority restoration before retention.</summary>
    /// <param name="sequence">Monotonic checkpoint identifier within the epoch.</param>
    /// <param name="lobby">Session authority continuation.</param>
    /// <param name="arena">Complete match state, absent in lobby.</param>
    /// <param name="host">Authority-only gameplay supplement, absent in lobby.</param>
    public MigrationCheckpoint(ulong sequence, LobbyRestoreState lobby, ResumeCheckpoint? arena, HostRestoreState? host)
    {
        if (sequence == 0 || (lobby.State.Phase == SessionPhase.Arena) != (arena is not null) || (arena is null) != (host is null))
        {
            throw new ArgumentException("Invalid migration checkpoint phase.");
        }

        if (arena is not null)
        {
            if (arena.Configuration != lobby.Configuration)
            {
                throw new ArgumentException("Checkpoint configuration boundaries disagree.");
            }

            if (arena.Items.World.Session != lobby.State.Match || !lobby.State.Players.Select(player => player.Id).ToHashSet().SetEquals(arena.Items.World.Vehicles.Select(vehicle => vehicle.State.VehicleId)))
            {
                throw new ArgumentException("Checkpoint roster or match mismatch.");
            }

            // Prove the whole continuation is restorable before publishing or accepting it.
            _ = HostVehicleSession.Restore(arena, host!, lobby.State.CurrentHostId);
        }

        Sequence = sequence;
        Lobby = lobby;
        Arena = arena;
        Host = host;
    }

    /// <summary>Epoch-scoped checkpoint identity.</summary>
    public ulong Sequence { get; }
    /// <summary>Stable identities, session time and reservations.</summary>
    public LobbyRestoreState Lobby { get; }
    /// <summary>Existing complete gameplay resync state.</summary>
    public ResumeCheckpoint? Arena { get; }
    /// <summary>Continuation supplement required only by replacement authority.</summary>
    public HostRestoreState? Host { get; }
}
