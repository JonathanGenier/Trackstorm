using System.Numerics;
using Trackstorm.Core.Items;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Arenas;

/// <summary>Match-owned staged destruction driven by accepted observations and committed item impacts.</summary>
public sealed class EnvironmentAuthority
{
    public const int MaximumMoving = 16;
    public const float IntactHealth = 180;
    public const float BrokenHealth = 60;
    private readonly EnvironmentLayout _layout;
    private EnvironmentRockState[] _rocks;
    private bool[] _plants;
    private ulong _tick;

    public EnvironmentAuthority(EnvironmentLayout layout)
    {
        _layout = layout;
        _rocks = layout.InitialStages.Select(stage => new EnvironmentRockState(stage, 0, 0, default, default)).ToArray();
        _plants = new bool[layout.Plants.Count];
    }

    public EnvironmentSnapshot Snapshot(ulong session, ulong tick) => new(session, tick, _rocks, _plants);

    public void Restore(EnvironmentSnapshot snapshot)
    {
        if (snapshot.Rocks.Count != _layout.Rocks.Count || snapshot.Plants.Count != _layout.Plants.Count) { throw new ArgumentException("Environment layout mismatch."); }
        _rocks = snapshot.Rocks.ToArray();
        _plants = snapshot.Plants.ToArray();
        _tick = snapshot.Tick;
    }

    /// <summary>Called once after the existing world/item transaction commits; repeated ticks do nothing.</summary>
    public void Advance(ulong tick, IReadOnlyList<VehicleStepRequest> requests, IReadOnlyList<ItemEvent> events, ItemAuthority items)
    {
        if (tick <= _tick) { return; }
        var damage = new float[_rocks.Length];
        var pushes = new Vector3[_rocks.Length];
        foreach (var request in requests)
        {
            var observation = request.Observation;
            foreach (var contact in observation.Contacts)
            {
                int index = contact.EnvironmentRock - 1;
                if (index < 0 || index >= _rocks.Length || tick < _rocks[index].ImpactReadyTick) { continue; }
                float severity = Math.Max(0, -Vector3.Dot(contact.RelativeVelocity, contact.Normal));
                damage[index] = Math.Max(damage[index], Math.Min(120, Math.Max(0, severity - 4) * 12));
                pushes[index] = contact.RelativeVelocity;
            }
            for (int i = 0; i < _plants.Length; i++)
            {
                if (!_plants[i] && UnderVehicle(_layout.Plants[i], observation.Physics, 0.25f)) { _plants[i] = true; }
            }
            for (int i = 0; i < _rocks.Length; i++)
            {
                if (_rocks[i].Stage == 1 || !UnderVehicle(_layout.Rocks[i] + _rocks[i].Offset, observation.Physics, 0.6f)) { continue; }
                Vector3 velocity = observation.Physics.LinearVelocity;
                pushes[i] = velocity;
                if (tick >= _rocks[i].ImpactReadyTick) { damage[i] = Math.Max(damage[i], Math.Min(120, Math.Max(0, velocity.Length() - 2) * 12)); }
            }
        }
        foreach (var impact in events.Where(e => e.Impact && e.Item is HeldItem.Missile or HeldItem.Salvo))
        {
            for (int i = 0; i < _rocks.Length; i++)
            {
                var effect = items.Explosion(impact.Position, _layout.Rocks[i] + _rocks[i].Offset, impact.Item);
                damage[i] += effect.Damage;
                pushes[i] += effect.Impulse / 1000;
            }
            for (int i = 0; i < _plants.Length; i++)
            {
                if (!_plants[i] && items.Explosion(impact.Position, _layout.Plants[i], impact.Item).Damage > 1) { _plants[i] = true; }
            }
        }
        int moving = 0;
        for (int i = 0; i < _rocks.Length; i++)
        {
            var rock = _rocks[i];
            if (damage[i] > 0 && rock.Stage < 3)
            {
                float total = rock.Damage + damage[i];
                rock = total >= (rock.Stage == 1 ? IntactHealth : BrokenHealth)
                    ? rock with { Stage = (byte)(rock.Stage + 1), Damage = 0, ImpactReadyTick = tick + 12 }
                    : rock with { Damage = total, ImpactReadyTick = tick + 12 };
            }
            if (rock.Stage > 1)
            {
                Vector3 velocity = rock.Velocity * 0.9f;
                Vector3 push = new(pushes[i].X, 0, pushes[i].Z);
                if (push.LengthSquared() > 0.01f) { velocity = Vector3.Normalize(push) * Math.Min(6, push.Length() * 0.35f); }
                if (velocity.LengthSquared() < 0.01f || moving >= MaximumMoving) { velocity = default; }
                else { moving++; }
                Vector3 offset = rock.Offset + velocity / 60;
                if (offset.LengthSquared() > 64) { offset = Vector3.Normalize(offset) * 8; velocity = default; }
                rock = rock with { Offset = offset, Velocity = velocity };
            }
            _rocks[i] = rock;
        }
        _tick = tick;
    }

    private static bool UnderVehicle(Vector3 point, VehiclePhysicsState physics, float margin)
    {
        Vector3 local = Vector3.Transform(point - physics.Position, Quaternion.Conjugate(physics.Orientation));
        return Math.Abs(local.X) < 1.33f + margin && Math.Abs(local.Z) < 2.405f + margin && local.Y is > -1.9f and < 0.3f;
    }
}
