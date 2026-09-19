using Trackstorm.Client.Bootstrap;

namespace Trackstorm.Transport.Tests;

/// <summary>Verifies deterministic startup state and recovery contracts.</summary>
internal sealed class StartupFlowTests
{
    /// <summary>Confirms the required successful startup order.</summary>
    [Test]
    public void StartupUsesRequiredDeterministicOrder()
    {
        var flow = new StartupFlow();

        Assert.That(flow.Stage, Is.EqualTo(StartupStage.Preloader));
        flow.ShowSplash();
        Assert.That(flow.Stage, Is.EqualTo(StartupStage.Splash));
        flow.ShowFrontendLoader();
        Assert.That(flow.Stage, Is.EqualTo(StartupStage.FrontendLoading));
        flow.Complete();
        Assert.That(flow.Stage, Is.EqualTo(StartupStage.MainMenu));
    }

    /// <summary>Confirms each recoverable failure returns to the phase that owns it.</summary>
    /// <param name="phase">Failure phase under test.</param>
    /// <param name="expectedStage">Stage entered for the replay.</param>
    [TestCase(StartupFailurePhase.FrontendDependencies, StartupStage.Preloader)]
    [TestCase(StartupFailurePhase.FrontendSetup, StartupStage.FrontendLoading)]
    [TestCase(StartupFailurePhase.ApplicationInitialization, StartupStage.FrontendLoading)]
    public void FailureRetryReplaysOwningPhase(StartupFailurePhase phase, StartupStage expectedStage)
    {
        StartupFlow flow = phase == StartupFailurePhase.FrontendDependencies ? new StartupFlow() : LoadingFlow();

        flow.Fail(phase);
        Assert.That(flow.Stage, Is.EqualTo(StartupStage.Failed));
        Assert.That(flow.FailurePhase, Is.EqualTo(phase));
        Assert.That(flow.Retry(), Is.EqualTo(phase));
        Assert.That(flow.Stage, Is.EqualTo(expectedStage));
        Assert.That(flow.FailurePhase, Is.Null);
    }

    /// <summary>Confirms Main Menu remains gated until a retried application attempt completes.</summary>
    [Test]
    public void ApplicationFailureCannotBypassSuccessfulCompletion()
    {
        var flow = LoadingFlow();

        flow.Fail(StartupFailurePhase.ApplicationInitialization);
        flow.Retry();
        Assert.That(flow.Stage, Is.EqualTo(StartupStage.FrontendLoading));
        flow.Complete();

        Assert.That(flow.Stage, Is.EqualTo(StartupStage.MainMenu));
    }

    /// <summary>Confirms callers cannot skip startup states.</summary>
    [Test]
    public void InvalidTransitionsAreRejected()
    {
        var flow = new StartupFlow();

        Assert.Throws<InvalidOperationException>(flow.Complete);
        Assert.Throws<InvalidOperationException>(() => flow.Fail(StartupFailurePhase.FrontendSetup));
        Assert.Throws<InvalidOperationException>(() => flow.Retry());
        Assert.That(flow.Stage, Is.EqualTo(StartupStage.Preloader));
    }

    private static StartupFlow LoadingFlow()
    {
        var flow = new StartupFlow();
        flow.ShowSplash();
        flow.ShowFrontendLoader();
        return flow;
    }
}
