using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Arenas;

/// <summary>Complete current-match environment boundary, including damage and movement continuation.</summary>
public sealed class EnvironmentSnapshot
{
    public EnvironmentSnapshot(ulong session, ulong tick, IEnumerable<EnvironmentRockState> rocks, IEnumerable<bool> plants)
    {
        ArgumentOutOfRangeException.ThrowIfZero(session);
        var copy = rocks.ToArray();
        var cleared = plants.ToArray();
        if (copy.Length > 256 || cleared.Length > 4096 || copy.Count(r => r.Velocity != default) > EnvironmentAuthority.MaximumMoving ||
            copy.Any(r => r.Stage is < 1 or > 3 || !float.IsFinite(r.Damage) || r.Damage < 0 || r.Damage >= (r.Stage == 1 ? EnvironmentAuthority.IntactHealth : EnvironmentAuthority.BrokenHealth) ||
                (r.Stage == 3 && r.Damage != 0) || (r.ImpactReadyTick > tick && r.ImpactReadyTick - tick > 12) ||
                !VehiclePhysicsState.IsFinite(r.Offset) || !VehiclePhysicsState.IsFinite(r.Velocity) ||
                r.Offset.LengthSquared() > 64.001f || r.Velocity.LengthSquared() > 36.001f || r.Velocity.Y != 0 || r.Offset.Y != 0 ||
                (r.Stage == 1 && (r.Offset != default || r.Velocity != default))))
        {
            throw new ArgumentException("Invalid environment boundary.");
        }
        Session = session;
        Tick = tick;
        Rocks = Array.AsReadOnly(copy);
        Plants = Array.AsReadOnly(cleared);
    }

    public ulong Session { get; }
    public ulong Tick { get; }
    public IReadOnlyList<EnvironmentRockState> Rocks { get; }
    public IReadOnlyList<bool> Plants { get; }
}
