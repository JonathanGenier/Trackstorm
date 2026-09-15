using System.Buffers.Binary;

namespace Trackstorm.Client.Networking;

/// <summary>Bounded unordered reassembly and deduplication, independent of application message purpose.</summary>
internal sealed class EosUnreliableWindow(TimeProvider time)
{
    /// <summary>Newest sequence plus its fifteen predecessors; each owns one fixed 64 KiB buffer.</summary>
    internal const int Capacity = 16;
    /// <summary>Incomplete messages expire one second after their first accepted fragment, without deadline extension.</summary>
    internal const int LifetimeSeconds = 1;
    private readonly Entry[] _entries = Enumerable.Range(0, Capacity).Select(_ => new Entry()).ToArray();
    private uint _newest;
    private bool _started;

    /// <summary>Returns each complete in-window message once, in completion order rather than sequence order.</summary>
    /// <param name="packet">Nonce-validated framed packet with a complete header.</param>
    /// <returns>Stable caller-owned payload, or null for incomplete, duplicate, expired or out-of-window traffic.</returns>
    internal byte[]? Receive(ReadOnlySpan<byte> packet)
    {
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(packet[17..]);
        if (!_started || unchecked((int)(sequence - _newest)) > 0)
        {
            _started = true;
            _newest = sequence;
            foreach (var previous in _entries)
            {
                if (previous.Occupied && unchecked(_newest - previous.Sequence) >= Capacity)
                {
                    previous.Occupied = false;
                    previous.Assembly.Reset();
                }
            }
        }
        else if (unchecked(_newest - sequence) >= Capacity)
        {
            return null;
        }

        var entry = _entries[sequence % Capacity];
        if (!entry.Occupied)
        {
            entry.Assembly.Reset();
            entry.Occupied = true;
            entry.Retired = false;
            entry.Sequence = sequence;
            entry.Started = time.GetTimestamp();
        }

        if (entry.Retired)
        {
            return null;
        }

        var payload = entry.Assembly.Receive(packet, false);
        if (payload is not null)
        {
            entry.Retired = true;
        }

        return payload;
    }

    /// <summary>Expires incomplete messages even on idle polls; tombstones prevent late fragments from restarting them.</summary>
    internal void Expire()
    {
        foreach (var entry in _entries)
        {
            if (entry.Occupied && !entry.Retired && time.GetElapsedTime(entry.Started).TotalSeconds >= LifetimeSeconds)
            {
                entry.Retired = true;
                entry.Assembly.Reset();
            }
        }
    }

    private sealed class Entry
    {
        internal EosPacketAssembly Assembly { get; } = new();
        internal uint Sequence { get; set; }
        internal long Started { get; set; }
        internal bool Occupied { get; set; }
        internal bool Retired { get; set; }
    }
}
