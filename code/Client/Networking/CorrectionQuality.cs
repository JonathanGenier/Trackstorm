namespace Trackstorm.Client.Networking;

/// <summary>Constant-space local correction counters, split at two seconds after prediction initialization.</summary>
internal sealed class CorrectionQuality
{
    internal long Samples { get; private set; }
    internal long AboveCentimetre { get; private set; }
    internal long AtLeastDecimetre { get; private set; }
    internal long AtLeastMetre { get; private set; }
    internal long AtLeastThreeMetres { get; private set; }
    internal float Maximum { get; private set; }

    internal void Record(float error)
    {
        Samples++;
        Maximum = Math.Max(Maximum, error);
        if (error > 0.01f) { AboveCentimetre++; }
        if (error >= 0.1f) { AtLeastDecimetre++; }
        if (error >= 1) { AtLeastMetre++; }
        if (error >= 3) { AtLeastThreeMetres++; }
    }

    public override string ToString() => $"{Samples} reconciliations; >1cm {AboveCentimetre}; >=10cm {AtLeastDecimetre}; >=1m {AtLeastMetre}; >=3m {AtLeastThreeMetres}; max {Maximum:0.000} m";
}
