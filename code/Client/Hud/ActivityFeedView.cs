using Trackstorm.Core.Events;

namespace Trackstorm.Client.Hud;

/// <summary>Bounded, timed projection of the existing journal; never detects gameplay outcomes.</summary>
internal sealed class ActivityFeedView : IDisposable
{
    /// <summary>Maximum simultaneous visible rows.</summary>
    internal const int Capacity = 5;
    /// <summary>Independent row lifetime in presentation seconds.</summary>
    internal const double Duration = 5;
    private readonly List<ActivityFeedEntry> _entries = new(Capacity);
    private EventStream? _source;
    private bool _gameplay;
    private double _elapsed;

    /// <summary>Live rows in oldest-to-newest order.</summary>
    internal IReadOnlyList<ActivityFeedEntry> Entries => _entries;

    /// <summary>Releases the journal subscription without retaining historical feed rows.</summary>
    public void Dispose()
    {
        if (_source is not null)
        {
            _source.Appended -= OnAppended;
        }

        _source = null;
        _entries.Clear();
    }

    /// <summary>Advances presentation time and consumes new authoritative identities only.</summary>
    /// <param name="source">Current shared journal.</param>
    /// <param name="gameplay">Whether the arena is present.</param>
    /// <param name="delta">Elapsed presentation seconds.</param>
    internal void Update(EventStream? source, bool gameplay, double delta)
    {
        if (!double.IsFinite(delta) || delta < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        _elapsed += delta;
        _gameplay = gameplay;
        _entries.RemoveAll(entry => entry.ExpiresAt <= _elapsed);
        if (!ReferenceEquals(source, _source))
        {
            Dispose();
            _source = source;
            if (_source is not null)
            {
                _source.Appended += OnAppended;
            }
        }

        if (!gameplay)
        {
            _entries.Clear();
        }
    }

    private static ActivityFeedEntry? Project(RuntimeEvent entry, double expiresAt)
    {
        string actor = string.IsNullOrWhiteSpace(entry.ActorName) ? "Player" : entry.ActorName;
        string target = string.IsNullOrWhiteSpace(entry.TargetName) ? "Player" : entry.TargetName;
        return (entry.Category, entry.Kind) switch
        {
            (EventCategory.Session, "Joined") => new($"{actor} joined the game", ActivityFeedTone.Arrival, expiresAt),
            (EventCategory.Session, "Left") => new($"{actor} left the game", ActivityFeedTone.Departure, expiresAt),
            (EventCategory.Network, "Disconnected") => new($"{actor} disconnected", ActivityFeedTone.Departure, expiresAt),
            (EventCategory.Network, "Reconnected") => new($"{actor} reconnected", ActivityFeedTone.Arrival, expiresAt),
            (EventCategory.Session, "Match reservation ended") => new($"{actor} left the game", ActivityFeedTone.Departure, expiresAt),
            (EventCategory.Lifecycle, "Kill") => new($"{actor} killed {target}{(entry.Cause == "Missile" ? " with Missile" : string.Empty)}", ActivityFeedTone.Death, expiresAt),
            (EventCategory.Lifecycle, "Dead") when entry.Context != "Scored kill" => new($"{target} died", ActivityFeedTone.Death, expiresAt),
            _ => null,
        };
    }

    private void OnAppended(RuntimeEvent entry)
    {
        if (!_gameplay || entry.Local)
        {
            return;
        }

        ActivityFeedEntry? row = Project(entry, _elapsed + Duration);
        if (row is null)
        {
            return;
        }

        if (_entries.Count == Capacity)
        {
            _entries.RemoveAt(0);
        }

        _entries.Add(row);
    }
}
