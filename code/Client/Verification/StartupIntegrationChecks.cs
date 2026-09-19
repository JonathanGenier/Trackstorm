using Godot;
using Trackstorm.Client.Bootstrap;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises production startup order, recovery, and persistent MenuShell ownership.</summary>
internal sealed partial class StartupIntegrationChecks : Node
{
    private readonly List<StartupStage> _stages = [StartupStage.Preloader];
    private StartupController _startup = null!;
    private ulong _loaderBackground;
    private double _seconds;
    private bool _recovered;

    /// <inheritdoc/>
    public override void _Ready() => _startup.StageChanged += OnStageChanged;

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _seconds += delta;
        if (_seconds > 15)
        {
            Fail("startup verification timed out");
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree() => _startup.StageChanged -= OnStageChanged;

    /// <summary>Supplies the production startup owner before this check enters the tree.</summary>
    /// <param name="startup">Startup owner under verification.</param>
    internal void Initialize(StartupController startup) => _startup = startup;

    private void OnStageChanged(StartupStage stage)
    {
        _stages.Add(stage);
        if (stage == StartupStage.Splash)
        {
            Check(_startup.GetNodeOrNull<MenuShell>("MenuShell") is null, "Splash completes before MenuShell exists");
        }
        else if (stage == StartupStage.FrontendLoading)
        {
            ulong current = _startup.BackgroundInstanceId;
            Check(current != 0, "MenuShell background begins with Loader");
            if (_loaderBackground == 0)
            {
                _loaderBackground = current;
            }
            else
            {
                Check(current == _loaderBackground, "Retry preserves MenuShell background");
            }
        }
        else if (stage == StartupStage.Failed)
        {
            Check(GetParent().GetNodeOrNull<Networking.DevelopmentSession>("DevelopmentSession") is null, "Failure cannot enter Main Menu");
            _recovered = true;
            _startup.RetryForVerification();
        }
        else if (stage == StartupStage.MainMenu)
        {
            Check(_recovered, "Failure state recovered through retry");
            Check(_startup.BackgroundInstanceId == _loaderBackground, "Main Menu retains exact Loader background instance");
            Check(GetParent().GetNodeOrNull<Networking.DevelopmentSession>("DevelopmentSession") is not null, "Main Menu exists only after initialization");
            StartupStage[] expected = [StartupStage.Preloader, StartupStage.Splash, StartupStage.FrontendLoading, StartupStage.Failed, StartupStage.FrontendLoading, StartupStage.MainMenu];
            Check(_stages.SequenceEqual(expected), "Startup transition order is explicit and deterministic");
            GD.Print("Startup integration passed: Preloader, Splash, persistent MenuShell Loader, failure recovery, and Main Menu.");
            GetTree().Quit();
        }
    }

    private void Check(bool condition, string message)
    {
        if (!condition)
        {
            Fail(message);
        }
    }

    private void Fail(string message)
    {
        GD.PushError("Startup integration failed: " + message);
        GetTree().Quit(1);
    }
}
