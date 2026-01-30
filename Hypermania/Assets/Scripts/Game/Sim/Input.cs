using System;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Netcode.Rollback;

namespace Game.Sim
{
    public struct GameInput : IInput<GameInput>
    {
        public InputFlags Flags;

        public readonly bool Equals(GameInput other)
        {
            return Flags == other.Flags;
        }

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
        public int SerdeSize()
        {
            return sizeof(int);
        }

        public GameInput(InputFlags flags)
        {
            Flags = flags;
        }

        public static GameInput None => new GameInput(InputFlags.None);

        [MethodImplAttribute(MethodImplOptions.AggressiveInlining)]
        public bool HasInput(InputFlags input)
        {
            return (Flags & input) != 0;
        }
    }

    // Input is an enum that uses the Flags attribute, which means that it can use bitwise operations on initialization and when checking whether certain enum values are present in an Input.
    // For example, if the user presses left and up, we would set the Input = Input.Left | Input.Up, which sets the bits accordingly.
    // To check if the user has pressed down, we can use userInput.HasFlag(Input.Down).

    // Note: Bitwise operators work better than .HasFlag() here, we're running input every frame.
    // We also use the same enums when checking for input, so bitwise operators work well here.
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
        SpecialAttack = 1 << 7,
        SuperAttack = 1 << 8,
        Grab = 1 << 9,
        Mania1 = 1 << 10,
        Mania2 = 1 << 11,
        Mania3 = 1 << 12,
        Mania4 = 1 << 13,
        Mania5 = 1 << 14,
        Mania6 = 1 << 15,
    }
}
