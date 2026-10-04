using System.Text.Json;
using Trackstorm.Core.Events;

namespace Trackstorm.Core.Tests;

/// <summary>Only structured historical item causes migrate; identities, context and unrelated text remain untouched.</summary>
internal sealed class ShieldJournalMigrationTests
{
    [TestCase(EventCategory.Item, true)]
    [TestCase(EventCategory.Developer, true)]
    [TestCase(EventCategory.Network, false)]
    public void HistoricalJournalDecodePreservesOutcomeExceptCanonicalItemCause(EventCategory category, bool migrates)
    {
        var historical = new RuntimeEvent
        {
            Sequence = 12, Milliseconds = 1234, Category = category, Kind = "Destroyed", Cause = "Tombstone",
            Actor = 2, Target = 1, ActorName = "Tombstone", Context = "Tombstone", Amount = 125, Tick = 60,
        };
        byte[] bytes = [(byte)'T', (byte)'E', 1, .. JsonSerializer.SerializeToUtf8Bytes(new[] { historical })];
        var decoded = EventCodec.Decode(bytes).Single();
        Assert.That(decoded, Is.EqualTo(historical with { Cause = migrates ? "Shield" : historical.Cause }));
        Assert.That(EventCodec.Decode(EventCodec.Encode(new[] { decoded })).Single(), Is.EqualTo(decoded));
    }
}
