using Godot;

namespace Trackstorm.Client.Items;

/// <summary>Stopped world-space trail emitters outlive the projectile, within the presentation owner's cap.</summary>
internal sealed partial class MissileSmokeResidue : Node3D
{
    internal float Lifetime { get; init; }
    private float _age;

    public override void _Process(double delta)
    {
        _age += (float)delta;
        if (_age >= Lifetime) { QueueFree(); }
    }
}
