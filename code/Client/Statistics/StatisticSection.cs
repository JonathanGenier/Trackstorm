namespace Trackstorm.Client.Statistics;

/// <summary>A display-only category; adding diagnostics does not add UI controls or authority.</summary>
/// <param name="Title">Category heading.</param>
/// <param name="Text">Allowlisted current values.</param>
internal sealed record StatisticSection(string Title, string Text);
