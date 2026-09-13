using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Scene-authored collision surface; tuning and handling rules remain in Core.</summary>
public sealed partial class SurfaceBody : StaticBody3D
{
    /// <summary>Identifier reported by fixed-step support detection.</summary>
    [Export]
    public SurfaceType Surface { get; set; } = SurfaceType.Concrete;
}
