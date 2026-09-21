namespace Trackstorm.Client.Frontend;

/// <summary>Independent content and action for one reusable hanging plate.</summary>
internal sealed record MainMenuEntry(string Id, string Label, int Icon, bool Available, string Status, Action Activate);
