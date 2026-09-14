using System;
using System.Linq;

namespace Epic.OnlineServices;

/// <summary>Trackstorm extension to the pinned official binding; no upstream files are changed.</summary>
public sealed partial class Helper
{
    /// <summary>
    /// Removes abandoned managed registrations only AFTER the owning native platform has been released.
    /// All EOS operations and this method must run on the same owner thread.
    /// </summary>
    public static void ReleaseTrackstormCallbacks(object owner)
    {
        IntPtr[] addresses;
        lock (s_ClientDatas)
        {
            addresses = s_ClientDatas.Where(pair => ReferenceEquals(pair.Value, owner)).Select(pair => pair.Key).ToArray();
        }

        foreach (var address in addresses)
        {
            RemoveCallback(address);
        }
    }
}
