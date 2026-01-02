using System;
using System.Buffers.Binary;
using Netcode.Rollback;

public struct Input : IInput<Input>
{
    public InputFlags Flags;
    public readonly bool Equals(Input other) { return Flags == other.Flags; }

    public int Deserialize(ReadOnlySpan<byte> inBytes)
    {
        Flags = (InputFlags)BinaryPrimitives.ReadInt32LittleEndian(inBytes);
        return sizeof(int);
    }
    public int Serialize(Span<byte> outBytes)
    {
        BinaryPrimitives.WriteInt32LittleEndian(outBytes, (int)Flags);
        return sizeof(int);
    }
    /// <summary>
    /// Input's serialization size is assumed to be constant regardless of the input's value in the networking code.
    /// </summary>
    /// <returns></returns>
    public int SerdeSize() { return sizeof(int); }

    public Input(InputFlags flags) { Flags = flags; }
}

// Input is an enum that uses the Flags attribute, which means that it can use bitwise operations on initialization and when checking whether certain enum values are present in an Input.
// For example, if the user presses left and up, we would set the Input = Input.Left | Input.Up, which sets the bits accordingly.
// To check if the user has pressed down, we can use userInput.HasFlag(Input.Down).
[Flags]
public enum InputFlags : int
{
    None = 0,
    Up = 1 << 1,
    Down = 1 << 2,
    Left = 1 << 3,
    Right = 1 << 4,
    LightAttack = 1 << 5,
    MediumAttack = 1 << 6,
    HeavyAttack = 1 << 7,
    Grab = 1 << 8
}