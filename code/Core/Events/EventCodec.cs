using System.Text.Json;

namespace Trackstorm.Core.Events;

/// <summary>Bounded reliable event envelope. The existing session/connection envelope owns sender fencing.</summary>
public static class EventCodec
{
    /// <summary>Maximum encoded packet size.</summary>
    public const int MaximumBytes = 48000;
    /// <summary>Maximum outcomes per reliable batch.</summary>
    public const int MaximumEvents = 16;
    /// <summary>Recognizes the versioned event header.</summary>
    /// <param name="data">Untrusted packet.</param>
    /// <returns>Whether the header matches.</returns>
    public static bool IsEvent(ReadOnlySpan<byte> data) => data.Length >= 3 && data[0] == (byte)'T' && data[1] == (byte)'E' && data[2] == 1;

    /// <summary>Encodes ordered authoritative outcomes.</summary>
    /// <param name="entries">Bounded ordered batch.</param>
    /// <returns>Detached packet bytes.</returns>
    public static byte[] Encode(IReadOnlyList<RuntimeEvent> entries)
    {
        Validate(entries);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(entries);
        if (json.Length + 3 > MaximumBytes)
        {
            throw new ArgumentException("Event packet exceeds bound.");
        }

        return [(byte)'T', (byte)'E', 1, .. json];
    }

    /// <summary>Decodes and validates a complete batch before installation.</summary>
    /// <param name="data">Untrusted packet.</param>
    /// <returns>Detached immutable outcomes.</returns>
    public static IReadOnlyList<RuntimeEvent> Decode(ReadOnlySpan<byte> data)
    {
        if (!IsEvent(data) || data.Length > MaximumBytes)
        {
            throw new ArgumentException("Invalid event envelope.");
        }

        try
        {
            var entries = JsonSerializer.Deserialize<RuntimeEvent[]>(data[3..], new JsonSerializerOptions { MaxDepth = 4 }) ?? throw new ArgumentException("Missing events.");
            Validate(entries);
            return Array.AsReadOnly(entries);
        }
        catch (JsonException)
        {
            throw new ArgumentException("Malformed event envelope.");
        }
    }

    private static void Validate(IReadOnlyList<RuntimeEvent> entries)
    {
        if (entries.Count is < 1 or > MaximumEvents)
        {
            throw new ArgumentException("Invalid event count.");
        }

        ulong previous = 0;
        foreach (var entry in entries)
        {
            if (entry is null)
            {
                throw new ArgumentException("Missing event.");
            }

            entry.Validate();
            if (entry.Local || entry.Sequence <= previous)
            {
                throw new ArgumentException("Events must be authoritative and ordered.");
            }

            previous = entry.Sequence;
        }
    }
}
