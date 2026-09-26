using System.Numerics;

namespace Trackstorm.Core.Arenas;

/// <summary>Authored closed X/Z perimeter and below-map fail-safe; never physical containment.</summary>
public sealed class ArenaBoundary
{
    private readonly Vector2[] _points;

    /// <summary>Copies the ordered outside edge of the visible perimeter.</summary>
    public ArenaBoundary(IEnumerable<Vector2> points, float minimumHeight)
    {
        _points = points.ToArray();
        if (_points.Length is < 3 or > 4096 || !float.IsFinite(minimumHeight) ||
            _points.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y)))
        { throw new ArgumentException("Boundary requires finite ordered points and floor."); }
        double area = 0;
        for (int i = 0; i < _points.Length; i++)
        {
            Vector2 a = _points[i], b = _points[(i + 1) % _points.Length];
            if (Vector2.DistanceSquared(a, b) < 0.000001f) { throw new ArgumentException("Duplicate perimeter vertex."); }
            area += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        if (Math.Abs(area) < 0.01) { throw new ArgumentException("Degenerate perimeter."); }
        MinimumHeight = minimumHeight;
    }

    /// <summary>Below-map height, independent of horizontal exterior distance.</summary>
    public float MinimumHeight { get; }

    /// <summary>Inclusive polygon test without a ceiling or a world-origin radius approximation.</summary>
    public bool Contains(Vector3 position)
    {
        if (!Vehicles.VehiclePhysicsState.IsFinite(position) || position.Y < MinimumHeight) { return false; }
        var point = new Vector2(position.X, position.Z);
        bool inside = false;
        for (int i = 0, j = _points.Length - 1; i < _points.Length; j = i++)
        {
            Vector2 a = _points[j], b = _points[i], edge = b - a;
            float t = Math.Clamp(Vector2.Dot(point - a, edge) / edge.LengthSquared(), 0, 1);
            if (Vector2.DistanceSquared(point, a + t * edge) <= 0.000001f) { return true; }
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (double)(b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) { inside = !inside; }
        }
        return inside;
    }
}
