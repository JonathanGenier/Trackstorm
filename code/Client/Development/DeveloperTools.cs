namespace Trackstorm.Client.Development;

/// <summary>Build-time switch; Release builds omit developer entry points unless explicitly enabled.</summary>
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
