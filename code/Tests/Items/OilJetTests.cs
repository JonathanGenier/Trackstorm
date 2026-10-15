using System.Numerics;
using Trackstorm.Core.Items;

namespace Trackstorm.Core.Tests.Items;

[TestFixture]
internal sealed class OilJetTests
{
    [Test]
    public void ExtremeSpringLeanCannotDirectTheJetThroughTheBox()
    {
        Vector3 mouth = OilPour.Mouth(new(0, 0, -.45f));
        Vector3 velocity = OilJet.Velocity(mouth, 18, new(0, -9.81f, 0), 0, 0);
        for (float time = 0; time < .3f; time += .001f)
        {
            Vector3 point = mouth + velocity * time - Vector3.UnitY * (4.905f * time * time);
            Assert.That(point.Z > 2.30f || point.Y > 2.14f, Is.True, $"Jet enters the box at {point}.");
        }
    }

    [TestCase(18f, 9.81f, 0f)]
    [TestCase(18f, 9.81f, .8f)]
    [TestCase(1f, 40f, 1.57f)]
    [TestCase(30f, 1f, -1.57f)]
    [TestCase(18f, 9.81f, 3.141593f)]
    public void BallisticEnvelopeClearsHardwareAcrossThePermittedSpringSphere(float speed, float gravity, float pitch)
    {
        Vector3 pull = Vector3.Transform(-Vector3.UnitY * gravity, Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch));
        for (int sample = 0; sample < 129; sample++)
        {
            float y = 1 - 2 * sample / 128f;
            float horizontal = MathF.Sqrt(Math.Max(0, 1 - y * y));
            Vector3 offset = new Vector3(MathF.Sin(sample * 2.399963f) * horizontal, y, MathF.Cos(sample * 2.399963f) * horizontal) * .45f;
            Vector3 mouth = OilPour.Mouth(offset);
            Vector3 velocity = OilJet.Velocity(mouth, speed, pull, sample % 40, 25);
            Assert.That(velocity.Z, Is.GreaterThan(0));
            bool cleared = false;
            for (float time = 0; time < 5; time += .002f)
            {
                Vector3 point = mouth + velocity * time + pull * (.5f * time * time);
                Assert.That(point.Z > 2.30f || point.Y > 2.14f, Is.True, $"Box crossing: {sample}, {point}.");
                Assert.That(point.Z > 2.56f || point.Y > 1.39f, Is.True, $"Rack crossing: {sample}, {point}.");
                Assert.That(point.Z > 2.95f || point.Y > .64f, Is.True, $"Rear body crossing: {sample}, {point}.");
                if (point.Z >= 3.05f) { cleared = true; break; }
            }
            Assert.That(cleared, Is.True, "The ballistic jet reaches the rear clearance plane.");
        }
    }
}
