using System.Numerics;
using Trackstorm.Client.Hud;
using Trackstorm.Core.Items;
using Trackstorm.Core.Matches;
using Trackstorm.Core.Settings;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Transport.Tests;

/// <summary>Pure Client presentation checks, without a Godot scene tree.</summary>
[TestFixture]
internal sealed class CombatHudTests
{
    [Test]
    public void NonCircusModeClearsCircusPresentationAndNextMatchStartsWithoutFeedback()
    {
        var feedback = new CircusScoreFeedback();
        var circus = new MatchState(10, 5, 5, MatchPhase.Active, null, null,
            [new PlayerScore(1, 0, 0, 0, 0) { CircusScore = 100 }]);
        Assert.That(feedback.Project(circus, 1, 0)!.Total, Is.EqualTo("100"));
        var combat = new MatchState(0, 1, 5, MatchPhase.Waiting, null, null,
            [new PlayerScore(1, 0, 0, 0, 0)], mode: MatchMode.FirstToTarget);
        Assert.That(feedback.Project(combat, 1, 10), Is.Null);
        var fresh = new MatchState(0, 1, 5, MatchPhase.Waiting, null, null, combat.Players);
        var projected = feedback.Project(fresh, 1, 20)!;
        Assert.That(projected.Total, Is.EqualTo("0"));
        Assert.That(projected.Multiplier, Is.EqualTo("x1"));
        Assert.That(projected.Rows, Is.Empty);
    }

    /// <summary>Units change numbers, never the common visual scale.</summary>
    [Test]
    public void SpeedUnitsAndVisualScale()
    {
        foreach (SpeedUnit unit in Enum.GetValues<SpeedUnit>())
        {
            Assert.That(CombatHudView.ConvertSpeed(0, unit), Is.Zero);
            Assert.That(CombatHudView.NormalizeSpeed(0), Is.Zero);
            Assert.That(CombatHudView.NormalizeSpeed(100 / 3.6), Is.EqualTo(0.5).Within(1e-10));
            Assert.That(CombatHudView.NormalizeSpeed(200 / 3.6), Is.EqualTo(1).Within(1e-10));
            Assert.That(CombatHudView.NormalizeSpeed(80), Is.EqualTo(1));
        }

        Assert.That(CombatHudView.ConvertSpeed(10, SpeedUnit.KilometresPerHour), Is.EqualTo(36));
        Assert.That(CombatHudView.ConvertSpeed(10, SpeedUnit.MilesPerHour), Is.EqualTo(22.3693629).Within(1e-6));
        Assert.That(CombatHudView.UnitSuffix(SpeedUnit.KilometresPerHour), Is.EqualTo("km/h"));
        Assert.That(CombatHudView.UnitSuffix(SpeedUnit.MilesPerHour), Is.EqualTo("mph"));
        Assert.That(CombatHudView.NormalizeSpeed(double.NaN), Is.Zero);
        Assert.That(CombatHudView.NormalizeSpeed(-1), Is.Zero);
    }

    /// <summary>Health always uses actual capacity, including zero and future vehicles.</summary>
    [Test]
    public void HealthFormattingAndNormalization()
    {
        Assert.That(CombatHudView.FormatHealth(0, 1000), Is.EqualTo("0/1000"));
        Assert.That(CombatHudView.FormatHealth(850, 1000), Is.EqualTo("850/1000"));
        Assert.That(CombatHudView.FormatHealth(1000, 1000), Is.EqualTo("1000/1000"));
        Assert.That(CombatHudView.NormalizeHealth(0, 1000), Is.Zero);
        Assert.That(CombatHudView.NormalizeHealth(1000, 1000), Is.EqualTo(1));
        Assert.That(CombatHudView.NormalizeHealth(750, 1500), Is.EqualTo(0.5));
        Assert.That(CombatHudView.NormalizeHealth(1500, 1000), Is.EqualTo(1));
        Assert.That(CombatHudView.NormalizeHealth(1, 0), Is.Zero);
    }

    /// <summary>Source identity, life and health are untouched across live projections.</summary>
    [Test]
    public void SnapshotChangesAndItemMappingAreReadOnly()
    {
        VehicleSnapshot initial = State(850, 1000, 10);
        foreach (HeldItem item in Enum.GetValues<HeldItem>())
        {
            var slot = new ItemSlot(1, 1, 7, item) { SecondToken = 8, SecondItem = HeldItem.Oil, ActiveSlot = 1 };
            CombatHudView view = CombatHudView.From(initial, slot, SpeedUnit.KilometresPerHour);
            Assert.That(view.Item, Is.EqualTo(item));
            Assert.That(view.SecondItem, Is.EqualTo(HeldItem.Oil));
            Assert.That(view.ActiveSlot, Is.EqualTo(1));
            Assert.That(CombatHudView.From(initial, slot with { Vehicle = 2 }, 0).SecondItem, Is.EqualTo(HeldItem.None));
            Assert.That(view.ItemName, Is.EqualTo(item == HeldItem.None ? "EMPTY" : item == HeldItem.Nitro ? "NITRO 100%" : item.ToString().ToUpperInvariant()));
            Assert.That(view.Standing, Is.EqualTo("--"));
            Assert.That(view.Timer, Is.EqualTo("--:--"));
            Assert.That(view.HealthFill, Is.EqualTo(0.85));
            Assert.That(view.Speed, Is.EqualTo("36"));
            Assert.That(slot.Item, Is.EqualTo(item));
        }

        Assert.That(CombatHudView.From(initial, new ItemSlot(2, 1, 1, HeldItem.Missile), 0).Item, Is.EqualTo(HeldItem.None));
        Assert.That(CombatHudView.From(initial, new ItemSlot(1, 2, 1, HeldItem.Wrench), 0).Item, Is.EqualTo(HeldItem.None));
        Assert.That(CombatHudView.From(State(0, 1000, 0), new ItemSlot(1, 1, 1, HeldItem.Wrench), 0).Item, Is.EqualTo(HeldItem.None));
        var changed = CombatHudView.From(State(375, 1500, 200 / 3.6f), new ItemSlot(1, 1, 8, HeldItem.Missile), SpeedUnit.MilesPerHour);
        Assert.That(changed.Health, Is.EqualTo("375/1500"));
        Assert.That(changed.HealthFill, Is.EqualTo(0.25));
        Assert.That(changed.Speed, Is.EqualTo("124"));
        Assert.That(changed.SpeedFill, Is.EqualTo(1).Within(1e-6));
        Assert.That(initial.Damage.CurrentHP, Is.EqualTo(850));
        Assert.That(initial.Speed, Is.EqualTo(10));
    }

    /// <summary>Pending, banked and death-lost rows use authoritative snapshots and expire by category.</summary>
    [Test]
    public void CircusProjectionUpdatesCategoriesInPlaceAndDoesNotReplayInitialAwards()
    {
        var feedback = new CircusScoreFeedback();
        var pending = new StuntState
        {
            Life = 1,
            Tick = 10,
            Drift = new StuntProgress(10, 12.5),
            Airtime = new StuntProgress(8, 4),
            JumpOrigin = System.Numerics.Vector3.One,
            JumpDistance = 3,
            LongJumpBasePoints = 6,
        };
        var first = new MatchState(10, 1, 5, MatchPhase.Active, null, null,
            [new PlayerScore(1, 0, 0, 0, 0) { CircusScore = 125.5, Stunts = pending }],
            awards: [new CircusScoreAward(1, CircusScoreCategory.Kill, 125.5)]);
        CircusHudView initial = feedback.Project(first, 1, 0)!;
        Assert.That(initial.Total, Is.EqualTo("125.5"));
        Assert.That(initial.Multiplier, Is.EqualTo("x1"));
        Assert.That(initial.Rows.Select(row => row.Category), Is.EqualTo(new[] { CircusScoreCategory.Drift, CircusScoreCategory.Airtime, CircusScoreCategory.LongJump }));
        Assert.That(initial.Rows.All(row => row.Kind == CircusFeedbackKind.Pending), Is.True, "Initial publication is state, not historical award replay.");

        var continuing = new MatchState(11, 2, 5, MatchPhase.Active, null, null,
            [first.Players[0] with { CircusScore = 250.5, Stunts = pending with { Tick = 11, Drift = new StuntProgress(11, 14) } }],
            awards:
            [
                new CircusScoreAward(1, CircusScoreCategory.Collision, 25),
                new CircusScoreAward(1, CircusScoreCategory.Kill, 100),
            ]);
        CircusHudView live = feedback.Project(continuing, 1, 100)!;
        Assert.That(live.Rows.Count, Is.EqualTo(5));
        Assert.That(live.Rows.Single(row => row.Category == CircusScoreCategory.Drift).Points, Is.EqualTo(14));
        Assert.That(live.Rows.Single(row => row.Category == CircusScoreCategory.Kill).Kind, Is.EqualTo(CircusFeedbackKind.Banked));

        var death = new MatchState(12, 3, 5, MatchPhase.Active, null, null,
            [continuing.Players[0] with { Deaths = 1, KillStreak = 0, ProcessedLife = 1, Stunts = null }],
            [new ScoredDeath(1, 1, 0)]);
        CircusHudView lost = feedback.Project(death, 1, 200)!;
        Assert.That(lost.Rows.Single(row => row.Category == CircusScoreCategory.Drift).Kind, Is.EqualTo(CircusFeedbackKind.Lost));
        Assert.That(lost.Rows.Single(row => row.Category == CircusScoreCategory.Airtime).Kind, Is.EqualTo(CircusFeedbackKind.Lost));
        Assert.That(feedback.Project(death, 1, 2100)!.Rows, Is.Empty);
    }

    private static VehicleSnapshot State(float hp, float max, float speed)
    {
        var pose = new VehiclePhysicsState(Vector3.Zero, Quaternion.Identity, new Vector3(speed, 0, 0), Vector3.Zero);
        return new VehicleSnapshot(1, 1, new VehicleState(0, pose, false, false, 0, 0), new VehicleDamageState(max, hp, null, null), pose);
    }
}
