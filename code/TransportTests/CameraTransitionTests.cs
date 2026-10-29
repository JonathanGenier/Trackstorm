using System.Numerics;
using Trackstorm.Client.Vehicles;

namespace Trackstorm.Transport.Tests;

[TestFixture]
internal sealed class CameraTransitionTests
{
    [Test]
    public void BurstyMousePacketsContinueMovingBetweenSamplesWithoutLosingTravel()
    {
        var filter = new CameraLookFilter();
        float total = 0;
        float maximumStep = 0;
        for (int frame = 0; frame < 144; frame++)
        {
            float movement = filter.Advance(frame % 3 == 0 ? new(.03f, 0) : default, default, 1f / 144, true).X;
            Assert.That(movement, Is.GreaterThan(0), "Render frames between input packets must not freeze");
            maximumStep = Math.Max(maximumStep, movement); total += movement;
        }
        Assert.That(maximumStep, Is.LessThan(.02f), "Packet steps are softened without delaying an entire sample");
        total += filter.Advance(default, default, .2f, false).X;
        Assert.That(total, Is.EqualTo(1.44f).Within(.00001));
    }
    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void MousePacketsConserveDisplacementAndSettlePromptly(int fps)
    {
        var filter = new CameraLookFilter();
        Vector2 total = default;
        for (int i = 0; i < fps; i++) total += filter.Advance(new(.9f / fps, -.3f / fps), default, 1f / fps, true);
        Assert.That(total.X, Is.InRange(.88f, .901f));
        for (int i = 0; i < fps / 5; i++) total += filter.Advance(default, default, 1f / fps, true);
        Assert.That(total.X, Is.EqualTo(.9f).Within(.00001));
        Assert.That(total.Y, Is.EqualTo(-.3f).Within(.00001));
        filter.Reset();
        Assert.That(filter.Advance(default, default, 1f / fps, true), Is.EqualTo(Vector2.Zero));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void ControllerStartsImmediatelyAndStopsWithoutDeferredRotation(int fps)
    {
        var filter = new CameraLookFilter();
        Vector2 total = default;
        for (int i = 0; i < fps; i++)
        {
            Vector2 movement = filter.Advance(default, new(2.2f, 0), 1f / fps, false);
            Assert.That(movement.X, Is.GreaterThan(0));
            total += movement;
        }
        Assert.That(total.X, Is.EqualTo(2.2f * (1 - 1f / 60)).Within(.00001));
        Assert.That(filter.Advance(default, default, 1f / fps, false), Is.EqualTo(Vector2.Zero));
    }

    [TestCase(30)]
    [TestCase(60)]
    [TestCase(144)]
    public void TacticalFramingEntersReturnsAndSurvivesRepeatedReversals(int fps)
    {
        var transition = new CameraAimTransition();
        transition.Advance(true, 1f / fps);
        Assert.That(transition.Amount, Is.InRange(.001f, .11f));
        for (int i = 1; i < fps; i++) transition.Advance(true, 1f / fps);
        Assert.That(transition.Amount, Is.GreaterThan(.999f));
        for (int i = 0; i < fps; i++) transition.Advance(false, 1f / fps);
        Assert.That(transition.Amount, Is.LessThan(.00001f));
        for (int i = 0; i < fps * 10; i++)
        {
            float previous = transition.Amount;
            transition.Advance(i % 10 < 5, 1f / fps);
            Assert.That(transition.Amount, Is.InRange(0f, 1f));
            Assert.That(Math.Abs(transition.Amount - previous), Is.LessThan(6f / fps));
        }
        transition.Reset();
        Assert.That(transition.Amount, Is.Zero);
    }
}
