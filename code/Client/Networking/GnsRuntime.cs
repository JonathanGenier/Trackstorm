using System.Runtime.InteropServices;
using GnsSharp;

namespace Trackstorm.Client.Networking;

/// <summary>Shares the process-global native runtime among gateways on one owning thread.</summary>
internal static class GnsRuntime
{
    private static readonly object Gate = new();
    private static readonly FSteamNetworkingSocketsDebugOutput DebugOutput = OnDebugOutput;
    [ThreadStatic]
    private static bool _capturingListenError;
    [ThreadStatic]
    private static string? _listenError;
    private static int _references;
    private static int _threadId;
    private static IntPtr _library;

    /// <summary>Acquires initialization without allowing callbacks to race across threads.</summary>
    internal static void Acquire()
    {
        lock (Gate)
        {
            if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            {
                throw new PlatformNotSupportedException("The packaged transport currently supports Windows x64.");
            }

            if (_references != 0 && _threadId != Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException("All transport gateways must use the same owning thread.");
            }

            if (_references == 0)
            {
                if (_library == IntPtr.Zero)
                {
                    // The engine executable is outside the game assembly directory. Resolve native
                    // dependencies beside the binding, never through the current working directory.
                    string directory = AppContext.BaseDirectory;
                    _library = NativeLibrary.Load(Path.Combine(directory, "GameNetworkingSockets.dll"), typeof(GameNetworkingSockets).Assembly, DllImportSearchPath.UseDllDirectoryForDependencies);
                }

                if (!GameNetworkingSockets.Init(out string? error))
                {
                    throw new InvalidOperationException($"GameNetworkingSockets initialization failed: {error}");
                }

                _threadId = Environment.CurrentManagedThreadId;
                ISteamNetworkingUtils.User!.SetDebugOutputFunction(ESteamNetworkingSocketsDebugOutputType.Error, DebugOutput);
            }

            _references++;
        }
    }

    /// <summary>Captures the synchronous native creation error; background diagnostics cannot contaminate it.</summary>
    /// <param name="endpoint">Validated local endpoint.</param>
    /// <param name="configuration">Per-listener options.</param>
    /// <param name="error">Native creation diagnostic, if supplied.</param>
    /// <returns>The native listener handle, or Invalid.</returns>
    internal static HSteamListenSocket CreateListener(in SteamNetworkingIPAddr endpoint, SteamNetworkingConfigValue_t[] configuration, out string? error)
    {
        _listenError = null;
        _capturingListenError = true;
        try
        {
            return ISteamNetworkingSockets.User!.CreateListenSocketIP(in endpoint, configuration);
        }
        finally
        {
            error = _listenError;
            _capturingListenError = false;
            _listenError = null;
        }
    }

    /// <summary>Shuts down the native runtime only after the last gateway releases its handles.</summary>
    internal static void Release()
    {
        lock (Gate)
        {
            if (--_references == 0)
            {
                GameNetworkingSockets.Kill();
                _threadId = 0;
                // Keep the single module mapping alive: P/Invoke caches function addresses. Kill
                // releases sockets/threads; unloading the DLL would invalidate subsequent Init calls.
            }
        }
    }

    private static void OnDebugOutput(ESteamNetworkingSocketsDebugOutputType type, string message)
    {
        // Do not call native APIs or user handlers from this callback. Native spew may hold its global lock.
        if (_capturingListenError && message.StartsWith("Cannot create listen socket.", StringComparison.Ordinal))
        {
            _listenError = message.Trim();
        }
    }
}
