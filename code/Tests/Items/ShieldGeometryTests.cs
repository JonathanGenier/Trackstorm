using System.Numerics;
using Trackstorm.Core.Items;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class ShieldGeometryTests
{
    private static ShieldState Wall => new(1, 1, 0, 0, ShieldStage.WorldWall, 1000);

    [TestCase(-1)]
    [TestCase(1)]
    public void WingsInterceptAtAngledFaceAndLeaveFormerStraightFootprintOpen(int side)
    {
        var wall = Wall;
        Assert.That(ShieldGeometry.Intersect(wall, new(side * 2.7f, 0, -.1f), new(side * 2.7f, 0, .1f)), Is.Null);
        Assert.That(ShieldGeometry.Intersect(wall, new(side * 2.4f, 0, -2), new(side * 2.4f, 0, 1)), Is.Not.Null);
        Assert.That(ShieldGeometry.ClosestPoint(wall, default, new(side * 2.4f, 0, -.55f)), Is.EqualTo(new Vector3(side * 2.4f, 0, -.55f)));
        Assert.That(ShieldGeometry.Intersect(wall, new(0, 0, -2), new(0, 0, 2)), Is.Not.Null);
    }

    [Test]
    public void CapturedScaleAndOrientationTransformAllThreePanelsTogether()
    {
        var wall = Wall with { Position = new(10, 4, -8), Orientation = Quaternion.CreateFromYawPitchRoll(.7f, .2f, -.3f), WallSize = new(12, 6, 2) };
        var scale = ShieldGeometry.DeployedScale(wall.WallSize);
        Vector3 World(Vector3 p) => wall.Position + Vector3.Transform(p * scale, wall.Orientation);
        Assert.That(ShieldGeometry.Intersect(wall, World(new(2.4f, 0, -2)), World(new(2.4f, 0, 1))), Is.Not.Null);
        Assert.That(ShieldGeometry.Intersect(wall, World(new(2.7f, 0, -.1f)), World(new(2.7f, 0, .1f))), Is.Null);
        Assert.That(ShieldGeometry.DeployedCorners(wall.WallSize), Has.Count.EqualTo(3));
        Assert.That(ShieldGeometry.Overlaps(wall, wall), Is.True);
        Assert.That(ShieldGeometry.Overlaps(wall, wall with { Position = wall.Position + new Vector3(30, 0, 0) }), Is.False);
    }

    [Test]
    public void ClosestPointLiesOnWingRatherThanFormerStraightBox()
    {
        var point = new Vector3(2.4f, 0, 0);
        var closest = ShieldGeometry.ClosestPoint(Wall, default, point);
        Assert.That(closest.Z, Is.LessThan(-.1f));
        Assert.That(Vector3.Distance(closest, point), Is.LessThan(.55f));
        Assert.That(ShieldGeometry.Intersect(Wall, closest - Vector3.UnitZ * .01f, closest + Vector3.UnitZ * .01f), Is.Not.Null);
    }
}
