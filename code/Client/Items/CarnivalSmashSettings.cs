namespace Trackstorm.Client.Items;

/// <summary>Device-local smash preview, captured per collection; never changes pickup radius or respawn.</summary>
internal sealed record CarnivalSmashSettings
{
    private static CarnivalSmashSettings _current = new();
    internal static CarnivalSmashSettings Current
    {
        get => _current;
        set
        {
            if (!float.IsFinite(value.Size) || value.Size is < .5f or > 1.5f)
                throw new ArgumentException("Crate smash size must be between 0.5 and 1.5.");
            _current = value;
        }
    }
    internal float Size { get; init; } = 1;
}
