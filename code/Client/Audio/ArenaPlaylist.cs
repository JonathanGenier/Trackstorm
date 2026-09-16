namespace Trackstorm.Client.Audio;

/// <summary>Client-owned random start followed by a fixed cyclic playlist.</summary>
internal sealed class ArenaPlaylist
{
    private readonly Func<int, int> _select;

    /// <summary>Uses a caller-owned selector only at each new match start.</summary>
    /// <param name="select">Injected starting-index selector.</param>
    internal ArenaPlaylist(Func<int, int> select) => _select = select;

    /// <summary>Current track index, or minus one when stopped.</summary>
    internal int Index { get; private set; } = -1;

    /// <summary>Whether this playlist belongs to an active match.</summary>
    internal bool Active => Index >= 0;

    /// <summary>Starts once; repeated active publications preserve the selected track.</summary>
    /// <returns>The selected presentation value.</returns>
    internal bool Start()
    {
        if (Active)
        {
            return false;
        }

        int index = _select(3);
        if (index is < 0 or >= 3)
        {
            throw new InvalidOperationException("Playlist selector must return one of three track indices.");
        }

        Index = index;
        return true;
    }

    /// <summary>Advances one track and wraps; stopped playlists ignore late completion.</summary>
    /// <returns>The selected presentation value.</returns>
    internal bool Advance()
    {
        if (!Active)
        {
            return false;
        }

        Index = (Index + 1) % 3;
        return true;
    }

    /// <summary>Clears the active track and permits a fresh randomized start.</summary>
    internal void Stop() => Index = -1;
}
