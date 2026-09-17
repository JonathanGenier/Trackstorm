using System.Runtime.InteropServices;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using Trackstorm.Core.Sessions;

namespace Trackstorm.Client.Online;

/// <summary>EOS Initialize/Shutdown are process-scoped; only platform handles can be repeatedly recreated.</summary>
internal static class EosProcessRuntime
{
    private static readonly object Gate = new();
    private static int _thread;
    private static bool _initialized;
    private static bool _shutdown;
    private static bool _leased;
    private static IntPtr _library;

    /// <summary>Gets whether the process SDK is initialized and has not shut down.</summary>
    public static bool Initialized => _initialized && !_shutdown;

    /// <summary>Initializes EOS once and reserves exclusive platform ownership on this thread.</summary>
    public static void Acquire()
    {
        lock (Gate)
        {
            if (_shutdown)
            {
                throw new InvalidOperationException("EOS SDK was shut down. Restart Trackstorm before using EOS again.");
            }

            if (_leased || (_initialized && _thread != System.Environment.CurrentManagedThreadId))
            {
                throw new InvalidOperationException("EOS already has an owner. Stop the existing EOS platform before starting another.");
            }

            if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
            {
                throw new InvalidOperationException("This EOS integration requires Windows x64.");
            }

            if (!_initialized)
            {
                if (_library == IntPtr.Zero)
                {
                    _library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "EOSSDK-Win64-Shipping.dll"), typeof(PlatformInterface).Assembly, DllImportSearchPath.UseDllDirectoryForDependencies);
                }

                var options = new InitializeOptions { ProductName = "Trackstorm", ProductVersion = GameVersion.Current.ToString() };
                Result result = PlatformInterface.Initialize(ref options);
                if (result != Result.Success)
                {
                    throw new InvalidOperationException($"EOS initialization failed ({result}). Check SDK version and process ownership.");
                }

                _thread = System.Environment.CurrentManagedThreadId;
                _initialized = true;
            }

            _leased = true;
        }
    }

    /// <summary>Returns platform ownership without performing terminal SDK shutdown.</summary>
    public static void Release()
    {
        lock (Gate)
        {
            if (!_leased || _thread != System.Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException("Only the EOS owner thread may release its platform lease.");
            }

            _leased = false;
        }
    }

    /// <summary>Permanently shuts down the SDK after the platform has been released.</summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            if (!_initialized || _shutdown)
            {
                return;
            }

            if (_leased || _thread != System.Environment.CurrentManagedThreadId)
            {
                throw new InvalidOperationException("Release the EOS platform on its owner thread before SDK shutdown.");
            }

            _shutdown = true;
            Result result = PlatformInterface.Shutdown();
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"EOS shutdown failed ({result}). Restart the application.");
            }
        }
    }
}
