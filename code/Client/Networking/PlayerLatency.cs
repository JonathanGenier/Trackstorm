using System.Buffers.Binary;
using System.Diagnostics;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Networking;

/// <summary>Bounded host-published presentation diagnostics. Session/roster revisions prevent retired bindings from leaking into new players.</summary>
internal sealed class PlayerLatency
{
    private readonly Dictionary<ulong, int?> _samples = new();
    private readonly Func<double> _seconds;
    private ulong _session;
    private ulong _authorityEpoch;
    private ulong _roster;
    private ulong _match;
    private ulong _sequence;
    private double _sampledAt = double.NegativeInfinity;

    /// <summary>Uses monotonic presentation time so a stalled simulation cannot keep latency fresh.</summary>
    /// <param name="seconds">Optional controlled monotonic clock for tests.</param>
    internal PlayerLatency(Func<double>? seconds = null) => _seconds = seconds ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);

    /// <summary>Identifies the independent diagnostics protocol.</summary>
    /// <param name="data">Transport payload.</param>
    /// <returns>Whether the prefix belongs to diagnostics.</returns>
    internal static bool IsLatency(ReadOnlySpan<byte> data) => data.Length >= 2 && data[0] == (byte)'T' && data[1] == (byte)'P';

    /// <summary>Returns only fresh values for the exact authoritative roster. The host has no network hop and displays unavailable.</summary>
    /// <param name="state">Current roster.</param>
    /// <param name="playerId">Stable identity.</param>
    /// <returns>Fresh milliseconds or unavailable.</returns>
    internal int? Get(LobbySnapshot state, ulong playerId) => Matches(state) && _seconds() - _sampledAt is >= 0 and <= 3 && state.Players.Any(player => player.Id == playerId && player.Connected) ? _samples.GetValueOrDefault(playerId) : null;

    /// <summary>Immediately invalidates the previous stream when the local transport is interrupted.</summary>
    internal void Clear()
    {
        _samples.Clear();
        _session = 0;
        _sampledAt = double.NegativeInfinity;
    }


    /// <summary>Samples only currently connected, authoritative peer-to-player bindings.</summary>
    /// <param name="state">Current roster.</param>
    /// <param name="peers">Authoritative transport bindings.</param>
    /// <param name="gateway">Transport-neutral statistics provider.</param>
    /// <returns>Bounded complete publication.</returns>
    internal byte[] Sample(LobbySnapshot state, IReadOnlyDictionary<ulong, ulong> peers, ITransportGateway gateway)
    {
        var data = new byte[44 + (state.Players.Count * 12)];
        data[0] = (byte)'T';
        data[1] = (byte)'P';
        data[2] = 2;
        data[3] = (byte)state.Players.Count;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(4), state.Session);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(12), state.Revision);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(20), state.Match);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(28), _sequence + 1);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(36), state.AuthorityEpoch);
        for (int index = 0; index < state.Players.Count; index++)
        {
            ulong id = state.Players[index].Id;
            int ping = -1;
            foreach (var binding in peers)
            {
                if (state.Players[index].Connected && binding.Value == id && gateway.Connections.GetValueOrDefault(binding.Key) == TransportConnectionState.Connected &&
                    gateway.GetStatistics(binding.Key).PingMilliseconds is >= 0 and <= 60000 and var sample)
                {
                    ping = sample;
                }
            }

            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(44 + (index * 12)), id);
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(52 + (index * 12)), ping);
        }

        Accept(data, state);
        return data;
    }

    /// <summary>Atomically accepts complete fresh publications for the exact session roster; caller authenticates the sender.</summary>
    /// <param name="data">Complete payload.</param>
    /// <param name="state">Current authoritative roster.</param>
    /// <returns>Whether accepted.</returns>
    internal bool Accept(ReadOnlySpan<byte> data, LobbySnapshot state)
    {
        if (!IsLatency(data) || data.Length < 44 || data[2] != 2 || data[3] is < 1 or > 8 || data.Length != 44 + (data[3] * 12) ||
            BinaryPrimitives.ReadUInt64LittleEndian(data[4..]) != state.Session || BinaryPrimitives.ReadUInt64LittleEndian(data[12..]) != state.Revision ||
            BinaryPrimitives.ReadUInt64LittleEndian(data[20..]) != state.Match || BinaryPrimitives.ReadUInt64LittleEndian(data[36..]) != state.AuthorityEpoch || data[3] != state.Players.Count)
        {
            return false;
        }

        ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(data[28..]);
        if (sequence == 0 || (Matches(state) && sequence <= _sequence))
        {
            return false;
        }

        var samples = new Dictionary<ulong, int?>();
        for (int index = 0; index < data[3]; index++)
        {
            ulong id = BinaryPrimitives.ReadUInt64LittleEndian(data[(44 + (index * 12))..]);
            int ping = BinaryPrimitives.ReadInt32LittleEndian(data[(52 + (index * 12))..]);
            if (!state.Players.Any(player => player.Id == id) || ping is < -1 or > 60000 || (id == state.CurrentHostId && ping != -1) || !samples.TryAdd(id, ping < 0 ? null : ping))
            {
                return false;
            }
        }

        _samples.Clear();
        foreach (var pair in samples)
        {
            _samples.Add(pair.Key, pair.Value);
        }

        _session = state.Session;
        _authorityEpoch = state.AuthorityEpoch;
        _roster = state.Revision;
        _match = state.Match;
        _sequence = sequence;
        _sampledAt = _seconds();
        return true;
    }

    private bool Matches(LobbySnapshot state) => _session == state.Session && _authorityEpoch == state.AuthorityEpoch && _roster == state.Revision && _match == state.Match;
}
