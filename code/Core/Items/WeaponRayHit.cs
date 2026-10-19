namespace Trackstorm.Core.Items;

/// <summary>Host native closest-hit observation on a Core-authored segment; zero vehicle denotes world cover.</summary>
public sealed record WeaponRayHit(float Fraction, ulong Vehicle)
{
    /// <summary>Host-observed surface normal, when the native adapter supplies it.</summary>
    public System.Numerics.Vector3 Normal { get; init; }
}
