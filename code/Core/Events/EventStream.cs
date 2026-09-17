namespace Trackstorm.Core.Events;

/// <summary>Single-threaded bounded event journal with independent authoritative and local order.</summary>
public sealed class EventStream
{
    private readonly Queue<RuntimeEvent> _entries = new();
    private readonly int _capacity;
    private ulong _localSequence;
    private ulong _milliseconds;

    /// <summary>Creates a bounded journal; time and display names are supplied by its runtime owner.</summary>
    /// <param name="capacity">Maximum retained entries.</param>
    public EventStream(int capacity = 1024)
    {
        if (capacity is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    /// <summary>Notifies live projections after an outcome passes the journal's ordering and deduplication.</summary>
    public event Action<RuntimeEvent>? Appended;

    /// <summary>Resolves safe display names at publication time.</summary>
    public Func<ulong, string>? PlayerName { get; set; }
    /// <summary>High watermark retained independently of bounded history.</summary>
    public ulong LastSequence { get; private set; }
    /// <summary>Presentation invalidation count.</summary>
    public ulong Revision { get; private set; }
    /// <summary>Current caller-supplied elapsed clock.</summary>
    public ulong Milliseconds => _milliseconds;
    /// <summary>Detached bounded history in observed stream order.</summary>
    public IReadOnlyList<RuntimeEvent> Entries => _entries.ToArray();

    /// <summary>Advances the caller-owned monotonic elapsed clock.</summary>
    /// <param name="milliseconds">Elapsed monotonic milliseconds.</param>
    public void AdvanceTime(ulong milliseconds) => _milliseconds = Math.Max(_milliseconds, milliseconds);

    /// <summary>Starts an empty journal at an explicitly committed authority epoch, without replaying old outcomes.</summary>
    /// <param name="milliseconds">Restored session clock.</param>
    public void ResetAuthority(ulong milliseconds)
    {
        _entries.Clear();
        LastSequence = 0;
        _localSequence = 0;
        _milliseconds = milliseconds;
        Revision++;
    }

    /// <summary>Records a committed outcome; never pass credentials, raw provider errors or platform identities.</summary>
    /// <param name="category">Owning system.</param>
    /// <param name="kind">Stable outcome type.</param>
    /// <param name="actor">Responsible player.</param>
    /// <param name="target">Affected player.</param>
    /// <param name="cause">Allowlisted cause.</param>
    /// <param name="context">Safe mechanic or setting context.</param>
    /// <param name="amount">Exact applied amount or new value.</param>
    /// <param name="hp">Remaining health.</param>
    /// <param name="maxHP">Health capacity.</param>
    /// <param name="life">Affected life.</param>
    /// <param name="tick">Authority tick.</param>
    /// <param name="previous">Previous setting value.</param>
    /// <param name="local">Whether this is a local diagnostic.</param>
    public void Record(EventCategory category, string kind, ulong actor = 0, ulong target = 0, string cause = "", string context = "", double? amount = null, double? hp = null, double? maxHP = null, ulong life = 0, ulong tick = 0, double? previous = null, bool local = false)
    {
        string Name(ulong id) => id == 0 ? string.Empty : Sessions.PlayerName.Sanitize(PlayerName?.Invoke(id) ?? $"Player {id}");
        var entry = new RuntimeEvent
        {
            Sequence = local ? checked(_localSequence + 1) : checked(LastSequence + 1),
            Milliseconds = _milliseconds,
            Category = category,
            Kind = kind,
            Actor = actor,
            Target = target,
            ActorName = Name(actor),
            TargetName = Name(target),
            Cause = cause,
            Context = context,
            Amount = amount,
            RemainingHP = hp,
            MaximumHP = maxHP,
            Life = life,
            Tick = tick,
            PreviousValue = previous,
            Local = local,
        };
        entry.Validate();
        if (local)
        {
            _localSequence = entry.Sequence;
            Append(entry);
        }
        else
        {
            Accept(entry);
        }
    }

    /// <summary>Accepts a trusted host outcome once, even after its original row has been evicted.</summary>
    /// <param name="entry">Validated trusted-host outcome.</param>
    /// <returns>False for previously observed sequence identities.</returns>
    public bool Accept(RuntimeEvent entry)
    {
        entry.Validate();
        if (entry.Local || entry.Sequence <= LastSequence)
        {
            return false;
        }

        LastSequence = entry.Sequence;
        Append(entry);
        return true;
    }

    private void Append(RuntimeEvent entry)
    {
        if (_entries.Count == _capacity)
        {
            _entries.Dequeue();
        }

        _entries.Enqueue(entry);
        Revision++;
        Appended?.Invoke(entry);
    }
}
