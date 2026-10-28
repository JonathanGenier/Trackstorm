using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Fits the complete angled panel footing to static terrain, preserving projected heading.</summary>
internal sealed class ShieldGround
{
    private readonly Vector3[] _feet;
    private readonly Vector3[] _support;
    private readonly bool[] _supported;
    private readonly float _height;

    internal ShieldGround(Vector3 size)
    {
        _height = size.Y;
        var feet = new List<Vector3>();
        foreach (var corners in ShieldGeometry.DeployedCorners(VehicleBody.ToCore(size)))
        {
            // Corners 0, 4, 1 span each panel's bottom face, including the flared wings.
            var origin = VehicleBody.ToGodot(corners[0]);
            var across = VehicleBody.ToGodot(corners[4] - corners[0]);
            var depth = VehicleBody.ToGodot(corners[1] - corners[0]);
            int columns = Math.Max(1, (int)Math.Ceiling(across.Length() / .45f));
            int rows = Math.Max(1, (int)Math.Ceiling(depth.Length() / .45f));
            for (int x = 0; x <= columns; x++)
            for (int z = 0; z <= rows; z++)
            { feet.Add(origin + across * (x / (float)columns) + depth * (z / (float)rows)); }
        }
        _feet = feet.ToArray();
        _support = new Vector3[_feet.Length];
        _supported = new bool[_feet.Length];
    }

    internal Transform3D? Seat(PhysicsDirectSpaceState3D space, Vector3 center, Vector3 heading)
    {
        heading.Y = 0;
        if (heading.LengthSquared() < .01f) { return null; }
        heading = heading.Normalized();
        var basis = new Basis(Vector3.Up.Cross(heading), Vector3.Up, heading);
        using var ray = new PhysicsRayQueryParameters3D { CollisionMask = 1 };
        // Tilting changes the actual projected footprint. Re-sample it rather than
        // fitting unrotated probes and subsequently moving the feet off their support.
        for (int iteration = 0; iteration < 4; iteration++)
        {
            int count = 0;
            var mean = Vector3.Zero;
            for (int i = 0; i < _feet.Length; i++)
            {
                var offset = basis * _feet[i];
                var point = center + offset;
                ray.From = new(point.X, center.Y + _height / 2 + 2, point.Z);
                ray.To = new(point.X, center.Y - _height / 2 - 8, point.Z);
                using var hit = space.IntersectRay(ray);
                _supported[i] = hit.Count > 0 && hit["collider"].AsGodotObject() is StaticBody3D terrain &&
                    (terrain.IsInGroup("landing_terrain") || terrain is SurfaceBody) && hit["normal"].AsVector3().Y >= .55f;
                if (!_supported[i]) { continue; }
                _support[i] = hit["position"].AsVector3() - center;
                mean += _support[i]; count++;
            }
            // Permit a small overhang at a track edge, but never bridge a chasm or
            // balance the complete wall on one isolated wing or obstacle.
            if (count < _feet.Length * .75f) { return null; }
            mean /= count;
            double xx = 0, xz = 0, zz = 0, xy = 0, zy = 0;
            for (int i = 0; i < _feet.Length; i++)
            {
                if (!_supported[i]) { continue; }
                var p = _support[i] - mean;
                xx += p.X * p.X; xz += p.X * p.Z; zz += p.Z * p.Z;
                xy += p.X * p.Y; zy += p.Z * p.Y;
            }
            double determinant = xx * zz - xz * xz;
            if (determinant < .000001) { return null; }
            var slope = new Vector2((float)((xy * zz - zy * xz) / determinant), (float)((zy * xx - xy * xz) / determinant));
            // Forty degrees accommodates the banks while keeping a defensive wall upright.
            if (slope.Length() > Mathf.Tan(Mathf.DegToRad(40))) { return null; }
            var up = new Vector3(-slope.X, 1, -slope.Y).Normalized();
            var back = new Vector3(heading.X, -up.Dot(heading) / up.Y, heading.Z).Normalized();
            var fitted = new Basis(up.Cross(back).Normalized(), up, back);
            if (iteration < 3) { basis = fitted; continue; }
            // Use the footprint we actually queried. The small residual plane-fit
            // difference is handled by lifting to the highest foot, never burying it.
            float lift = float.NegativeInfinity;
            for (int i = 0; i < _feet.Length; i++)
            { if (_supported[i]) { lift = Math.Max(lift, _support[i].Y - (basis * _feet[i]).Y); } }
            float gaps = 0;
            for (int i = 0; i < _feet.Length; i++)
            { if (_supported[i]) { gaps += lift + (basis * _feet[i]).Y - _support[i].Y; } }
            if (gaps / count > .35f) { return null; }
            return new(basis, center + Vector3.Up * (lift + .02f));
        }
        return null;
    }
}
