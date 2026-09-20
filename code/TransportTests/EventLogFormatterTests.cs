using Trackstorm.Client.Development;
using Trackstorm.Core.Events;

namespace Trackstorm.Transport.Tests;

/// <summary>Protects wording and semantic color boundaries independently of the native renderer.</summary>
[TestFixture]
internal sealed class EventLogFormatterTests
{
    /// <summary>Names matching message words must style only the structured identity positions.</summary>
    [TestCase(EventCategory.Damage, "Applied", "Missile took 3.125 damage from Missile (Missile) — HP 96.875/100", 2)]
    [TestCase(EventCategory.Healing, "Applied", "Missile healed 3.125 from Missile — HP 96.875/100", 1)]
    [TestCase(EventCategory.Developer, "Setting changed", "Missile changed speed: 1 → 3.125", 1)]
    [TestCase(EventCategory.Lifecycle, "Kill", "Missile killed Missile from Missile", 2)]
    [TestCase(EventCategory.Lifecycle, "Dead", "Missile dead from Missile (Missile)", 2)]
    [TestCase(EventCategory.Item, "Used", "Missile Used — Missile — Missile — speed (3.125)", 2)]
    public void WordingAndIdentityPositionsRemainExact(EventCategory category, string kind, string message, int names)
    {
        var entry = new RuntimeEvent
        {
            Sequence = 7, Milliseconds = 125104, Category = category, Kind = kind,
            Actor = 1, Target = 2, ActorName = "Missile", TargetName = "Missile", Cause = "Missile",
            Context = "speed", Amount = 3.125, PreviousValue = 1, RemainingHP = 96.875, MaximumHP = 100,
        };
        var players = new List<string>();
        var timestamps = new List<string>();
        var categories = new List<string>();
        var normal = new List<string>();
        EventLogFormatter.Write(entry, normal.Add, timestamps.Add, categories.Add, players.Add);
        Assert.Multiple(() =>
        {
            Assert.That(EventLogFormatter.Format(entry), Is.EqualTo($"[02:05.104] [{category}] [Host #7] {message}"));
            Assert.That(players, Is.EqualTo(Enumerable.Repeat("Missile", names)));
            Assert.That(timestamps, Is.EqualTo(new[] { "[02:05.104]" }));
            Assert.That(categories, Is.EqualTo(new[] { $"[{category}]" }));
            Assert.That(string.Concat(normal).Contains("Missile", StringComparison.Ordinal), Is.EqualTo(category != EventCategory.Developer));
        });
    }

    /// <summary>Literal text and independent local identity survive styling without parser interpretation.</summary>
    [Test]
    public void LocalDiagnosticTextIsLiteral()
    {
        var entry = new RuntimeEvent { Sequence = 2, Category = EventCategory.Network, Kind = "[b]safe[/b]", Local = true };
        Assert.That(EventLogFormatter.Format(entry), Is.EqualTo("[00:00.000] [Network] [Local #2] [b]safe[/b]"));
    }
}
