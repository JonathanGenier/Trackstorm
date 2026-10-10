using Godot;
using Trackstorm.Core.Items;

namespace Trackstorm.Client.Items;

/// <summary>Production geared turret, rigid cradle lift, telescoping missile and symmetric fins.</summary>
internal sealed partial class MissileLauncher : Node3D
{
    internal const string AssetPath = "res://assets/items/missile/MissileLauncher.glb";
    private readonly Node3D _yaw;
    private readonly Node3D _lift;
    private readonly Node3D _pitch;
    private readonly MissileVisual _missile = new();
    internal bool Loaded { get => _missile.Visible; set => _missile.Visible = value; }

    internal MissileLauncher()
    {
        Name = "MissileLauncher";
        var model = Networking.MatchResourceLoader.LoadResource<PackedScene>(AssetPath).Instantiate<Node3D>();
        AddChild(model);
        _yaw = (Node3D)model.FindChild("TurretYaw", true, false);
        _lift = (Node3D)model.FindChild("MountLift", true, false);
        _pitch = (Node3D)model.FindChild("CradlePitch", true, false);
        _pitch.AddChild(_missile);
    }

    internal void Present(float progress, WeaponAimSolution? aim, float delta)
    {
        float lift = Ease(progress, .36f, .54f);
        _lift.Position = Vector3.Up * (.27f * lift);
        _missile.SetDeployment(Ease(progress, .54f, .80f), Ease(progress, .80f, 1));
        float blend = 1 - MathF.Exp(-18 * Math.Max(0, delta));
        // Return articulation above the open deck before the rack can descend.
        float yaw = progress >= .999f ? aim?.Yaw ?? 0 : 0;
        float pitch = progress >= .999f ? aim?.Pitch ?? 0 : 0;
        _yaw.Rotation = new(0, Mathf.LerpAngle(_yaw.Rotation.Y, yaw, blend), 0);
        _pitch.Rotation = new(Mathf.LerpAngle(_pitch.Rotation.X, pitch, blend), 0, 0);
    }

    internal bool Centered => Math.Abs(Mathf.AngleDifference(_yaw.Rotation.Y, 0)) < .01f && Math.Abs(Mathf.AngleDifference(_pitch.Rotation.X, 0)) < .01f;
    internal static float Ease(float value, float start, float end) =>
        Mathf.SmoothStep(0, 1, Mathf.Clamp((value - start) / (end - start), 0, 1));
}
