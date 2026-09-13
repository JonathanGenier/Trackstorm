using System.Buffers.Binary;
using Godot;

namespace Trackstorm.Client.Vehicles;

/// <summary>Original procedural impact sound and body flash; presentation never changes gameplay state.</summary>
internal sealed partial class VehicleFeedback : Node3D
{
    private readonly AudioStreamPlayer3D _audio = new() { MaxDistance = 65, UnitSize = 12, VolumeDb = -12 };
    private readonly AudioStreamWav _impact = Tone(false);
    private readonly AudioStreamWav _explosion = Tone(true);
    private StandardMaterial3D _material = null!;
    private Color _paint;
    private float _flashSeconds;
    private bool _destroyed;

    /// <summary>Number of feedback cues submitted to the engine, for runtime checks.</summary>
    internal int CueCount { get; private set; }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _audio.Bus = AudioServer.GetBusIndex("SFX") >= 0 ? "SFX" : "Master";
        AddChild(_audio);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _flashSeconds = Math.Max(0, _flashSeconds - (float)delta);
        _material.AlbedoColor = _flashSeconds > 0 ? new Color("fff0bf") : _destroyed ? new Color("35383d") : _paint;
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _audio.Stop();
        _audio.Stream = null;
        _impact.Dispose();
        _explosion.Dispose();
    }

    /// <summary>Receives the owned chassis material and its normal paint.</summary>
    /// <param name="material">Visual surface only.</param>
    /// <param name="paint">Undamaged body color.</param>
    internal void Initialize(StandardMaterial3D material, Color paint)
    {
        _material = material;
        _paint = paint;
    }

    /// <summary>Shows a confirmed effect and submits its procedural cue.</summary>
    /// <param name="explosion">Whether the effect calls for the longer blast sound.</param>
    /// <param name="destroyed">Current authoritative wreck state.</param>
    internal void Present(bool explosion, bool destroyed)
    {
        _destroyed = destroyed;
        _flashSeconds = 0.18f;
        _audio.Stream = explosion ? _explosion : _impact;
        _audio.Play();
        CueCount++;
    }

    /// <summary>Clears presentation on explicit respawn.</summary>
    internal void Reset()
    {
        _destroyed = false;
        _flashSeconds = 0;
        _audio.Stop();
    }

    private static AudioStreamWav Tone(bool explosion)
    {
        const int rate = 22050;
        int count = explosion ? 9922 : 3307;
        byte[] samples = new byte[count * 2];
        for (int index = 0; index < count; index++)
        {
            float time = (float)index / rate;
            float envelope = MathF.Sin(MathF.PI * index / count) * MathF.Exp(-(explosion ? 8 : 22) * time);
            float frequency = explosion ? 65 : 150;
            float wave = MathF.Sin(MathF.Tau * frequency * time) + (0.35f * MathF.Sin(MathF.Tau * 937 * time));
            BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(index * 2), (short)(wave * envelope * 18000));
        }

        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = samples };
    }
}
