using System.Numerics;
using Trackstorm.Client.Vehicles;
using Trackstorm.Core.Items;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class DrivingWeaponAimTests
{
    [Test]
    public void SuspensionPitchDoesNotChangeRoadRelativeRay()
    {
        Vector3 expected = DrivingWeaponAim.Direction(-Vector3.UnitZ, Vector3.UnitY);
        foreach (float pitch in new[] { -.15f, -.05f, 0, .05f, .15f })
        {
            Vector3 forward = Vector3.Transform(-Vector3.UnitZ, Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch));
            Assert.That(Vector3.Distance(expected, DrivingWeaponAim.Direction(forward, Vector3.UnitY)), Is.LessThan(.00001));
        }
        Assert.That(WeaponAim.BodyDistance(WeaponAim.Pivot, expected, .37f), Is.Null);
        Assert.That((WeaponAim.Pivot + expected * 35).Y, Is.InRange(.5f, 1.1f));
    }

    [TestCase(-.25f)]
    [TestCase(.25f)]
    public void RoadGradeIsPreserved(float slope)
    {
        Vector3 normal = Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(Vector3.UnitX, slope));
        Vector3 ray = DrivingWeaponAim.Direction(-Vector3.UnitZ, normal);
        Assert.That(ray.Length(), Is.EqualTo(1).Within(.00001));
        Assert.That(Vector3.Dot(ray, normal), Is.EqualTo(-MathF.Sin(MathF.PI / 120)).Within(.00001));
    }
}
