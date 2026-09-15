using System.Numerics;

namespace Trackstorm.Client.Networking;

/// <summary>Presentation-only correction offsets; authoritative transforms are never delayed.</summary>
internal sealed class CorrectionSmoothing
{
    /// <summary>Sub-centimetre position corrections do not need a visible offset.</summary>
    internal const float IgnoreDistance = 0.01f;
    /// <summary>Large discontinuities are shown immediately instead of dragging a ghost through walls.</summary>
    internal const float SnapDistance = 3;
    /// <summary>Visible position offset from the current gameplay pose.</summary>
    internal Vector3 Offset { get; private set; }
    /// <summary>Visible orientation offset.</summary>
    internal Quaternion Rotation { get; private set; } = Quaternion.Identity;
    /// <summary>Observed large-correction count for diagnostics.</summary>
    internal int HardSnaps { get; private set; }

    /// <summary>Discards correction offsets at an authoritative new-life boundary.</summary>
    internal void Reset()
    {
        Offset = Vector3.Zero;
        Rotation = Quaternion.Identity;
    }

    /// <summary>Preserves the visible pose for small corrections and snaps large discontinuities.</summary>
    /// <param name="before">Old predicted position including existing visual offset.</param>
    /// <param name="after">New predicted position.</param>
    /// <param name="beforeRotation">Old visible orientation.</param>
    /// <param name="afterRotation">New authoritative/predicted orientation.</param>
    internal void Correct(Vector3 before, Vector3 after, Quaternion beforeRotation, Quaternion afterRotation)
    {
        Vector3 error = before - after;
        float distance = error.Length();
        if (distance >= SnapDistance)
        {
            Offset = Vector3.Zero;
            Rotation = Quaternion.Identity;
            HardSnaps++;
            return;
        }

        Offset = distance <= IgnoreDistance ? Vector3.Zero : error;
        Rotation = Quaternion.Normalize(beforeRotation * Quaternion.Inverse(afterRotation));
    }

    /// <summary>Decays visible correction continuously, independently of render frequency.</summary>
    /// <param name="seconds">Nonnegative render delta.</param>
    internal void Advance(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds));
        }

        float weight = 1 - MathF.Exp(-15 * seconds);
        Offset = Vector3.Lerp(Offset, Vector3.Zero, weight);
        Rotation = Quaternion.Slerp(Rotation, Quaternion.Identity, weight);
    }
}
