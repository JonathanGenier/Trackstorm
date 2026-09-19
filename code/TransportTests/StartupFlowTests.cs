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

    /// <summary>Confirms failure cannot bypass a new loader attempt.</summary>
    [Test]
    public void InitializationFailureMustRecoverThroughLoader()
    {
        var flow = LoadingFlow();

        flow.Fail();
        Assert.That(flow.Stage, Is.EqualTo(StartupStage.Failed));
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
