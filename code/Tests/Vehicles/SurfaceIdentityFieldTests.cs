using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Tests.Vehicles;

/// <summary>Material boundary contracts are independent of any handling tuning.</summary>
[TestFixture]
internal sealed class SurfaceIdentityFieldTests
{
    [TestCase(0, 0, 0, 0, SurfaceIdentity.Grass)]
    [TestCase(0.5f, 0, 0, 0, SurfaceIdentity.Dirt)]
    [TestCase(1, 0.25f, 0, 0, SurfaceIdentity.Mud)]
    [TestCase(1, 0.65f, 0, 0, SurfaceIdentity.DeepMud)]
    [TestCase(0, 0, 0.5f, 0, SurfaceIdentity.Rock)]
    [TestCase(1, 1, 1, 0.5f, SurfaceIdentity.Water)]
    [TestCase(0.499f, 0.249f, 0.499f, 0.499f, SurfaceIdentity.Grass)]
    public void BoundariesResolveUnambiguously(float dirt, float wet, float rock, float water, SurfaceIdentity expected) =>
        Assert.That(SurfaceIdentityField.Resolve(dirt, wet, rock, water), Is.EqualTo(expected));

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(-0.01f)]
    [TestCase(1.01f)]
    public void MalformedFieldsAreRejected(float value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SurfaceIdentityField.Resolve(value, 0, 0, 0));
}
