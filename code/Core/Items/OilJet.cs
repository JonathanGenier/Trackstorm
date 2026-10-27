using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>Chassis-rearward ejection; the mouth may move, but the spring never steers aim.</summary>
public static class OilJet
{
    /// <summary>Vehicle-relative initial velocity for a bounded ballistic parcel.</summary>
    public static Vector3 Velocity(Vector3 mouth, float speed, Vector3 gravity, int ordinal, float travel)
    {
        float phase = ordinal * 2.399963f;
        float turbulence = .2f + .8f * Math.Min(1, travel / 18);
        float lateral = .05f + .95f * Math.Min(1, travel / 16);
        Vector3 velocity = Vector3.Normalize(new Vector3(.32f * MathF.Sin(phase) * lateral,
            -1.45f + .09f * MathF.Cos(phase) * turbulence, 1)) * speed;
        // Clear the unchanged box and rear body, including a liquid-envelope
        // margin. Solve the ballistic height at each exit plane, rather than
        // turning the nozzle with the head or moving its origin off the mouth.
        Clear(2.39f, 2.25f);
        Clear(2.65f, 1.50f);
        Clear(3.05f, .75f);
        return velocity;

        void Clear(float rear, float height)
        {
            float distance = rear - mouth.Z;
            if (distance <= 0) { return; }
            // Even at deliberately slow/high-gravity developer settings, the
            // jet must reach the rear plane before gravity reverses its motion.
            velocity.Z = Math.Max(velocity.Z, MathF.Sqrt(Math.Max(0, -2 * gravity.Z * distance)) + .5f);
            float endSpeed = MathF.Sqrt(velocity.Z * velocity.Z + 2 * gravity.Z * distance);
            float time = 2 * distance / (velocity.Z + endSpeed);
            velocity.Y = Math.Max(velocity.Y, (height - mouth.Y - .5f * gravity.Y * time * time) / time);
            if (gravity.Y > 0)
            { velocity.Y = Math.Max(velocity.Y, -MathF.Sqrt(2 * gravity.Y * Math.Max(0, mouth.Y - height))); }
        }
    }
}
