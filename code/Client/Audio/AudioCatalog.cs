namespace Trackstorm.Client.Audio;

/// <summary>Explicit clip selection; all required clips are repository-tracked assets.</summary>
internal static class AudioCatalog
{
    /// <summary>Resolves an explicitly selected audio asset.</summary>
    /// <param name="cue">Selected presentation category.</param>
    /// <returns>The selected presentation value.</returns>
    internal static string Path(AudioCue cue) => "res://assets/audio/" + cue switch
    {
        AudioCue.EngineIdle => "freesound/vehicle/idle.wav",
        AudioCue.EngineLow => "freesound/vehicle/low.wav",
        AudioCue.EngineHigh => "freesound/vehicle/high.wav",
        AudioCue.Skid => "freesound/vehicle/skid.wav",
        AudioCue.Collision => "kenney/impact/impactMetal_light_000.ogg",
        AudioCue.HeavyCollision => "kenney/impact/impactMetal_heavy_000.ogg",
        AudioCue.Damage => "kenney/impact/impactMetal_medium_001.ogg",
        AudioCue.Destruction => "freesound/combat/destruction.wav",
        AudioCue.MissileFire => "freesound/combat/fire.wav",
        AudioCue.MissileTravel => "kenney/scifi/engineCircular_000.ogg",
        AudioCue.MissileImpact => "kenney/impact/impactMetal_medium_000.ogg",
        AudioCue.Explosion => "freesound/combat/explosion.wav",
        AudioCue.WeaponPickup => "kenney/scifi/forceField_000.ogg",
        AudioCue.WrenchPickup => "kenney/scifi/forceField_001.ogg",
        AudioCue.WrenchUse => "kenney/interface/confirmation_001.ogg",
        AudioCue.Kill => "kenney/interface/confirmation_002.ogg",
        AudioCue.Death => "freesound/combat/death.wav",
        AudioCue.Respawn => "kenney/scifi/forceField_002.ogg",
        AudioCue.Countdown => "kenney/interface/tick_001.ogg",
        AudioCue.MatchStart => "kenney/interface/confirmation_004.ogg",
        AudioCue.MatchEnd => "kenney/interface/confirmation_003.ogg",
        AudioCue.EndSting => "freesound/combat/end.wav",
        AudioCue.Ambience => "freesound/arena/ambience.wav",
        _ => throw new ArgumentOutOfRangeException(nameof(cue)),
    };

    /// <summary>Resolves one of the three project-provided arena tracks.</summary>
    /// <param name="index">Zero-based playlist position.</param>
    /// <returns>The selected presentation value.</returns>
    internal static string Song(int index) => "res://assets/audio/project/music/Welcome to the Carnage Circus Arena" + (index == 0 ? string.Empty : $" {index + 1}") + ".mp3";
}
