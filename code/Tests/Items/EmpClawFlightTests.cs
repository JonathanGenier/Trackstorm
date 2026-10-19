using System.Numerics;
using Trackstorm.Core.Development;
using Trackstorm.Core.Items;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Simulation;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class EmpClawFlightTests
{
    private HostVehicleSession _host = null!;
    private readonly Dictionary<ulong, Vector3> _positions = new();
    private Func<ulong, Vector3, Vector3, WeaponRayHit?> _ray = (_, _, _) => null;
    private Func<Vector3, Vector3, MissileTerrainSample?>? _terrain;
    private Vector3 _aim = -Vector3.UnitZ;
    private ulong _target;

    [SetUp]
    public void Setup()
    {
        _host = new(297, matchConfiguration: new() { MinimumPlayers = 1, CountdownTicks = 1 });
        _host.Join(2); _host.Join(3);
        _positions.Clear();
        _positions[1] = new(0, 10, 0); _positions[2] = new(0, 11.81f, -80); _positions[3] = new(60, 10, 50);
        _aim = -Vector3.UnitZ; _target = 0; _terrain = null; _ray = (_, _, _) => null;
        Step(2);
        Assert.That(_host.Items.Grant(_host.World, 1, HeldItem.EmpClaw), Is.True);
        Step(100);
    }

    private void Step(int ticks = 1)
    {
        for (int i = 0; i < ticks; i++)
        {
            var slot = _host.Items.Slots.FirstOrDefault(s => s.Vehicle == 1)?.Active;
            if (slot is { Item: HeldItem.EmpClaw })
            { _host.Items.RequestAim(_host.World, 1, slot.Life, slot.Token, 0, _host.World.State.Tick + 1, _aim,
                _target, _target == 0 ? 0 : _host.World.GetVehicle(_target).LifeId); }
            _host.Step(default, s => new VehicleObservation(new(_positions[s.VehicleId], Quaternion.Identity, Vector3.Zero, Vector3.Zero), Vector3.UnitY),
                raycastWeapon: (owner, a, b) => _ray(owner, a, b), missileTerrain: _terrain);
        }
    }

    private MissileState Fire(ulong target = 0)
    {
        _target = target; Step();
        var slot = _host.Items.Slots.Single(s => s.Vehicle == 1).Active;
        Assert.That(_host.Items.RequestUse(_host.World, 1, slot.Life, slot.Token), Is.True);
        Step();
        return _host.Items.Missiles.Single(p => p.Id == slot.Token);
    }

    [Test]
    public void LockedIdentitySurvivesCameraChangesOcclusionAndCloserRival()
    {
        var shot = Fire(2);
        Assert.That(shot.EmpClaw!.Target, Is.EqualTo(2));
        _aim = Vector3.UnitX; _target = 3;
        _positions[2] = new(40, 11.81f, -70);
        _positions[3] = new(0, 11.81f, -30);
        // Long visibility rays encounter cover; flight segments remain clear until physical contact.
        _ray = (_, a, b) => Vector3.Distance(a, b) > 5 ? new(.3f, 0) : null;
        Step(30);
        shot = _host.Items.Missiles.Single();
        Assert.That(shot.EmpClaw!.Target, Is.EqualTo(2));
        Assert.That(shot.Velocity.X, Is.GreaterThan(1));
        _ray = (_, _, _) => new(.5f, 0);
        Step();
        Assert.That(_host.Items.Missiles, Is.Empty, "Occlusion never grants travel through the actual wall.");
        Assert.That(_host.Items.Events.Single().EmpClawContact, Is.Null);
    }

    [Test]
    public void UnlockedFirstSegmentIsStraightThenFirstVisibleForwardEnemyIsCommitted()
    {
        _positions[2] = new(0, 11.81f, 30); // Behind.
        _positions[3] = new(60, 11.81f, -20); // Outside cone.
        var shot = Fire();
        Assert.That(shot.EmpClaw!.Target, Is.Zero);
        Assert.That(shot.Velocity, Is.EqualTo(-Vector3.UnitZ * 65));
        Step(10);
        Assert.That(_host.Items.Missiles.Single().EmpClaw!.Target, Is.Zero);
        _positions[3] = new(2, 11.81f, -80);
        _ray = (_, a, b) => Vector3.Distance(a, b) > 5 ? new(.2f, 0) : null;
        Step();
        Assert.That(_host.Items.Missiles.Single().EmpClaw!.Target, Is.Zero, "Cover prevents first acquisition.");
        _ray = (_, _, _) => null;
        Step();
        Assert.That(_host.Items.Missiles.Single().EmpClaw!.Target, Is.EqualTo(3));
        _positions[2] = new(0, 11.81f, -25);
        Step();
        Assert.That(_host.Items.Missiles.Single().EmpClaw!.Target, Is.EqualTo(3));
    }

    [Test]
    public void LaunchDeceleratesWithinHalfSecondAndHomingHasABoundedTurn()
    {
        var shot = Fire(2);
        Assert.That(shot.Velocity.Length() * 3.6f, Is.GreaterThan(200));
        _positions[2] = new(80, 11.81f, 10);
        for (int tick = 0; tick < 60; tick++)
        {
            var previous = _host.Items.Missiles.Single(); Step(); shot = _host.Items.Missiles.Single();
            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(previous.Velocity), Vector3.Normalize(shot.Velocity)), -1, 1)) * 180 / MathF.PI;
            Assert.That(angle, Is.LessThanOrEqualTo(100f / 60 + .01f));
            Assert.That(shot.Velocity.Length(), Is.InRange(100f / 3.6f - .001f, previous.Velocity.Length() + .001f));
            Assert.That(Vector3.Distance(shot.Position, previous.Position), Is.EqualTo(shot.Velocity.Length() / 60).Within(.001));
            if (tick >= 30) { Assert.That(shot.Velocity.Length() * 3.6f, Is.EqualTo(100).Within(.01)); }
        }
    }

    [Test]
    public void InterceptionHandsOffActualCarOnceWithoutDamageOrSelfHit()
    {
        Fire(2);
        _ray = (owner, _, _) => { Assert.That(owner, Is.EqualTo(1), "Every sweep excludes the original owner."); return new(.4f, 3) { Normal = Vector3.UnitZ }; };
        Step();
        Assert.That(_host.Items.Missiles, Is.Empty);
        var impact = _host.Items.Events.Single();
        Assert.That(impact.EmpClawContact!.Vehicle, Is.EqualTo(3));
        Assert.That(impact.EmpClawContact.Life, Is.EqualTo(_host.World.GetVehicle(3).LifeId));
        Assert.That(impact.EmpClawContact.LocalPosition, Is.EqualTo(impact.Position - _positions[3]));
        Step(); Assert.That(_host.Items.Events, Is.Empty);
    }

    [Test]
    public void DeadlineSurvivesRetuningSerializationAndAuthorityRestore()
    {
        _positions[2] = new(0, 10, 100); _positions[3] = new(0, 10, 150);
        var shot = Fire();
        ulong deadline = shot.EmpClaw!.ExpiresAtTick;
        Step(20);
        var publication = new ItemPublication(_host.Items.Revision, _host.Snapshot(), _host.Items.Slots, _host.Items.Missiles, []);
        var decoded = ItemCodec.DecodeState(ItemCodec.EncodeState(publication));
        Assert.That(decoded.Missiles, Is.EqualTo(publication.Missiles));
        _host.Items.Restore(decoded, _host.Items.Revision, _host.Items.TokenHighWater);
        Assert.That(_host.TryConfigure(0, new Dictionary<string, double> { ["items.emp_claw_lifetime"] = 1, ["items.missile_speed"] = 250 }, out _), Is.True);
        Assert.That(_host.Items.Missiles.Single().Velocity.Length(), Is.LessThan(65), "Missile speed tuning cannot change EMP phase speed.");
        Step((int)(deadline - _host.World.State.Tick - 1));
        Assert.That(_host.Items.Missiles.Single().RemainingTicks, Is.EqualTo(1));
        Step(); Assert.That(_host.Items.Missiles, Is.Empty); Assert.That(_host.Items.Events, Is.Empty);
    }

    [Test]
    public void TargetNewLifeDoesNotReplaceCapturedVictim()
    {
        var shot = Fire(2); var original = shot.EmpClaw!;
        var current = _host.World.State;
        _host.World.Restore(new SimulationState(current.Tick, current.LastInput, current.Vehicles.Select(v => v.VehicleId == 2
            ? new VehicleSnapshot(2, v.LifeId + 1, v.Movement, v.Damage, v.ObservedPhysics) : v), current.Match));
        _positions[2] = new(80, 11.81f, -10); _positions[3] = new(0, 11.81f, -30);
        Step(10);
        Assert.That(_host.Items.Missiles.Single().EmpClaw, Is.EqualTo(original));
        Assert.That(_host.Items.Missiles.Single().Velocity.X, Is.Zero);
    }

    [Test]
    public void CollisionFailureRollsBackFlightAndContactTogether()
    {
        Fire(2); var before = _host.Items.Missiles.Single(); ulong tick = _host.World.State.Tick;
        _ray = (_, _, _) => new(float.NaN, 3);
        Assert.Throws<ArgumentException>(() => Step());
        Assert.That(_host.Items.Missiles.Single(), Is.EqualTo(before)); Assert.That(_host.World.State.Tick, Is.EqualTo(tick));
        _ray = (_, _, _) => new(.5f, 0); Step();
        Assert.That(_host.Items.Events.Count(e => e.Impact), Is.EqualTo(1));
    }

    [Test]
    public void TuningIsReplicatedValidatedAndNeverAcceptsMoreThanSixtySeconds()
    {
        var edits = new Dictionary<string, double> { ["items.emp_claw_launch_speed"] = 80, ["items.emp_claw_cruise_speed"] = 30,
            ["items.emp_claw_deceleration"] = 100, ["items.emp_claw_response"] = 8, ["items.emp_claw_acquisition_degrees"] = 10,
            ["items.emp_claw_acquisition_range"] = 90, ["items.emp_claw_terrain_clearance"] = 1.5 };
        Assert.That(_host.TryConfigure(0, edits, out _), Is.True);
        var state = GameplayConfigurationCodec.Decode(GameplayConfigurationCodec.Encode(297, _host.Configuration));
        Assert.That(state.State.Configuration, Is.EqualTo(_host.Configuration.Configuration));
        foreach (var invalid in new[] { ("items.emp_claw_lifetime", 61d), ("items.emp_claw_launch_speed", 200d / 3.6), ("items.emp_claw_acquisition_degrees", 91d) })
        { Assert.That(_host.TryConfigure(0, new Dictionary<string, double> { [invalid.Item1] = invalid.Item2 }, out _), Is.False); }
        var shot = Fire(); Assert.That(shot.Velocity.Length(), Is.EqualTo(80).Within(.001));
    }

    [Test]
    public void CapturedIdentityDeadlineAndContactAreValidatedAcrossWireBoundaries()
    {
        var shot = Fire(2);
        ItemPublication Publication(MissileState value) => new(_host.Items.Revision, _host.Snapshot(), _host.Items.Slots, [value], []);
        Assert.Throws<ArgumentException>(() => Publication(shot with { EmpClaw = shot.EmpClaw! with { Target = 1 } }));
        Assert.Throws<ArgumentException>(() => Publication(shot with { EmpClaw = shot.EmpClaw! with { TargetLife = 0 } }));
        Assert.Throws<ArgumentException>(() => Publication(shot with { EmpClaw = shot.EmpClaw! with { ExpiresAtTick = shot.EmpClaw.ExpiresAtTick + 1 } }));
        var baseline = Publication(shot);
        Step(3);
        byte[] wire = ProjectileMotionCodec.Encode(baseline, _host.World.State.Tick, _host.Items.Missiles);
        Assert.That(ProjectileMotionCodec.Decode(wire, baseline).Missiles, Is.EqualTo(_host.Items.Missiles));
        var position = _host.Items.Missiles.Single().Position;
        _positions[3] = position;
        _ray = (_, _, _) => new(.5f, 3) { Normal = Vector3.UnitZ };
        Step();
        var contact = new ItemPublication(_host.Items.Revision, _host.Snapshot(), _host.Items.Slots, [], _host.Items.Events);
        Assert.That(ItemCodec.DecodeState(ItemCodec.EncodeState(contact)).Events, Is.EqualTo(contact.Events));
        Assert.Throws<ArgumentException>(() => new ItemPublication(contact.Revision, contact.World, contact.Slots, [],
            [contact.Events.Single() with { EmpClawContact = contact.Events.Single().EmpClawContact! with { Vehicle = 1 } }]));
    }

    [Test]
    public void UntrustedAimCandidateCannotBypassOwnerOrEligibilityValidation()
    {
        var slot = _host.Items.Slots.Single().Active;
        Assert.That(_host.Items.RequestAim(_host.World, 1, slot.Life, slot.Token, 0, 10000, -Vector3.UnitZ, 1, slot.Life), Is.False);
        Assert.Throws<ArgumentException>(() => ItemCodec.EncodeAim(297, slot.Life, slot.Token, 0, 1, -Vector3.UnitZ, 2, 0));
        var wire = ItemCodec.EncodeAim(297, slot.Life, slot.Token, 0, 1, -Vector3.UnitZ, 2, 1);
        Assert.That(ItemCodec.DecodeAim(wire).Target, Is.EqualTo(2));
        wire[2] = 28;
        Assert.Throws<ArgumentException>(() => ItemCodec.DecodeAim(wire));
        _positions[2] = new(0, 11.81f, 50);
        var shot = Fire(2);
        Assert.That(shot.EmpClaw!.Target, Is.Zero, "A client-nominated rear target cannot bypass the accepted forward cone.");
    }
}
