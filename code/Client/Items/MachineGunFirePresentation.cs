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
        // Arena pose, rack aiming and recoil run first. Network receipt is not a
        // render pose: reserve now, capture the physical muzzle on first display.
        ProcessPriority = 100;
        TopLevel = true;
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
                Reserve(shot);
            }
        }
    }

    internal void Clear()
    {
        ActiveCount = 0;
        Multimesh.VisibleInstanceCount = 0;
        _revision = 0;
    }

    private void Reserve(ItemEvent shot)
    {
        Vector3 origin = VehicleBody.ToGodot(shot.Origin), end = VehicleBody.ToGodot(shot.Position);
        Vector3 accepted = end - origin;
        // Cover inside the muzzle plane publishes a zero-length ray. Never turn
        // that into a sideways/backward flash or extend a streak beyond its hit.
        if (accepted.LengthSquared() < .0001f) { return; }
        int owned = 0;
        for (int i = 0; i < ActiveCount; i++) { if (_streaks[i].Owner == shot.Owner) { owned++; } }
        if (ActiveCount == Capacity || owned >= PerShooterCapacity) { DroppedCount++; return; }
        _streaks[ActiveCount++] = new(shot, end, MachineGunVfxSettings.Current);
        PeakCount = Math.Max(PeakCount, ActiveCount);
    }

    public override void _Process(double delta) => Advance((float)delta);

    internal ItemEvent? NewlyPresentedRound(int index) => _streaks[index].NewlyPresented ? _streaks[index].Shot : null;

    internal void Advance(float seconds)
    {
        var view = GetViewport();
        var lens = view.GetCamera3D();
        Vector3 camera = lens?.GlobalPosition ?? Vector3.Up * 10;
        float projection = lens is null ? 1 : Math.Abs(lens.GetCameraProjection().Y.Y);
        float pixelScale = 2 / Math.Max(1, view.GetVisibleRect().Size.Y * projection);
        var bounds = new Aabb();
        for (int i = 0; i < ActiveCount;)
        {
            ref var streak = ref _streaks[i];
            streak.NewlyPresented = !streak.Started;
            if (!streak.Started)
            {
                var shot = streak.Shot;
                if (Muzzle?.Invoke(shot, shot.MachineGunBarrel) is not { } muzzle ||
                    (streak.End - muzzle).Dot(streak.End - VehicleBody.ToGodot(shot.Origin)) <= 0)
                { _streaks[i] = _streaks[--ActiveCount]; continue; }
                streak.Start = muzzle;
                streak.Started = true;
                EmittedCount++;
                _barrelEmissions[shot.MachineGunBarrel]++;
            }
            float distance = streak.Start.DistanceTo(streak.End);
            float length = streak.Tracer ? streak.Settings.TracerLength : streak.Settings.BulletLength;
            // Near hits must survive more than a one-frame flash. Only cosmetic travel
            // slows down; the accepted damage/impact already happened and the endpoint stays fixed.
            float speed = Math.Min(streak.Settings.Speed, distance / streak.Settings.MinimumFlightSeconds);
            float head = length + streak.Age * speed;
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
            // A subpixel gold ribbon disappears under the normal chase view. Keep a
            // small luminous core in screen pixels, with hard metre caps for distant fire.
            float depth = lens is null ? camera.DistanceTo(middle) : Math.Max(.1f, -lens.ToLocal(middle).Z);
            float width = streak.Tracer ? Math.Clamp(depth * pixelScale * 3.8f, .075f, .36f)
                : Math.Clamp(depth * pixelScale * 2.4f, .055f, .24f);
            var basis = new Basis(across * width * streak.Settings.Width, direction * from.DistanceTo(to), across.Cross(direction));
            Multimesh.SetInstanceTransform(i, new(basis, middle));
            Multimesh.SetInstanceCustomData(i, new(streak.Tracer ? 1 : 0, streak.Color / 2f,
                streak.Tracer ? streak.Settings.TracerBrightness : streak.Settings.BulletBrightness, 0));
            bounds = i == 0 ? new(from, Vector3.Zero) : bounds.Expand(from);
            bounds = bounds.Expand(to);
            // Newly received rounds are rendered once at the actual muzzle even
            // on a long frame. Subsequent frames advance only forward, then retire.
            streak.Age += Math.Max(0, seconds);
            i++;
        }
        Multimesh.VisibleInstanceCount = ActiveCount;
        CustomAabb = bounds.Grow(.4f);
    }

    private struct Streak(ItemEvent shot, Vector3 end, MachineGunVfxSettings settings)
    {
        internal ItemEvent Shot { get; } = shot;
        internal ulong Owner => Shot.Owner;
        internal Vector3 Start { get; set; }
        internal bool Started { get; set; }
        internal bool NewlyPresented { get; set; }
        internal Vector3 End { get; } = end;
        internal bool Tracer => Shot.Tracer;
        internal MachineGunVfxSettings Settings { get; } = settings;
        internal int Color => Tracer ? Shot.MachineGunBarrel + 1 : 0;
        internal float Age { get; set; }
    }
}
