using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Client.Settings;
using Trackstorm.Core.Networking.Transport;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

public sealed partial class OnlineLobbyUiChecks
{
    private async Task VerifyManagement()
    {
        _session.SetFrontendPresentation(false, 1);
        foreach (string role in new[] { "idle", "host", "client" })
        {
            var provider = new UiProvider();
            using var coordinator = new OnlineLobbyCoordinator(provider, new OnlineProductUserId(new string('1', 32)));
            using var gateway = new ManagementGateway();
            var session = new DevelopmentSession { NavigationInput = new Input.PlayerInputAdapter(_bindings), OnlineCoordinator = () => coordinator, OnlineStatus = () => EosLobbyStatus.Connected };
            int logouts = 0;
            session.OnlineLogout = () =>
            {
                Require(session.LeaveComplete && coordinator.Active is null && !coordinator.CanLeave, "Logout preceded authoritative/membership cleanup.");
                Require(role == "idle" || gateway.Stopped, "Logout retained its online transport.");
                logouts++;
            };
            AddChild(session);
            if (role == "host") coordinator.Create("Original", LobbyAccess.Locked, "test-code");
            if (role == "client") { coordinator.Refresh(); coordinator.Join("public", null); }
            if (role != "idle")
            {
                session.OpenOnline(gateway, role == "host" ? 0UL : 1UL, role);
                if (role == "client")
                {
                    var authority = new LobbyAuthority(coordinator.Active!.Session, "Host");
                    ulong player = authority.Join(2, GameVersion.Current.ToString(), "Client");
                    gateway.Receive(LobbyCodec.EncodeState(authority.State, player));
                }
                session.Advance(default);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Require(session.Stage == ApplicationStage.Lobby, $"Management fixture {role} did not enter authoritative lobby: {session.Stage}, {session.Lobby?.Failure}.");
                if (role == "host")
                {
                    var original = coordinator.Active!;
                    var snapshot = session.Lobby!.State;
                    var configuration = session.Lobby.Authority!.Configuration;
                    void Press(string text) => Descendants(session).OfType<Button>().Single(button => button.IsVisibleInTree() && button.Text == text).EmitSignal(Button.SignalName.Pressed);
                    Press("Lobby Settings");
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (DisplayServer.GetName() != "headless") await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    Capture("management-lobby-settings");
                    Press("Rename lobby");
                    var edit = Descendants(session).OfType<LineEdit>().Single(node => node.IsVisibleInTree() && node.PlaceholderText == "Lobby name");
                    edit.CaretColumn = edit.Text.Length;
                    foreach (var (key, expected) in new[] { (Key.Left, edit.Text.Length - 1), (Key.Right, edit.Text.Length), (Key.Home, 0), (Key.End, edit.Text.Length) })
                    {
                        foreach (bool pressed in new[] { true, false })
                        {
                            using var inputEvent = new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed };
                            Godot.Input.ParseInputEvent(inputEvent);
                            Godot.Input.FlushBufferedEvents();
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                        }
                        Require(edit.HasFocus() && edit.CaretColumn == expected, $"Rename {key} did not preserve native caret editing.");
                    }
                    edit.Text = "Renamed Game";
                    Press("Save name");
                    Require(coordinator.Active!.Name == "Renamed Game", "Host rename UI did not reach coordinator.");
                    Require(coordinator.Active.Id == original.Id && coordinator.Active.Session == original.Session && coordinator.Active.Access == original.Access && coordinator.Active.Credential == original.Credential, "Rename changed identity/access.");
                    Require(ReferenceEquals(snapshot, session.Lobby.State) && ReferenceEquals(configuration, session.Lobby.Authority.Configuration), "Rename changed roster/Ready/map/configuration.");
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    if (DisplayServer.GetName() != "headless") await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    Capture("management-rename");
                }
                else
                {
                    Require(!Descendants(session).OfType<Button>().Any(button => button.IsVisibleInTree() && button.Text is "Lobby Settings" or "Rename lobby"), "Non-host received rename UI.");
                    coordinator.Rename("Forbidden");
                    Require(coordinator.Status.Contains("Only the host", StringComparison.Ordinal) && coordinator.Active!.Name == "Arena Public", "Non-host rename was accepted.");
                }
            }
            var settings = new PlayerSettingsController();
            var input = new Input.PlayerInputAdapter(_bindings);
            settings.Initialize(input, ProjectSettings.GlobalizePath("res://.godot/ts139-management-settings.json"));
            AddChild(settings);
            var panel = new SettingsPanel { CanLogoutOnline = () => session.CanLogoutOnline, LogoutOnline = session.LogoutOnline };
            panel.Initialize(settings, input);
            settings.AddChild(panel);
            panel.OpenFrontendSettings();
            panel._Process(0);
            var logout = Descendants(panel).OfType<Button>().Single(button => button.Text.StartsWith("EOS logout", StringComparison.Ordinal));
            Require(logout.IsVisibleInTree(), "Settings does not expose EOS logout.");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (DisplayServer.GetName() != "headless") await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Capture("management-logout-" + role);
            provider.DeferLeave = role != "idle";
            logout.EmitSignal(Button.SignalName.Pressed);
            if (role == "client") Require(session.Lobby is not null && provider.CompleteLeave is null, "Client skipped authoritative Leave before online cleanup.");
            session.LogoutOnline(); // Repeated requests cannot start parallel cleanup/logout.
            for (int i = 0; i < 240; i++) { session.Advance(default); coordinator.Tick(); }
            if (role != "idle")
            {
                Require(logouts == 0 && provider.CompleteLeave is not null, "Logout did not wait for membership callback.");
                provider.CompleteLeave!();
            }
            session.Advance(default);
            Require(logouts == 1 && session.Stage == ApplicationStage.MainMenu, "Logout did not complete exactly once at Main Menu.");
            settings.QueueFree();
            session.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        _session.SetFrontendPresentation(true, 1);
        GD.Print("Online management passed: host rename preserves state; client rename rejected; Settings logout idle/host/client waits for authoritative and delayed membership cleanup.");
    }

    private sealed class ManagementGateway : ITransportGateway
    {
        private readonly Queue<TransportMessage> _messages = new();
        public event Action<TransportConnectionChange>? ConnectionChanged;
        internal bool Stopped { get; private set; }
        public bool IsListening => false;
        public TransportConnectionState ConnectionState => TransportConnectionState.Connected;
        public IReadOnlyDictionary<ulong, TransportConnectionState> Connections { get; } = new Dictionary<ulong, TransportConnectionState> { [1] = TransportConnectionState.Connected };
        public void Listen(TransportEndpoint endpoint) { }
        public ulong Connect(TransportEndpoint endpoint) => 1;
        public void Poll() { }
        public void Stop() => Stopped = true;
        public void Dispose() => Stop();
        public void ConfigureSimulation(NetworkSimulation simulation) { }
        public TransportStatistics GetStatistics(ulong peerId) => default;
        public bool TryReceive(out TransportMessage message) => _messages.TryDequeue(out message);
        public void Disconnect(ulong peerId) => ConnectionChanged?.Invoke(new(peerId, TransportConnectionState.Disconnected, TransportDisconnectReason.LocalRequest, "Closed"));
        public void Send(TransportMessage message) { }
        internal void Receive(byte[] payload) => _messages.Enqueue(new(1, payload, TransportDelivery.Reliable));
    }
}
