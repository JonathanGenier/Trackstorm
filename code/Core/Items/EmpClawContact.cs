using System.Numerics;

namespace Trackstorm.Core.Items;

/// <summary>Single committed hit handoff for attachment: actual victim/life and contact in its local frame.
/// It carries no EMP timer, health, ammunition damage or impairment authority.</summary>
public sealed record EmpClawContact(ulong Vehicle, ulong Life, Vector3 LocalPosition, Vector3 LocalNormal);
