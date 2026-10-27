using System.Numerics;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Arenas;

/// <summary>Matching-build authored rock and soft-cover identities, in stable scene-path order.</summary>
public sealed class EnvironmentLayout
{
    public const int PiecesPerRock = 4;
    public const int MaximumPieces = 256 * PiecesPerRock;
    public const byte MaximumStage = 16;
    public const float SizeRatio = 0.72f;
    private readonly EnvironmentRockBounds? _debrisBounds;
    private readonly float[] _debrisYaw;

    public EnvironmentLayout(IEnumerable<Vector3> rocks, IEnumerable<Vector3> plants, IEnumerable<float>? sizes = null, float minimumSize = 0.5f,
        IEnumerable<EnvironmentRockBounds>? bounds = null, EnvironmentRockBounds? debrisBounds = null, IEnumerable<float>? debrisYaw = null)
    {
        var roots = rocks.ToArray();
        var dimensions = sizes?.ToArray() ?? Enumerable.Repeat(minimumSize * 2, roots.Length).ToArray();
        if (!float.IsFinite(minimumSize) || minimumSize <= 0 || dimensions.Length != roots.Length ||
            dimensions.Any(s => !float.IsFinite(s) || s < minimumSize || s > minimumSize * 64)) { throw new ArgumentException("Invalid authored rock sizes."); }
        RootCount = roots.Length;
        // Debris bounds are authored in units of mesh-volume equivalent diameter.
        _debrisBounds = debrisBounds;
        _debrisBounds?.Validate();
        _debrisYaw = debrisYaw?.ToArray() ?? new float[RootCount];
        if (_debrisYaw.Length != RootCount || _debrisYaw.Any(yaw => !float.IsFinite(yaw))) { throw new ArgumentException("Invalid debris headings."); }
        Bounds = bounds is null ? null : Array.AsReadOnly(bounds.ToArray());
        if (Bounds is not null)
        {
            if (Bounds.Count != RootCount) { throw new ArgumentException("Rock bounds count mismatch."); }
            foreach (var box in Bounds) { box.Validate(); }
        }
        MinimumSize = minimumSize;
        Sizes = Array.AsReadOnly(dimensions);
        Rocks = Array.AsReadOnly(roots.SelectMany(p => Enumerable.Repeat(p, PiecesPerRock)).ToArray());
        Plants = Array.AsReadOnly(plants.ToArray());
        InitialStages = Array.AsReadOnly(Enumerable.Range(0, Rocks.Count).Select(i => (byte)(i % PiecesPerRock != 0 ? 0 : dimensions[i / PiecesPerRock] <= minimumSize / SizeRatio ? 2 : 1)).ToArray());
        if (Rocks.Count > MaximumPieces || Plants.Count > 4096 || Rocks.Concat(Plants).Any(p => !VehiclePhysicsState.IsFinite(p) || p.LengthSquared() > 1000000))
        {
            throw new ArgumentException("Invalid environment layout.");
        }
    }

    public IReadOnlyList<Vector3> Rocks { get; }
    public IReadOnlyList<Vector3> Plants { get; }
    public IReadOnlyList<byte> InitialStages { get; }
    public IReadOnlyList<float> Sizes { get; }
    public IReadOnlyList<EnvironmentRockBounds>? Bounds { get; }
    public int RootCount { get; }
    public float MinimumSize { get; }

    /// <summary>Size-derived terminal depth; reserved sibling slots share their authored root's profile.</summary>
    public byte FinalStage(int piece) => (byte)Math.Max(2, 1 + (int)Math.Ceiling(Math.Log(MinimumSize / Sizes[piece / PiecesPerRock]) / Math.Log(SizeRatio)));
    public float Size(int piece, byte stage) => Math.Max(MinimumSize, Sizes[piece / PiecesPerRock] * MathF.Pow(SizeRatio, Math.Max(0, stage - 1)));

    internal EnvironmentRockBounds? BlastBounds(int piece, EnvironmentRockState state)
    {
        if (state.Stage == 1) { return Bounds?[piece / PiecesPerRock]; }
        if (_debrisBounds is null) { return null; }
        float scale = Size(piece, state.Stage);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _debrisYaw[piece / PiecesPerRock] + (piece % PiecesPerRock) * 2.39996f);
        return new(Rocks[piece] + state.Offset + Vector3.Transform(_debrisBounds.Center * scale, rotation),
            _debrisBounds.Size * scale, rotation * _debrisBounds.Orientation);
    }
}
