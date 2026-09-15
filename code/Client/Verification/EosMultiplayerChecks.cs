using System.Diagnostics;
using System.Text.Json;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Core.Input;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

/// <summary>Opt-in real-device EOS discovery, gameplay and cleanup check using production composition.</summary>
public sealed partial class EosMultiplayerChecks : Node
{
    private readonly EosIdentityService _identity = new(() => new EosSdkPlatform());
    private readonly List<float> _errors = new();
    private OnlineLobbyCoordinator? _coordinator;
    private DevelopmentSession? _session;
    private EosP2pTransport? _gateway;
    private string _name = string.Empty;
    private string _output = string.Empty;
    private bool _host;
    private bool _created;
    private bool _done;
    private int _players;
    private int _stage;
    private int _snapshots;
    private double _elapsed;
    private double _arenaSeconds;
    private double _nextSearch;
    private double _peakTick;

    /// <inheritdoc/>
    public override void _Ready()
    {
        Engine.PhysicsTicksPerSecond = 60;
        Engine.MaxFps = 60;
        string[] args = OS.GetCmdlineUserArgs();
        string Value(string key, string fallback) => args.FirstOrDefault(arg => arg.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] ?? fallback;
        _name = LobbyName.Sanitize(Value("--eos-test-name", "Trackstorm remote verification"));
        _output = Value("--eos-test-output", "eos-multiplayer-result.json");
        _host = args.Contains("--eos-test-host");
        _players = int.Parse(Value("--eos-test-players", "2"), System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            if (_players is < 2 or > 8)
            {
                throw new ArgumentException("EOS test requires 2–8 distinct devices.");
            }

            _identity.Start(EosClientConfiguration.Resolve());
            _identity.Login();
            GD.Print($"EOS remote check: {(_host ? "HOST" : "CLIENT")}; waiting for {_players} devices in '{_name}'.");
        }
        catch (Exception exception)
        {
            Finish(false, exception.Message);
        }
    }

    /// <inheritdoc/>
    public override void _PhysicsProcess(double delta)
    {
        if (_done)
        {
            return;
        }

        try
        {
            _elapsed += delta;
            if (_elapsed > 900)
            {
                throw new InvalidOperationException("Remote test timed out waiting for discovery, peers or cleanup.");
            }

            long started = Stopwatch.GetTimestamp();
            _identity.Tick();
            _peakTick = Math.Max(_peakTick, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (_identity.State == OnlineIdentityState.Failed)
            {
                throw new InvalidOperationException(_identity.Diagnostics);
            }

            if (_identity.State != OnlineIdentityState.LoggedIn)
            {
                return;
            }

            if (_coordinator is null)
            {
                _coordinator = new OnlineLobbyCoordinator(_identity.CreateLobbyProvider(), _identity.ProductUserId!);
                _coordinator.TransportFactory = credential => _gateway = _identity.CreateTransport(_coordinator, credential);
                _session = new DevelopmentSession { OnlineCoordinator = () => _coordinator, OnlineStatus = () => EosLobbyStatus.Connected };
                AddChild(_session);
            }

            _coordinator.Tick();
            if (_stage == 4)
            {
                if (!_coordinator.CanLeave)
                {
                    Finish(true, "Discovery/connect/Ready/Start/arena/Return/leave completed with real EOS peer packets.");
                }

                return;
            }

            if (_stage == 0 && _coordinator.Active is null && !_coordinator.Busy)
            {
                if (_host && !_created)
                {
                    _created = true;
                    _coordinator.Create(_name, LobbyAccess.Public, null);
                }
                else if (!_host)
                {
                    var row = _coordinator.Browser.Rows.FirstOrDefault(row => row.Name == _name);
                    if (row is not null)
                    {
                        _coordinator.Join(row.Id, null);
                    }
                    else if (_elapsed >= _nextSearch)
                    {
                        _nextSearch = _elapsed + 2;
                        _coordinator.Refresh();
                    }
                }
            }

            _session!.Advance(new InputFrame(0, _arenaSeconds is > 1 and < 5 ? (short)10000 : (short)0, _arenaSeconds < 5 ? (ushort)30000 : (ushort)0, 0, 0, 0, 0));
            var lobby = _session.Lobby;
            if (_stage == 3 && !_host && _coordinator.Active is null)
            {
                _stage = 4;
                return;
            }

            if (_stage > 0 && lobby is null)
            {
                throw new InvalidOperationException("Gameplay connection ended before the remote check completed.");
            }

            if (lobby?.State is null)
            {
                return;
            }

            if (_stage == 0)
            {
                if (lobby.State.Players.Count == _players)
                {
                    lobby.Request(LobbyCommand.Ready, true);
                    if (_host && lobby.State.CanStart)
                    {
                        lobby.Request(LobbyCommand.Start);
                    }
                }

                if (_session.Arena is not null)
                {
                    _stage = 1;
                    _session.Arena.Driver.LocalCorrected += _ => _errors.Add(_session.Arena.Driver.Prediction!.PredictionError);
                    GD.Print("EOS remote check: arena entered.");
                }
            }
            else if (_stage == 1)
            {
                _arenaSeconds += delta;
                _snapshots = Math.Max(_snapshots, _session.Arena?.Driver.ReceivedSnapshots ?? 0);
                if (_host && _arenaSeconds >= 20)
                {
                    lobby.Request(LobbyCommand.Return);
                }

                if (lobby.State.Phase == SessionPhase.Lobby)
                {
                    if (!_host && _snapshots < 100)
                    {
                        throw new InvalidOperationException("EOS client received fewer than 100 authoritative arena snapshots.");
                    }

                    _stage = 3;
                    lobby.Request(LobbyCommand.Ready, true);
                    GD.Print("EOS remote check: returned to lobby; acknowledging completion.");
                }
            }
            else if (_stage == 3 && _host && lobby.State.Players.Count != _players)
            {
                throw new InvalidOperationException("A peer left before acknowledging the completed arena and Return.");
            }
            else if (_stage == 3 && _host && lobby.State.CanStart)
            {
                _session.Leave();
                _stage = 4;
            }
        }
        catch (Exception exception)
        {
            Finish(false, exception.Message);
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _session?.Leave();
        _coordinator?.Dispose();
        _identity.Dispose();
        EosProcessRuntime.Shutdown();
    }

    private void Finish(bool passed, string detail)
    {
        _done = true;
        _errors.Sort();
        var report = new
        {
            Passed = passed,
            Detail = detail,
            Host = _host,
            Players = _players,
            Stage = _stage,
            Seconds = _elapsed,
            ArenaSeconds = _arenaSeconds,
            Snapshots = _snapshots,
            SentPackets = _gateway?.SentPackets,
            ReceivedPackets = _gateway?.ReceivedPackets,
            ReliablePackets = _gateway?.ReliablePackets,
            SentBytes = _gateway?.SentBytes,
            PeakPacketBytes = _gateway?.PeakPacketBytes,
            PeakGatewayPollMilliseconds = _gateway?.PeakPollMilliseconds,
            PeakSdkTickMilliseconds = _peakTick,
            Rtt = (double?)null,
            ErrorP99 = _errors.Count == 0 ? 0 : _errors[(int)((_errors.Count - 1) * 0.99)],
            ErrorMaximum = _errors.Count == 0 ? 0 : _errors[^1],
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(_output, json);
        GD.Print(json);
        GetTree().Quit(passed ? 0 : 1);
    }
}
