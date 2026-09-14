using Epic.OnlineServices.Version;
using Godot;
using Trackstorm.Client.Online;

namespace Trackstorm.Client.Verification;

/// <summary>Runs native initialization in an isolated process, optionally authenticating with real dev configuration.</summary>
public sealed partial class EosIntegrationChecks : Node
{
    private EosIdentityService? _identity;
    private EosConfiguration? _configuration;
    private OnlineProductUserId? _firstIdentity;
    private int _cycles;

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
                    Finish("three real platform/login/logout cycles with stable identity");
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
        _identity?.Dispose();
        EosProcessRuntime.Shutdown();
    }

    private void StartCycle()
    {
        _identity!.Start(_configuration!);
        _identity.Login();
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
