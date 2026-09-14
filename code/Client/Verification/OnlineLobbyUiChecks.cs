using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises the production browser controls with a deterministic provider; no EOS authentication is claimed.</summary>
public sealed partial class OnlineLobbyUiChecks : Node
{
    private readonly UiProvider _provider = new();
    private OnlineLobbyCoordinator _coordinator = null!;
    private DevelopmentSession _session = null!;
    private double _elapsed;
    private int _stage;

    /// <inheritdoc />
    public override void _Ready()
    {
        Engine.MaxFps = 60;
        _coordinator = new OnlineLobbyCoordinator(_provider, new OnlineProductUserId(new string('1', 32)));
        _session = new DevelopmentSession { OnlineCoordinator = () => _coordinator };
        AddChild(_session);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed < 0.4)
        {
            return;
        }

        _elapsed = 0;
        try
        {
            switch (_stage++)
            {
                case 0:
                    Require(Controls<Label>().Any(label => label.Text == "LOCKED"), "Locked row missing.");
                    Require(!Controls<LineEdit>().Any(edit => edit.IsVisibleInTree() && edit.PlaceholderText.Contains("IP:", StringComparison.Ordinal)), "IP field visible in normal flow.");
                    Capture("browser");
                    Edit("Search lobbies").Text = "aRENa";
                    Edit("Search lobbies").EmitSignal(LineEdit.SignalName.TextChanged, "aRENa");
                    break;
                case 1:
                    Require(_coordinator.Browser.Rows.Count == 1, "Search did not filter.");
                    Press("Arena Public");
                    break;
                case 2:
                    Require(_coordinator.Active?.Access == LobbyAccess.Public, "Public join did not proceed directly.");
                    _coordinator.Leave();
                    Edit("Search lobbies").Text = string.Empty;
                    Edit("Search lobbies").EmitSignal(LineEdit.SignalName.TextChanged, string.Empty);
                    break;
                case 3:
                    Press("Private Game");
                    break;
                case 4:
                    Edit("Enter lobby access code").Text = "wrong";
                    Press("Join locked lobby");
                    Require(_coordinator.Active is null, "Wrong credential joined.");
                    Capture("locked-prompt");
                    Edit("Enter lobby access code").Text = "test-code";
                    Press("Join locked lobby");
                    break;
                case 5:
                    Require(_coordinator.Active?.Access == LobbyAccess.Locked, "Correct credential rejected.");
                    _coordinator.Leave();
                    Edit("Lobby name").Text = "My Game";
                    break;
                case 6:
                    Press("Host Game");
                    break;
                case 7:
                    Require(_coordinator.IsHost, "Host control failed.");
                    Edit("Lobby name").Text = "Renamed Game";
                    Press("Rename lobby");
                    break;
                case 8:
                    Require(_coordinator.Active?.Name == "Renamed Game", "Rename control failed.");
                    Capture("host-renamed");
                    _coordinator.Leave();
                    GD.Print("Online lobby UI integration passed: fake-provider browser/search/public/locked/create/rename controls; no native EOS authentication.");
                    GetTree().Quit();
                    break;
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Online lobby UI verification failed at stage {_stage - 1}: {exception.Message}");
            GetTree().Quit(1);
        }
    }

    /// <inheritdoc />
    public override void _ExitTree() => _coordinator?.Dispose();

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private IEnumerable<T> Controls<T>()
        where T : Node
        => Descendants(_session).OfType<T>();

    private LineEdit Edit(string prefix) => Controls<LineEdit>().First(edit => edit.PlaceholderText.StartsWith(prefix, StringComparison.Ordinal));
    private void Press(string prefix) => Controls<Button>().First(button => button.IsVisibleInTree() && (button.Text.StartsWith(prefix, StringComparison.Ordinal) || button.TooltipText.StartsWith(prefix, StringComparison.Ordinal))).EmitSignal(Button.SignalName.Pressed);

    private void Capture(string name)
    {
        if (DisplayServer.GetName() != "headless")
        {
            string directory = ProjectSettings.GlobalizePath("res://.godot/online-lobby-checks");
            System.IO.Directory.CreateDirectory(directory);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(directory, name + ".png"));
        }
    }

    private sealed class UiProvider : IOnlineLobbyProvider
    {
        private readonly OnlineProductUserId _local = new(new string('1', 32));
        private readonly OnlineProductUserId _remote = new(new string('2', 32));
        private readonly LobbyCredential _credential = LobbyCredential.Create("test-code");
        private OnlineLobby? _active;
        public void Search(Action<IReadOnlyList<OnlineLobby>, string?> completed) => completed(new[] { new OnlineLobby("public", "Arena Public", _remote, 100, LobbyAccess.Public, 2, 8, OnlineLobby.CurrentProtocol, true, null), new OnlineLobby("locked", "Private Game", _remote, 200, LobbyAccess.Locked, 3, 8, OnlineLobby.CurrentProtocol, true, _credential) }, null);
        public void Create(OnlineLobby lobby, Action<OnlineLobby?, string?> completed)
        {
            _active = lobby with { Id = "hosted", Owner = _local };
            completed(_active, null);
        }

        public void Join(string id, Action<OnlineLobby?, string?> completed) => Search((rows, _) =>
        {
            _active = rows.Single(row => row.Id == id);
            completed(_active, null);
        });
        public void Update(OnlineLobby lobby, Action<OnlineLobby?, string?> completed)
        {
            _active = lobby;
            completed(lobby, null);
        }

        public void SetJoinable(string id, bool open, Action<OnlineLobby?, string?> completed) => completed(_active, null);
        public void Leave(string id, bool destroy, Action<string?> completed)
        {
            _active = null;
            completed(null);
        }

        public IDisposable Watch(string id, Action<OnlineLobby?> changed) => new Subscription();
        public void Dispose()
        {
        }
    }

    private sealed class Subscription : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
