using Godot;
using Trackstorm.Core.Input;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Vehicles;

/// <summary>Small local arena for exercising production movement against ramps, walls, vehicles, and movable props.</summary>
public sealed partial class VehicleArena : Node3D
{
    private readonly Camera3D _camera = new() { Current = true, Fov = 65 };
    private readonly Label _status = new();
    private readonly Label _health = new();
    private readonly Label _title = new() { Text = "LOCAL VEHICLE ARENA" };
    private readonly Label _instructions = new() { Text = "Drive / brake / steer with your bindings. Hold drift through a turn, then release for boost.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private MeshInstance3D? _blast;
    private float _blastSeconds;

    /// <summary>Controllable local vehicle.</summary>
    internal VehicleBody Player { get; private set; } = null!;
    /// <summary>Second physical vehicle for collision checks.</summary>
    internal VehicleBody Target { get; private set; } = null!;
    /// <summary>Movable prop for native interaction checks.</summary>
    internal RigidBody3D Crate { get; private set; } = null!;

    /// <inheritdoc/>
    public override void _Ready()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("172235"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b9d6ed"),
                AmbientLightEnergy = 0.65f,
            }
        });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = 1.4f, ShadowEnabled = true });
        AddStatic(new Vector3(80, 1, 80), new Vector3(0, -0.5f, 0), new Color("303b4b"));
        AddStatic(new Vector3(1, 4, 80), new Vector3(-40, 1.5f, 0), new Color("bf6d39"));
        AddStatic(new Vector3(1, 4, 80), new Vector3(40, 1.5f, 0), new Color("bf6d39"));
        AddStatic(new Vector3(80, 4, 1), new Vector3(0, 1.5f, -40), new Color("bf6d39"));
        AddStatic(new Vector3(80, 4, 1), new Vector3(0, 1.5f, 40), new Color("bf6d39"));
        StaticBody3D ramp = AddStatic(new Vector3(8, 0.4f, 12), new Vector3(0, 0.95f, -5), new Color("607789"));
        ramp.Rotation = new Vector3(0.2f, 0, 0);
        AddStatic(new Vector3(5, 2, 5), new Vector3(-15, 1, -10), new Color("7b879a"));
        for (int z = -32; z <= 32; z += 8)
        {
            AddChild(VehicleBody.Box(new Vector3(0.15f, 0.02f, 3), new Vector3(-5, 0.02f, z), new Color("e4be60")));
            AddChild(VehicleBody.Box(new Vector3(0.15f, 0.02f, 3), new Vector3(5, 0.02f, z), new Color("e4be60")));
        }

        Player = new VehicleBody { Name = "PlayerVehicle", Position = new Vector3(0, 1, 20), VehicleId = 1 };
        Target = new VehicleBody { Name = "TargetVehicle", Position = new Vector3(12, 1, -15), VehicleId = 2, Paint = new Color("f28b46") };
        AddChild(Player);
        AddChild(Target);
        Crate = new RigidBody3D { Name = "MovableCrate", Position = new Vector3(12, 1.2f, 5), Mass = 150, ContinuousCd = true };
        Crate.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.8f, 1.8f, 1.8f) } });
        Crate.AddChild(VehicleBody.Box(new Vector3(1.8f, 1.8f, 1.8f), Vector3.Zero, new Color("ddba5b")));
        AddChild(Crate);
        AddChild(_camera);
        _camera.Position = new Vector3(0, 8, 32);
        var layer = new CanvasLayer { Layer = 1 };
        AddChild(layer);
        var backdrop = new PanelContainer { AnchorRight = 1, OffsetLeft = 190, OffsetTop = 22, OffsetRight = -24, OffsetBottom = 22, GrowVertical = Control.GrowDirection.End };
        backdrop.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color("172235"), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 8, ContentMarginBottom = 8 });
        layer.AddChild(backdrop);
        var panel = new VBoxContainer();
        backdrop.AddChild(panel);
        panel.AddChild(_title);
        panel.AddChild(_status);
        panel.AddChild(_health);
        panel.AddChild(_instructions);
        var actions = new HBoxContainer();
        panel.AddChild(actions);
        var reset = new Button { Text = "Reset vehicles", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        reset.Pressed += ResetVehicles;
        actions.AddChild(reset);
        var explode = new Button { Text = "Detonate nearby", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, TooltipText = "Damage and push nearby vehicles with an explosion." };
        explode.Pressed += () => Explode(Player.GlobalPosition + new Vector3(-2, -0.2f, 0.5f));
        actions.AddChild(explode);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        Vector3 forward = -Player.GlobalBasis.Z;
        Vector3 desired = Player.GlobalPosition - (forward * 11) + (Vector3.Up * 6);
        _camera.GlobalPosition = _camera.GlobalPosition.Lerp(desired, 1 - MathF.Exp(-6 * (float)delta));
        _camera.LookAt(Player.GlobalPosition + (Vector3.Up * 0.5f));
        VehicleState state = Player.State;
        bool compact = GetViewport().GetVisibleRect().Size.Y < 500;
        _title.Visible = !compact;
        _instructions.Visible = !compact;
        _status.Text = state.BoostTicks > 0 ? "BOOST" : state.Drifting ? "DRIFT — hold your turn to charge" : state.Grounded ? "GROUNDED" : "AIRBORNE";
        VehicleDamageState health = Player.DamageState;
        _health.Text = health.Destroyed ? "DESTROYED — reset vehicles to drive again" : $"HP  {health.CurrentHP:0} / {health.MaxHP:0}     Target HP  {Target.DamageState.CurrentHP:0} / {Target.DamageState.MaxHP:0}";
        _health.Modulate = health.Destroyed ? new Color("ff906b") : Colors.White;
        if (_blast is not null)
        {
            _blastSeconds -= (float)delta;
            _blast.Scale = Vector3.One * (1 + ((0.5f - _blastSeconds) * 10));
            if (_blastSeconds <= 0)
            {
                _blast.QueueFree();
                _blast = null;
            }
        }
    }

    /// <summary>Routes only logical input into the local body adapter.</summary>
    /// <param name="input">Current fixed-step frame.</param>
    internal void SubmitInput(InputFrame input) => Player.SubmitInput(input);

    /// <summary>Local authority demonstration of the item-independent Core explosion helper.</summary>
    /// <param name="center">World-space blast center.</param>
    internal void Explode(Vector3 center)
    {
        foreach (VehicleBody vehicle in new[] { Player, Target })
        {
            DamageEffect effect = VehicleDamageMath.Explosion(VehicleBody.ToCore(center), VehicleBody.ToCore(vehicle.GlobalPosition), 8, 55, 15000, new System.Numerics.Vector3(0.7f, 0.25f, -0.6f));
            vehicle.ApplyEffect(effect, new DamageContext("explosion", Player.VehicleId, "local-arena-blast"));
        }

        _blast?.QueueFree();
        _blast = new MeshInstance3D
        {
            Position = center,
            Mesh = new SphereMesh { Radius = 0.5f, Height = 1 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1, 0.45f, 0.1f, 0.3f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
        };
        _blastSeconds = 0.5f;
        AddChild(_blast);
    }

    private StaticBody3D AddStatic(Vector3 size, Vector3 position, Color color)
    {
        var body = new StaticBody3D { Position = position };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        body.AddChild(VehicleBody.Box(size, Vector3.Zero, color));
        AddChild(body);
        return body;
    }

    private void ResetVehicles()
    {
        Player.ResetBody(new VehiclePhysicsState(new System.Numerics.Vector3(0, 1, 20), System.Numerics.Quaternion.Identity, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero));
        Target.ResetBody(new VehiclePhysicsState(new System.Numerics.Vector3(12, 1, -15), System.Numerics.Quaternion.Identity, System.Numerics.Vector3.Zero, System.Numerics.Vector3.Zero));
    }
}
