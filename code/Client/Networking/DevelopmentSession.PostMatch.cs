using Godot;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Networking;

internal sealed partial class DevelopmentSession
{
    private readonly Button _retryExit = new() { Text = "Retry cleanup" };
    private bool _exitToMenu;
    private bool _quitFromPodium;

    /// <summary>Explicit retained handoff, released on teardown or replaced by accepted recovery state.</summary>
    internal PostMatchContext? PostMatch { get; private set; }
    /// <summary>Recoverable local transition rejection; the retained result remains available.</summary>
    internal string PostMatchStatus { get; private set; } = string.Empty;
    /// <summary>Shared input owner for remapped Podium navigation.</summary>
    internal Input.PlayerInputAdapter? NavigationInput { get; set; }
    /// <summary>Higher overlays suspend Podium focus and navigation.</summary>
    internal Func<bool> OverlayOpen { get; set; } = () => false;
    /// <summary>Composition supplies the existing controlled application shutdown.</summary>
    internal Action QuitApplication { get; set; } = () => { };
    /// <summary>Only current, synchronized host authority may change the whole match.</summary>
    internal bool CanManagePostMatch => PostMatch?.Matches(_lobby?.State) == true && !_leaving && !_exitToMenu &&
        _lobby is { Authority: not null, Reconnecting: false, Failure.Length: 0 } &&
        _lobby.Migration?.Frozen != true && _arena?.Driver is { EntryReady: true, FinalResults: not null };

    /// <summary>Validates scene requests at dispatch time and transfers through existing session owners.</summary>
    internal bool NavigatePostMatch(PostMatchDestination destination)
    {
        if (Stage != ApplicationStage.Podium || PostMatch?.Matches(_lobby?.State) != true)
        {
            return false;
        }

        switch (destination)
        {
            case PostMatchDestination.Rematch:
                if (!CanManagePostMatch) return false;
                if (!_lobby!.Restart())
                {
                    PostMatchStatus = "Rematch could not start. Wait for pending connections to settle, then retry.";
                    return false;
                }
                RemoveArena();
                // Establish Loader immediately; native construction still occurs only in Advance.
                _arenaGeneration = _lobby.State!.Match;
                _matchLoader = new MatchResourceLoader(_lobby.State.Map);
                break;
            case PostMatchDestination.Lobby:
            case PostMatchDestination.EndMatch:
                if (!CanManagePostMatch || !_lobby!.Request(LobbyCommand.Return)) return false;
                RemoveArena();
                break;
            case PostMatchDestination.MainMenu:
            case PostMatchDestination.Quit:
                _exitToMenu = true;
                _quitFromPodium = destination == PostMatchDestination.Quit;
                BeginMenuExit();
                break;
            default:
                return false;
        }

        Render();
        return true;
    }

    private void BeginMenuExit()
    {
        if (_quitFromPodium) QuitApplication();
        else Leave();
    }
}
