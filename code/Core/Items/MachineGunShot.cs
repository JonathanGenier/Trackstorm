using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>Deterministic short-range ray construction and damage falloff; no independent projectile authority.</summary>
internal static class MachineGunShot
{
    internal static Vector3 Direction(ulong token, int ordinal, Quaternion orientation, float spreadDegrees)
    {
        // Stateless sampling keeps spread identical after checkpoint replacement and independent of loot RNG.
        ulong seed = unchecked(token * 0x9E3779B97F4A7C15UL + (ulong)ordinal);
        double Sample()
        {
            seed = unchecked(seed + 0x9E3779B97F4A7C15UL);
            ulong value = seed;
            value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
            value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
            return ((value ^ (value >> 31)) >> 11) * (1.0 / 9007199254740992.0);
        }
        double radius = Math.Sqrt(Sample()) * Math.Tan(spreadDegrees * Math.PI / 180);
        double angle = Sample() * Math.Tau;
        return Vector3.Normalize(Vector3.Transform(new Vector3((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)), -1), orientation));
    }

    internal static float Falloff(float distance, ItemConfiguration config) => distance >= config.MachineGunRange ? 0 :
        MathF.Pow(1 - Math.Clamp((distance - config.MachineGunFalloffStart) / (config.MachineGunRange - config.MachineGunFalloffStart), 0, 1), config.MachineGunFalloff);
}
