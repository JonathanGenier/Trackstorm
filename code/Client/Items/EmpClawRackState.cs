namespace Trackstorm.Client.Items;

/// <summary>Read-only presentation input for the future EMP authority adapter. Bits identify
/// the three original rounds, never a locally decremented ammunition count. Sequence zero
/// means no shot; reconstruction seeds the sequence without replaying historical discharge.</summary>
internal sealed record EmpClawRackState(ulong Life, ulong Token, ulong Revision, bool Selected,
    byte RoundMask, ulong ShotSequence = 0, int FiredRound = -1, float ReloadDuration = 50f / 60, float ReloadRemaining = 0)
{
    internal void Validate()
    {
        if (!float.IsFinite(ReloadDuration) || ReloadDuration <= 0 || !float.IsFinite(ReloadRemaining) || ReloadRemaining < 0 || Life == 0 || Token == 0 || RoundMask > 7 || FiredRound is < -1 or > 2 ||
            (FiredRound >= 0 && (RoundMask & (1 << FiredRound)) != 0))
        { throw new ArgumentException("EMP presentation requires a valid capability and accepted remaining-round mask."); }
    }
}
