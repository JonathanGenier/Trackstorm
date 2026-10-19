using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Resolved presentation pose/state for one accepted claw. The caller resolves
/// target-local attachments; this view contains no target acquisition or hit logic.</summary>
internal sealed record EmpClawView(ulong Id, Transform3D Pose, EmpClawVisualState State);
