using System.Numerics;

namespace Trackstorm.Core.Vehicles;

/// <summary>Plain fixed-step suspension compression observations, in metres, ordered front-left/right then rear-left/right.</summary>
public readonly record struct WheelSupport
{
    /// <summary>Copies bounded wheel observations; zero means an extended/unsupported wheel.</summary>
    /// <param name="compression">Four nonnegative spring compressions.</param>
    public WheelSupport(Vector4 compression)
    {
        if (new[] { compression.X, compression.Y, compression.Z, compression.W }.Any(value => !float.IsFinite(value) || value < 0 || value > 1))
        {
            throw new ArgumentException("Wheel compression must be finite and within travel.", nameof(compression));
        }

        Compression = compression;
    }

    /// <summary>Individual spring compression, in metres.</summary>
    public Vector4 Compression { get; }
}
