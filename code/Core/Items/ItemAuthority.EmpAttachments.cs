using Trackstorm.Core.Vehicles;

namespace Trackstorm.Core.Items;

public sealed partial class ItemAuthority
{
    /// <summary>Bounds the match-owned attachment set; in-flight EMPs reserve their places.</summary>
    public const int MaximumEmpAttachments = 64;
    private readonly List<EmpClawAttachment> _empAttachments = [];
    private readonly HashSet<ulong> _empRemovals = [];

    /// <summary>Independent immutable anchors and deadlines, never refreshed by another claw.</summary>
    public IReadOnlyList<EmpClawAttachment> EmpAttachments => _empAttachments.ToArray();

    /// <summary>Host-only seam for removing one identified claw at the next atomic fixed boundary.
    /// Duplicate requests are harmless. This does not provide a client command or nose hit detection.</summary>
    public bool RemoveEmpAttachment(ulong id) => _empAttachments.Any(claw => claw.Id == id) && _empRemovals.Add(id);

    private static bool EmpTargetLives(EmpClawAttachment claw, Simulation.Simulation world) =>
        world.State.Match?.Phase != Matches.MatchPhase.Finished &&
        world.State.Vehicles.Any(v => v.VehicleId == claw.Vehicle && v.LifeId == claw.Life && v.CanInteract);

    private List<EmpClawAttachment> StageEmpAttachments(Simulation.Simulation world, ulong tick,
        IReadOnlyList<VehicleStepRequest> requests) => _empAttachments.Where(claw =>
            claw.ExpiresAtTick > tick && !_empRemovals.Contains(claw.Id) && EmpTargetLives(claw, world) &&
            !requests.Any(r => r.VehicleId == claw.Vehicle && r.Reset.HasValue)).ToList();
}
