namespace Trackstorm.Core.Items;

/// <summary>Host native closest-hit observation on a Core-authored segment; zero vehicle denotes world cover.</summary>
public sealed record WeaponRayHit(float Fraction, ulong Vehicle);
