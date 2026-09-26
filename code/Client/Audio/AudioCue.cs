namespace Trackstorm.Client.Audio;

/// <summary>Existing arena feedback slots; never authoritative gameplay state.</summary>
internal enum AudioCue
{
    /// <summary>Engine Idle.</summary>
    EngineIdle,
    /// <summary>Short confirmed machine-gun report.</summary>
    MachineGunFire,
    /// <summary>Engine Low.</summary>
    EngineLow,
    /// <summary>Engine High.</summary>
    EngineHigh,
    /// <summary>Skid.</summary>
    Skid,
    /// <summary>Collision.</summary>
    Collision,
    /// <summary>Heavy Collision.</summary>
    HeavyCollision,
    /// <summary>Damage.</summary>
    Damage,
    /// <summary>Destruction.</summary>
    Destruction,
    /// <summary>Missile Fire.</summary>
    MissileFire,
    /// <summary>Missile Travel.</summary>
    MissileTravel,
    /// <summary>Missile Impact.</summary>
    MissileImpact,
    /// <summary>Explosion.</summary>
    Explosion,
    /// <summary>Weapon Pickup.</summary>
    WeaponPickup,
    /// <summary>Wrench Pickup.</summary>
    WrenchPickup,
    /// <summary>Wrench Use.</summary>
    WrenchUse,
    /// <summary>Kill.</summary>
    Kill,
    /// <summary>Death.</summary>
    Death,
    /// <summary>Respawn.</summary>
    Respawn,
    /// <summary>Countdown.</summary>
    Countdown,
    /// <summary>Match Start.</summary>
    MatchStart,
    /// <summary>Match End.</summary>
    MatchEnd,
    /// <summary>End Sting.</summary>
    EndSting,
    /// <summary>Ambience.</summary>
    Ambience,
}
