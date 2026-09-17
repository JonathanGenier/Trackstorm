namespace Trackstorm.Client.Hud;

/// <summary>A timed presentation row, not a separate gameplay event.</summary>
internal sealed record ActivityFeedEntry(string Text, ActivityFeedTone Tone, double ExpiresAt);
