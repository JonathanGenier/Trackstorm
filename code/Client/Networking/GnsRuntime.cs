using System.Runtime.InteropServices;
using GnsSharp;

namespace Trackstorm.Client.Networking;

/// <summary>Shares the process-global native runtime among gateways on one owning thread.</summary>
internal static class GnsRuntime
{
    private static readonly object Gate = new();
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
            }

            _references++;
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
}
