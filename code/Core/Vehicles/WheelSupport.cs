using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Plain fixed-step suspension compression observations, in metres, ordered front-left/right then rear-left/right.</summary>
public readonly record struct WheelSupport
{
    /// <summary>Copies bounded wheel observations; zero means an extended/unsupported wheel.</summary>
    /// <param name="compression">Four nonnegative spring compressions.</param>
    /// <param name="frontLeft">Optional observed front-left surface.</param>
    /// <param name="frontRight">Optional observed front-right surface.</param>
    /// <param name="rearLeft">Optional observed rear-left surface.</param>
    /// <param name="rearRight">Optional observed rear-right surface.</param>
    public WheelSupport(Vector4 compression, SurfaceType? frontLeft = null, SurfaceType? frontRight = null, SurfaceType? rearLeft = null, SurfaceType? rearRight = null)
    {
        if (new[] { compression.X, compression.Y, compression.Z, compression.W }.Any(value => !float.IsFinite(value) || value < 0 || value > 1))
        {
            throw new ArgumentException("Wheel compression must be finite and within travel.", nameof(compression));
        }

        Compression = compression;
        foreach (var surface in new[] { frontLeft, frontRight, rearLeft, rearRight })
        {
            if (surface.HasValue && !Enum.IsDefined(surface.Value)) { throw new ArgumentException("Invalid wheel surface."); }
        }
        FrontLeft = frontLeft;
        FrontRight = frontRight;
        RearLeft = rearLeft;
        RearRight = rearRight;
    }

    /// <summary>Individual spring compression, in metres.</summary>
    public Vector4 Compression { get; }
    /// <summary>Observed front-left material; null uses the aggregate fallback.</summary>
    public SurfaceType? FrontLeft { get; }
    /// <summary>Observed front-right material; null uses the aggregate fallback.</summary>
    public SurfaceType? FrontRight { get; }
    /// <summary>Observed rear-left material; null uses the aggregate fallback.</summary>
    public SurfaceType? RearLeft { get; }
    /// <summary>Observed rear-right material; null uses the aggregate fallback.</summary>
    public SurfaceType? RearRight { get; }
}
