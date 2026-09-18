using Epic.OnlineServices.Version;
using Godot;
using Trackstorm.Client.Networking;
using Trackstorm.Client.Online;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Verification;

/// <summary>Runs native initialization in an isolated process, optionally authenticating with real dev configuration.</summary>
public sealed partial class EosIntegrationChecks : Node
{
    private EosIdentityService? _identity;
    private EosConfiguration? _configuration;
    private OnlineProductUserId? _firstIdentity;
    private int _cycles;
    private OnlineLobbyCoordinator? _lobby;
    private EosP2pTransport? _transport;
    private DevelopmentSession? _gameplay;
    private int _p2pFrames;
    private bool _p2pLeaving;
    private ulong _p2pStartedAt;

    /// <inheritdoc />
    public override void _Ready()
    {
        try
        {
            EosProcessRuntime.Acquire();
            bool duplicateRejected = false;
            try
            {
                EosProcessRuntime.Acquire();
            }
            catch (InvalidOperationException)
            {
                duplicateRejected = true;
            }

            if (!duplicateRejected)
            {
                throw new InvalidOperationException("Duplicate EOS initialization was not rejected.");
            }

            string version = VersionInterface.GetVersion();
            GD.Print($"EOS native version: {version}");
            if (!version.StartsWith("1.19.1.2-", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("EOS native version differs from the pinned binding.");
            }

            EosProcessRuntime.Release();
            if (OS.GetCmdlineUserArgs().Contains("--eos-authenticate"))
            {
                try
                {
                    _configuration = EosClientConfiguration.Resolve();
                }
                catch (InvalidOperationException exception)
                {
                    GD.Print($"EOS configuration failure: {exception.Message}");
                    Fail();
                    return;
                }

                _identity = new(() => new EosSdkPlatform());
                StartCycle();
            }
            else
            {
                using var missing = new EosIdentityService(() => new EosSdkPlatform());
                missing.Start(new EosConfiguration());
                if (missing.State != OnlineIdentityState.Failed || missing.PlatformInitialized)
                {
                    throw new InvalidOperationException("Missing EOS configuration was not handled safely.");
                }

                Finish("native SDK initialization/version and invalid configuration handling verified; authentication NOT tested");
            }
        }
        catch (Exception exception)
        {
            GD.Print($"EOS setup failure type: {exception.GetType().Name}");
            Fail();
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_identity is null)
        {
            return;
        }

        try
        {
            _identity.Tick();
            if (_identity.State == OnlineIdentityState.Failed)
            {
                GD.Print(_identity.Diagnostics);
                Fail();
            }
            else if (_identity.State == OnlineIdentityState.LoggedIn)
            {
                if (OS.GetCmdlineUserArgs().Contains("--eos-p2p-check") && !CheckP2pCycle())
                {
                    return;
                }

                if (_firstIdentity is not null && !_firstIdentity.Equals(_identity.ProductUserId))
                {
                    throw new InvalidOperationException("Device identity changed across platform cycles.");
                }

                _firstIdentity = _identity.ProductUserId;
                GD.Print(_identity.Diagnostics);
                ++_cycles;
                _identity.Logout();
            }
            else if (_identity.State == OnlineIdentityState.Stopped)
            {
                if (_cycles == 3)
                {
                    Finish("three real platform/login/logout cycles with stable identity" + (OS.GetCmdlineUserArgs().Contains("--eos-p2p-check") ? "; real lobby/P2P notification/listen/stop and solo Ready/Start/arena/Return cycles, remote traffic NOT tested" : string.Empty));
                }
                else
                {
                    StartCycle();
                }
            }
        }
        catch (Exception)
        {
            Fail();
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _transport?.Dispose();
        _lobby?.Dispose();
        _identity?.Dispose();
        EosProcessRuntime.Shutdown();
    }

    private void StartCycle()
    {
        _identity!.Start(_configuration!);
        _identity.Login();
    }

    private bool CheckP2pCycle()
    {
        if (_lobby is null)
        {
            _lobby = new OnlineLobbyCoordinator(_identity!.CreateLobbyProvider(), _identity.ProductUserId!);
            _lobby.Create("Trackstorm P2P verification", LobbyAccess.Public, null);
        }

        _lobby.Tick();
        if (_p2pLeaving)
        {
            if (_lobby.Busy)
            {
                return false;
            }

            if (_lobby.CanLeave)
            {
                throw new InvalidOperationException("EOS verification lobby cleanup failed.");
            }

            _lobby.Dispose();
            _lobby = null;
            _p2pLeaving = false;
            _p2pFrames = 0;
            return true;
        }

        if (_lobby.Active is null)
        {
            if (!_lobby.Busy)
            {
                throw new InvalidOperationException("EOS verification lobby creation failed.");
            }

            return false;
        }

        if (_transport is null)
        {
            _transport = _identity!.CreateTransport(_lobby, null);
            _transport.Listen(EosP2pTransport.Endpoint(_lobby.Active, _lobby.Identity));
            _gameplay = new DevelopmentSession { OnlineCoordinator = () => _lobby, OnlineStatus = () => EosLobbyStatus.Connected };
            AddChild(_gameplay);
            _transport.Authorize = _gameplay.OpenOnline(_transport, 0, "Host").AuthorizePeer;
            _p2pStartedAt = Time.GetTicksMsec();
        }

        _gameplay!.Advance(default);
        if (_p2pFrames == 0)
        {
            // Online authority waits for the asynchronous membership proof before admitting commands.
            if (_gameplay.Lobby!.Migration?.Frozen == true)
            {
                if (Time.GetTicksMsec() - _p2pStartedAt >= 10000 || _gameplay.Lobby.Failure.Length != 0)
                {
                    throw new InvalidOperationException("EOS gameplay authority did not become available.");
                }

                return false;
            }

            if (!_gameplay.Lobby!.Request(LobbyCommand.Ready, true) || !_gameplay.Lobby.Request(LobbyCommand.Start))
            {
                throw new InvalidOperationException("EOS host could not enter the arena through lobby authority.");
            }
        }

        if (++_p2pFrames < 20)
        {
            return false;
        }

        if (_gameplay.Arena is null || !_gameplay.Lobby!.Request(LobbyCommand.Return))
        {
            throw new InvalidOperationException("EOS host arena/return integration failed.");
        }

        _gameplay.Advance(default);
        if (_gameplay.Arena is not null)
        {
            throw new InvalidOperationException("EOS arena did not return to the lobby.");
        }

        _gameplay.Leave();
        RemoveChild(_gameplay);
        _gameplay.QueueFree();
        _gameplay = null;
        _transport.Stop();
        _transport.Stop();
        _transport.Dispose();
        _transport = null;
        _p2pLeaving = true;
        _lobby.Leave();
        return false;
    }

    private void Finish(string evidence)
    {
        _identity?.Dispose();
        _identity = null;
        EosProcessRuntime.Shutdown();
        bool rejected = false;
        try
        {
            EosProcessRuntime.Acquire();
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }

        if (!rejected)
        {
            throw new InvalidOperationException("Terminal SDK shutdown guard failed.");
        }

        GD.Print($"EOS integration passed: {evidence}; terminal shutdown verified.");
        GetTree().Quit();
    }

    private void Fail()
    {
        GD.Print("EOS integration FAILED. Check runtime files, VC++ x64 prerequisites and development configuration; authentication material is not logged.");
        GetTree().Quit(1);
    }
}
