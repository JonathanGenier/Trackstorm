namespace Trackstorm.Core.Items;

/// <summary>One player's two fixed held slots and selection in one vehicle life. Each grant token is match-unique.</summary>
public sealed record ItemSlot(ulong Vehicle, ulong Life, ulong Token, HeldItem Item)
{
    /// <summary>Fixed physical magazine and release stroke shared with presentation.</summary>
    public const int SalvoMagazineSize = 5;
    public const int SalvoReleaseTicks = 5;
    public const float SalvoDeploymentSeconds = 1.25f;
    /// <summary>A nonzero launch deadline irrevocably commits the remaining magazine.</summary>
    public bool SalvoCommitted => Item == HeldItem.Salvo && SalvoReadyTick != 0;
    /// <summary>Reserved projectile capacity prevents other weapons starving an accepted volley.</summary>
    public int ReservedSalvoShots => (SalvoCommitted ? SalvoShots : 0) +
        (SecondItem == HeldItem.Salvo && SecondSalvoReadyTick != 0 ? SecondSalvoShots : 0);
    /// <summary>Minimum complete Wrench deployment; local mechanics additionally gate use.</summary>
    public const float WrenchDeploymentSeconds = .8f;
    /// <summary>Complete selected thruster deployment, including replacement, at 60 Hz (0.6 seconds).</summary>
    public const int NitroDeploymentDurationTicks = 36;
    /// <summary>Remaining authoritative deployment time for the selected Nitro; zero means ready.</summary>
    public int NitroDeploymentTicks { get; init; }
    /// <summary>Captured selected-Missile deployment boundaries, in authoritative ticks.</summary>
    public ulong MissileDeployStartTick { get; init; }
    /// <summary>No Missile use is accepted before this boundary.</summary>
    public ulong MissileReadyTick { get; init; }
    /// <summary>Captured full return interval, retained through a subsequent pickup.</summary>
    public ulong MissileStowStartTick { get; init; }
    /// <summary>The next weapon may deploy only after this boundary.</summary>
    public ulong MissileStowEndTick { get; init; }
    /// <summary>Unfired Salvo rounds in the first physical slot.</summary>
    public int SalvoShots { get; init; } = Item == HeldItem.Salvo ? 5 : 0;
    /// <summary>Unfired Salvo rounds in the second physical slot.</summary>
    public int SecondSalvoShots { get; init; }
    /// <summary>Next automatic launch tick; zero means awaiting the first deliberate trigger.</summary>
    public ulong SalvoReadyTick { get; init; }
    /// <summary>Second-slot automatic launch deadline; zero means uncommitted.</summary>
    public ulong SecondSalvoReadyTick { get; init; }
    /// <summary>Discrete resource in the first physical slot, absent for other items.</summary>
    public MachineGunAmmo? Ammo { get; init; }
    /// <summary>Discrete resource in the second physical slot.</summary>
    public MachineGunAmmo? SecondAmmo { get; init; }
    /// <summary>Accepted EMP presentation load in the first physical slot.</summary>
    public EmpClawLoad? EmpClaw { get; init; }
    /// <summary>Accepted EMP presentation load in the second physical slot.</summary>
    public EmpClawLoad? SecondEmpClaw { get; init; }
    /// <summary>Generic percentage projection for the HUD.</summary>
    public double ResourcePercentage => Ammo?.Percentage ?? NitroCharge;
    /// <summary>Generic second-slot percentage projection.</summary>
    public double SecondResourcePercentage => SecondAmmo?.Percentage ?? SecondNitroCharge;
    /// <summary>Remaining percentage in the first physical slot; zero for other items.</summary>
    public double NitroCharge { get; init; } = Item == HeldItem.Nitro ? 100 : 0;
    /// <summary>Remaining percentage in the second physical slot.</summary>
    public double SecondNitroCharge { get; init; }
    /// <summary>Exact capability engaged by an accepted use, retained only while held.</summary>
    public ulong EngagedToken { get; init; }
    /// <summary>Second physical slot's grant capability; zero before its first grant.</summary>
    public ulong SecondToken { get; init; }
    /// <summary>Second physical slot's held identity.</summary>
    public HeldItem SecondItem { get; init; }
    /// <summary>Selected physical slot, zero or one, including when empty.</summary>
    public byte ActiveSlot { get; init; }
    /// <summary>Monotonic selection command watermark within this life.</summary>
    public ulong SelectionRevision { get; init; }
    /// <summary>Capability for the currently selected slot, for existing item handlers.</summary>
    public ItemSlot Active => new(Vehicle, Life, ActiveSlot == 0 ? Token : SecondToken, ActiveSlot == 0 ? Item : SecondItem)
    { NitroCharge = ActiveSlot == 0 ? NitroCharge : SecondNitroCharge, NitroDeploymentTicks = NitroDeploymentTicks, Ammo = ActiveSlot == 0 ? Ammo : SecondAmmo,
        EmpClaw = ActiveSlot == 0 ? EmpClaw : SecondEmpClaw,
        SalvoShots = ActiveSlot == 0 ? SalvoShots : SecondSalvoShots,
        SalvoReadyTick = ActiveSlot == 0 ? SalvoReadyTick : SecondSalvoReadyTick };
    /// <summary>Whether acquisition must leave both held items untouched.</summary>
    public bool Full => Item != HeldItem.None && SecondItem != HeldItem.None;
}
