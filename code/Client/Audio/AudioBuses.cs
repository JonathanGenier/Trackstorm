using Godot;

namespace Trackstorm.Client.Audio;

/// <summary>Idempotent routing setup; existing settings remain the only volume owner.</summary>
internal static class AudioBuses
{
    /// <summary>Creates missing buses and restores hierarchy without changing saved gains.</summary>
    internal static void Ensure()
    {
        foreach (string name in new[] { "Music", "SFX", "Vehicle", "Weapons", "UI" })
        {
            int index = AudioServer.GetBusIndex(name);
            if (index < 0)
            {
                AudioServer.AddBus();
                index = AudioServer.BusCount - 1;
                AudioServer.SetBusName(index, name);
            }

            AudioServer.SetBusSend(index, name is "Music" or "SFX" ? "Master" : "SFX");
        }
    }
}
