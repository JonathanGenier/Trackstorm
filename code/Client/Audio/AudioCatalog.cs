namespace Trackstorm.Client.Audio;

/// <summary>Explicit clip selection; private Sonniss derivatives are produced by setup-audio.ps1.</summary>
internal static class AudioCatalog
{
    /// <summary>Resolves an explicitly selected audio asset.</summary>
    /// <param name="cue">Selected presentation category.</param>
    /// <returns>The selected presentation value.</returns>
    internal static string Path(AudioCue cue) => "res://assets/audio/" + cue switch
    {
        AudioCue.EngineIdle => "sonniss/vehicle/idle.wav",
        AudioCue.EngineLow => "sonniss/vehicle/low.wav",
        AudioCue.EngineHigh => "sonniss/vehicle/high.wav",
        AudioCue.Skid => "sonniss/vehicle/skid.wav",
        AudioCue.Collision => "kenney/impact/impactMetal_light_000.ogg",
        AudioCue.HeavyCollision => "kenney/impact/impactMetal_heavy_000.ogg",
        AudioCue.Damage => "kenney/impact/impactMetal_medium_001.ogg",
        AudioCue.Destruction => "sonniss/combat/destruction.wav",
        AudioCue.MissileFire => "sonniss/combat/fire.wav",
        AudioCue.MissileTravel => "kenney/scifi/engineCircular_000.ogg",
        AudioCue.MissileImpact => "kenney/impact/impactMetal_medium_000.ogg",
        AudioCue.Explosion => "sonniss/combat/explosion.wav",
        AudioCue.WeaponPickup => "kenney/scifi/forceField_000.ogg",
        AudioCue.WrenchPickup => "kenney/scifi/forceField_001.ogg",
        AudioCue.WrenchUse => "kenney/interface/confirmation_001.ogg",
        AudioCue.Kill => "kenney/interface/confirmation_002.ogg",
        AudioCue.Death => "sonniss/combat/death.wav",
        AudioCue.Respawn => "kenney/scifi/forceField_002.ogg",
        AudioCue.Countdown => "kenney/interface/tick_001.ogg",
        AudioCue.MatchStart => "kenney/interface/confirmation_004.ogg",
        AudioCue.MatchEnd => "kenney/interface/confirmation_003.ogg",
        AudioCue.EndSting => "sonniss/combat/end.wav",
        AudioCue.Ambience => "sonniss/arena/ambience.wav",
        _ => throw new ArgumentOutOfRangeException(nameof(cue)),
    };

    /// <summary>Resolves one of the three project-provided arena tracks.</summary>
    /// <param name="index">Zero-based playlist position.</param>
    /// <returns>The selected presentation value.</returns>
    internal static string Song(int index) => "res://assets/audio/project/music/Welcome to the Carnage Circus Arena" + (index == 0 ? string.Empty : $" {index + 1}") + ".mp3";
}
