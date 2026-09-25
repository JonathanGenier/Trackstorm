namespace Trackstorm.Client.Audio;

/// <summary>Pure routing and gain conversion shared by native playback and existing settings.</summary>
internal static class AudioRouting
{
    /// <summary>Maps each sound category to its settings-controlled bus.</summary>
    /// <param name="cue">Selected presentation category.</param>
    /// <returns>The selected presentation value.</returns>
    internal static string Bus(AudioCue cue) => cue switch
    {
        AudioCue.EngineIdle or AudioCue.EngineLow or AudioCue.EngineHigh or AudioCue.Skid or
            AudioCue.Collision or AudioCue.HeavyCollision or AudioCue.Damage or AudioCue.Destruction => "Vehicle",
        AudioCue.MachineGunFire or AudioCue.MissileFire or AudioCue.MissileTravel or AudioCue.MissileImpact or AudioCue.Explosion => "Weapons",
        AudioCue.Ambience => "SFX",
        _ => "UI",
    };

    /// <summary>Clamps finite linear gain and restores the settings default for invalid data.</summary>
    /// <param name="value">Linear settings gain.</param>
    /// <returns>The selected presentation value.</returns>
    internal static float Gain(double value) => double.IsFinite(value) ? (float)Math.Clamp(value, 0, 1) : 1;

    /// <summary>Converts linear gain to decibels with an explicit mute floor.</summary>
    /// <param name="value">Linear settings gain.</param>
    /// <returns>The selected presentation value.</returns>
    internal static float Decibels(double value) => Gain(value) == 0 ? -80 : 20 * MathF.Log10(Gain(value));
}
