namespace Trackstorm.Client.Hud;

/// <summary>Optional slot-local resource content. A missing fraction is a count-only readout.</summary>
internal sealed record ItemHudResource(string Text, double? Fraction);
