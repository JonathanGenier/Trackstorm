namespace Trackstorm.Core.Sessions;

/// <summary>Single-attempt, unanimous survivor agreement on an exact recoverable boundary.</summary>
public sealed class MigrationElection
{
    private readonly HashSet<ulong> _voters;
    private readonly HashSet<ulong> _accepted = new();

    /// <summary>Derives candidate and electorate solely from a validated checkpoint.</summary>
    /// <param name="checkpoint">Shared pre-loss roster.</param>
    /// <param name="digest">Digest of the exact recoverable bytes.</param>
    public MigrationElection(MigrationCheckpoint checkpoint, string digest)
    {
        var state = checkpoint.Lobby.State;
        _voters = state.Players.Where(player => player.Connected && player.Id != state.CurrentHostId).Select(player => player.Id).ToHashSet();
        if (_voters.Count < 2 || digest.Length != 64 || !digest.All(Uri.IsHexDigit) || state.AuthorityEpoch == ulong.MaxValue)
        {
            throw new ArgumentException("Migration requires at least two eligible survivors and a valid boundary.");
        }

        Session = state.Session;
        Epoch = state.AuthorityEpoch;
        Candidate = _voters.Min();
        Digest = digest;
    }

    /// <summary>Stable logical session.</summary>
    public ulong Session { get; }
    /// <summary>Old authority fence being retired.</summary>
    public ulong Epoch { get; }
    /// <summary>Lowest connected surviving stable identity.</summary>
    public ulong Candidate { get; }
    /// <summary>Exact checkpoint being agreed.</summary>
    public string Digest { get; }
    /// <summary>All eligible survivors must agree; missing peers cause bounded failure.</summary>
    public bool Agreed => _accepted.SetEquals(_voters);
    /// <summary>Whether the one permitted transition was committed.</summary>
    public bool Committed { get; private set; }

    /// <summary>Accepts an authenticated vote once; no arrival-order choice is made.</summary>
    /// <param name="player">Identity resolved from the authenticated transport.</param>
    /// <param name="session">Claimed session.</param>
    /// <param name="epoch">Expected old authority epoch.</param>
    /// <param name="candidate">Deterministically selected candidate.</param>
    /// <param name="digest">Exact proposed checkpoint.</param>
    /// <returns>Whether a new agreeing vote was retained.</returns>
    public bool Vote(ulong player, ulong session, ulong epoch, ulong candidate, string digest) =>
        !Committed && session == Session && epoch == Epoch && candidate == Candidate && digest == Digest && _voters.Contains(player) && _accepted.Add(player);

    /// <summary>Advances once only after complete agreement.</summary>
    /// <returns>The new epoch.</returns>
    public ulong Commit()
    {
        if (Committed || !Agreed)
        {
            throw new InvalidOperationException("Migration has not reached one coherent authority.");
        }

        Committed = true;
        return checked(Epoch + 1);
    }
}
