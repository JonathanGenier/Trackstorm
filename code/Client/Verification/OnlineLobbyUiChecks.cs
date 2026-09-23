using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

/// <summary>Exercises the production browser controls with a deterministic provider; no EOS authentication is claimed.</summary>
public sealed partial class OnlineLobbyUiChecks : Node
{
    private readonly UiProvider _provider = new();
    private readonly EosLobbyStatus[] _identityStates =
    {
        EosLobbyStatus.Initializing,
        EosLobbyStatus.FromIdentity(OnlineIdentityState.LoggingIn, false, true, null),
        new("EOS: Configuration invalid. Correct the embedded values or explicit TRACKSTORM_EOS_CONFIG override, then retry login.", "EOS configuration is invalid.", CanRetry: true),
        EosLobbyStatus.FromIdentity(OnlineIdentityState.Failed, false, true, "Check deployment and retry login."),
        EosLobbyStatus.FromIdentity(OnlineIdentityState.Failed, false, false, "Run setup-eos.ps1."),
        EosLobbyStatus.FromIdentity(OnlineIdentityState.Stopped, false, false, null),
        EosLobbyStatus.FromIdentity(OnlineIdentityState.LoggedIn, false, true, null),
    };

    private OnlineLobbyCoordinator _coordinator = null!;
    private DevelopmentSession _session = null!;
    private double _elapsed;
    private int _stage = -7;
    private bool _online;
    private int _loginRequests;
    private ResumeLocatorStore? _resumeStore;
    private ReservationGateway? _reservationGateway;
    private OnlineSessionBinding? _reservationBinding;
    private Input.PlayerInputBindings _bindings = null!;

    /// <inheritdoc />
    public override async void _Ready()
    {
        Engine.MaxFps = 60;
        var local = new OnlineProductUserId(new string('1', 32));
        _resumeStore = new ResumeLocatorStore(ProjectSettings.GlobalizePath("res://.godot/ts-122-startup-lookup.json"));
        _resumeStore.Save(new ResumeLocator("missing", 999, 2, 1, local.Value, 1, new string('2', 32)));
        _provider.DeferLookup = true;
        _online = true;
        _coordinator = new OnlineLobbyCoordinator(_provider, local, resumeStore: _resumeStore);
        _bindings = new Input.PlayerInputBindings();
        _session = new DevelopmentSession { NavigationInput = new Input.PlayerInputAdapter(_bindings), OnlineCoordinator = () => _online ? _coordinator : null, OnlineStatus = () => _online ? EosLobbyStatus.Connected : _identityStates[_stage + 7], OnlineLogin = () => _loginRequests++ };
        AddChild(_session);
        SetProcess(false);
        try
        {
            _coordinator.Tick();
            for (int frame = 0; frame < 20; frame++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (DisplayServer.GetName() != "headless") await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                Capture($"startup-lookup-{frame:00}");
                Require(_session.MainMenu.IsVisibleInTree(), "Main Menu remains visible on every passive lookup frame");
            }
            Require(_session.MainMenu.IsVisibleInTree() && !Controls<PanelContainer>().Single(panel => panel.Name == "LobbyBrowser").IsVisibleInTree(), "Read-only saved-session lookup must not replace Main Menu with the blue browser panel");
            _provider.CompleteLookup!();
            _provider.DeferLookup = false;
            _online = false;
            _resumeStore.Clear();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        catch (Exception exception)
        {
            GD.PushError("Main Menu lookup transition failed: " + exception.Message);
            GetTree().Quit(1);
            return;
        }
        SetProcess(true);
        Press("Play");
        SetProcess(false);
        for (int frame = 0; frame < 85; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        SetProcess(true);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _reservationBinding?.Driver.Pump(delta);
        _coordinator.Tick();
        _elapsed += delta;
        if (_elapsed < 0.4)
        {
            return;
        }

        _elapsed = 0;
        try
        {
            if (_stage < 0)
            {
                var expected = _identityStates[_stage + 7];
                Require(Controls<Button>().Single(button => button.IsVisibleInTree() && button.Text == "Host Game").Disabled, "Unauthenticated Host Game was enabled.");
                Require(Controls<Label>().Single(label => label.Name == "PlayStatus").Text == expected.Text, "EOS state not shown in multiplayer panel.");
                Require(Controls<Button>().Single(button => button.IsVisibleInTree() && button.Text == "Host Game").TooltipText.Length > 0, "Disabled Host Game has no reason.");
                Require(Controls<Button>().Any(button => button.IsVisibleInTree() && button.Text.Contains("Direct-IP", StringComparison.Ordinal)), "Developer fallback is not visible.");
                Require(!Controls<LineEdit>().Any(edit => edit.IsVisibleInTree() && edit.Name == "DirectAddress"), "IP field shown before fallback selection.");
                if (expected.CanRetry)
                {
                    int before = _loginRequests;
                    Press("EOS dev login");
                    Require(_loginRequests == before + 1, "Retry did not reach the identity owner.");
                }

                if (_stage == -5)
                {
                    Capture("configuration-missing");
                }

                _stage++;
                _online = _stage == 0;
                return;
            }

            switch (_stage++)
            {
                case 0:
                    Require(!Controls<Button>().Single(button => button.IsVisibleInTree() && button.Text == "Host Game").Disabled, "Host Game did not enable after authentication and coordinator creation.");
                    Require(!Controls<VBoxContainer>().Single(control => control.Name == "PlayDecision").IsVisibleInTree(), "Normal launch showed a retained-match prompt without a locator.");
                    Require(Controls<LineEdit>().Single(edit => edit.Name == "LobbySearch").IsVisibleInTree(), "Normal launch did not enter the lobby browser.");
                    Require(Controls<Label>().Any(label => label.Text == "Locked"), "Locked row missing.");
                    Require(!Controls<LineEdit>().Any(edit => edit.IsVisibleInTree() && edit.PlaceholderText.Contains("IP:", StringComparison.Ordinal)), "IP field visible in normal flow.");
                    Require(Controls<Button>().Any(button => button.TooltipText.StartsWith("Game version mismatch.", StringComparison.Ordinal) && button.TooltipText.Contains(GameVersion.Current.ToString(), StringComparison.Ordinal)), "Visible version mismatch missing.");
                    Require(Controls<Button>().Any(button => button.Disabled && button.TooltipText.StartsWith("Game version mismatch.", StringComparison.Ordinal)), "Incompatible Join was not disabled.");
                    Require(_provider.ResumeRequests == 0, "Normal launch attempted retained-session membership recovery without a locator.");
                    Capture("browser");
                    Edit("Search lobbies").Text = "aRENa";
                    Edit("Search lobbies").EmitSignal(LineEdit.SignalName.TextChanged, "aRENa");
                    break;
                case 1:
                    Require(_session.PlayMenu.VisibleRows.Count == 1, "Search did not filter.");
                    ActivateRow("public");
                    break;
                case 2:
                    Require(_coordinator.Active?.Access == LobbyAccess.Public, "Public join did not proceed directly.");
                    _coordinator.Leave();
                    _session.PlayMenu._Process(0);
                    Edit("Search lobbies").Text = string.Empty;
                    Edit("Search lobbies").EmitSignal(LineEdit.SignalName.TextChanged, string.Empty);
                    break;
                case 3:
                    ActivateRow("locked");
                    break;
                case 4:
                    Edit("Access code").Text = "wrong";
                    Press("Join locked lobby");
                    Require(_coordinator.Active is null, "Wrong credential joined.");
                    Capture("locked-prompt");
                    Edit("Access code").Text = "test-code";
                    Press("Join locked lobby");
                    break;
                case 5:
                    Require(_coordinator.Active?.Access == LobbyAccess.Locked, "Correct credential rejected.");
                    _coordinator.Leave();
                    _session.PlayMenu._Process(0);
                    Press("Cancel");
                    Press("Host Game");
                    Edit("Lobby name").Text = "My Game";
                    break;
                case 6:
                    _provider.DeferCreate = true;
                    Press("Create lobby");
                    Require(_session.Stage == ApplicationStage.Admission && _session.Lobby is null, "Creating must wait for authoritative admission.");
                    break;
                case 7:
                    Require(_session.Stage == ApplicationStage.Admission && Controls<Label>().Any(label => label.IsVisibleInTree() && label.Text.Contains("Creating lobby", StringComparison.Ordinal)), "Pending creation has visible progress.");
                    _provider.CompleteCreate!();
                    _provider.CompleteCreate = null;
                    Require(_session.Stage == ApplicationStage.Admission && _session.Lobby is null, "EOS membership alone cannot enter the joined Lobby.");

                    Require(_coordinator.IsHost, "Host control failed.");
                    _coordinator.Rename("Renamed Game");
                    break;
                case 8:
                    Require(_coordinator.Active?.Name == "Renamed Game", "Rename control failed.");
                    Capture("host-renamed");
                    _coordinator.Leave();
                    _session.PlayMenu._Process(0);
                    Press("Cancel");
                    BeginMissingLookup();
                    break;
                case 9:
                    Require(_coordinator.Active is null, "Missing retained lobby created membership.");
                    Require(!_coordinator.HasRetainedDecision, "Missing retained lobby created a decision.");
                    Require(_resumeStore!.Load(new string('1', 32)) is null, "Missing retained lobby did not retire the stale locator.");
                    Require(!Controls<VBoxContainer>().Single(control => control.Name == "PlayDecision").IsVisibleInTree(), "Local locator exposed the retained-match prompt before authority confirmation.");
                    Require(Controls<LineEdit>().Single(edit => edit.Name == "LobbySearch").IsVisibleInTree(), "Lobby browser was hidden while retained-session validation was pending.");
                    Require(Controls<Button>().Any(button => button.IsVisibleInTree() && button.TooltipText.EndsWith("Arena Public", StringComparison.Ordinal)), "Lobby list was unavailable while retained-session validation was pending.");
                    Require(!Controls<Label>().Single(label => label.Name == "PlayStatus").Text.Contains("unavailable", StringComparison.OrdinalIgnoreCase), "Missing retained lobby was presented as EOS unavailable.");
                    Require(_provider.ResumeRequests == 0, "Missing retained lobby attempted membership recovery.");
                    BeginDecision();
                    break;
                case 10:
                    Require(_coordinator.Active is null, "Read-only retained lookup restored EOS membership.");
                    Require(!_coordinator.HasRetainedDecision, "Read-only retained lookup exposed a confirmed decision.");
                    Require(Controls<Button>().Any(button => button.IsVisibleInTree() && button.Text == "Check previous session"), "Found retained metadata did not expose explicit validation.");
                    Require(_provider.ResumeRequests == 0, "Startup lookup called the legacy EOS Resume path.");
                    Press("Check previous session");
                    break;
                case 11:
                    Require(_provider.ResumeRequests == 1, "Explicit check did not request retained validation exactly once.");
                    _reservationGateway = new ReservationGateway();
                    _reservationBinding = _coordinator.AttachTransport(_reservationGateway, 1, "Player");
                    Require(_coordinator.RetainedDecision == RetainedSessionDecision.Checking, "Explicit retained validation did not begin authority inspection.");
                    Require(!Controls<LineEdit>().Any(edit => edit.Name == "LobbySearch" && edit.IsVisibleInTree()), "Authority inspection must hide browser controls.");
                    _reservationGateway!.ConfirmAvailable();
                    Capture("retained-checking");
                    break;
                case 12:
                    Require(_coordinator.RetainedDecision == RetainedSessionDecision.Choose, "Reservation prompt missing.");
                    Require(Controls<VBoxContainer>().Single(control => control.Name == "PlayDecision").IsVisibleInTree(), "Confirmed reservation did not expose the retained-match prompt.");
                    Require(!Controls<Button>().Any(button => button.IsVisibleInTree() && button.Text == "Host Game" && !button.Disabled), "Browser enabled during reservation decision.");
                    Capture("retained-choice");
                    ClickChoice("No");
                    AcceptChoice("No", false);
                    break;
                case 13:
                    Require(_coordinator.RetainedDecision == RetainedSessionDecision.Leaving, "Unconfirmed release returned to browser.");
                    Require(_reservationGateway!.AbandonRequests == 1, "Double click sent duplicate release.");
                    Capture("retained-leaving");
                    _reservationGateway.ConfirmAbandon();
                    break;
                case 14:
                    Require(!_coordinator.HasRetainedDecision && _coordinator.Active is null, "Acknowledged release did not return to browser.");
                    Require(_resumeStore!.Load(new string('1', 32)) is null, "Released locator persisted.");
                    Require(Controls<LineEdit>().Single(edit => edit.Name == "LobbySearch").IsVisibleInTree(), "Browser returns only after acknowledged abandon clears retained state.");
                    _reservationBinding = null;
                    BeginDecision();
                    break;
                case 15:
                    Require(_coordinator.Active is null, "Second startup lookup restored EOS membership.");
                    Press("Check previous session");
                    break;
                case 16:
                    _reservationGateway = new ReservationGateway();
                    _reservationBinding = _coordinator.AttachTransport(_reservationGateway, 1, "Player");
                    Require(_coordinator.RetainedDecision == RetainedSessionDecision.Checking, "Second explicit validation did not begin authority inspection.");
                    Require(!Controls<LineEdit>().Any(edit => edit.Name == "LobbySearch" && edit.IsVisibleInTree()), "Second authority inspection must hide browser controls.");
                    _reservationGateway!.ConfirmAvailable();
                    break;
                case 17:
                    Require(_coordinator.RetainedDecision == RetainedSessionDecision.Choose, "Second reservation confirmation did not expose the choice.");
                    AcceptChoice("Yes", true);
                    AcceptChoice("Yes", false);
                    break;
                case 18:
                    Require(_coordinator.RetainedDecision == RetainedSessionDecision.Reconnecting, "Reconnect choice not submitted.");
                    Require(_reservationGateway!.ResumeRequests == 1, "Reconnect did not use exactly one existing resume intent.");
                    Capture("retained-reconnecting");
                    _coordinator.FailRetainedConnection();
                    _reservationBinding = null;
                    break;
                case 19:
                    Require(_coordinator.RetainedDecision == RetainedSessionDecision.Failed, "Failed resume must remain recoverable.");
                    Require(_resumeStore!.Load(new string('1', 32)) is not null, "Failure must preserve the locator.");
                    Require(Controls<LineEdit>().Single(edit => edit.Name == "LobbySearch").IsVisibleInTree(), "Failed validation restores ordinary browsing without erasing the hint.");
                    Require(Controls<Button>().Where(button => button.IsVisibleInTree() && (button.Text == "Host Game" || button.Text == "Back")).All(button => !button.Disabled), "Failed validation does not trap Host or Back.");
                    Capture("retained-failure");
                    break;
                case 20:
                    Require(Controls<LineEdit>().Single(edit => edit.Name == "LobbySearch").IsVisibleInTree(), "Dismissal restores browser without claiming release.");
                    Press("Check previous session");
                    Require(_coordinator.RetainedDecision == RetainedSessionDecision.Checking, "Preserved hint can retry validation.");
                    GD.Print("Online lobby UI integration passed: Main Menu opens the browser; asynchronous creation waits for authoritative admission; missing hints remain a scoped lookup result, found hints do not restore membership, explicit validation precedes authority confirmation, and release/reconnect controls remain idempotent; fake provider, no native EOS authentication.");
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
    public override void _ExitTree()
    {
        _session.ProcessMode = ProcessModeEnum.Disabled;
        _bindings.Dispose();
        _coordinator?.Dispose();
        _resumeStore?.Clear();
    }

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

    private void BeginDecision()
    {
        _coordinator.Dispose();
        _resumeStore = new ResumeLocatorStore(ProjectSettings.GlobalizePath("res://.godot/ts68-ui-resume.json"));
        var local = new OnlineProductUserId(new string('1', 32));
        _resumeStore.Save(new ResumeLocator("public", 100, 2, 1, local.Value, 1, new string('2', 32)));
        _coordinator = new OnlineLobbyCoordinator(_provider, local, resumeStore: _resumeStore);
        _coordinator.Tick();
    }

    private void BeginMissingLookup()
    {
        _coordinator.Dispose();
        _resumeStore = new ResumeLocatorStore(ProjectSettings.GlobalizePath("res://.godot/ts68-ui-resume.json"));
        var local = new OnlineProductUserId(new string('1', 32));
        _resumeStore.Save(new ResumeLocator("missing", 999, 2, 1, local.Value, 1, new string('2', 32)));
        _coordinator = new OnlineLobbyCoordinator(_provider, local, resumeStore: _resumeStore);
        _coordinator.Tick();
    }

    private IEnumerable<T> Controls<T>()
        where T : Node
        => Descendants(_session).OfType<T>();

    private LineEdit Edit(string prefix) => Controls<LineEdit>().First(edit => edit.IsVisibleInTree() && edit.PlaceholderText.StartsWith(prefix, StringComparison.Ordinal));
    private void ActivateRow(string id)
    {
        var row = Controls<Button>().Single(button => button.HasMeta("lobby_id") && (string)button.GetMeta("lobby_id") == id);
        using var input = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true };
        input.DoubleClick = false;
        row.EmitSignal(Control.SignalName.GuiInput, input);
        input.DoubleClick = true;
        row.EmitSignal(Control.SignalName.GuiInput, input);
    }
    private void Press(string prefix) => Controls<Button>().First(button => button.IsVisibleInTree() && (button.Text.StartsWith(prefix, StringComparison.Ordinal) || button.TooltipText.StartsWith(prefix, StringComparison.Ordinal))).EmitSignal(Button.SignalName.Pressed);

    private void AcceptChoice(string text, bool controller)
    {
        Controls<Button>().Single(button => button.IsVisibleInTree() && button.Text == text).GrabFocus();
        foreach (bool pressed in new[] { true, false })
        {
            using InputEvent input = controller ? new InputEventJoypadButton { Device = 0, ButtonIndex = JoyButton.A, Pressed = pressed }
                : new InputEventKey { Keycode = Key.Enter, PhysicalKeycode = Key.Enter, Pressed = pressed };
            Godot.Input.ParseInputEvent(input); Godot.Input.FlushBufferedEvents();
        }
    }

    private void ClickChoice(string text)
    {
        var button = Controls<Button>().Single(button => button.IsVisibleInTree() && button.Text == text);
        Vector2 position = button.GetGlobalRect().GetCenter();
        foreach (bool pressed in new[] { true, false })
        {
            using var input = new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = pressed };
            Godot.Input.ParseInputEvent(input); Godot.Input.FlushBufferedEvents();
        }
    }

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
        internal int ResumeRequests { get; private set; }
        internal bool DeferCreate { get; set; }
        internal Action? CompleteCreate { get; set; }
        internal bool DeferLookup { get; set; }
        internal Action? CompleteLookup { get; set; }
        public void Search(Action<IReadOnlyList<OnlineLobby>, string?> completed) => completed(new[] { new OnlineLobby("public", "Arena Public", _remote, 100, LobbyAccess.Public, 2, 8, OnlineLobby.CurrentProtocol, true, null), new OnlineLobby("locked", "Private Game", _remote, 200, LobbyAccess.Locked, 3, 8, OnlineLobby.CurrentProtocol, true, _credential), new OnlineLobby("incompatible", "Different build", _remote, 300, LobbyAccess.Public, 1, 8, OnlineLobby.CurrentProtocol, true, null) { Version = new GameVersion(GameVersion.Current.Release, GameVersion.Current.Revision == 0 ? 1 : GameVersion.Current.Revision - 1).ToString() } }, null);
        public void Lookup(string id, Action<OnlineLobbyLookup> completed)
        {
            void Lookup() => Search((rows, failure) => completed(new(rows.SingleOrDefault(row => row.Id == id), failure)));
            if (DeferLookup) CompleteLookup = Lookup;
            else Lookup();
        }
        public void Create(OnlineLobby lobby, Action<OnlineLobby?, string?> completed)
        {
            _active = lobby with { Id = "hosted", Owner = _local };
            if (DeferCreate)
            {
                CompleteCreate = () => completed(_active, null);
            }
            else
            {
                completed(_active, null);
            }
        }

        public void Join(string id, Action<OnlineLobby?, string?> completed) => Search((rows, _) =>
        {
            _active = rows.Single(row => row.Id == id);
            completed(_active, null);
        });

        public void Resume(string id, Action<OnlineLobby?, string?> completed)
        {
            ResumeRequests++;
            Join(id, (lobby, failure) => completed(lobby! with { MemberIds = [_remote, _local] }, failure));
        }

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

        public IDisposable Watch(string id, Action<OnlineLobby?, OnlineLobbyUpdate> changed, Action<OnlineProductUserId>? retired = null) => new Subscription();
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

    private sealed class ReservationGateway : ITransportGateway
    {
        private readonly Queue<TransportMessage> _received = new();
        public event Action<TransportConnectionChange>? ConnectionChanged;
        public bool IsListening => false;
        public TransportConnectionState ConnectionState => TransportConnectionState.Connected;
        public IReadOnlyDictionary<ulong, TransportConnectionState> Connections { get; } = new Dictionary<ulong, TransportConnectionState> { [1] = TransportConnectionState.Connected };
        internal int AbandonRequests { get; private set; }
        internal int ResumeRequests { get; private set; }
        public void Listen(TransportEndpoint endpoint) => throw new NotSupportedException();
        public ulong Connect(TransportEndpoint endpoint) => 1;
        public void Poll()
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }

        public void ConfigureSimulation(NetworkSimulation simulation)
        {
        }

        public TransportStatistics GetStatistics(ulong peerId) => default;
        public bool TryReceive(out TransportMessage message) => _received.TryDequeue(out message);
        public void Disconnect(ulong peerId) => ConnectionChanged?.Invoke(new(peerId, TransportConnectionState.Disconnected, TransportDisconnectReason.LocalRequest, "Closed"));
        public void Send(TransportMessage message)
        {
            var command = LobbyCodec.DecodeCommand(message.Payload.Span).Command;
            if (command == LobbyCommand.Abandon)
            {
                AbandonRequests++;
            }
            else if (command == LobbyCommand.Resume)
            {
                ResumeRequests++;
            }
        }

        internal void ConfirmAvailable() => _received.Enqueue(new(1, LobbyCodec.EncodeReservation(100, 2, 1, 1, ReservationResult.Available), TransportDelivery.Reliable));
        internal void ConfirmAbandon() => _received.Enqueue(new(1, LobbyCodec.EncodeReservation(100, 2, 1, 1, ReservationResult.Abandoned), TransportDelivery.Reliable));
    }
}
