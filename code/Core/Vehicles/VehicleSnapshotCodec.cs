using System.Text.Json;

namespace Trackstorm.Core.Vehicles;

/// <summary>Version-two complete aggregate envelope including authoritative lifecycle and deadline.</summary>
public static class VehicleSnapshotCodec
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true, MaxDepth = 16 };

    /// <summary>Encodes identity/life, commands, solved physics, health, attribution and accepted impulses.</summary>
    /// <param name="snapshot">Complete authoritative aggregate.</param>
    /// <returns>Version byte followed by bounded UTF-8 JSON with explicit nested movement encodings.</returns>
    public static byte[] Encode(VehicleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var observed = new VehicleState(snapshot.Movement.Tick, snapshot.ObservedPhysics, false, false, 0, 0);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(new Document(snapshot.VehicleId, snapshot.LifeId, VehicleStateCodec.Encode(snapshot.Movement), VehicleStateCodec.Encode(observed), snapshot.Damage, snapshot.Effects.ToArray(), snapshot.Lifecycle, snapshot.RespawnAtTick), Options);
        if (json.Length > 65535)
        {
            throw new ArgumentException("Vehicle aggregate exceeds the supported envelope size.", nameof(snapshot));
        }

        byte[] bytes = new byte[json.Length + 1];
        bytes[0] = 2;
        json.CopyTo(bytes, 1);
        return bytes;
    }

    /// <summary>Rejects unknown versions, oversized/truncated payloads and invalid nested gameplay state.</summary>
    /// <param name="bytes">One version-two aggregate envelope.</param>
    /// <returns>Validated vehicle state, independent of any native object.</returns>
    public static VehicleSnapshot Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 2 or > 65536 || bytes[0] != 2)
        {
            throw new ArgumentException("Expected a bounded version-two vehicle aggregate.", nameof(bytes));
        }

        try
        {
            Document document = JsonSerializer.Deserialize<Document>(bytes[1..], Options) ?? throw new ArgumentException("Missing vehicle aggregate.");
            VehicleState movement = VehicleStateCodec.Decode(document.Movement);
            VehicleState observed = VehicleStateCodec.Decode(document.Observed);
            if (observed.Tick != movement.Tick || observed.Grounded || observed.Drifting || observed.SteeringAngle != 0 || observed.Handbrake != 0 || observed.FrontSlip != 0 || observed.RearSlip != 0 || observed.LongitudinalAcceleration != 0 || observed.LateralAcceleration != 0 || observed.LandingIntensity != 0 || observed.Wheels != default || observed.CurrentSurface != SurfaceType.Concrete || document.Effects is null)
            {
                throw new ArgumentException("Malformed solved-state envelope.");
            }

            return new VehicleSnapshot(document.VehicleId, document.LifeId, movement, document.Damage, observed.Physics, document.Effects, document.Lifecycle, document.RespawnAtTick);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Malformed vehicle aggregate JSON.", nameof(bytes), exception);
        }
    }

    private sealed record Document(ulong VehicleId, ulong LifeId, byte[] Movement, byte[] Observed, VehicleDamageState Damage, VehicleEffectRequest[] Effects, VehicleLifecycle Lifecycle, ulong? RespawnAtTick);
}
