namespace Trackstorm.Core.Matches;

/// <summary>Authoritative source category for one banked Circus award.</summary>
public enum CircusScoreCategory : byte
{
    Kill = 1,
    Collision = 2,
    Drift = 3,
    Airtime = 4,
    LongJump = 5,
    TopSpeed = 6,
    /// <summary>Authoritative overspeed, including recovery after Nitro release.</summary>
    Nitro = 7,
    /// <summary>Confirmed rival entry into an owned Oil patch.</summary>
    Oil = 8,
    /// <summary>Actual authoritative HP removed by registered damaging items.</summary>
    ItemDamage = 9,
}
