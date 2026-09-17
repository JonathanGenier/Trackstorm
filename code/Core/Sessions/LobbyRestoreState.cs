namespace Trackstorm.Core.Sessions;

/// <summary>Detached authority continuation data; subjects are authenticated adapter identities, never credentials.</summary>
public sealed class LobbyRestoreState
{
    /// <summary>Validates complete roster continuation before replacement authority is constructed.</summary>
    /// <param name="state">Published lobby boundary.</param>
    /// <param name="tick">Host session clock.</param>
    /// <param name="nextId">Highest issued identity, including departed players.</param>
    /// <param name="subjects">Authenticated subject for every retained player.</param>
    /// <param name="configuration">Current authoritative session tuning, including lobby edits and arena continuation.</param>
    public LobbyRestoreState(LobbySnapshot state, ulong tick, ulong nextId, IReadOnlyDictionary<ulong, string> subjects, Development.GameplayConfigurationState? configuration = null)
    {
        if (nextId < state.Players.Max(player => player.Id) || nextId == ulong.MaxValue ||
            subjects.Count != state.Players.Count || subjects.Values.Distinct(StringComparer.Ordinal).Count() != subjects.Count ||
            state.Players.Any(player => !subjects.TryGetValue(player.Id, out string? subject) || string.IsNullOrWhiteSpace(subject) || subject.Length > 256))
        {
            throw new ArgumentException("Invalid lobby continuation state.");
        }

        State = state;
        Configuration = configuration ?? new(0, new());
        Tick = tick;
        NextId = nextId;
        Subjects = new System.Collections.ObjectModel.ReadOnlyDictionary<ulong, string>(new Dictionary<ulong, string>(subjects));
    }

    /// <summary>Immutable published roster.</summary>
    public LobbySnapshot State { get; }
    /// <summary>Authoritative tuning retained across lobby migration and successive arenas.</summary>
    public Development.GameplayConfigurationState Configuration { get; }
    /// <summary>Authoritative session clock, distinct from match time.</summary>
    public ulong Tick { get; }
    /// <summary>Identity allocation high-water mark.</summary>
    public ulong NextId { get; }
    /// <summary>Provider-neutral authenticated rebind mapping.</summary>
    public IReadOnlyDictionary<ulong, string> Subjects { get; }
}
