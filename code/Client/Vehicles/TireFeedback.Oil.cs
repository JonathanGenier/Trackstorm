using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Presentation-only oil carried by four tires; never creates collision or item state.</summary>
internal sealed partial class TireFeedback
{
    private const float ContaminationSeconds = 6;
    private readonly float[] _oilRemaining = new float[4];
    private readonly int[] _oilTicks = new int[4];
    private readonly bool[] _oilSplash = new bool[4];
    private readonly Vector3?[] _oilPrevious = new Vector3?[4];
    private readonly Vector3[] _oilNormals = new Vector3[4];
    private readonly StandardMaterial3D[] _oilCoating = new StandardMaterial3D[4];
    private readonly GpuParticles3D[] _oilSpray = new GpuParticles3D[4];
    private ulong _oilLife;
    private ulong _oilTick;
    private float _oilImpairmentSeconds;
    private float _oilRecoverySeconds;
    internal int OilMarksWritten { get; private set; }
    internal int OilSplashes { get; private set; }
    internal float OilCoverage(int wheel) => Mathf.SmoothStep(0, 1, _oilRemaining[wheel] / ContaminationSeconds);

    private void InitializeOil()
    {
        for (int i = 0; i < 4; i++)
        {
            _oilCoating[i] = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(.006f, .008f, .009f, 0), Roughness = .13f,
                Metallic = .12f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            var material = new StandardMaterial3D { AlbedoColor = new Color(.006f, .008f, .009f), Roughness = .12f, Metallic = .12f };
            var process = new ParticleProcessMaterial
            {
                Direction = Vector3.Up, Spread = 65, Gravity = new Vector3(0, -9.8f, 0),
                InitialVelocityMin = .6f, InitialVelocityMax = 2,
                ScaleMin = .035f, ScaleMax = .09f,
            };
            _oilSpray[i] = new GpuParticles3D
            {
                Amount = 32, Lifetime = .55, LocalCoords = false, Emitting = false,
                ProcessMaterial = process, DrawPass1 = new SphereMesh { Radius = .5f, Height = 1, RadialSegments = 6, Rings = 3, Material = material },
                VisibilityAabb = new Aabb(new Vector3(-8, -5, -8), new Vector3(16, 12, 16)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_oilSpray[i]);
        }
    }

    private void ObserveOil(float delta)
    {
        var source = Source();
        if (source is not { } value || !value.State.CanInteract || _oilLife != value.State.LifeId)
        {
            Array.Clear(_oilRemaining); Array.Clear(_oilTicks); Array.Clear(_oilSplash);
            _oilTick = 0;
            _oilLife = source?.State.LifeId ?? 0;
            for (int i = 0; i < 4; i++)
            {
                BreakOilTrail(i);
                if (_tires[i] is { } tire) { tire.MaterialOverlay = null; }
            }
        }
        for (int i = 0; i < 4; i++) { _oilRemaining[i] = Math.Max(0, _oilRemaining[i] - delta); }
        if (source is not { } current || !current.State.CanInteract) { return; }
        // Only accepted simulation progress can reveal an entry. Repeated render samples
        // cannot refresh contamination. No historical burst on checkpoint reconstruction.
        ulong tick = current.State.Movement.Tick;
        if (tick > _oilTick)
        {
            ulong passed = _oilTick == 0 ? 0 : tick - _oilTick;
            bool retuned = _oilTick != 0 && (_oilImpairmentSeconds != current.Configuration.OilImpairmentSeconds ||
                _oilRecoverySeconds != current.Configuration.OilRecoverySeconds);
            for (int i = 0; i < 4; i++)
            {
                int remaining = current.State.Movement.Oil[i];
                int expected = Math.Max(0, _oilTicks[i] - (int)Math.Min(passed, int.MaxValue));
                if (remaining > expected && (!retuned || _oilTicks[i] == 0))
                {
                    float total = current.Configuration.OilImpairmentSeconds;
                    float age = Math.Max(0, total - remaining / (float)current.Configuration.TicksPerSecond);
                    _oilRemaining[i] = Math.Max(_oilRemaining[i], ContaminationSeconds - age);
                    _oilSplash[i] = _oilTick != 0;
                }
                _oilTicks[i] = remaining;
            }
            _oilTick = tick;
            _oilImpairmentSeconds = current.Configuration.OilImpairmentSeconds;
            _oilRecoverySeconds = current.Configuration.OilRecoverySeconds;
        }
        for (int i = 0; i < 4; i++)
        {
            _oilCoating[i].AlbedoColor = new Color(.006f, .008f, .009f, OilCoverage(i) * .92f);
            if (_tires[i] is { } tire) { tire.MaterialOverlay = _oilRemaining[i] > 0 ? _oilCoating[i] : null; }
        }
    }

    private void BreakOilTrail(int wheel)
    {
        _oilPrevious[wheel] = null;
        if (_oilSpray[wheel] is { } spray) { spray.Emitting = false; }
    }

    private void PresentOil(int wheel, VehicleSnapshot state, Transform3D pose, Vector3 point, Vector3 normal, float width, bool supported)
    {
        float coverage = OilCoverage(wheel);
        var compression = state.Movement.Wheels.Compression;
        float support = wheel switch { 0 => compression.X, 1 => compression.Y, 2 => compression.Z, _ => compression.W };
        if (!supported || support <= 0 || coverage <= .005f) { BreakOilTrail(wheel); return; }
        var spray = _oilSpray[wheel];
        spray.GlobalPosition = point + normal * .045f;
        float slip = wheel < 2 ? state.Movement.FrontSlip : state.Movement.RearSlip;
        bool splash = _oilSplash[wheel];
        _oilSplash[wheel] = false;
        spray.Emitting = splash || state.Speed > 1.2f;
        spray.AmountRatio = Math.Clamp(coverage * (splash ? 1 : .15f + state.Speed / 65 + slip * .5f), .05f, 1);
        var process = (ParticleProcessMaterial)spray.ProcessMaterial;
        Vector3 velocity = VehicleBody.ToGodot(state.Movement.Physics.LinearVelocity);
        process.Direction = (normal * .8f - velocity.Normalized() * .7f).Normalized();
        process.InitialVelocityMax = Math.Clamp(.8f + state.Speed * .13f + slip * 2, 1, 6);
        if (splash) { OilSplashes++; spray.Restart(); }
        if (_tires[wheel] is { } tire) { tire.MaterialOverlay = _oilCoating[wheel]; }
        if (_oilPrevious[wheel] is { } previous)
        {
            Vector3 direction = point - previous;
            float length = direction.Length();
            if (length > MaximumSegmentLength || normal.Dot(_oilNormals[wheel]) < .97f) { _oilPrevious[wheel] = null; }
            else if (length >= .12f / Math.Max(_quality, .25f))
            {
                // Probe the span as well as both endpoints: never bridge a missing floor,
                // step or road level with a floating strip.
                Vector3 middle = (point + previous) * .5f;
                _ray.From = middle + normal * .16f;
                _ray.To = middle - normal * .16f;
                using var hit = GetWorld3D().DirectSpaceState.IntersectRay(_ray);
                if (hit.Count != 0 && hit["normal"].AsVector3().Dot(normal) > .97f && hit["position"].AsVector3().DistanceTo(middle) < .06f)
                {
                    Vector3 forward = direction.Slide(normal).Normalized();
                    if (!forward.IsZeroApprox())
                    {
                        var basis = new Basis(normal.Cross(forward).Normalized() * width * _width, normal, forward * (length + .035f));
                        _batch.Write(new Transform3D(basis, middle + normal * .022f),
                            new Color(.005f, .007f, .008f, Math.Clamp(coverage * .88f * _intensity, 0, 1)), 12, 8, .14f, true);
                        OilMarksWritten++;
                    }
                }
                _oilPrevious[wheel] = point;
            }
        }
        else { _oilPrevious[wheel] = point; }
        _oilNormals[wheel] = normal;
    }

    private void DisposeOil()
    {
        for (int i = 0; i < 4; i++)
        {
            if (_tires[i] is { } tire && IsInstanceValid(tire)) { tire.MaterialOverlay = null; }
            _oilCoating[i].Dispose();
            var spray = _oilSpray[i];
            var mesh = (SphereMesh)spray.DrawPass1;
            var material = mesh.Material;
            var process = spray.ProcessMaterial;
            spray.DrawPass1 = null; spray.ProcessMaterial = null;
            material?.Dispose(); mesh.Dispose(); process?.Dispose();
        }
    }
}
