using System.Buffers.Binary;

namespace Trackstorm.Client.Networking;

/// <summary>One bounded assembly per delivery stream; unreliable replacement abandons incomplete older messages.</summary>
internal sealed class EosPacketAssembly
{
    /// <summary>Maximum completed payload accepted by either delivery stream.</summary>
    internal const int MaximumPayload = 65536;
    /// <summary>Type, two nonces, message sequence, total size and fragment index.</summary>
    internal const int Header = 29;
    /// <summary>Usable bytes after framing within EOS's packet limit.</summary>
    internal const int FragmentBytes = 1170 - Header;
    private readonly byte[] _buffer = new byte[MaximumPayload];
    private uint _sequence;
    private int _length;
    private ulong _fragments;
    private bool _started;
    private bool _complete;

    /// <summary>Validates and copies a fragment, returning a stable payload only on completion.</summary>
    /// <param name="packet">Complete framed packet, including its header.</param>
    /// <param name="reliable">Whether incomplete replacement violates delivery guarantees.</param>
    /// <returns>Completed caller-owned payload or null.</returns>
    internal byte[]? Receive(ReadOnlySpan<byte> packet, bool reliable)
    {
        uint sequence = BinaryPrimitives.ReadUInt32LittleEndian(packet[17..]);
        int length = BinaryPrimitives.ReadInt32LittleEndian(packet[21..]);
        int index = BinaryPrimitives.ReadInt32LittleEndian(packet[25..]);
        int count = Math.Max(1, (length + FragmentBytes - 1) / FragmentBytes);
        if (length is < 0 or > MaximumPayload || index < 0 || index >= count || packet.Length - Header != Math.Min(FragmentBytes, length - (index * FragmentBytes)))
        {
            throw new ArgumentException("Invalid EOS packet fragment.");
        }

        if (!_started || sequence != _sequence)
        {
            if (_started && unchecked((int)(sequence - _sequence)) <= 0)
            {
                return null;
            }

            if (reliable && _started && !_complete)
            {
                throw new ArgumentException("Incomplete reliable EOS message.");
            }

            _started = true;
            _complete = false;
            _sequence = sequence;
            _length = length;
            _fragments = 0;
        }

        if (_length != length)
        {
            throw new ArgumentException("Inconsistent EOS fragments.");
        }

        if (_complete)
        {
            return null;
        }

        packet[Header..].CopyTo(_buffer.AsSpan(index * FragmentBytes));
        _fragments |= 1UL << index;
        if (_fragments != (1UL << count) - 1)
        {
            return null;
        }

        _complete = true;
        // The public transport contract transfers stable payload ownership, requiring one copy per complete message.
        return _buffer.AsSpan(0, length).ToArray();
    }
}
