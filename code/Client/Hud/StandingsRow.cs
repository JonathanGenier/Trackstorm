namespace Trackstorm.Client.Hud;

/// <summary>One detached presentation row; identity never depends on name or transport ID.</summary>
/// <param name="PlayerId">Stable identity.</param>
/// <param name="Rank">Core rank.</param>
/// <param name="Name">Session name.</param>
/// <param name="Kills">Confirmed kills.</param>
/// <param name="Deaths">Confirmed deaths.</param>
/// <param name="Ping">Safe latency text.</param>
/// <param name="Winner">Recorded winner identity.</param>
/// <param name="Local">Local player marker.</param>
/// <param name="Connected">Authoritative online state; retained offline participants keep their rank and totals.</param>
internal sealed record StandingsRow(ulong PlayerId, int Rank, string Name, int Kills, int Deaths, string Ping, bool Winner, bool Local, bool Connected);
