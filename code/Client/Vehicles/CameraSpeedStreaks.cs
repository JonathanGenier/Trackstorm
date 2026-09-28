using Godot;

namespace Trackstorm.Client.Vehicles;

/// <summary>Sparse peripheral motion below the HUD, with an untouched central driving/aim region.</summary>
internal sealed partial class CameraSpeedStreaks : Control
{
    private float _travel;
    private float _strength;

    internal void Present(float delta, float speed, float strength, bool visible)
    {
        _travel = (_travel + Math.Max(0, delta) * Math.Max(0, speed) * 0.018f) % 1;
        _strength = strength;
        Visible = visible && strength > 0.005f;
        if (Visible) { QueueRedraw(); }
    }

    internal void Reset() => _travel = _strength = 0;

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        Vector2 center = size * new Vector2(0.5f, 0.44f);
        // A fixed pool, deterministic phases, and speed-driven travel avoid per-use allocations
        // and a repeating ignition animation. Only the outer side margins receive streaks.
        for (int i = 0; i < 22; i++)
        {
            float phase = (_travel + i * 0.618034f) % 1;
            float side = i % 2 == 0 ? -1 : 1;
            float y = 0.10f + ((i * 0.381966f) % 1) * 0.72f;
            Vector2 edge = new(size.X * (side < 0 ? 0.01f : 0.99f), size.Y * y);
            Vector2 ray = edge - center;
            float radius = 0.80f + phase * 0.27f;
            Vector2 end = center + ray * radius;
            Vector2 start = center + ray * (radius - 0.045f - 0.075f * _strength);
            float alpha = MathF.Sin(phase * MathF.PI) * 0.24f * _strength;
            DrawLine(start, end, new Color(0.78f, 0.9f, 1, alpha), Math.Max(1, size.Y / 720f), true);
        }
    }
}
