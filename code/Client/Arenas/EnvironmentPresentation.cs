using Godot;
using Trackstorm.Core.Development;

namespace Trackstorm.Client.Arenas;

/// <summary>Reconstructable lighting package driven solely by the accepted session preset.</summary>
internal sealed partial class EnvironmentPresentation : Node3D
{
    private readonly WorldEnvironment _world = new();
    private readonly DirectionalLight3D _sun = new() { ShadowEnabled = true };
    private EnvironmentPreset? _current;
    private Godot.Environment? _environment;
    private Sky? _sky;
    private ShaderMaterial? _material;

    internal EnvironmentPreset? Current => _current;
    internal static string DisplayName(EnvironmentPreset preset) => preset switch
    {
        EnvironmentPreset.ClearBlue => "Clear Blue",
        EnvironmentPreset.Night => "Night",
        EnvironmentPreset.EmberSky => "Ember Sky",
        EnvironmentPreset.Apocalypse => "Apocalypse",
        EnvironmentPreset.NeonSunset => "Neon Sunset",
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    public override void _Ready()
    {
        AddChild(_world);
        AddChild(_sun);
        Apply(EnvironmentPreset.ClearBlue);
    }

    internal void Apply(EnvironmentPreset preset)
    {
        if (_current == preset) { return; }
        // Explicit packages: no clock, interpolation or shared-resource mutation.
        (string Zenith, string Horizon, string Light, string Ambient, float Energy, float Fill, float Elevation, float Azimuth, float Fog, float Exposure) p = preset switch
        {
            EnvironmentPreset.ClearBlue => ("367fbd", "b5d0d9", "fff1dc", "b7cee4", 1.4f, .65f, -55f, -25f, .00035f, 1f),
            EnvironmentPreset.Night => ("020613", "15233e", "b8cced", "9baed0", .4f, .5f, -38f, 130f, .0008f, 1f),
            EnvironmentPreset.EmberSky => ("4a1c24", "ee783a", "ff9a53", "bd795e", 1.15f, .55f, -22f, -65f, .0018f, 1f),
            EnvironmentPreset.Apocalypse => ("181c1c", "8c8a63", "d1cb8c", "87948a", .75f, .42f, -65f, 30f, .003f, .85f),
            EnvironmentPreset.NeonSunset => ("182967", "ed819e", "ffb174", "a08ac5", .9f, .55f, -12f, -90f, .001f, 1f),
            _ => throw new ArgumentOutOfRangeException(nameof(preset)),
        };
        _world.Environment = null;
        ReleaseResources();
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/effects/EnvironmentSky.gdshader") };
        _material.SetShaderParameter("zenith", new Color(p.Zenith));
        _material.SetShaderParameter("horizon", new Color(p.Horizon));
        _material.SetShaderParameter("light_color", new Color(p.Light));
        _material.SetShaderParameter("sunset", preset == EnvironmentPreset.NeonSunset);
        _material.SetShaderParameter("night", preset == EnvironmentPreset.Night);
        _material.SetShaderParameter("cloud_amount", preset == EnvironmentPreset.Apocalypse ? 1f : preset == EnvironmentPreset.ClearBlue ? .15f : .45f);
        _sky = new Sky { SkyMaterial = _material };
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky, Sky = _sky,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(p.Ambient), AmbientLightEnergy = p.Fill,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic, TonemapExposure = p.Exposure,
            FogEnabled = true, FogLightColor = new Color(p.Horizon), FogDensity = p.Fog,
            FogSkyAffect = .35f,
        };
        _world.Environment = _environment;
        _sun.LightColor = new Color(p.Light);
        _sun.LightEnergy = p.Energy;
        _sun.RotationDegrees = new Vector3(p.Elevation, p.Azimuth, 0);
        _current = preset;
    }

    public override void _ExitTree()
    {
        _world.Environment = null;
        ReleaseResources();
    }

    private void ReleaseResources()
    {
        _environment?.Dispose();
        _sky?.Dispose();
        _material?.Dispose();
    }
}
