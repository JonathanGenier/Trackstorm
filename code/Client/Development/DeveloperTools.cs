namespace Trackstorm.Client.Development;

/// <summary>QA builds enable developer entry points; shipping builds can explicitly disable them.</summary>
internal static class DeveloperTools
{
    /// <summary>Whether this build enables developer UI and actions.</summary>
    internal static bool Enabled
    {
        get
        {
#if TRACKSTORM_DEVELOPER_TOOLS
            return true;
#else
            return false;
#endif
        }
    }
}
