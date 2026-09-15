namespace Trackstorm.Core.Items;

/// <summary>Detached authoritative availability and last claim; activation ticks use the world clock.</summary>
/// <param name="Id">Configured arena marker identity.</param>
/// <param name="Available">Whether a new claim is allowed.</param>
/// <param name="NextActivationTick">Zero initially, otherwise the last claim's cooldown deadline.</param>
/// <param name="ClaimedBy">Last awarded vehicle, or zero before the first claim.</param>
/// <param name="Token">Last granted ownership token, or zero initially.</param>
/// <param name="Item">Last awarded item, or None initially.</param>
public sealed record ItemSpawnState(string Id, bool Available, ulong NextActivationTick, ulong ClaimedBy, ulong Token, HeldItem Item);
