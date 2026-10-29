namespace Trackstorm.Core.Vehicles;

/// <summary>Life-scoped continuation of the fourth practice car's single pickup trip.</summary>
public sealed record PondPracticeState(ulong Vehicle, ulong Life, PondPracticePhase Phase, bool Acquired = false);
