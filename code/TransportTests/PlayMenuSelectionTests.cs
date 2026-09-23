using Trackstorm.Client.Frontend;
using Trackstorm.Client.Online;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class PlayMenuSelectionTests
{
    private static LobbyRow Row(string id, LobbyAccess access = LobbyAccess.Public, int members = 2, bool joinable = true, string mode = "Circus") => new(id, "Circus " + id, members, 8, access, joinable, "1", "") { GameMode = mode };

    [Test]
    public void FiltersCombineWithSearchAndDoNotChangeSource()
    {
        LobbyRow[] rows = [Row("a"), Row("b", LobbyAccess.Locked), Row("c", members: 8, joinable: false), Row("d", mode: "FirstToTarget")];
        var model = new PlayMenuSelection { Filter = "Open Only" };
        Assert.That(model.Apply(rows, "CIRCUS a").Select(row => row.Id), Is.EqualTo(new[] { "a" }));
        model.Filter = "Locked Only";
        Assert.That(model.Apply(rows, "").Select(row => row.Id), Is.EqualTo(new[] { "b" }));
        model.Filter = "Has Space";
        Assert.That(model.Apply(rows, ""), Has.Length.EqualTo(3));
        model.Filter = "Mode: FirstToTarget";
        Assert.That(model.Apply(rows, "").Select(row => row.Id), Is.EqualTo(new[] { "d" }));
        Assert.That(rows[2].Joinable, Is.False);
        Assert.That(PlayMenuSelection.Choices(rows), Does.Contain("Mode: Circus"));
        Assert.That(PlayMenuSelection.Choices([rows[0]]), Has.Length.EqualTo(4));
    }

    [Test]
    public void AcceptRequiresSameJoinableRowWithinBoundAndResetsOnSelectionOrRefresh()
    {
        var model = new PlayMenuSelection();
        Assert.That(model.Accept(Row("a"), 0), Is.False);
        Assert.That(model.Accept(Row("a"), 0.46), Is.False);
        Assert.That(model.Accept(Row("b"), 0.5), Is.False);
        Assert.That(model.Accept(Row("b"), 0.7), Is.True);
        Assert.That(model.Accept(Row("b"), 0.8), Is.False);
        model.Select(null);
        Assert.That(model.Accept(Row("b"), 0.9), Is.False);
        model.Reset();
        Assert.That(model.Accept(Row("b"), 1), Is.False);
        Assert.That(model.Accept(Row("b", joinable: false), 1.1), Is.False);
        Assert.That(model.Accept(Row("b"), 1.2), Is.False);
    }

    [Test]
    public void MouseDoubleClickCannotJoinAReplacementRowOrSurviveRefresh()
    {
        var model = new PlayMenuSelection();
        Assert.That(model.MousePress(Row("a"), false), Is.False);
        Assert.That(model.MousePress(Row("b"), true), Is.False);
        model.Reset();
        Assert.That(model.MousePress(Row("b"), true), Is.False);
        Assert.That(model.MousePress(Row("b"), true), Is.True);
        Assert.That(model.MousePress(Row("b", joinable: false), true), Is.False);
    }

    [TestCase(0)]
    [TestCase(100)]
    public void AllCountsRemainDynamic(int count)
    {
        var model = new PlayMenuSelection();
        Assert.That(model.Apply(Enumerable.Range(0, count).Select(i => Row(i.ToString())), ""), Has.Length.EqualTo(count));
    }
}
