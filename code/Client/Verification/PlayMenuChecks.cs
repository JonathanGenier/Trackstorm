using Godot;
using Trackstorm.Client.Frontend;
using Trackstorm.Client.Input;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;

namespace Trackstorm.Client.Verification;

/// <summary>Rendered production Play Menu exercised with controlled discovery; no remote EOS evidence.</summary>
public sealed partial class PlayMenuChecks : Node
{
    private readonly Provider _provider = new();
    private DevelopmentSession _session = null!;
    private OnlineLobbyCoordinator _coordinator = null!;
    private PlayerInputBindings _bindings = null!;
    public override async void _Ready()
    {
        Engine.MaxFps = 60;
        GetWindow().Size = new Vector2I(1280, 720);
        _bindings = new PlayerInputBindings();
        _coordinator = new OnlineLobbyCoordinator(_provider, new OnlineProductUserId(new string('1', 32)));
        _session = new DevelopmentSession { NavigationInput = new PlayerInputAdapter(_bindings), OnlineCoordinator = () => _coordinator, OnlineStatus = () => EosLobbyStatus.Connected };
        AddChild(_session);
        try
        {
            await Frames(4);
            Click(_session.MainMenu.Targets[0]);
            Require(!_session.MainMenu.Interactive && !_session.PlayMenu.Visible, "Main rig hoists alone with blocked interaction");
            await Frames(24);
            Require(!_session.MainMenu.Visible && !_session.PlayMenu.Interactive, "Main clears before Play drops");
            await Frames(65);
            Require(_session.PlayMenu.Interactive && _session.PlayMenu.VisibleRows.Count == 100, "100 dynamic lobbies after catch");
            await Capture("100-lobbies");
            Require(Controls<Button>().Single(button => button.Text.StartsWith("Free Play", StringComparison.Ordinal)).Disabled, "Free Play disabled");
            Button row = Rows().First();
            Click(row);
            Require(_provider.Joins == 0, "Single mouse click does not join");
            Click(row, true);
            Require(_provider.Joins == 1, "Double mouse click uses provider join");
            await Frames(3);
            row.GrabFocus(); Tap(Key.Enter); await Frames(32); Tap(Key.Enter);
            Require(_provider.Joins == 1, "Expired Accept does not join");
            Tap(Key.Enter);
            Require(_provider.Joins == 2, $"Bounded double Accept joins same row (joins={_provider.Joins}, focus={GetViewport().GuiGetFocusOwner()?.GetPath()}, target={row.GetPath()}, windowFocus={GetWindow().HasFocus()}, interactive={_session.PlayMenu.Interactive})");
            await Frames(2);
            row.GrabFocus(); Joy(JoyButton.A); Joy(JoyButton.A);
            Require(_provider.Joins == 3, "Controller equivalent double Accept joins");
            Tap(Key.Enter); Tap(Key.Down); Tap(Key.Enter);
            Require(_provider.Joins == 3, "Changing row resets pending Accept");
            var search = Controls<LineEdit>().Single(edit => edit.Name == "LobbySearch");
            search.Text = "Arena 09";
            await Frames(3);
            Require(_session.PlayMenu.VisibleRows.Count == 10, "Case insensitive name search");
            Click(Button("All  ▾")); Tap(Key.Down); Tap(Key.Enter);
            await Frames(3);
            Require(_session.PlayMenu.VisibleRows.Count == 5, "Search and Open Only intersect");
            Click(Button("Open Only  ▾")); Tap(Key.Down); Tap(Key.Escape);
            Require(Button("Open Only  ▾").Text.StartsWith("Open", StringComparison.Ordinal), "Filter cancel preserves prior choice");
            search.Text = "no-such-lobby";
            await Frames(3); await Capture("filtered-empty");
            Require(_session.PlayMenu.VisibleRows.Count == 0, "Filtered empty state");
            search.Text = "";
            Click(Button("Open Only  ▾")); Tap(Key.Up); Tap(Key.Enter);
            await Frames(3);
            var scroll = Controls<ScrollContainer>().Single(control => control.IsVisibleInTree() && control.GetChildCount() > 0 && control.GetChild(0) is VBoxContainer box && box.GetChildren().OfType<Button>().Any());
            scroll.ScrollVertical = 10000;
            await Frames(3);
            Require(scroll.ScrollVertical > 0, "Overflow scrolls inside fixed list");
            await Capture("scrolled-bottom");
            _provider.Count = 0; _coordinator.Refresh(); await Frames(3);
            Require(_session.PlayMenu.VisibleRows.Count == 0, "Refresh removes all rows");
            await Capture("zero-lobbies");
            _provider.DeferSearch = true; _coordinator.Refresh(); await Frames(3);
            Require(Button("Refresh").Disabled, "Refresh disabled while discovery is pending");
            await Capture("loading");
            _provider.CompleteSearch!(); _provider.DeferSearch = false; await Frames(3);
            _provider.SearchFailure = "Fixture discovery failure — refresh to retry.";
            _coordinator.Refresh(); await Frames(3); await Capture("discovery-failure");
            Require(!Button("Refresh").Disabled, "Search failure allows retry");
            _provider.SearchFailure = null;
            _provider.Count = 100; _coordinator.Refresh(); await Frames(3);
            foreach (var size in new[] { new Vector2I(640, 360), new Vector2I(1280, 720), new Vector2I(1600, 900) })
            {
                GetWindow().Size = size; await Frames(6);
                Rect2 viewport = GetViewport().GetVisibleRect();
                foreach (string text in new[] { "Host Game", "Back", "Refresh", "All  ▾" })
                    Require(viewport.Encloses(Button(text).GetGlobalRect()), text + " fits " + size);
                var version = _session.PlayMenu.GetNode<Label>("GameVersion");
                Require(version.Text == $"v{Core.Sessions.GameVersion.Current}" && version.Position.X == 16 && version.MouseFilter == Control.MouseFilterEnum.Ignore && viewport.Encloses(version.GetGlobalRect()), "Canonical bottom-left version at " + size);
                Require(Button("Back").GetGlobalRect().End.Y < version.GetGlobalRect().Position.Y, "Version footer is separate from rig at " + size);
                var interior = Controls<Control>().Single(control => control.Name == "BrowserInterior");
                foreach (Control control in interior.GetChildren().OfType<Control>())
                    Require(interior.GetGlobalRect().Grow(0.1f).Encloses(control.GetGlobalRect()), "Browser child fits safe interior: " + control.GetType().Name);
                Require(scroll.GetGlobalRect().Size.X == interior.GetGlobalRect().Size.X, "Scrolling content cannot widen beyond safe interior");
                await Capture($"layout-{size.X}x{size.Y}");
            }
            Click(Button("All  ▾")); await Frames(3); await Capture("filter-choices");
            Click(Button("Locked Only")); await Frames(3);
            Require(_session.PlayMenu.VisibleRows.All(row => row.Access == LobbyAccess.Locked), "Locked Only UI choice");
            Click(Button("Locked Only  ▾")); await Frames(3); Click(Button("Has Space")); await Frames(3);
            Require(_session.PlayMenu.VisibleRows.All(row => row.Members < row.Capacity), "Has Space UI choice");
            Click(Button("Has Space  ▾")); await Frames(3); Click(Button("Mode: Circus")); await Frames(3);
            Require(_session.PlayMenu.VisibleRows.All(row => row.GameMode == "Circus"), "Advertised mode UI choice");
            Click(Button("Mode: Circus  ▾")); await Frames(3); Click(Button("All")); await Frames(3);
            await HeldClick(Button("Host Game")); await Frames(3); await Capture("host-form");
            Click(Button("Create lobby")); await Frames(3); await Capture("host-failure");
            Require(_coordinator.Active is null && Button("Create lobby").IsVisibleInTree(), "Failed host remains retryable");
            Click(Button("Cancel")); await Frames(3);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                await HeldClick(Button("Back")); Require(!_session.PlayMenu.Interactive, "Back blocks interaction");
                await Frames(96);
                Require(_session.MainMenu.Interactive && !_session.PlayMenu.Visible, "Main Menu settled after Back");
                Click(_session.MainMenu.Targets[0]); await Frames(85);
                Require(_session.PlayMenu.Interactive, "Repeated Play settles");
            }
            Click(Button("Direct-IP / LAN")); await Frames(4);
            Require(Controls<LineEdit>().Any(edit => edit.IsVisibleInTree() && edit.Name == "DirectAddress"), "Explicit Direct-IP fallback remains available");
            foreach (var size in new[] { new Vector2I(640, 360), new Vector2I(1280, 720), new Vector2I(1600, 900) })
            {
                GetWindow().Size = size; await Frames(6);
                var version = Controls<Label>().Single(label => label.IsVisibleInTree() && label.Name == "GameVersion");
                Require(version.Text == $"v{Core.Sessions.GameVersion.Current}" && version.Position.X == 16 && version.AnchorTop == 1 && GetViewport().GetVisibleRect().Encloses(version.GetGlobalRect()), "Direct-IP uses canonical bottom-left version at " + size);
                var fallback = Controls<PanelContainer>().Single(panel => panel.Name == "LobbyBrowser");
                Require(GetViewport().GetVisibleRect().Encloses(fallback.GetGlobalRect()) && fallback.GetGlobalRect().End.Y <= version.GetGlobalRect().Position.Y, "Direct-IP panel cannot cover the version footer at " + size);
                Require(!Controls<Label>().Any(label => label.Text.StartsWith("TRACKSTORM ", StringComparison.Ordinal) && label.Text.Contains("MULTIPLAYER", StringComparison.Ordinal)), "Legacy centered version header removed");
                await Capture($"direct-version-{size.X}x{size.Y}");
            }
            Click(Button("Back to Main Menu")); await Frames(96);
            Require(_session.MainMenu.Interactive, "Direct-IP Back returns through the complementary transition");
            Click(_session.MainMenu.Targets[0]); await Frames(85);
            await CheckPassiveLookupActions();
            await CheckCandidateDuringBack();
            await CheckUnconfirmedHint(true);
            await Capture("flag-a"); await Frames(20); await Capture("flag-b");
            GD.Print("Play Menu checks passed: 0/100, fixed scrolling, search/filter, mouse and logical double Accept, controller equivalent, expiry, transitions, viewport bounds, host form.");
            _session.ProcessMode = ProcessModeEnum.Disabled; _coordinator.Dispose(); _bindings.Dispose(); GetTree().Quit();
        }
        catch (Exception exception) { GD.PushError(exception.ToString()); GetTree().Quit(1); }
    }
    private IEnumerable<T> Controls<T>() where T : Node => Descendants(_session).OfType<T>();
    private async Task CheckCandidateDuringBack()
    {
        await HeldClick(Button("Back")); await Frames(96);
        _coordinator.Dispose();
        var store = new ResumeLocatorStore(ProjectSettings.GlobalizePath("res://.godot/play-menu-back-lookup.json"));
        var local = new OnlineProductUserId(new string('1', 32));
        store.Save(new ResumeLocator("old-hint", 999, 2, 1, local.Value, 1, new string('2', 32)));
        _provider.LookupHint = true; _provider.LookupFailure = false;
        _coordinator = new OnlineLobbyCoordinator(_provider, local, resumeStore: store);
        _coordinator.Tick();
        Click(_session.MainMenu.Targets[0]); await Frames(85);
        int resumes = _provider.Resumes;
        Button("Back").GrabFocus(); Joy(JoyButton.A);
        _provider.CompleteLookup!();
        await Frames(96);
        Require(_session.MainMenu.Interactive && _provider.Resumes == resumes && _coordinator.NeedsRetainedValidation, "Matching lookup during Back cannot pull the player out of Main Menu");
        Click(_session.MainMenu.Targets[0]); await Frames(85);
        Require(_provider.Resumes == resumes + 1 && _coordinator.RetainedDecision == RetainedSessionDecision.Checking, "Next Play validates the matching candidate exactly once");
        _coordinator.Dispose();
        _coordinator = new OnlineLobbyCoordinator(_provider, local);
        store.Clear(); await Frames(3);
    }
    private async Task CheckUnconfirmedHint(bool lookupFailure)
    {
        await HeldClick(Button("Back")); await Frames(96);
        _coordinator.Dispose();
        var store = new ResumeLocatorStore(ProjectSettings.GlobalizePath("res://.godot/play-menu-unconfirmed-hint.json"));
        var local = new OnlineProductUserId(new string('1', 32));
        var hint = new ResumeLocator("old-hint", 999, 2, 1, local.Value, 1, new string('2', 32));
        store.Save(hint);
        _provider.LookupFailure = lookupFailure;
        _provider.LookupHint = true;
        _coordinator = new OnlineLobbyCoordinator(_provider, local, resumeStore: store);
        _coordinator.Tick(); _provider.CompleteLookup!();
        Require(_coordinator.CanResumeRetained && _coordinator.CanStartFreshSession, "Unconfirmed hint permits explicit check and fresh admission");
        int resumes = _provider.Resumes;
        for (int cycle = 0; cycle < 2; cycle++)
        {
            Click(_session.MainMenu.Targets[0]); await Frames(85); _coordinator.Tick(); await Frames(3);
            Require(_provider.Resumes == resumes && _coordinator.Active is null, "Opening Play must not turn an unconfirmed hint into a reconnect attempt");
            Require(!Button("Host Game").Disabled && !Button("Back").Disabled && Button("Check previous session").IsVisibleInTree(), "Unconfirmed hint keeps Host/Back/browser available with explicit retry");
            Require(Controls<LineEdit>().Single(edit => edit.Name == "LobbySearch").IsVisibleInTree(), "Hint alone cannot expose a reconnect modal");
            await HeldClick(Button("Host Game")); await Frames(3);
            Require(Button("Create lobby").IsVisibleInTree(), "Host opens while unconfirmed hint is preserved");
            Click(Button("Cancel")); await Frames(3);
            await Capture(lookupFailure ? "lookup-failed-no-auto-reconnect" : "hint-no-auto-reconnect");
            if (cycle == 0) { await HeldClick(Button("Back")); await Frames(96); Require(_session.MainMenu.Interactive, "Back works with unconfirmed hint"); }
        }
        Require(store.Load(local.Value) == hint, "UI entry never erases saved hint or claims abandonment");
        store.Clear();
    }
    private async Task CheckPassiveLookupActions()
    {
        _coordinator.Dispose();
        var store = new ResumeLocatorStore(ProjectSettings.GlobalizePath("res://.godot/play-menu-pending-lookup.json"));
        var local = new OnlineProductUserId(new string('1', 32));
        store.Save(new ResumeLocator("missing", 999, 2, 1, local.Value, 1, new string('2', 32)));
        _coordinator = new OnlineLobbyCoordinator(_provider, local, resumeStore: store);
        _coordinator.Tick();
        await Frames(3);
        Require(_coordinator.CheckingSavedSession && !_coordinator.CanStartFreshSession, "Pending detection gates fresh admission");
        Require(Button("Host Game").Disabled && !Button("Back").Disabled && !Controls<LineEdit>().Single(edit => edit.Name == "LobbySearch").IsVisibleInTree(), "Pending detection hides browser and gates Host while Back remains usable");
        int joins = _provider.Joins;
        _coordinator.Create("Must not bypass", LobbyAccess.Public, null);
        _coordinator.Join("0");
        Click(Button("Host Game"));
        _session.Open(true, "127.0.0.1:27020", "Player");
        await Frames(3);
        Require(_coordinator.CheckingSavedSession && _provider.Joins == joins && _session.Lobby is null && !Controls<Button>().Any(button => button.IsVisibleInTree() && button.Text == "Create lobby"), "Host, Join and Direct-IP cannot cancel pending detection");
        await Capture("pending-detection");
        var back = Button("Back"); float restingY = back.GetGlobalRect().Position.Y;
        await HeldClick(back); await Frames(10);
        Require(!_session.PlayMenu.Interactive && !_session.MainMenu.Visible && back.GetGlobalRect().Position.Y < restingY, "Back visibly hoists with both menus noninteractive");
        await Frames(14);
        Require(!_session.PlayMenu.Visible && _session.MainMenu.Visible && !_session.MainMenu.Interactive, "Play clears before Main drops");
        await Frames(75);
        Require(_session.MainMenu.Interactive, "Back restores Main interaction despite pending lookup");
        _provider.CompleteLookup!();
        Require(! _coordinator.HasRetainedDecision && store.Load(local.Value) is null, "Missing hint clears through existing owner");
        _provider.Count = 0;
        Click(_session.MainMenu.Targets[0]); await Frames(85); _coordinator.Refresh(); await Frames(3);
        Require(_session.PlayMenu.VisibleRows.Count == 0 && Controls<LineEdit>().Single(edit => edit.Name == "LobbySearch").IsVisibleInTree(), "No resumable game immediately shows empty browser");
        await HeldClick(Button("Host Game")); await Frames(3);
        Require(Button("Create lobby").IsVisibleInTree(), "Host works once delayed lookup establishes no previous game");
        Click(Button("Cancel")); await Frames(3);
        await Capture("no-previous-game");
        foreach (bool controller in new[] { false, true })
        {
            Button("Back").GrabFocus(); if (controller) Joy(JoyButton.A); else Tap(Key.Enter);
            await Frames(96); Require(_session.MainMenu.Interactive, "Logical Back completes complementary transition");
            Click(_session.MainMenu.Targets[0]); await Frames(85);
        }
    }
    private static IEnumerable<Node> Descendants(Node node) { foreach (Node child in node.GetChildren()) { yield return child; foreach (Node nested in Descendants(child)) yield return nested; } }
    private Button Button(string text) => Controls<Button>().Single(button => button.IsVisibleInTree() && button.Text == text);
    private IEnumerable<Button> Rows() => Controls<Button>().Where(button => button.IsVisibleInTree() && button.HasMeta("lobby_id"));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private async Task Frames(int count) { for (int i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = ProjectSettings.GlobalizePath("res://.godot/play-menu-checks");
        System.IO.Directory.CreateDirectory(directory);
        using var image = GetViewport().GetTexture().GetImage(); image.SavePng(System.IO.Path.Combine(directory, name + ".png"));
    }
    private static void Tap(Key key) { foreach (bool pressed in new[] { true, false }) { using var input = new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed }; Godot.Input.ParseInputEvent(input); Godot.Input.FlushBufferedEvents(); } }
    private static void Joy(JoyButton button) { foreach (bool pressed in new[] { true, false }) { using var input = new InputEventJoypadButton { Device = 0, ButtonIndex = button, Pressed = pressed }; Godot.Input.ParseInputEvent(input); Godot.Input.FlushBufferedEvents(); } }
    private async Task HeldClick(Button button)
    {
        Vector2 position = button.GetGlobalRect().GetCenter();
        if (DisplayServer.GetName() != "headless")
        {
            // Parsed events do not move the OS pointer. Align it before holding across frames.
            GetWindow().GrabFocus();
            await Frames(2);
            GetViewport().WarpMouse(position);
            await Frames(2);
            Require(GetWindow().HasFocus(), "Rendered pointer check requires window focus");
        }
        string text = button.Text;
        int activations = 0;
        void Activated() => activations++;
        button.Pressed += Activated;
        var samples = new List<string>();
        using var motion = new InputEventMouseMotion { Position = position, GlobalPosition = position, Relative = new Vector2(8, 8) };
        Godot.Input.ParseInputEvent(motion); Godot.Input.FlushBufferedEvents();
        foreach (bool pressed in new[] { true, false })
        {
            using var input = new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = pressed };
            Godot.Input.ParseInputEvent(input); Godot.Input.FlushBufferedEvents();
            if (pressed)
                for (int frame = 0; frame < 6; frame++)
                {
                    await Frames(1);
                    samples.Add($"focus={GetWindow().HasFocus()}, pressed={button.ButtonPressed}, mouse={GetViewport().GetMousePosition()}, disabled={button.Disabled}, interactive={_session.PlayMenu.Interactive}");
                }
        }
        button.Pressed -= Activated;
        if (activations != 1)
        {
            await Capture("pointer-activation-failure");
            throw new InvalidOperationException($"Held click '{text}' at {position} produced {activations} activations. " + string.Join("; ", samples));
        }
    }
    private static void Click(Button button, bool twice = false)
    {
        Vector2 position = button.GetGlobalRect().GetCenter();
        if (DisplayServer.GetName() != "headless")
        {
            button.GetWindow().GrabFocus();
            button.GetViewport().WarpMouse(position);
        }
        using var motion = new InputEventMouseMotion { Position = position, GlobalPosition = position, Relative = new Vector2(8, 8) };
        Godot.Input.ParseInputEvent(motion); Godot.Input.FlushBufferedEvents();
        foreach (bool pressed in new[] { true, false }) { using var input = new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = pressed, DoubleClick = twice }; Godot.Input.ParseInputEvent(input); Godot.Input.FlushBufferedEvents(); }
    }
    private sealed class Provider : IOnlineLobbyProvider
    {
        internal int Count { get; set; } = 100;
        internal int Joins { get; private set; }
        internal bool DeferSearch { get; set; }
        internal string? SearchFailure { get; set; }
        internal Action? CompleteSearch { get; private set; }
        internal Action? CompleteLookup { get; private set; }
        internal bool LookupFailure { get; set; }
        internal bool LookupHint { get; set; }
        internal int Resumes { get; private set; }
        public void Resume(string id, Action<OnlineLobby?, string?> completed) { Resumes++; completed(null, "Session unavailable"); }
        public void Lookup(string id, Action<OnlineLobbyLookup> completed) => CompleteLookup = () => completed(new(
            LookupHint && !LookupFailure ? new OnlineLobby(id, "Old session metadata", new OnlineProductUserId(new string('2', 32)), 999, LobbyAccess.Public, 1, 8, OnlineLobby.CurrentProtocol, false, null) : null,
            LookupFailure ? "Fixture lookup service unavailable" : null));
        public void Search(Action<IReadOnlyList<OnlineLobby>, string?> completed)
        {
            void Complete() => completed(Enumerable.Range(0, Count).Select(i => new OnlineLobby(i.ToString(), $"Arena {i:000}", new OnlineProductUserId(new string('2', 32)), (ulong)(i + 1), i % 2 == 0 ? LobbyAccess.Public : LobbyAccess.Locked, i % 8 + 1, 8, OnlineLobby.CurrentProtocol, true, i % 2 == 0 ? null : LobbyCredential.Create("test-code")) { GameMode = i % 3 == 0 ? "Circus" : "FirstToTarget" }).ToArray(), SearchFailure);
            if (DeferSearch) CompleteSearch = Complete; else Complete();
        }
        public void Create(OnlineLobby lobby, Action<OnlineLobby?, string?> completed) => completed(null, "Fixture create failure — retry available.");
        public void Join(string id, Action<OnlineLobby?, string?> completed) { Joins++; completed(null, "Fixture admission failure — retry available."); }
        public void Update(OnlineLobby lobby, Action<OnlineLobby?, string?> completed) => completed(lobby, null);
        public void SetJoinable(string id, bool open, Action<OnlineLobby?, string?> completed) => completed(null, "No active fixture lobby.");
        public void Leave(string id, bool destroy, Action<string?> completed) => completed(null);
        public IDisposable Watch(string id, Action<OnlineLobby?, OnlineLobbyUpdate> changed, Action<OnlineProductUserId>? retired = null) => new Subscription();
        public void Dispose() { }
        private sealed class Subscription : IDisposable { public void Dispose() { } }
    }
}
