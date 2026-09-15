namespace Trackstorm.Core.Vehicles;

/// <summary>Bounded immutable handling multipliers; multiplication with validated vehicle tuning stays finite.</summary>
public readonly record struct SurfaceModifiers
{
    /// <summary>Rejects nonfinite, negative or excessive multipliers before simulation.</summary>
    /// <param name="grip">Combined tire traction multiplier.</param>
    /// <param name="drag">Coasting drag multiplier; values above one add resistance under power too.</param>
    /// <param name="acceleration">Forward and reverse engine-force multiplier.</param>
    public SurfaceModifiers(float grip, float drag, float acceleration)
    {
        if (!float.IsFinite(grip) || !float.IsFinite(drag) || !float.IsFinite(acceleration) ||
            grip is < 0 or > 100 || drag is < 0 or > 100 || acceleration is < 0 or > 100)
        {
            throw new ArgumentException("Surface multipliers must be finite and between zero and 100.");
        }

        Grip = grip;
        Drag = drag;
        Acceleration = acceleration;
    }

    /// <summary>Lateral grip multiplier.</summary>
    public float Grip { get; }
    /// <summary>Ground resistance multiplier.</summary>
    public float Drag { get; }
    /// <summary>Drive acceleration multiplier.</summary>
    public float Acceleration { get; }
}
