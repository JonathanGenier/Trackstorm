using Godot;

namespace Trackstorm.Client.Vehicles;

/// <summary>Read-only world queries constrain the rendered camera; no body is moved or given impulses.</summary>
internal sealed class CameraObstruction : IDisposable
{
    private const float Skin = 0.03f;
    private readonly SphereShape3D _shape = new();
    private readonly PhysicsShapeQueryParameters3D _query = new() { CollisionMask = 1, Margin = 0, CollideWithAreas = false };
    private float _shortening;
    private float _releaseDelay;
    private Vector3 _previousPivot;
    private Vector3 _previousIntent;
    private float _lift;

    internal float Lift => _lift;

    internal Vector3 Resolve(PhysicsDirectSpaceState3D space, Vector3 pivot, Vector3 desired, Vector3 intent, float radius, Rid followedBody, float delta, bool reset)
    {
        _shape.Radius = radius;
        _query.Shape = _shape;
        _query.Exclude = followedBody.IsValid ? new Godot.Collections.Array<Rid> { followedBody } : new();
        // A short bounded look-ahead starts contraction before a fast orbit reaches a corner.
        // Use unshaken intent: collision feedback must not drive anticipatory zoom pulses.
        Vector3 previewPivot = pivot;
        Vector3 previewEnd = intent;
        if (!reset && delta > 0)
        {
            previewPivot += ((pivot - _previousPivot) * (0.2f / delta)).LimitLength(3);
            previewEnd += ((intent - _previousIntent) * (0.2f / delta)).LimitLength(5);
        }
        _previousPivot = pivot;
        _previousIntent = intent;
        float baseAllowed = AllowedDistance(space, ClearPivot(space, pivot), desired);
        // In a cramped view, a small raised pivot keeps the camera above the chassis.
        // Derive this from the unraised boom so clearing the wall cannot toggle the lift.
        float desiredLift = Math.Clamp((5.5f - baseAllowed) / 3, 0, 1) * 1.8f;
        _lift = reset ? desiredLift : Mathf.Lerp(_lift, desiredLift, ChaseCameraMotion.Blend(desiredLift > _lift ? 12 : 5, delta));
        if (_lift > 0.001f)
        {
            Vector3 clearPivot = ClearPivot(space, pivot);
            _lift = Math.Min(_lift, AllowedDistance(space, clearPivot, clearPivot + Vector3.Up * _lift));
        }
        pivot = ClearPivot(space, pivot + Vector3.Up * _lift);
        desired += Vector3.Up * _lift;
        previewPivot += Vector3.Up * _lift;
        previewEnd += Vector3.Up * _lift;

        Vector3 motion = desired - pivot;
        float length = motion.Length();
        if (length < 0.001f) return pivot;
        // Retain the original target-to-camera constraint even when raised. Otherwise a
        // low wall could put the camera on its far side and hide the car behind the wall.
        float allowed = Math.Min(baseAllowed, AllowedDistance(space, pivot, desired));
        float shortening = length - allowed;
        float previewLength = previewPivot.DistanceTo(previewEnd);
        float previewAllowed = reset ? previewLength : AllowedDistance(space, previewPivot, previewEnd);
        // Anticipation may soften a corner entry, but only current geometry may demand
        // a view closer than the vehicle's length. This avoids prediction burying the lens.
        float anticipated = Math.Max(shortening, Math.Min(Math.Max(0, length - 4.8f), previewLength - previewAllowed));
        if (reset)
        {
            _shortening = shortening;
            _releaseDelay = 0.12f;
        }
        else if (anticipated > _shortening)
        {
            // Safety takes precedence over easing through a newly encountered solid.
            _shortening = Math.Max(shortening, Mathf.Lerp(_shortening, anticipated, ChaseCameraMotion.Blend(12, delta)));
            _releaseDelay = 0.12f;
        }
        else
        {
            _releaseDelay = Math.Max(0, _releaseDelay - delta);
            if (_releaseDelay == 0) _shortening = Mathf.Lerp(_shortening, shortening, ChaseCameraMotion.Blend(5, delta));
        }
        return pivot + motion / length * Math.Max(0, length - _shortening);
    }

    private Vector3 ClearPivot(PhysicsDirectSpaceState3D space, Vector3 pivot)
    {
        // CastMotion ignores initial overlaps. Resolve a cramped/overturned target first,
        // using native contact separation, rather than casting through that collider.
        for (int attempt = 0; attempt < 4; attempt++)
        {
            _query.Transform = new Transform3D(Basis.Identity, pivot);
            _query.Motion = Vector3.Zero;
            var contacts = space.CollideShape(_query, 16);
            Vector3 separation = Vector3.Zero;
            for (int i = 0; i + 1 < contacts.Count; i += 2)
            {
                Vector3 push = contacts[i + 1] - contacts[i];
                if (push.LengthSquared() > separation.LengthSquared()) separation = push;
            }
            if (separation.LengthSquared() < 0.000001f) break;
            pivot += separation + separation.Normalized() * Skin;
        }

        return pivot;
    }

    private float AllowedDistance(PhysicsDirectSpaceState3D space, Vector3 pivot, Vector3 desired)
    {
        _query.Transform = new Transform3D(Basis.Identity, pivot);
        _query.Motion = desired - pivot;
        float length = _query.Motion.Length();
        float fraction = space.CastMotion(_query)[0];
        return fraction < 1 ? Math.Max(0, length * fraction - Skin) : length;
    }

    public void Dispose()
    {
        _query.Dispose();
        _shape.Dispose();
    }
}
