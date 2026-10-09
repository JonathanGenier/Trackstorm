using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Networking;

internal sealed partial class NetworkVehicleArena
{
    private readonly WeaponAimOverlay _aimOverlay = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private ulong _aimTarget;
    private float _aimTargetAngle;
    private Vector2 _aimTargetOffset;
    private ulong _aimTargetLife;
    private (ulong Life, ulong Token, ulong Selection) _aimCapability;
    private float _aimReleaseSeconds;
    private bool _aimController;
    private bool _aimAdjusted;
    private Vector2 _aimBearing;
    private ulong _aimDismissed;
    private ulong _aimDismissedLife;
    private ulong _aimIntentRevision;
    private float _aimRecoverySeconds;
    private Vector2 _aimPlacement;
    private System.Numerics.Vector2 _aimGesture;
    internal ulong AssistedCar => StickyAiming ? _aimTarget : 0;
    private bool StickyAiming => _driver.LocalItem?.Active.Item == HeldItem.MachineGun;
    internal WeaponAimOverlay AimOverlay => _aimOverlay;
    internal float AimRecoverySeconds => _aimRecoverySeconds;
    internal Action<string>? AimReleaseObserved { get; set; }
    internal string AimTargetDiagnostics(ulong vehicle)
    {
        Vector3 center = AimBodyCenter(_bodies[vehicle]);
        var trace = TraceViewedAim(_camera.GlobalPosition, center);
        return $"angle={(-_camera.GlobalBasis.Z).AngleTo(center - _camera.GlobalPosition):F5} sight={trace.Car?.VehicleId == vehicle} hit={trace.Point} lens={_camera.GlobalPosition} target={center} accepted={_driver.AcceptedAims.Count} active={CameraInput?.CameraAimActive} dismissed={_aimDismissed}";
    }

    private void ReleaseAim(string reason)
    {
        if (_aimTarget != 0) { AimReleaseObserved?.Invoke($"target={_aimTarget} reason={reason}"); }
        _aimTarget = 0;
        _aimRecoverySeconds = 0;
    }

    private void InitializeAiming()
    {
        var layer = new CanvasLayer { Layer = 1 };
        AddChild(layer);
        layer.AddChild(_aimOverlay);
        _aimOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _camera.AimFriction = AimFriction;
        _camera.AimAttraction = AimAttraction;
        _camera.AimTracking = TrackAim;
        _camera.AimReset = () => ResetAiming("camera presentation reset");
    }

    private void PrepareAiming()
    {
        bool enabled = _driver.IsActive && _driver.AllowsParticipation && _driver.LocalState is { CanInteract: true } &&
            _driver.LocalItem is { } slot && WeaponAim.Supports(slot.Active.Item) && CameraInput?.CameraEnabled == true;
        _camera.WeaponAiming = enabled;
        if (!enabled) { ResetAiming(); return; }
        var current = _driver.LocalItem!;
        var capability = (current.Life, current.Active.Token, current.SelectionRevision);
        if (_aimCapability != capability) { ReleaseAim("capability changed"); _aimDismissed = 0; _aimReleaseSeconds = 0; _aimCapability = capability; }
        if (CameraInput is { } input && (_aimIntentRevision != input.CameraAimRevision || !input.CameraAimActive))
        {
            ResetAssistance($"input boundary revision={_aimIntentRevision}->{input.CameraAimRevision} active={input.CameraAimActive}");
            _aimIntentRevision = input.CameraAimRevision;
        }
        if (StickyAiming) { ValidateStickyTarget(); }
        else { SelectAimTarget(); }
    }

    private void ResetAiming() => ResetAiming("aim disabled/lifecycle");

    private void ResetAiming(string reason)
    {
        _driver.DesiredAim = null;
        _camera.WeaponAiming = false;
        _aimCapability = default;
        ResetAssistance(reason);
        _aimOverlay.Reset();
    }

    private void ResetAssistance(string reason = "aim intent inactive")
    {
        ReleaseAim(reason);
        _aimTargetLife = 0;
        _aimReleaseSeconds = 0;
        _aimController = false;
        _aimDismissed = 0;
        _aimAdjusted = false;
        _aimBearing = default;
        _aimPlacement = default;
        _aimGesture = default;
    }

    private void PresentAiming(float delta)
    {
        foreach (var pair in _bodies)
        {
            var state = _driver.Latest?.Vehicles.FirstOrDefault(value => value.State.VehicleId == pair.Key)?.State;
            var inventory = (_driver.Host?.Items.Slots ?? _driver.ItemState?.Slots)?.FirstOrDefault(value => value.Vehicle == pair.Key);
            var aim = _driver.AcceptedAims.FirstOrDefault(value => value.Vehicle == pair.Key && value.Life == state?.LifeId && value.Token == inventory?.Active.Token);
            pair.Value.Rack.ObserveAim(state is { CanInteract: true } && inventory is not null && WeaponAim.Supports(inventory.Active.Item) ? aim : null);
        }
        if (!_camera.WeaponAiming || _driver.LocalState is not { } local || _driver.LocalItem is not { } slot) { return; }
        Vector2 center = _camera.GetViewport().GetVisibleRect().GetCenter();
        Vector3 lens = _camera.ProjectRayOrigin(center);
        Vector3 rayEnd = lens + _camera.ProjectRayNormal(center) * 300;
        // Free aim always resolves the actual centred ray. Assistance may acquire nearby cars.
        var hit = TraceViewedAim(lens, rayEnd);
        if (StickyAiming)
        {
            ValidateStickyTarget(delta);
            if (CameraInput?.CameraAimActive == true && _aimTarget == 0 && _aimReleaseSeconds <= 0 && _driver.AcceptedAims.Any(a => a.Vehicle == local.VehicleId && a.Life == local.LifeId && a.Token == slot.Active.Token) && FindAcquisition(lens) is { } acquired)
            {
                _aimTarget = acquired.VehicleId;
                _aimTargetLife = _driver.Latest!.Vehicles.Single(v => v.State.VehicleId == _aimTarget).State.LifeId;
                _aimAdjusted = false;
                _aimPlacement = default;
                _aimGesture = default;
                _aimRecoverySeconds = 0;
                _aimBearing = AimBearing(AimBodyCenter(acquired) - lens);
                ValidateStickyTarget();
            }
        }
        NetworkVehicleBody? framed = StickyAiming ? (_aimTarget != 0 ? _bodies[_aimTarget] : null) : hit.Car;
        Vector3 desiredPoint = hit.Point;
        if (StickyAiming && framed is not null)
        {
            // Retained assistance owns shot placement until target loss; camera look
            // remains free to depart without moving shots away before disengagement.
            desiredPoint = AimBodyCenter(framed);
        }
        Vector3 origin = VehicleBody.ToGodot(local.ObservedPhysics.Position + System.Numerics.Vector3.Transform(WeaponAim.Pivot, local.ObservedPhysics.Orientation));
        if (origin.DistanceSquaredTo(desiredPoint) > .001f) { _driver.DesiredAim = VehicleBody.ToCore((desiredPoint - origin).Normalized()); }
        var accepted = _driver.AcceptedAims.FirstOrDefault(value => value.Vehicle == local.VehicleId && value.Life == local.LifeId && value.Token == slot.Active.Token);
        Rect2? bounds = null;
        if (framed is { } target)
        {
            bounds = ProjectAimBounds(target);
        }
        _aimOverlay.Present(center, accepted?.Ready == true, bounds);
    }

    private (Vector3 Point, NetworkVehicleBody? Car) TraceViewedAim(Vector3 from, Vector3 to)
    {
        using var coverQuery = PhysicsRayQueryParameters3D.Create(from, to, 1);
        coverQuery.HitFromInside = true;
        using var cover = GetWorld3D().DirectSpaceState.IntersectRay(coverQuery);
        Vector3 point = cover.Count == 0 ? to : cover["position"].AsVector3();
        NetworkVehicleBody? selected = null;
        foreach (var pair in _bodies)
        {
            var state = _driver.Latest?.Vehicles.FirstOrDefault(value => value.State.VehicleId == pair.Key)?.State;
            if (pair.Key == _driver.LocalVehicleId || state is not { CanInteract: true } || !pair.Value.IsPresented) { continue; }
            // Query the native chassis hull at its displayed pose without moving the
            // authoritative collider. Remote interpolation and local smoothing can
            // otherwise put brackets on a car outside the camera's center ray.
            Transform3D toCollision = pair.Value.GlobalTransform * pair.Value.VisualTransform.AffineInverse();
            var excluded = new Godot.Collections.Array<Rid>();
            foreach (var other in _bodies)
            {
                if (other.Key != pair.Key) { excluded.Add(other.Value.GetRid()); }
            }
            using var carQuery = PhysicsRayQueryParameters3D.Create(toCollision * from, toCollision * point, 2, excluded);
            carQuery.HitFromInside = true;
            using var carHit = GetWorld3D().DirectSpaceState.IntersectRay(carQuery);
            if (carHit.Count == 0 || carHit["collider"].AsGodotObject() != pair.Value) { continue; }
            point = toCollision.AffineInverse() * carHit["position"].AsVector3();
            selected = pair.Value;
        }
        return (point, selected);
    }

    private Rect2? ProjectAimBounds(NetworkVehicleBody target)
    {
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        foreach (float x in new[] { -1.5f, 1.5f })
        foreach (float y in new[] { -.7f, 1.3f })
        foreach (float z in new[] { -2.6f, 2.6f })
        {
            Vector3 corner = target.VisualTransform * new Vector3(x, y, z);
            if (_camera.ToLocal(corner).Z > -_camera.Near) { continue; }
            Vector2 screen = _camera.UnprojectPosition(corner);
            min = min.Min(screen); max = max.Max(screen);
        }
        if (max.X <= min.X || max.Y <= min.Y) { return null; }
        // Nearby cars can extend outside the view. Keep their corner strokes onscreen.
        Rect2 bounds = new Rect2(min, max - min).Intersection(_camera.GetViewport().GetVisibleRect().Grow(-3));
        return bounds.HasArea() ? bounds : null;
    }

    private static Vector3 AimBodyCenter(NetworkVehicleBody body) => body.VisualTransform * new Vector3(0, .3f, 0);

    private static Vector2 AimBearing(Vector3 direction) => new(MathF.Atan2(direction.X, -direction.Z),
        -MathF.Atan2(direction.Y, new Vector2(direction.X, direction.Z).Length()));

    // The existing projected body envelope is shared, but only this selection metric
    // receives a margin. Rendered corners, native collision and spread stay unchanged.
    private float AimFrameGap(Rect2 frame)
    {
        Vector2 cursor = _camera.GetViewport().GetVisibleRect().GetCenter();
        Vector2 nearest = cursor.Clamp(frame.Position, frame.End);
        return _camera.ProjectRayNormal(cursor).AngleTo(_camera.ProjectRayNormal(nearest));
    }

    private float AcquisitionMargin(Rect2 frame, float cone)
    {
        float apparentSize = _camera.ProjectRayNormal(frame.Position).AngleTo(_camera.ProjectRayNormal(frame.End)) * .5f;
        return CameraAimAttraction.AcquisitionMargin(apparentSize, cone);
    }

    private NetworkVehicleBody? FindAcquisition(Vector3 lens)
    {
        float cone = _driver.Configuration.Configuration.Items.Aim.AssistDegrees * MathF.PI / 180;
        if (cone <= 0) { return null; }
        NetworkVehicleBody? selected = null;
        float best = float.MaxValue;
        foreach (var pair in _bodies)
        {
            var state = _driver.Latest?.Vehicles.FirstOrDefault(v => v.State.VehicleId == pair.Key)?.State;
            Vector3 point = AimBodyCenter(pair.Value), to = point - lens;
            if (pair.Key == _driver.LocalVehicleId || state is not { CanInteract: true } || !pair.Value.IsPresented ||
                to.LengthSquared() > 90000 || _camera.IsPositionBehind(point) ||
                !_camera.GetViewport().GetVisibleRect().HasPoint(_camera.UnprojectPosition(point))) { continue; }
            float angle = (-_camera.GlobalBasis.Z).AngleTo(to);
            if (ProjectAimBounds(pair.Value) is not { } frame) { continue; }
            float gap = AimFrameGap(frame);
            float allowance = AcquisitionMargin(frame, cone);
            if (pair.Key == _aimDismissed)
            {
                if (state.LifeId != _aimDismissedLife || gap > allowance) { _aimDismissed = 0; }
                else { continue; }
            }
            float score = gap + angle * .01f;
            if (gap > allowance || score >= best || !HasAimSight(pair.Key, lens, point)) { continue; }
            best = score; selected = pair.Value;
        }
        return selected;
    }

    private void ValidateStickyTarget(float delta = 0, bool finalView = false)
    {
        if (CameraInput?.CameraAimActive != true) { ResetAssistance(); return; }
        if (_aimTarget == 0) { return; }
        if (!_driver.AcceptedAims.Any(a => a.Vehicle == _driver.LocalVehicleId && a.Life == _aimCapability.Life && a.Token == _aimCapability.Token))
        { ReleaseAim("accepted aim expired"); return; }
        var state = _driver.Latest?.Vehicles.FirstOrDefault(v => v.State.VehicleId == _aimTarget)?.State;
        float cone = _driver.Configuration.Configuration.Items.Aim.AssistDegrees * MathF.PI / 180;
        if (cone <= 0 || state is not { CanInteract: true } || state.LifeId != _aimTargetLife ||
            !_bodies.TryGetValue(_aimTarget, out var body) || !body.IsPresented)
        { ReleaseAim("eligibility/life/configuration"); return; }
        // Before follow, the lens is from the previous render while bodies are new.
        // Only validate spatial visibility against this frame's resolved camera.
        if (delta <= 0 && !finalView) { return; }
        Vector3 center = AimBodyCenter(body);
        Vector3 to = center - _camera.GlobalPosition;
        Vector2 viewportCenter = _camera.GetViewport().GetVisibleRect().GetCenter();
        if (_camera.IsPositionBehind(center) || !_camera.GetViewport().GetVisibleRect().HasPoint(_camera.UnprojectPosition(center)))
        { ReleaseAim("offscreen"); return; }
        if (to.LengthSquared() > 90000) { ReleaseAim("range"); return; }
        if (!HasAimSight(_aimTarget, _camera.GlobalPosition, center)) { ReleaseAim("occluded"); return; }
        if (ProjectAimBounds(body) is not { } frame) { ReleaseAim("offscreen body"); return; }
        float gap = AimFrameGap(frame);
        // Angular framing is evaluated once, after this frame's follow/compensation.
        // Visibility and authority checks above remain immediate at both phases.
        if (delta > 0)
        {
            _aimRecoverySeconds = gap <= cone ? 0 : _aimRecoverySeconds + delta;
            if (gap > Math.Min(cone * 3, Mathf.DegToRad(20)) || _aimRecoverySeconds > .45f)
            { ReleaseAim($"angular recovery exhausted gap={gap:F5} seconds={_aimRecoverySeconds:F3}"); return; }
        }
        _aimTargetAngle = gap;
        _aimTargetOffset = _camera.UnprojectPosition(center) - viewportCenter;
    }

    private (Vector2 Pull, bool Engaged) AimAttraction(Vector2 mouse, Vector2 stick, Vector2 lookInput, float delta)
    {
        _aimReleaseSeconds = Math.Max(0, _aimReleaseSeconds - delta);
        if (!StickyAiming || CameraInput?.CameraAimActive != true || _driver.Configuration.Configuration.Items.Aim.AssistDegrees <= 0) { return (Vector2.Zero, false); }
        if (stick.LengthSquared() > .001f) { _aimController = true; }
        if (mouse.LengthSquared() > .001f) { _aimController = false; }
        _aimGesture = CameraAimAttraction.Gesture(_aimGesture, new(mouse.X, mouse.Y), new(stick.X, stick.Y), delta);
        if (_aimTarget == 0)
        {
            // A gentle deliberate exit stays free even if the player stops beside the
            // car. Moving back toward it, or leaving its acquisition region, rearms it.
            if (_aimDismissed != 0 && _bodies.TryGetValue(_aimDismissed, out var dismissed))
            {
                Vector2 toward = AimBearing(_camera.GlobalBasis.Inverse() * (AimBodyCenter(dismissed) - _camera.GlobalPosition));
                if (mouse.Dot(toward) > .00001f || stick.Dot(toward) > .00001f) { _aimDismissed = 0; }
            }
            // A continuing sweep must not pulse the brackets back on while crossing
            // the car slowly at low look sensitivity. Acquire again after input settles.
            if (_aimGesture.Length() > .045f) { _aimReleaseSeconds = .18f; }
            return (Vector2.Zero, false);
        }
        Vector3 local = _camera.GlobalBasis.Inverse() * (AimBodyCenter(_bodies[_aimTarget]) - _camera.GlobalPosition);
        var error = new System.Numerics.Vector2(MathF.Atan2(local.X, -local.Z), -MathF.Atan2(local.Y, new Vector2(local.X, local.Z).Length()));
        Vector2 bearing = AimBearing(AimBodyCenter(_bodies[_aimTarget]) - _camera.GlobalPosition);
        var motion = CameraAimAttraction.Motion(new(_aimBearing.X, _aimBearing.Y), new(bearing.X, bearing.Y), delta);
        bool fineInput = mouse.LengthSquared() > .25f || stick.LengthSquared() > .0225f;
        // Deliberate departure is measured in accumulated look space, independent
        // of the tiny distant frame and of involuntary camera/vehicle displacement.
        Vector2 intended = (_aimAdjusted ? _aimPlacement : new(error.X - motion.X, error.Y - motion.Y)) - lookInput;
        float cone = _driver.Configuration.Configuration.Items.Aim.AssistDegrees * MathF.PI / 180;
        if (CameraAimAttraction.Breakaway(new(intended.X, intended.Y), _aimGesture, new(lookInput.X, lookInput.Y), cone))
        { _aimDismissed = _aimTarget; _aimDismissedLife = _aimTargetLife; ReleaseAim("deliberate accumulated look"); _aimReleaseSeconds = .18f; return (Vector2.Zero, false); }
        // After an intentional fine adjustment, transport the selected offset with the
        // target instead of repeatedly pulling it back to centre. Actual input stays direct.
        // Approaching the car with small input must not cancel acquisition pull.
        // Preserve intentional placement once the cursor is on the body frame.
        if (!_aimAdjusted && fineInput && ProjectAimBounds(_bodies[_aimTarget]) is { } adjustedFrame && adjustedFrame.HasPoint(_camera.GetViewport().GetVisibleRect().GetCenter()))
        { _aimAdjusted = true; _aimPlacement = new(error.X - motion.X, error.Y - motion.Y); }
        if (_aimAdjusted) { _aimPlacement -= lookInput; }
        return (Vector2.Zero, true);
    }

    private Vector2 TrackAim(float delta)
    {
        if (!StickyAiming) { return Vector2.Zero; }
        ValidateStickyTarget(finalView: true);
        if (_aimTarget == 0) { return Vector2.Zero; }
        Vector3 direction = AimBodyCenter(_bodies[_aimTarget]) - _camera.GlobalPosition;
        Vector2 local = AimBearing(_camera.GlobalBasis.Inverse() * direction);
        var error = new System.Numerics.Vector2(local.X, local.Y);
        Vector2 bearing = AimBearing(direction);
        var motion = CameraAimAttraction.Motion(new(_aimBearing.X, _aimBearing.Y), new(bearing.X, bearing.Y), delta);
        // Keep unapplied bearing motion for the next frame instead of discarding a jolt
        // exceeding the existing 180-degree/s compensation cap.
        _aimBearing += new Vector2(motion.X, motion.Y);
        var tuning = _driver.Configuration.Configuration.Items.Aim;
        float strength = _aimController ? tuning.StickPull : tuning.MousePull;
        var pull = strength <= 0 ? System.Numerics.Vector2.Zero : motion +
            CameraAimAttraction.Pull(error - motion - new System.Numerics.Vector2(_aimPlacement.X, _aimPlacement.Y), strength, delta);
        return new(pull.X, pull.Y);
    }

    private void SelectAimTarget()
    {
        float cone = _driver.Configuration.Configuration.Items.Aim.AssistDegrees * MathF.PI / 180;
        ulong selected = 0;
        float score = float.MaxValue;
        Vector2 offset = Vector2.Zero;
        float angle = 0;
        foreach (var pair in _bodies)
        {
            var state = _driver.Latest?.Vehicles.FirstOrDefault(value => value.State.VehicleId == pair.Key)?.State;
            if (pair.Key == _driver.LocalVehicleId || state is not { CanInteract: true } || !pair.Value.IsPresented) { continue; }
            Vector3 center = pair.Value.VisualPosition + Vector3.Up * .35f;
            Vector3 to = center - _camera.GlobalPosition;
            if (to.LengthSquared() is < .01f or > 90000 || _camera.IsPositionBehind(center)) { continue; }
            float distance = to.Length();
            float candidateAngle = MathF.Acos(Math.Clamp((-_camera.GlobalBasis.Z).Dot(to / distance), -1, 1));
            // Target extent permits useful close encounters; total assist cone stays bounded.
            float extent = Math.Min(.10f, MathF.Atan2(1.4f, distance));
            if (cone <= 0 || candidateAngle > cone + extent) { continue; }
            if (!HasAimSight(pair.Key, _camera.GlobalPosition, center)) { continue; }
            float candidateScore = candidateAngle - (pair.Key == _aimTarget ? .012f : 0);
            if (candidateScore >= score) { continue; }
            score = candidateScore;
            selected = pair.Key;
            angle = Math.Max(0, candidateAngle - extent);
            Vector2 projected = _camera.UnprojectPosition(center);
            offset = projected - GetViewport().GetVisibleRect().Size * .5f;
        }
        _aimTarget = selected;
        _aimTargetAngle = angle;
        _aimTargetOffset = offset;
    }

    private (float Mouse, float Stick) AimFriction(Vector2 mouse, Vector2 stick, float delta)
    {
        if (_aimTarget == 0) { return (1, 1); }
        var tuning = _driver.Configuration.Configuration.Items.Aim;
        float weight = 1 - Mathf.Clamp(_aimTargetAngle / Math.Max(.0001f, tuning.AssistDegrees * MathF.PI / 180), 0, 1);
        // Only deliberate movement toward the target is slowed. Moving away or flicking
        // immediately removes assistance; zero input produces exactly zero view motion.
        float mouseWeight = mouse.Dot(_aimTargetOffset) > 0 ? weight * (1 - Mathf.SmoothStep(180, 900, mouse.Length() / Math.Max(.001f, delta))) : 0;
        float stickWeight = stick.Dot(_aimTargetOffset) > 0 ? weight * (1 - Mathf.SmoothStep(.55f, .9f, stick.Length())) : 0;
        return (1 - tuning.MouseFriction * mouseWeight, 1 - tuning.StickFriction * stickWeight);
    }

    private bool HasAimSight(ulong target, Vector3 from, Vector3 to)
    {
        return TraceViewedAim(from, to).Car?.VehicleId == target;
    }
}
