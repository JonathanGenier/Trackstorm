using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Arenas;

/// <summary>Matching-build authored rock and soft-cover identities, in stable scene-path order.</summary>
public sealed class EnvironmentLayout
{
    public EnvironmentLayout(IEnumerable<Vector3> rocks, IEnumerable<Vector3> plants, IEnumerable<byte>? initialStages = null)
    {
        Rocks = Array.AsReadOnly(rocks.ToArray());
        Plants = Array.AsReadOnly(plants.ToArray());
        InitialStages = Array.AsReadOnly(initialStages?.ToArray() ?? Enumerable.Repeat((byte)1, Rocks.Count).ToArray());
        if (InitialStages.Count != Rocks.Count || InitialStages.Any(s => s is not (1 or 3))) { throw new ArgumentException("Invalid authored rock stages."); }
        if (Rocks.Count > 256 || Plants.Count > 4096 || Rocks.Concat(Plants).Any(p => !VehiclePhysicsState.IsFinite(p) || p.LengthSquared() > 1000000))
        {
            throw new ArgumentException("Invalid environment layout.");
        }
    }

    public IReadOnlyList<Vector3> Rocks { get; }
    public IReadOnlyList<Vector3> Plants { get; }
    public IReadOnlyList<byte> InitialStages { get; }
}
