using System;
using MemoryPack;

namespace Game.Sim
{
    [MemoryPackable]
    public partial class InputHistory
    {
        public struct InputHistoryEntry
        {
            public bool Pressed;
            public InputFlags Input;
        }

        [MemoryPackInclude]
        private GameInput[] _buffer;

        [MemoryPackInclude]
        private int _next;

        [MemoryPackInclude]
        private int _count;

        // The structure of this input history follows a circular array / buffer, for constant access times to previos frames.
        // We only add on the last frame at the end, for constant O(1) time.
        public InputHistory()
        {
            _buffer = new GameInput[64];
            _next = 0;
            _count = 0;
        }

        public void Clear()
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _next = 0;
            _count = 0;
        }

        public void PushInput(GameInput input)
        {
            _buffer[_next] = input;
            _next = (_next + 1) % _buffer.Length;
            if (_count < _buffer.Length)
            {
                _count = _count + 1;
            }
        }

        public GameInput GetInput(int framesAgo)
        {
            if (framesAgo < 0 || framesAgo >= _count)
            {
                return new GameInput(InputFlags.None);
            }
            int idx = (_next - 1 - framesAgo + _buffer.Length) % _buffer.Length;
            return _buffer[idx];
        }

        public bool IsHeld(InputFlags flag)
        {
            return GetInput(0).HasInput(flag);
        }

        // Checks if the button was pressed within the last couple of frames.
        public bool PressedRecently(InputFlags flag, int withinFrames, int beforeFrames = 0)
        {
            return HasInputSeqeunce(
                stackalloc InputHistoryEntry[] {
                    new InputHistoryEntry { Pressed = false, Input = flag },
                    new InputHistoryEntry { Pressed = true, Input = flag },
                },
                withinFrames,
                beforeFrames
            );
        }

        public bool PressedAndReleasedRecently(InputFlags flag, int withinFrames, int beforeFrames = 0)
        {
            return HasInputSeqeunce(
                stackalloc InputHistoryEntry[] {
                    new InputHistoryEntry { Pressed = false, Input = flag },
                    new InputHistoryEntry { Pressed = true, Input = flag },
                    new InputHistoryEntry { Pressed = false, Input = flag },
                },
                withinFrames,
                beforeFrames
            );
        }

        public bool HasInputSeqeunce(ReadOnlySpan<InputHistoryEntry> sequence, int withinFrames, int beforeFrames = 0)
        {
            if (withinFrames < 0 || withinFrames >= _count)
            {
                return false;
            }
            int seqPtr = 0;
            for (int i = withinFrames - 1; i >= beforeFrames && seqPtr < sequence.Length; i--)
            {
                if (GetInput(i).HasInput(sequence[seqPtr].Input) == sequence[seqPtr].Pressed)
                {
                    seqPtr++;
                }
            }
            return seqPtr == sequence.Length;
        }

        public bool ReleasedRecently(InputFlags flag, int withinFrames, int beforeFrames = 0)
        {
            return HasInputSeqeunce(
                stackalloc InputHistoryEntry[] {
                    new InputHistoryEntry { Pressed = true, Input = flag },
                    new InputHistoryEntry { Pressed = false, Input = flag },
                },
                withinFrames,
                beforeFrames
            );
        }
    }
}
