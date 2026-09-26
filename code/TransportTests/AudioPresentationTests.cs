using System.Numerics;
using Trackstorm.Client.Audio;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Networking.Replication;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Audio decisions remain pure Client logic with controllable time and randomness.</summary>
[TestFixture]
internal sealed class AudioPresentationTests
{
    [Test]
    public void SalvoNextShotCapabilityDoesNotSoundLikeAnotherPickup()
    {
        var projection = new AudioEventProjection();
        var cues = new List<AudioCue>();
        projection.Cue += (cue, _) => cues.Add(cue);
        var world = new WorldSnapshot(1, 1, [new ReplicatedVehicle(State(), 0)]);
        var slot = new ItemSlot(1, 1, 1, HeldItem.Salvo);
        projection.Items(new(1, world, [slot], [], []));
        projection.Items(new(2, world, [slot with { Token = 2, SalvoShots = 4 }], [], [new(1, 1, HeldItem.Salvo, Vector3.Zero, false)]));
        Assert.That(cues, Is.EqualTo(new[] { AudioCue.MissileFire }));
        projection.Items(new(3, world, [new(1, 1, 3, HeldItem.Salvo)], [], []));
        Assert.That(cues.Last(), Is.EqualTo(AudioCue.WeaponPickup));
    }

    /// <summary>Every starting position advances through the defined sequence with one random call.</summary>
    [Test]
    public void PlaylistCyclesAndResets()
    {
        for (int start = 0; start < 3; start++)
        {
            int calls = 0;
            var playlist = new ArenaPlaylist(count =>
            {
                Assert.That(count, Is.EqualTo(3));
                calls++;
                return (start + calls - 1) % 3;
            });
            Assert.That(playlist.Advance(), Is.False);
            Assert.That(playlist.Start(), Is.True);
            Assert.That(playlist.Start(), Is.False);
            for (int step = 0; step < 9; step++)
            {
                Assert.That(playlist.Index, Is.EqualTo((start + step) % 3));
                playlist.Advance();
            }

            Assert.That(calls, Is.EqualTo(1));
            playlist.Stop();
            playlist.Stop();
            Assert.That(playlist.Index, Is.EqualTo(-1));
            Assert.That(playlist.Advance(), Is.False);
            playlist.Start();
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(playlist.Index, Is.EqualTo((start + 1) % 3), "Re-entry uses a fresh selector result, not the previous index.");
        }

        foreach (int invalid in new[] { -1, 3 })
        {
            var playlist = new ArenaPlaylist(_ => invalid);
            Assert.Throws<InvalidOperationException>(() => playlist.Start());
            Assert.That(playlist.Active, Is.False);
        }
    }

    /// <summary>All event slots map to the expected existing settings hierarchy.</summary>
    [Test]
    public void CategoriesRouteExplicitly()
    {
        AudioCue[] vehicle = [AudioCue.EngineIdle, AudioCue.EngineLow, AudioCue.EngineHigh, AudioCue.Skid, AudioCue.Collision, AudioCue.HeavyCollision, AudioCue.Damage, AudioCue.Destruction];
        AudioCue[] weapons = [AudioCue.MachineGunFire, AudioCue.MissileFire, AudioCue.MissileTravel, AudioCue.MissileImpact, AudioCue.Explosion];
        foreach (AudioCue cue in Enum.GetValues<AudioCue>())
        {
            string expected = vehicle.Contains(cue) ? "Vehicle" : weapons.Contains(cue) ? "Weapons" : cue == AudioCue.Ambience ? "SFX" : "UI";
            Assert.That(AudioRouting.Bus(cue), Is.EqualTo(expected), cue.ToString());
        }
    }

    /// <summary>Muted, clamped and invalid gains match the settings contract.</summary>
    [Test]
    public void SettingsGainBounds()
    {
        Assert.That(AudioRouting.Gain(-1), Is.Zero);
        Assert.That(AudioRouting.Gain(2), Is.EqualTo(1));
        Assert.That(AudioRouting.Gain(double.NaN), Is.EqualTo(1));
        Assert.That(AudioRouting.Gain(double.PositiveInfinity), Is.EqualTo(1));
        Assert.That(AudioRouting.Decibels(0), Is.EqualTo(-80));
        Assert.That(AudioRouting.Decibels(1), Is.Zero);
        Assert.That(AudioRouting.Decibels(0.5), Is.EqualTo(-6.0206).Within(0.0001));
    }

    /// <summary>Suppressed identities never become audible later; later valid events do.</summary>
    [Test]
    public void CooldownConsumesDuplicatesAndFloods()
    {
        var gate = new AudioCooldown();
        Assert.That(gate.TryAccept(1, 1, 0.25), Is.True);
        Assert.That(gate.TryAccept(1, 2, 0.25), Is.False);
        Assert.That(gate.TryAccept(2, 1.1, 0.25), Is.False);
        Assert.That(gate.TryAccept(2, 3, 0.25), Is.False);
        Assert.That(gate.TryAccept(3, 1.25, 0.25), Is.True);
        Assert.That(gate.TryAccept(4, double.NaN, 0.25), Is.False);
        gate.Seed(20);
        Assert.That(gate.TryAccept(20, 5, 0.25), Is.False);
        Assert.That(gate.TryAccept(21, 5, 0.25), Is.True);
    }

    /// <summary>Equal-power gains stay continuous and finite through both crossfade intervals.</summary>
    [Test]
    public void EngineCrossfadeIsContinuous()
    {
        Assert.That(EngineMix.Select(0).Idle, Is.EqualTo(1));
        Assert.That(EngineMix.Select(11.2f).Low, Is.EqualTo(1).Within(1e-5));
        Assert.That(EngineMix.Select(28).High, Is.EqualTo(1).Within(1e-5));
        EngineMix previous = EngineMix.Select(0);
        for (int step = 1; step <= 400; step++)
        {
            EngineMix current = EngineMix.Select(step / 10f);
            Assert.That((current.Idle * current.Idle) + (current.Low * current.Low) + (current.High * current.High), Is.EqualTo(1).Within(1e-5));
            Assert.That(Math.Abs(current.Idle - previous.Idle), Is.LessThan(0.02));
            Assert.That(Math.Abs(current.Low - previous.Low), Is.LessThan(0.02));
            Assert.That(Math.Abs(current.High - previous.High), Is.LessThan(0.02));
            previous = current;
        }

        Assert.That(EngineMix.Select(float.NaN).Idle, Is.EqualTo(1));
        Assert.That(EngineMix.Select(-10), Is.EqualTo(EngineMix.Select(10)));
    }

    /// <summary>Damage, death and respawn have independent identities across duplicate and delayed boundaries.</summary>
    [Test]
    public void VehicleFeedbackIsOncePerBoundary()
    {
        var projection = new AudioEventProjection { LocalVehicle = 1 };
        var cues = new List<AudioCue>();
        projection.Cue += (cue, _) => cues.Add(cue);
        projection.Vehicles([State()]);
        VehicleSnapshot damage = State(tick: 10, hp: 90, sequence: 1);
        projection.Vehicles([damage]);
        projection.Vehicles([damage]);
        Assert.That(cues, Is.EqualTo(new[] { AudioCue.Collision, AudioCue.Damage }));
        projection.Vehicles([State(tick: 11, hp: 80, sequence: 2)]);
        Assert.That(cues.Count, Is.EqualTo(2));
        VehicleSnapshot dead = State(tick: 30, hp: 0, sequence: 3);
        projection.Vehicles([dead]);
        projection.Vehicles([dead]);
        Assert.That(cues.Count(cue => cue == AudioCue.Destruction), Is.EqualTo(1));
        Assert.That(cues.Count(cue => cue == AudioCue.Death), Is.EqualTo(1));
        projection.Vehicles([State(life: 2, tick: 40)]);
        projection.Vehicles([dead]);
        Assert.That(cues.Count(cue => cue == AudioCue.Respawn), Is.EqualTo(1));
        int before = cues.Count;
        projection.Vehicles([State(life: 3, tick: 50, hp: 0, sequence: 1)], true);
        projection.Vehicles([State(life: 3, tick: 50, hp: 0, sequence: 1)]);
        Assert.That(cues.Count, Is.EqualTo(before));
    }

    /// <summary>Reliable revisions and outcome identities suppress repeats while preserving full-health repair feedback.</summary>
    [Test]
    public void ItemsInitializeAndDeduplicate()
    {
        var projection = new AudioEventProjection();
        var cues = new List<AudioCue>();
        projection.Cue += (cue, _) => cues.Add(cue);
        var world = new WorldSnapshot(1, 1, [new ReplicatedVehicle(State(), 0)]);
        projection.Items(new ItemPublication(1, world, [new ItemSlot(1, 1, 1, HeldItem.Missile)], [], []));
        Assert.That(cues, Is.Empty);
        var repair = new ItemEvent(2, 1, HeldItem.Wrench, Vector3.Zero, false);
        var publication = new ItemPublication(2, world, [new ItemSlot(1, 1, 2, HeldItem.Wrench)], [], [repair]);
        projection.Items(publication);
        projection.Items(publication);
        projection.Items(new ItemPublication(3, world, publication.Slots, [], [repair]));
        Assert.That(cues, Is.EqualTo(new[] { AudioCue.WrenchPickup, AudioCue.WrenchUse }));
        projection.Items(new ItemPublication(4, world, [new ItemSlot(1, 1, 3, HeldItem.Missile)], [], []), true);
        Assert.That(cues.Count, Is.EqualTo(2));
        var historicalLaunch = new ItemEvent(3, 1, HeldItem.Missile, Vector3.Zero, false);
        projection.Items(new ItemPublication(5, world, [], [], [historicalLaunch]), true);
        projection.Items(new ItemPublication(6, world, [], [], [historicalLaunch]));
        Assert.That(cues.Count, Is.EqualTo(2), "A reseeded outcome must remain consumed in subsequent revisions.");
    }

    /// <summary>Countdown changes play once and initial or resynchronized finished states stay quiet.</summary>
    [Test]
    public void MatchFeedbackUsesAuthoritativeTransitions()
    {
        var projection = new AudioEventProjection();
        var cues = new List<AudioCue>();
        projection.Cue += (cue, _) => cues.Add(cue);
        projection.Match(new MatchState(0, 1, 5, MatchPhase.Waiting, null, null, []));
        var countdown = new MatchState(1, 2, 5, MatchPhase.Countdown, 181, null, []);
        projection.Match(countdown);
        projection.Countdown(countdown, 1);
        projection.Countdown(countdown, 2);
        projection.Countdown(countdown, 61);
        var active = new MatchState(181, 3, 5, MatchPhase.Active, null, null, []);
        projection.Match(active);
        projection.Match(active);
        projection.Match(active, true);
        Assert.That(cues, Is.EqualTo(new[] { AudioCue.Countdown, AudioCue.Countdown, AudioCue.MatchStart }));
    }

    [Test]
    public void MachineGunSoundsEachNewRoundButNeverDuplicateOrSeededPublications()
    {
        var projection = new AudioEventProjection();
        var cues = new List<AudioCue>();
        projection.Cue += (cue, _) => cues.Add(cue);
        var world = new WorldSnapshot(1, 1, [new ReplicatedVehicle(State(), 0)]);
        var slot = new ItemSlot(1, 1, 1, HeldItem.MachineGun) { Ammo = new(500, 500) };
        projection.Items(new(1, world, [slot], [], []));
        var shot = new ItemEvent(1, 1, HeldItem.MachineGun, Vector3.UnitZ, true);
        var publication = new ItemPublication(2, world, [slot with { Ammo = new(499, 500) }], [], [shot]);
        projection.Items(publication);
        projection.Items(publication);
        projection.Items(new(3, world, [slot with { Ammo = new(498, 500) }], [], [shot]));
        projection.Items(new(4, world, [slot], [], [shot]), true);
        Assert.That(cues, Is.EqualTo(new[] { AudioCue.MachineGunFire, AudioCue.MachineGunFire }));
    }

    private static VehicleSnapshot State(ulong life = 1, ulong tick = 1, float hp = 100, ulong sequence = 0)
    {
        var physics = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, Vector3.Zero, Vector3.Zero);
        DamageEvent? damage = sequence == 0 ? null : new DamageEvent(sequence, tick, 10, new DamageContext("collision", 0, "world"), hp == 0);
        return new VehicleSnapshot(1, life, new VehicleState(tick, physics, true, false, 0, 0), new VehicleDamageState(100, hp, damage, damage?.Tick), physics);
    }
}
