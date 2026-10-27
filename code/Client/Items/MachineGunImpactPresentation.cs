using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Bounded arena-local chips, dust and fine sparks from accepted bullet contacts.</summary>
internal sealed partial class MachineGunImpactPresentation : MultiMeshInstance3D
{
    internal const string ShaderPath = "res://assets/items/machine-gun/BulletImpact.gdshader";
    internal const int Capacity = 128;
    internal const int PerShooterCapacity = 16;
    internal const int ParticlesPerImpact = 8;
    internal const float Lifetime = .42f;
    private readonly Burst[] _bursts = new Burst[Capacity];
    private ulong _revision;
    internal int ActiveCount { get; private set; }
    internal int PeakCount { get; private set; }
    internal int EmittedCount { get; private set; }
    internal int CoalescedCount { get; private set; }
    internal int DroppedCount { get; private set; }
    internal int SampledCount { get; private set; }
    internal int MetalCount { get; private set; }

    internal MachineGunImpactPresentation()
    {
        Name = "MachineGunImpacts";
        TopLevel = true;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        CastShadow = ShadowCastingSetting.Off;
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = new ShaderMaterial
                { Shader = Networking.MatchResourceLoader.LoadResource<Shader>(ShaderPath) } },
            InstanceCount = Capacity * ParticlesPerImpact, VisibleInstanceCount = 0 };
    }

    internal void Observe(ItemPublication state)
    {
        if (state.Revision <= _revision)
        {
            if (state.Events.Count == 0 && state.Revision < _revision) { Clear(); }
            else { return; }
        }
        _revision = state.Revision;
        foreach (var shot in state.Events)
        {
            if (shot.Item != HeldItem.MachineGun || !shot.Impact) { continue; }
            Vector3 point = VehicleBody.ToGodot(shot.Position);
            Vector3 direction = VehicleBody.ToGodot(shot.Position - shot.Origin);
            direction = direction.LengthSquared() > .0001f ? direction.Normalized() : Vector3.Down;
            int owned = 0;
            bool nearby = false;
            for (int i = 0; i < ActiveCount; i++)
            {
                if (_bursts[i].Owner != shot.Owner) { continue; }
                owned++;
                nearby |= _bursts[i].Age < .025f && _bursts[i].Point.DistanceSquaredTo(point) < .36f;
            }
            // Coalesce dense contact streams per shooter; isolated taps always survive.
            if (nearby) { CoalescedCount++; continue; }
            if (owned >= PerShooterCapacity || ActiveCount == Capacity) { DroppedCount++; continue; }
            var surface = MachineGunImpactSurface.Sample(GetWorld3D(), point, direction);
            SampledCount++;
            if (surface.Material == MachineGunImpactSurface.Kind.Metal) { MetalCount++; }
            _bursts[ActiveCount++] = new(shot.Owner, point + surface.Normal * .035f, surface,
                unchecked((uint)(shot.Token * 397 + (ulong)shot.MachineGunRound * 7919)));
            EmittedCount++;
            PeakCount = Math.Max(PeakCount, ActiveCount);
        }
    }

    internal void Clear()
    {
        ActiveCount = 0;
        Multimesh.VisibleInstanceCount = 0;
        _revision = 0;
    }

    public override void _Process(double delta) => Advance((float)delta);

    internal void Advance(float seconds)
    {
        var view = GetViewport();
        var camera = view.GetCamera3D();
        Basis lens = camera?.GlobalBasis ?? Basis.Identity;
        float pixelScale = 2 / Math.Max(1, view.GetVisibleRect().Size.Y * (camera is null ? 1 : Math.Abs(camera.GetCameraProjection().Y.Y)));
        var bounds = new Aabb();
        int count = 0;
        for (int i = 0; i < ActiveCount;)
        {
            ref var burst = ref _bursts[i];
            if (burst.Age >= Lifetime) { _bursts[i] = _bursts[--ActiveCount]; continue; }
            var normal = burst.Surface.Normal;
            Vector3 tangent = normal.Cross(Math.Abs(normal.Y) < .9f ? Vector3.Up : Vector3.Right).Normalized();
            Vector3 bitangent = normal.Cross(tangent);
            bool metal = burst.Surface.Material == MachineGunImpactSurface.Kind.Metal;
            bool wet = burst.Surface.Material is MachineGunImpactSurface.Kind.Wet or MachineGunImpactSurface.Kind.Water;
            bool water = burst.Surface.Material == MachineGunImpactSurface.Kind.Water;
            for (int p = 0; p < ParticlesPerImpact; p++)
            {
                float random = Hash(burst.Seed + (uint)p * 31);
                float angle = Hash(burst.Seed + (uint)p * 71 + 1) * Mathf.Tau;
                // Metal gets six angular flecks and two fine sparks, never a smoke cloud.
                // Stone gets four chips, one spark and three neutral dust wisps.
                int kind = p < (metal ? 6 : 4) ? 0 : metal || (p == 4 && burst.Surface.Material == MachineGunImpactSurface.Kind.Stone) ? 1 : 2;
                if (wet && p >= 4) { continue; }
                float life = kind == 1 ? .10f + random * .08f : kind == 2 ? .24f + random * .12f : .28f + random * .14f;
                float age = burst.Age;
                if (age >= life) { continue; }
                Vector3 scatter = tangent * MathF.Cos(angle) + bitangent * MathF.Sin(angle);
                Vector3 velocity = normal * (1.1f + random * 1.7f) + scatter * (.9f + random * 2.2f);
                if (kind == 1) { velocity *= 1.6f; }
                if (kind == 2) { velocity *= .28f; }
                Vector3 position = burst.Point + velocity * age + Vector3.Down * ((kind == 2 ? .3f : 5f) * age * age);
                float depth = camera is null ? 10 : Math.Max(.1f, -camera.ToLocal(position).Z);
                float size = kind == 2 ? .38f + age * .8f : Math.Clamp(depth * pixelScale * 3.2f, .11f, .26f) * (.7f + random * .6f);
                if (water) { size *= .7f; }
                float rotation = angle + age * (random - .5f) * 18;
                Vector3 x = lens.X * MathF.Cos(rotation) + lens.Y * MathF.Sin(rotation);
                Vector3 y = -lens.X * MathF.Sin(rotation) + lens.Y * MathF.Cos(rotation);
                if (kind == 1)
                {
                    Vector3 projected = velocity - lens.Z * velocity.Dot(lens.Z);
                    y = projected.LengthSquared() > .001f ? projected.Normalized() : lens.Y;
                    x = y.Cross(lens.Z).Normalized();
                }
                float fade = 1 - Mathf.SmoothStep(kind == 2 ? .05f : .45f, 1, age / life);
                var basis = new Basis(x * (kind == 1 ? size * .22f : size), y * (kind == 1 ? .12f + random * .13f : size), lens.Z);
                Multimesh.SetInstanceTransform(count, new(basis, position));
                float shade = water ? .72f : wet ? .28f : metal ? .62f + random * .25f : .43f + random * .3f;
                Multimesh.SetInstanceCustomData(count, new(kind / 2f, fade * (kind == 2 ? .24f : 1), shade, random));
                bounds = count++ == 0 ? new(position, Vector3.Zero) : bounds.Expand(position);
            }
            burst.Age += Math.Max(0, seconds);
            i++;
        }
        Multimesh.VisibleInstanceCount = count;
        CustomAabb = bounds.Grow(.8f);
    }

    private static float Hash(uint seed)
    {
        seed = unchecked((seed ^ (seed >> 16)) * 0x45d9f3b);
        seed = unchecked((seed ^ (seed >> 16)) * 0x45d9f3b);
        return ((seed ^ (seed >> 16)) & 0xffff) / 65535f;
    }

    private struct Burst(ulong owner, Vector3 point, MachineGunImpactSurface surface, uint seed)
    {
        internal ulong Owner { get; } = owner;
        internal Vector3 Point { get; } = point;
        internal MachineGunImpactSurface Surface { get; } = surface;
        internal uint Seed { get; } = seed;
        internal float Age { get; set; }
    }
}
