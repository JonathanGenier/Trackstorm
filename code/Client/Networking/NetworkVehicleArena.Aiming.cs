using Godot;
using Trackstorm.Client.Items;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Networking;

internal sealed partial class NetworkVehicleArena
{
    private readonly WeaponAimOverlay _aimOverlay = new() { MouseFilter = Control.MouseFilterEnum.Ignore };
    private readonly AssistedTargetIndicator _targetIndicator = new();
    internal AssistedTargetIndicator TargetIndicator => _targetIndicator;
    private ulong _aimTarget;
    private readonly MagneticAimCursor _magneticCursor = new();
    private Vector2 _cursorInput;
    private Vector3 _drivingNormal = Vector3.Up;
    private float? _drivingElevation;
    private ulong _aimTargetLife;
    private (ulong Life, ulong Token, ulong Selection) _aimCapability;
    private float _aimReleaseSeconds;
    private float _controllerCapabilityHold;
    private ulong _controllerResumeTarget;
    private bool _aimController;
    private ulong _aimDismissed;
    private ulong _aimDismissedLife;
    private Vector2 _aimDismissedDirection;
    private float _aimReturnTravel;
    private ulong _aimIntentRevision;
    private float _aimRecoverySeconds;
    private System.Numerics.Vector2 _aimGesture;
    internal ulong AssistedCar => StickyAiming ? _aimTarget : 0;
    private bool StickyAiming => WeaponAim.SupportsArticulation(_driver.LocalItem?.Active.Item ?? HeldItem.None);
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
        _targetIndicator.Clear();
        if (_aimTarget != 0) { AimReleaseObserved?.Invoke($"target={_aimTarget} reason={reason}"); }
        _aimTarget = 0;
        _aimRecoverySeconds = 0;
        _magneticCursor.End();
    }

    private void InitializeAiming()
    {
        AddChild(_targetIndicator);
        var layer = new CanvasLayer { Layer = 1 };
        AddChild(layer);
        layer.AddChild(_aimOverlay);
        _aimOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _camera.AimFriction = AimFriction;
        _camera.AimAttraction = AimAttraction;
        _camera.AimTracking = null;
        _camera.AimReset = () => ResetAiming("camera presentation reset");
    }

    private void PrepareAiming()
    {
        bool enabled = _driver.IsActive && _driver.AllowsParticipation && _driver.LocalState is { CanInteract: true } &&
            _driver.LocalItem is { } slot && WeaponAim.SupportsArticulation(slot.Active.Item) && CameraInput is { CameraEnabled: true, FreeCameraActive: false };
        _camera.WeaponAiming = enabled;
        if (!enabled) { ResetAiming(); return; }
        var current = _driver.LocalItem!;
        var capability = (current.Life, current.Active.Token, current.SelectionRevision);
        // Accepted token retirement within one Salvo magazine preserves the same
        // local assisted target; it does not simulate a player selection change.
        if (current.Active.SalvoCommitted && _aimCapability.Life == current.Life && _aimCapability.Selection == current.SelectionRevision)
        { capability = (current.Life, _aimCapability.Token, current.SelectionRevision); }
        if (_aimCapability != capability)
        {
            // A shot can rotate its capability token without a player selection.
            // Preserve only controller framing briefly while fresh accepted aim
            // arrives; target eligibility and firing authority still reset normally.
            if (_aimController && _aimTarget != 0 && _aimCapability.Life == current.Life &&
                _aimCapability.Selection == current.SelectionRevision)
            { _controllerCapabilityHold = .25f; _controllerResumeTarget = _aimTarget; }
            else { _controllerCapabilityHold = 0; _controllerResumeTarget = 0; }
            ReleaseAim("capability changed"); _aimDismissed = 0; _aimReleaseSeconds = 0; _aimCapability = capability;
        }
        if (CameraInput is { } input && (_aimIntentRevision != input.CameraAimRevision || !input.CameraAimActive))
        {
            ResetAssistance($"input boundary revision={_aimIntentRevision}->{input.CameraAimRevision} active={input.CameraAimActive}");
            _aimIntentRevision = input.CameraAimRevision;
        }
        ValidateStickyTarget();
    }

    private void ResetAiming() => ResetAiming("aim disabled/lifecycle");

    private void ResetAiming(string reason)
    {
        _drivingNormal = Vector3.Up;
        _drivingElevation = null;
        _driver.DesiredAim = null;
        _driver.DesiredAimTarget = _driver.DesiredAimTargetLife = 0;
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
        _controllerCapabilityHold = 0;
        _controllerResumeTarget = 0;
        _aimDismissed = 0;
        _aimDismissedDirection = default;
        _aimReturnTravel = 0;
        _magneticCursor.Reset();
        _cursorInput = default;
        _aimGesture = default;
    }

    private void PresentAiming(float delta)
    {
        foreach (var pair in _bodies)
        {
            var state = _driver.Latest?.Vehicles.FirstOrDefault(value => value.State.VehicleId == pair.Key)?.State;
            var inventory = (_driver.Host?.Items.Slots ?? _driver.ItemState?.Slots)?.FirstOrDefault(value => value.Vehicle == pair.Key);
            var aim = state?.LifeId == inventory?.Life ? _driver.PresentedAim(inventory) : null;
            pair.Value.Rack.ObserveAim(state is { CanInteract: true } && inventory is not null && WeaponAim.SupportsArticulation(inventory.Active.Item) ? aim : null);
        }
        if (!_camera.WeaponAiming || _driver.LocalState is not { } local || _driver.LocalItem is not { } slot || slot.Life != local.LifeId) { return; }
        UpdateMagneticCursor(delta);
        Vector2 center = AimCursorPosition();
        Vector3 lens = _camera.ProjectRayOrigin(center);
        float rayLength = slot.Active.Item == HeldItem.Salvo && CameraInput?.CameraAimActive == true
            ? Math.Max(300, SalvoFlight.AimRange(_driver.Configuration.Configuration.Items, true) + lens.DistanceTo(VehicleBody.ToGodot(local.ObservedPhysics.Position))) : 300;
        Vector3 rayEnd = lens + _camera.ProjectRayNormal(center) * rayLength;
        // Aim always resolves the visible cursor ray. Assistance may acquire nearby cars.
        var hit = TraceViewedAim(lens, rayEnd);
        if (StickyAiming)
        {
            ValidateStickyTarget(delta);
            if (CameraInput?.CameraAimActive == true && _aimTarget == 0 && _aimReleaseSeconds <= 0 && _driver.PresentedAim(slot) is not null && FindAcquisition(lens) is { } acquired)
            {
                _aimTarget = acquired.VehicleId;
                _aimTargetLife = _driver.Latest!.Vehicles.Single(v => v.State.VehicleId == _aimTarget).State.LifeId;

                _aimGesture = default;
                _aimRecoverySeconds = 0;
                Vector2 bearing = AimBearing(_camera.GlobalBasis.Inverse() * (AimBodyCenter(acquired) - lens));
                _magneticCursor.Begin(new(bearing.X, bearing.Y));
                _controllerResumeTarget = 0;
                ValidateStickyTarget();
            }
        }
        NetworkVehicleBody? framed = StickyAiming ? (_aimTarget != 0 ? _bodies[_aimTarget] : null) : hit.Car;
        _driver.DesiredAimTarget = slot.Active.Item == HeldItem.EmpClaw && CameraInput?.CameraAimActive == true ? framed?.VehicleId ?? 0 : 0;
        _driver.DesiredAimTargetLife = _driver.Latest?.Vehicles.FirstOrDefault(v => v.State.VehicleId == _driver.DesiredAimTarget)?.State.LifeId ?? 0;
        Vector3 desiredPoint = hit.Point;
        Vector3 origin = VehicleBody.ToGodot(local.ObservedPhysics.Position + System.Numerics.Vector3.Transform(WeaponAim.Pivot, local.ObservedPhysics.Orientation));
        // An unobstructed sky ray has no finite convergence surface. Preserve its
        // direction from the weapon pivot, including exactly vertical free aim.
        if (hit.Point == rayEnd) { desiredPoint = origin + _camera.ProjectRayNormal(center) * 300; }
        if (origin.DistanceSquaredTo(desiredPoint) > .001f) { _driver.DesiredAim = VehicleBody.ToCore((desiredPoint - origin).Normalized()); }
        // Road-relative intent removes suspension pitch from both the shot and its
        // projected cursor. It is not target seeking and never bypasses host clearance.
        if (CameraInput?.CameraAimActive != true)
        {
            Vector3 position = VehicleBody.ToGodot(local.ObservedPhysics.Position);
            using var query = PhysicsRayQueryParameters3D.Create(position + Vector3.Up * 2, position - Vector3.Up * 5, 1);
            using var support = GetWorld3D().DirectSpaceState.IntersectRay(query);
            Vector3 normal = support.Count > 0 && support["normal"].AsVector3().Y >= .55f
                ? support["normal"].AsVector3() : Vector3.Up;
            _drivingNormal = _drivingNormal.Lerp(normal, 1 - MathF.Exp(-8 * Math.Max(0, delta))).Normalized();
            Transform3D displayed = _bodies[local.VehicleId].VisualTransform;
            Vector3 forward = -displayed.Basis.Z;
            _driver.DesiredAim = DrivingWeaponAim.Direction(VehicleBody.ToCore(forward), VehicleBody.ToCore(_drivingNormal));
            // The lens follows the interpolated body. Project from that same rendered
            // muzzle frame, as projectile presentation does, rather than mixing a
            // fixed-tick pivot with a render-time camera. Authority retains its own pose.
            Vector3 forwardPoint = displayed * VehicleBody.ToGodot(WeaponAim.Pivot) + VehicleBody.ToGodot(_driver.DesiredAim.Value) * 60;
            Vector3 viewPoint = _camera.ToLocal(forwardPoint);
            float horizontalDistance = new Vector2(viewPoint.X, viewPoint.Z).Length();
            float elevation = MathF.Atan2(viewPoint.Y, horizontalDistance);
            _drivingElevation = _drivingElevation is float previous
                ? Mathf.Lerp(previous, elevation, 1 - MathF.Exp(-6 * Math.Max(0, delta))) : elevation;
            viewPoint.Y = MathF.Tan(_drivingElevation.Value) * horizontalDistance;
            forwardPoint = _camera.ToGlobal(viewPoint);
            // Stabilize the requested convergence point, not only the drawing. The
            // authoritative ray and visible square consume this same moving point.
            _driver.DesiredAim = VehicleBody.ToCore((forwardPoint - origin).Normalized());
            if (!_camera.IsPositionBehind(forwardPoint)) { center = _camera.UnprojectPosition(forwardPoint); }
        }
        else { _drivingElevation = null; }
        var accepted = _driver.PresentedAim(slot);
        Rect2? bounds = null;
        if (framed is { } target)
        {
            bounds = ProjectAimBounds(target);
        }
        _aimOverlay.SizeMultiplier = (float)(CameraSettings?.Current.CrosshairSize ?? 1);
        bool ready = accepted?.Ready == true && _driver.WrenchReturning?.Invoke() != true;
        ready &= slot.Active.Item switch
        {
            HeldItem.Missile => _driver.MissileReady?.Invoke(slot) != false,
            HeldItem.EmpClaw => _driver.EmpClawReady?.Invoke(slot) != false &&
                slot.Active.EmpClaw is { Remaining: > 0 } load && (_driver.Latest?.Tick ?? 0) >= load.ReadyTick,
            HeldItem.Salvo => _driver.SalvoReady?.Invoke(slot) != false,
            _ => true,
        };
        _aimOverlay.Present(center, ready, bounds, StickyAiming && framed is not null);
        if (StickyAiming && framed is not null)
        {
            bool grounded = _driver.Latest?.Vehicles.FirstOrDefault(v => v.State.VehicleId == framed.VehicleId)?.State.Movement.Grounded == true;
            _targetIndicator.Present(framed.VehicleId, framed.VisualTransform, _camera, accepted?.Ready == true, grounded);
        }
        else { _targetIndicator.Clear(); }
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
        Vector2 cursor = AimCursorPosition();
        Vector2 nearest = cursor.Clamp(frame.Position, frame.End);
        return _camera.ProjectRayNormal(cursor).AngleTo(_camera.ProjectRayNormal(nearest));
    }

    private float AcquisitionMargin(Rect2 frame, float cone)
    {
        float apparentSize = _camera.ProjectRayNormal(frame.Position).AngleTo(_camera.ProjectRayNormal(frame.End)) * .5f;
        return CameraAimAttraction.AcquisitionMargin(apparentSize, cone, _aimController);
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
            // A neutral stick must not switch rivals just because a shot rotated
            // its token. Fresh directional input immediately removes this preference.
            if (_controllerCapabilityHold > 0 && _controllerResumeTarget != 0 &&
                (pair.Key != _controllerResumeTarget || state?.LifeId != _aimTargetLife)) { continue; }
            Vector3 point = AimBodyCenter(pair.Value), to = point - lens;
            if (pair.Key == _driver.LocalVehicleId || state is not { CanInteract: true } || !pair.Value.IsPresented ||
                !WithinAimReach(point, lens) || _camera.IsPositionBehind(point) ||
                !_camera.GetViewport().GetVisibleRect().HasPoint(_camera.UnprojectPosition(point))) { continue; }
            float angle = _camera.ProjectRayNormal(AimCursorPosition()).AngleTo(to);
            if (ProjectAimBounds(pair.Value) is not { } frame) { continue; }
            float gap = AimFrameGap(frame);
            float allowance = AcquisitionMargin(frame, cone);
            Vector2 toward = AimBearing(_camera.GlobalBasis.Inverse() * to) - new Vector2(_magneticCursor.Offset.X, _magneticCursor.Offset.Y);
            // An approach may acquire while the mouse/stick is still moving. Only
            // a sweep away from this candidate suppresses acquisition.
            if (_aimGesture.Length() > .045f && System.Numerics.Vector2.Dot(_aimGesture, new(toward.X, toward.Y)) <= 0) { continue; }
            if (pair.Key == _aimDismissed)
            {
                if (state.LifeId != _aimDismissedLife) { _aimDismissed = 0; }
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
        if (_driver.LocalItem?.Life != _driver.LocalState?.LifeId || _driver.PresentedAim(_driver.LocalItem) is null)
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
        if (_camera.IsPositionBehind(center) || !_camera.GetViewport().GetVisibleRect().HasPoint(_camera.UnprojectPosition(center)))
        { ReleaseAim("offscreen"); return; }
        if (!WithinAimReach(center, _camera.GlobalPosition)) { ReleaseAim("range"); return; }
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

    }

    private (Vector2 Pull, bool Engaged) AimAttraction(Vector2 mouse, Vector2 stick, Vector2 lookInput, float delta)
    {
        _aimReleaseSeconds = Math.Max(0, _aimReleaseSeconds - delta);
        _controllerCapabilityHold = Math.Max(0, _controllerCapabilityHold - delta);
        if (!StickyAiming || CameraInput?.CameraAimActive != true || _driver.Configuration.Configuration.Items.Aim.AssistDegrees <= 0) { return (Vector2.Zero, false); }
        if (stick.LengthSquared() > .001f) { _aimController = true; _controllerResumeTarget = 0; }
        if (mouse.LengthSquared() > .001f) { _aimController = false; _controllerCapabilityHold = 0; _controllerResumeTarget = 0; }
        _aimGesture = CameraAimAttraction.Gesture(_aimGesture, new(mouse.X, mouse.Y), new(stick.X, stick.Y), delta);
        if (_aimTarget == 0)
        {
            // A gentle deliberate exit stays free even if the player stops beside the
            // car. Only deliberate return travel rearms the same living car.
            if (_aimDismissed != 0 && _bodies.TryGetValue(_aimDismissed, out var dismissed))
            {
                Vector2 toward = AimBearing(_camera.GlobalBasis.Inverse() * (AimBodyCenter(dismissed) - _camera.GlobalPosition))
                    - new Vector2(_magneticCursor.Offset.X, _magneticCursor.Offset.Y);
                // Rotating past the rear of the car during the same sweep is not a
                // reversal. Require actual input reversal before rearming nearby.
                _aimReturnTravel = MagneticAimCursor.ReturnTravel(_aimReturnTravel,
                    new(lookInput.X, lookInput.Y), new(toward.X, toward.Y),
                    new(_aimDismissedDirection.X, _aimDismissedDirection.Y), delta);
                if (_aimReturnTravel >= .012f) { _aimDismissed = 0; _aimReturnTravel = 0; }
            }
            return (Vector2.Zero, _aimController && (_aimDismissed != 0 || _controllerCapabilityHold > 0));
        }
        // Intent retains a physical-input floor so a low sensitivity preference
        // cannot make a deliberate sweep or full-stick release inescapable.
        Vector2 physical = mouse * .003f + stick * stick.Length() * (2.2f * delta);
        _cursorInput = physical.LengthSquared() > lookInput.LengthSquared() ? physical : lookInput;
        return (Vector2.Zero, true);
    }

    private Vector2 AimCursorPosition()
    {
        var offset = _magneticCursor.Offset;
        Vector3 ray = new(MathF.Sin(offset.X) * MathF.Cos(offset.Y), -MathF.Sin(offset.Y), -MathF.Cos(offset.X) * MathF.Cos(offset.Y));
        Rect2 viewport = _camera.GetViewport().GetVisibleRect().Grow(-8);
        return _camera.UnprojectPosition(_camera.GlobalPosition + _camera.GlobalBasis * ray * 10).Clamp(viewport.Position, viewport.End);
    }

    private (float Mouse, float Stick) AimFriction(Vector2 mouse, Vector2 stick, float delta)
    {
        if (_aimTarget == 0) return (1, 1);
        var tuning = _driver.Configuration.Configuration.Items.Aim;
        float retention = _magneticCursor.Retention;
        float mouseWeight = retention * (1 - Mathf.SmoothStep(180, 900, mouse.Length() / Math.Max(.001f, delta)));
        float stickWeight = retention * (1 - Mathf.SmoothStep(.55f, .9f, stick.Length()));
        return (1 - tuning.MouseFriction * mouseWeight, 1 - tuning.StickFriction * stickWeight);
    }

    private void UpdateMagneticCursor(float delta)
    {
        ValidateStickyTarget(finalView: true);
        if (_aimTarget == 0)
        {
            if (_aimDismissed == 0) _magneticCursor.Recover(delta);
            _cursorInput = default;
            return;
        }
        Vector2 target = AimBearing(_camera.GlobalBasis.Inverse() * (AimBodyCenter(_bodies[_aimTarget]) - _camera.GlobalPosition));
        var tuning = _driver.Configuration.Configuration.Items.Aim;
        _magneticCursor.Advance(new(target.X, target.Y), new(_cursorInput.X, _cursorInput.Y), _aimController,
            _aimController ? tuning.StickPull : tuning.MousePull, delta);
        if (_magneticCursor.Released)
        {
            _aimDismissed = _aimTarget;
            _aimDismissedLife = _aimTargetLife;
            _aimDismissedDirection = _cursorInput.Normalized();
            _aimReturnTravel = 0;
            ReleaseAim("deliberate magnetic cursor departure");
            // The dismissed identity remains excluded; adjacent controller targets
            // need not wait through the longer mouse re-acquisition pause.
            _aimReleaseSeconds = _aimController ? .06f : .18f;
        }
        _cursorInput = default;
    }

    private bool HasAimSight(ulong target, Vector3 from, Vector3 to)
    {
        var center = TraceViewedAim(from, to);
        if (center.Car?.VehicleId == target) { return true; }
        // Another car between the lens and target still owns that sightline.
        if (center.Car is not null || !_bodies.TryGetValue(target, out var body)) { return false; }
        // A crest or narrow prop can obscure the centre while substantial body
        // remains visible. Sample real hull locations, never an expanded hitbox;
        // every sample must independently pass the same closest native sight test.
        foreach (Vector3 sample in new[] { new Vector3(-.9f, .3f, 0), new Vector3(.9f, .3f, 0),
            new Vector3(0, .9f, 0), new Vector3(0, .3f, -1.6f), new Vector3(0, .3f, 1.6f) })
        {
            Vector3 point = body.VisualTransform * sample;
            if (!_camera.IsPositionBehind(point) && _camera.GetViewport().GetVisibleRect().HasPoint(_camera.UnprojectPosition(point)) &&
                TraceViewedAim(from, point).Car?.VehicleId == target) { return true; }
        }
        return false;
    }

    // Acquisition is a viewing aid, not a promise that the selected projectile can reach.
    private static bool WithinAimReach(Vector3 point, Vector3 lens) => point.DistanceSquaredTo(lens) <= 90000;
}
