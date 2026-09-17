using Godot;
using Trackstorm.Core.Vehicles;

namespace Trackstorm.Client.Audio;

/// <summary>One arena-owned spatial emitter with continuously running, smoothly mixed engine layers.</summary>
internal sealed partial class VehicleAudio : Node3D
{
    private readonly List<AudioStreamPlayer3D> _layers = new();
    private VehicleSnapshot? _state;
    private float _speed;
    private float _skid;
    private float _presence;

    /// <inheritdoc/>
    public override void _Ready()
    {
        foreach (AudioCue cue in new[] { AudioCue.EngineIdle, AudioCue.EngineLow, AudioCue.EngineHigh, AudioCue.Skid })
        {
            var player = ArenaAudio.Loop3D(cue);
            AddChild(player);
            player.Play();
            _layers.Add(player);
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        float factor = 1 - MathF.Exp(-8 * (float)delta);
        _speed = Mathf.Lerp(_speed, _state?.Speed ?? 0, factor);
        _presence = Mathf.Lerp(_presence, _state?.CanInteract == true ? 1 : 0, factor);
        _skid = Mathf.Lerp(_skid, _state is { CanInteract: true } state && state.Movement.Grounded && state.Movement.Drifting ? Math.Clamp(state.Speed / 8, 0, 1) : 0, factor);
        EngineMix mix = EngineMix.Select(_speed);
        float[] gains = [mix.Idle, mix.Low, mix.High, _skid];
        for (int index = 0; index < _layers.Count; index++)
        {
            _layers[index].VolumeDb = AudioRouting.Decibels(gains[index] * _presence) - (index == 3 ? 14 : 20);
            _layers[index].PitchScale = index == 3 ? 1 : mix.Pitch;
        }
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        foreach (AudioStreamPlayer3D layer in _layers)
        {
            layer.Stop();
            layer.Stream = null;
        }
    }

    /// <summary>Updates reconstructable continuous emitters from displayed authoritative state.</summary>
    /// <param name="state">Confirmed gameplay boundary.</param>
    /// <param name="position">Displayed arena-space position provider.</param>
    internal void Follow(VehicleSnapshot state, Vector3 position)
    {
        _state = state;
        Position = position;
    }
}
