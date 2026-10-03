using System.Buffers.Binary;

namespace Trackstorm.Core.Input;

/// <summary>One tick of device-independent input. Integer axes and explicit edges replay without device history.</summary>
public readonly record struct InputFrame
{
    /// <summary>Version byte, tick, six axes and three button masks, in little-endian order.</summary>
    public const int SerializedSize = 27;

    private const InputButtons ValidButtons = (InputButtons)32767;

    /// <summary>Creates a frame; a tap within one tick may set both pressed and released.</summary>
    /// <param name="tick">Caller-supplied simulation tick.</param>
    /// <param name="steering">Signed strength from -32767 to 32767.</param>
    /// <param name="accelerate">Throttle from 0 to 65535.</param>
    /// <param name="brake">Brake/reverse request from 0 to 65535.</param>
    /// <param name="held">Buttons held at capture.</param>
    /// <param name="pressed">Buttons pressed since the preceding capture.</param>
    /// <param name="released">Buttons released since the preceding capture.</param>
    /// <param name="airPitch">Signed aerial pitch.</param>
    /// <param name="airYaw">Signed aerial yaw.</param>
    /// <param name="airRoll">Signed aerial roll.</param>
    public InputFrame(ulong tick, short steering, ushort accelerate, ushort brake, InputButtons held, InputButtons pressed, InputButtons released, short airPitch = 0, short airYaw = 0, short airRoll = 0)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(steering, short.MinValue);
        ArgumentOutOfRangeException.ThrowIfEqual(airPitch, short.MinValue);
        ArgumentOutOfRangeException.ThrowIfEqual(airYaw, short.MinValue);
        ArgumentOutOfRangeException.ThrowIfEqual(airRoll, short.MinValue);
        ValidateButtons(held);
        ValidateButtons(pressed);
        ValidateButtons(released);
        Tick = tick;
        Steering = steering;
        Accelerate = accelerate;
        Brake = brake;
        Held = held;
        Pressed = pressed;
        Released = released;
        AirPitch = airPitch;
        AirYaw = airYaw;
        AirRoll = airRoll;
    }

    /// <summary>Caller-supplied tick; zero is valid for a neutral/default frame.</summary>
    public ulong Tick { get; }

    /// <summary>Signed steering in [-32767,32767].</summary>
    public short Steering { get; }

    /// <summary>Independent forward throttle in [0,65535].</summary>
    public ushort Accelerate { get; }

    /// <summary>Independent brake/reverse request in [0,65535].</summary>
    public ushort Brake { get; }

    /// <summary>Signed aerial pitch: positive is nose up.</summary>
    public short AirPitch { get; }

    /// <summary>Signed aerial yaw: positive is right.</summary>
    public short AirYaw { get; }

    /// <summary>Signed aerial roll: positive is right.</summary>
    public short AirRoll { get; }

    /// <summary>Aggregate held state at the tick boundary.</summary>
    public InputButtons Held { get; }

    /// <summary>At least one press occurred since the preceding tick.</summary>
    public InputButtons Pressed { get; }

    /// <summary>At least one release occurred since the preceding tick.</summary>
    public InputButtons Released { get; }

    /// <summary>Writes the exact version-two wire representation without CLR layout or floating-point dependence.</summary>
    /// <param name="destination">Exactly SerializedSize bytes.</param>
    public void Write(Span<byte> destination)
    {
        if (destination.Length != SerializedSize)
        {
            throw new ArgumentException("A frame requires exactly 27 bytes.", nameof(destination));
        }

        destination[0] = 2;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[1..], Tick);
        BinaryPrimitives.WriteInt16LittleEndian(destination[9..], Steering);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[11..], Accelerate);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[13..], Brake);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[15..], (ushort)Held);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[17..], (ushort)Pressed);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[19..], (ushort)Released);
        BinaryPrimitives.WriteInt16LittleEndian(destination[21..], AirPitch);
        BinaryPrimitives.WriteInt16LittleEndian(destination[23..], AirYaw);
        BinaryPrimitives.WriteInt16LittleEndian(destination[25..], AirRoll);
    }

    /// <summary>Reads a single version-two frame and rejects malformed values, versions and lengths.</summary>
    /// <param name="source">Exactly one encoded frame.</param>
    /// <returns>The validated logical frame.</returns>
    public static InputFrame Read(ReadOnlySpan<byte> source)
    {
        if (source.Length != SerializedSize || source[0] != 2)
        {
            throw new ArgumentException("Expected one version-two input frame.", nameof(source));
        }

        return new InputFrame(
            BinaryPrimitives.ReadUInt64LittleEndian(source[1..]),
            BinaryPrimitives.ReadInt16LittleEndian(source[9..]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[11..]),
            BinaryPrimitives.ReadUInt16LittleEndian(source[13..]),
            (InputButtons)BinaryPrimitives.ReadUInt16LittleEndian(source[15..]),
            (InputButtons)BinaryPrimitives.ReadUInt16LittleEndian(source[17..]),
            (InputButtons)BinaryPrimitives.ReadUInt16LittleEndian(source[19..]),
            BinaryPrimitives.ReadInt16LittleEndian(source[21..]),
            BinaryPrimitives.ReadInt16LittleEndian(source[23..]),
            BinaryPrimitives.ReadInt16LittleEndian(source[25..]));
    }

    private static void ValidateButtons(InputButtons buttons)
    {
        if ((buttons & ~ValidButtons) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(buttons));
        }
    }
}
