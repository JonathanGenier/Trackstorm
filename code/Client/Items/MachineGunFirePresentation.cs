using Godot;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>One fixed-size arena pool of travelling cosmetic streaks; accepted hits remain instantaneous.</summary>
internal sealed partial class MachineGunFirePresentation : MultiMeshInstance3D
{
    internal const string ShaderPath = "res://assets/items/machine-gun/BulletStreak.gdshader";
    internal const int Capacity = 512;
    internal const int PerShooterCapacity = 64;
    internal const float MaximumLifetime = .55f;
    private readonly Streak[] _streaks = new Streak[Capacity];
    private ulong _revision;
    private readonly int[] _barrelEmissions = new int[2];
    internal int EmittedFromBarrel(int side) => _barrelEmissions[side];
    internal Func<ItemEvent, int, Vector3?>? Muzzle { get; set; }
    internal int ActiveCount { get; private set; }
    internal int PeakCount { get; private set; }
    internal int DroppedCount { get; private set; }
    internal int EmittedCount { get; private set; }

    internal MachineGunFirePresentation()
    {
        Name = "MachineGunFire";
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        CastShadow = ShadowCastingSetting.Off;
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One, Material = new ShaderMaterial
                { Shader = Networking.MatchResourceLoader.LoadResource<Shader>(ShaderPath) } },
            InstanceCount = Capacity, VisibleInstanceCount = 0 };
    }

    internal void Observe(ItemPublication state)
    {
        if (state.Revision <= _revision)
        {
            // Recovery is an empty accepted checkpoint, not a replay of old shots.
            if (state.Events.Count == 0 && state.Revision < _revision) { Clear(); }
            else { return; }
        }
        _revision = state.Revision;
        foreach (var group in state.Events.Where(e => e.Item == HeldItem.MachineGun).GroupBy(e => (e.Owner, e.Token)))
        {
            var inventory = state.Slots.FirstOrDefault(s => s.Vehicle == group.Key.Owner);
            var vehicle = state.World.Vehicles.FirstOrDefault(v => v.State.VehicleId == group.Key.Owner)?.State;
            if (inventory is null || vehicle is not { CanInteract: true } || inventory.Life != vehicle.LifeId) { continue; }
            foreach (var shot in group)
            {
                if (Muzzle?.Invoke(shot, shot.MachineGunBarrel) is { } muzzle) { Emit(shot, muzzle); }
            }
        }
    }

    internal void Clear()
    {
        ActiveCount = 0;
        Multimesh.VisibleInstanceCount = 0;
        _revision = 0;
    }

    private void Emit(ItemEvent shot, Vector3 muzzle)
    {
        Vector3 origin = VehicleBody.ToGodot(shot.Origin), end = VehicleBody.ToGodot(shot.Position);
        Vector3 accepted = end - origin;
        // Cover inside the muzzle plane publishes a zero-length ray. Never turn
        // that into a sideways/backward flash or extend a streak beyond its hit.
        if (accepted.LengthSquared() < .0001f || (end - muzzle).Dot(accepted) <= 0) { return; }
        int owned = 0;
        for (int i = 0; i < ActiveCount; i++) { if (_streaks[i].Owner == shot.Owner) { owned++; } }
        if (ActiveCount == Capacity || owned >= PerShooterCapacity) { DroppedCount++; return; }
        _streaks[ActiveCount++] = new(shot.Owner, muzzle, end, shot.Tracer, MachineGunVfxSettings.Current, shot.MachineGunRound, shot.Token);
        PeakCount = Math.Max(PeakCount, ActiveCount);
        EmittedCount++;
        _barrelEmissions[shot.MachineGunBarrel]++;
    }

    public override void _Process(double delta) => Advance((float)delta);

    internal void Advance(float seconds)
    {
        Vector3 camera = GetViewport().GetCamera3D()?.GlobalPosition ?? Vector3.Up * 10;
        var bounds = new Aabb();
        for (int i = 0; i < ActiveCount;)
        {
            ref var streak = ref _streaks[i];
            float distance = streak.Start.DistanceTo(streak.End);
            float length = streak.Tracer ? streak.Settings.TracerLength : streak.Settings.BulletLength;
            float head = length + streak.Age * streak.Settings.Speed;
            float tail = Math.Max(0, head - length);
            if (tail >= distance || streak.Age >= MaximumLifetime)
            { _streaks[i] = _streaks[--ActiveCount]; continue; }
            Vector3 direction = (streak.End - streak.Start) / distance;
            Vector3 from = streak.Start + direction * tail;
            Vector3 to = streak.Start + direction * Math.Min(head, distance);
            Vector3 middle = (from + to) * .5f;
            Vector3 across = direction.Cross(camera - middle).Normalized();
            if (across.LengthSquared() < .001f)
            { across = direction.Cross(Math.Abs(direction.Y) < .9f ? Vector3.Up : Vector3.Right).Normalized(); }
            // A capped subpixel width floor keeps distant tracers readable without
            // turning every ordinary round into a bright rope at close range.
            float width = streak.Tracer ? Math.Clamp(camera.DistanceTo(middle) * .001f, .045f, .16f) : .025f;
            var basis = new Basis(across * width * streak.Settings.Width, direction * from.DistanceTo(to), across.Cross(direction));
            Multimesh.SetInstanceTransform(i, new(basis, middle));
            Multimesh.SetInstanceCustomData(i, new(streak.Tracer ? 1 : 0, streak.Color / 2f, streak.Settings.Brightness, 0));
            bounds = i == 0 ? new(from, Vector3.Zero) : bounds.Expand(from);
            bounds = bounds.Expand(to);
            // Newly received rounds are rendered once at the actual muzzle even
            // on a long frame. Subsequent frames advance only forward, then retire.
            streak.Age += Math.Max(0, seconds);
            i++;
        }
        Multimesh.VisibleInstanceCount = ActiveCount;
        CustomAabb = bounds.Grow(.2f);
    }

    private struct Streak(ulong owner, Vector3 start, Vector3 end, bool tracer, MachineGunVfxSettings settings, int round, ulong token)
    {
        internal ulong Owner { get; } = owner;
        internal Vector3 Start { get; } = start;
        internal Vector3 End { get; } = end;
        internal bool Tracer { get; } = tracer;
        internal MachineGunVfxSettings Settings { get; } = settings;
        internal int Color { get; } = tracer ? settings.TracerColor(token, round) : 0;
        internal float Age { get; set; }
    }
}
