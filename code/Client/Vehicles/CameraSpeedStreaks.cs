using Godot;

namespace Trackstorm.Client.Vehicles;

/// <summary>Sparse peripheral motion below the HUD, with an untouched central driving/aim region.</summary>
internal sealed partial class CameraSpeedStreaks : Control
{
    private const int Count = 16;
    private readonly float[] _progress = new float[Count];
    private readonly float[] _length = new float[Count];
    private readonly float[] _rate = new float[Count];
    private readonly float[] _opacity = new float[Count];
    private readonly Vector2[] _edge = new Vector2[Count];
    private Random _random = new(226);
    private float _strength;

    public override void _Ready() => Reset();

    internal void Present(float delta, float speed, float strength, bool visible)
    {
        for (int i = 0; i < Count; i++)
        {
            _progress[i] += Math.Max(0, delta) * Math.Max(0, speed) * 0.025f * _rate[i];
            if (_progress[i] >= 1)
            {
                _progress[i] %= 1;
                Scatter(i);
            }
        }
        _strength = strength;
        Visible = visible && strength > 0.005f;
        if (Visible) { QueueRedraw(); }
    }

    internal void Reset()
    {
        _strength = 0;
        _random = new(226);
        for (int i = 0; i < Count; i++)
        {
            Scatter(i);
            _progress[i] = _random.NextSingle();
        }
    }

    private void Scatter(int i)
    {
        // Independently resample every passage: no mirrored pairs, rows, equal lengths,
        // or shared travel clock. Fixed seed makes native capture sequences reproducible.
        _edge[i] = new Vector2(_random.Next(2) == 0 ? 0.01f : 0.99f, 0.06f + _random.NextSingle() * 0.78f);
        _length[i] = 0.018f + MathF.Pow(_random.NextSingle(), 2) * 0.11f;
        _rate[i] = 0.65f + _random.NextSingle() * 0.9f;
        _opacity[i] = 0.07f + _random.NextSingle() * 0.07f;
    }

    public override void _Draw()
    {
        Vector2 size = GetViewportRect().Size;
        Vector2 center = size * new Vector2(0.5f, 0.44f);
        // Start in the middle-side bands, then travel offscreen. Even the longest
        // tail stays outside the central third reserved for driving/aiming.
        for (int i = 0; i < Count; i++)
        {
            float phase = _progress[i];
            Vector2 ray = _edge[i] * size - center;
            float radius = 0.48f + phase * 0.70f;
            Vector2 end = center + ray * radius;
            Vector2 start = center + ray * (radius - _length[i]);
            float alpha = MathF.Sin(phase * MathF.PI) * _opacity[i] * _strength;
            float width = Math.Max(1, size.Y / 720f);
            Vector2 middleStart = start.Lerp(end, 0.25f);
            Vector2 middleEnd = start.Lerp(end, 0.75f);
            DrawLine(start, middleStart, new Color(0.83f, 0.91f, 1, alpha * 0.25f), width, true);
            DrawLine(middleStart, middleEnd, new Color(0.83f, 0.91f, 1, alpha), width, true);
            DrawLine(middleEnd, end, new Color(0.83f, 0.91f, 1, alpha * 0.4f), width, true);
        }
    }
}
