using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Yaw mount and pitched payload beneath the fixed Car rack.</summary>
internal sealed partial class WeaponAimMount : Node3D
{
    private readonly Node3D _pitch = new() { Name = "WeaponPitch" };
    internal WeaponAimMount(HeldItem item)
    {
        Name = "WeaponYaw";
        AddChild(new MeshInstance3D { Name = "YawPedestal", Position = new(0, -.2f, 0),
            Mesh = new CylinderMesh { Height = .4f, TopRadius = .16f, BottomRadius = .23f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.23f, .25f, .27f), Metallic = .8f, Roughness = .55f } });
        AddChild(_pitch);
        _pitch.AddChild(RackItemVisual.Create(item));
    }

    internal void Present(WeaponAimSolution? aim, float delta)
    {
        float blend = aim is null ? 1 : 1 - MathF.Exp(-30 * Math.Max(0, delta));
        Rotation = new(0, Mathf.LerpAngle(Rotation.Y, aim?.Yaw ?? 0, blend), 0);
        _pitch.Rotation = new(Mathf.LerpAngle(_pitch.Rotation.X, aim?.Pitch ?? 0, blend), 0, 0);
    }
}
