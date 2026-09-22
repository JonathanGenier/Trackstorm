using System.Globalization;
using Trackstorm.Core.Matches;

namespace Trackstorm.Client.Hud;

/// <summary>Read-only local Circus totals and temporary authoritative scoring feedback.</summary>
internal sealed record CircusHudView(string Total, string Multiplier, IReadOnlyList<CircusFeedbackRow> Rows)
{
    internal static string FormatPoints(double points) => points.ToString("0.##", CultureInfo.InvariantCulture);
    internal static string FormatMultiplier(double multiplier) => string.Create(CultureInfo.InvariantCulture, $"x{multiplier:0.##}");
}

/// <summary>One category-keyed live or recent scoring row.</summary>
internal sealed record CircusFeedbackRow(CircusScoreCategory Category, string Name, double Points, CircusFeedbackKind Kind, float Opacity);

internal enum CircusFeedbackKind
{
    Pending,
    Banked,
    Lost,
}
