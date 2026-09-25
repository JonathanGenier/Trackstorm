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
    public static float Health(byte stage) => stage == 1 ? IntactHealth : Math.Max(20, BrokenHealth * MathF.Pow(0.72f, stage - 2));
    /// <summary>Normal closing speed preserves glancing-contact rejection while rewarding solid impacts.</summary>
    public static float ImpactDamage(float severity) => Math.Min(360, 10 * MathF.Pow(Math.Max(0, severity - 3), 1.5f));
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
        for (int i = 0; i < snapshot.Rocks.Count; i++)
        {
            var rock = snapshot.Rocks[i];
            if (rock.Stage > _layout.FinalStage(i) || (i % EnvironmentLayout.PiecesPerRock == 0 && rock.Stage == 0) ||
                (i % EnvironmentLayout.PiecesPerRock != 0 && rock.Stage == 1) || (rock.Stage == _layout.FinalStage(i) && rock.Damage != 0)) { throw new ArgumentException("Invalid size-dependent rock continuation."); }
            int root = i / EnvironmentLayout.PiecesPerRock * EnvironmentLayout.PiecesPerRock;
            if ((_layout.InitialStages[root] == 2 && rock.Stage != _layout.InitialStages[i]) ||
                (snapshot.Rocks[root].Stage == 1 && i != root && rock.Stage != 0)) { throw new ArgumentException("Impossible rock split continuation."); }
        }
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
                // Sloping authored faces must not hide a solid horizontal car impact.
                // Keep tangential glances harmless and exclude nearly horizontal roof/support contacts.
                Vector3 face = new(contact.Normal.X, 0, contact.Normal.Z);
                float severity = face.LengthSquared() < 0.0625f ? 0 : Math.Max(0, -Vector3.Dot(contact.RelativeVelocity, Vector3.Normalize(face)));
                damage[index] = Math.Max(damage[index], ImpactDamage(severity));
                pushes[index] = contact.RelativeVelocity;
            }
            for (int i = 0; i < _plants.Length; i++)
            {
                if (!_plants[i] && UnderVehicle(_layout.Plants[i], observation.Physics, 0.25f)) { _plants[i] = true; }
            }
            for (int i = 0; i < _rocks.Length; i++)
            {
                if (_rocks[i].Stage == 0 || (_rocks[i].Stage == 1 && _layout.Size(i, 1) > 1.2f) ||
                    !UnderVehicle(_layout.Rocks[i] + _rocks[i].Offset, observation.Physics, Math.Min(2, _layout.Size(i, _rocks[i].Stage) * 0.4f))) { continue; }
                if (_rocks[i].Stage == 1 && observation.Contacts.Any(c => c.EnvironmentRock == i + 1)) { continue; }
                Vector3 velocity = observation.Physics.LinearVelocity;
                pushes[i] = velocity;
                if (tick >= _rocks[i].ImpactReadyTick) { damage[i] = Math.Max(damage[i], ImpactDamage(new Vector2(velocity.X, velocity.Z).Length())); }
            }
        }
        foreach (var impact in events.Where(e => e.Impact && e.Item is HeldItem.Missile or HeldItem.Salvo))
        {
            for (int i = 0; i < _rocks.Length; i++)
            {
                if (_rocks[i].Stage == 0) { continue; }
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
            if (rock.Stage == 0) { continue; }
            if (damage[i] > 0 && rock.Stage < _layout.FinalStage(i))
            {
                float total = rock.Damage + damage[i];
                if (total >= Health(rock.Stage))
                {
                    rock = rock with { Stage = (byte)(rock.Stage + 1), Damage = 0, ImpactReadyTick = tick + 12 };
                    int root = i / EnvironmentLayout.PiecesPerRock * EnvironmentLayout.PiecesPerRock;
                    int sibling = Enumerable.Range(root, EnvironmentLayout.PiecesPerRock).FirstOrDefault(slot => slot != i && _rocks[slot].Stage == 0, -1);
                    if (sibling >= 0)
                    {
                        float angle = (root * 0.37f) + (i % EnvironmentLayout.PiecesPerRock * 2.39996f);
                        Vector3 separation = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * Math.Min(1.6f, _layout.Size(i, rock.Stage) * 0.45f);
                        _rocks[sibling] = rock with { Offset = Bounded(rock.Offset + separation), Velocity = default };
                        rock = rock with { Offset = Bounded(rock.Offset - separation) };
                    }
                }
                else { rock = rock with { Damage = total, ImpactReadyTick = tick + 12 }; }
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

    private static Vector3 Bounded(Vector3 offset) => offset.LengthSquared() > 64 ? Vector3.Normalize(offset) * 8 : offset;

    private static bool UnderVehicle(Vector3 point, VehiclePhysicsState physics, float margin)
    {
        Vector3 local = Vector3.Transform(point - physics.Position, Quaternion.Conjugate(physics.Orientation));
        return Math.Abs(local.X) < 1.33f + margin && Math.Abs(local.Z) < 2.405f + margin && local.Y is > -1.9f and < 0.3f;
    }
}
