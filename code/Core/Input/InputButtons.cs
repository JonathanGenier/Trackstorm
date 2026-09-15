namespace Trackstorm.Core.Input;

/// <summary>Stable wire bits for digital controls, independent of vehicle axes.</summary>
[Flags]
public enum InputButtons : ushort
{
	/// <summary>No controls.</summary>
	None = 0,
	/// <summary>Drift.</summary>
	Drift = 1,
	/// <summary>Use item.</summary>
	UseItem = 2,
	/// <summary>Leaderboard.</summary>
	Leaderboard = 4,
	/// <summary>Menu up.</summary>
	MenuUp = 8,
	/// <summary>Menu down.</summary>
	MenuDown = 16,
	/// <summary>Menu left.</summary>
	MenuLeft = 32,
	/// <summary>Menu right.</summary>
	MenuRight = 64,
	/// <summary>Menu accept.</summary>
	MenuAccept = 128,
	/// <summary>Menu cancel.</summary>
	MenuCancel = 256,
	/// <summary>Pause.</summary>
	Pause = 512,
}
