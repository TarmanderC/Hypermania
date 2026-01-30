using System.Collections.Generic;
using MemoryPack;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    [MemoryPackable]
    public partial struct ManiaNote
    {
        public int Id;
        public Frame Tick;
        public int Length;
    }

    [MemoryPackable]
    public partial struct ManiaNoteChannel
    {
        public Deque<ManiaNote> Notes;
    }

    public enum ManiaEventKind
    {
        Hit,
        Missed,
        End,
    }

    [MemoryPackable]
    public partial struct ManiaEvent
    {
        public ManiaEventKind Kind;

        public ManiaNote Note;

        /// <summary>
        /// If the note was hit, how many ticks away from the exact timing it was hit
        /// </summary>
        public int Offset;

        /// <summary>
        /// If the note was missed, if it was hit early or late
        /// </summary>
        public bool Early;

        public static ManiaEvent EndEvent()
        {
            return new ManiaEvent { Kind = ManiaEventKind.End };
        }

        public static ManiaEvent HitEvent(in ManiaNote note, int offset)
        {
            return new ManiaEvent
            {
                Note = note,
                Offset = offset,
                Kind = ManiaEventKind.Hit,
            };
        }

        public static ManiaEvent MissEvent(in ManiaNote note, bool early)
        {
            return new ManiaEvent
            {
                Note = note,
                Early = early,
                Kind = ManiaEventKind.Missed,
            };
        }
    }

    [MemoryPackable]
    public partial struct ManiaConfig
    {
        public int NumKeys;

        /// <summary>
        /// The half window of when the note is able to be hit. In other words, there are 2 * HitHalfRange + 1 ticks
        /// in which a note may be hit (and not missed)
        /// </summary>
        public int HitHalfRange;

        /// <summary>
        /// The additional window outside of the hit window in which a note can be actively missed. This is used to
        /// prevent notes from being hit wayyyy to early.
        /// </summary>
        public int MissHalfRange;

        public int MissTotalRange => HitHalfRange + MissHalfRange;
    }

    [MemoryPackable]
    public partial struct ManiaState
    {
        /// <summary>
        /// Used to initialized the deque with capacity, not necessarily a hard cap
        /// </summary>
        const int MAX_NOTES = 100;
        public int TotalNoteCount;
        public ManiaConfig Config;
        public ManiaNoteChannel[] Channels;
        public Frame EndFrame;
        private static readonly InputFlags[] _channelInput =
        {
            InputFlags.Mania1,
            InputFlags.Mania2,
            InputFlags.Mania3,
            InputFlags.Mania4,
            InputFlags.Mania5,
            InputFlags.Mania6,
        };

        public static ManiaState Create(in ManiaConfig config)
        {
            ManiaState sim = new ManiaState();
            sim.Config = config;
            sim.TotalNoteCount = 0;
            sim.Channels = new ManiaNoteChannel[config.NumKeys];
            for (int i = 0; i < config.NumKeys; i++)
            {
                sim.Channels[i] = new ManiaNoteChannel { Notes = new Deque<ManiaNote>(MAX_NOTES) };
            }
            sim.EndFrame = Frame.NullFrame;
            return sim;
        }

        public void Enable(Frame endFrame)
        {
            EndFrame = endFrame;
        }

        public void QueueNote(int channel, ManiaNote note)
        {
            note.Id = TotalNoteCount++;
            Channels[channel].Notes.PushBack(note);
        }

        public void Tick(Frame frame, GameInput input, List<ManiaEvent> outEvents)
        {
            if (frame > EndFrame)
                return;
            for (int i = 0; i < Channels.Length; i++)
            {
                if (Channels[i].Notes.Count == 0)
                {
                    continue;
                }
                ManiaNote note = Channels[i].Notes.Front();
                Frame noteTick = note.Tick;
                bool hasInput = input.HasInput(_channelInput[i]);

                if (hasInput && frame < noteTick - Config.MissTotalRange)
                {
                    // tried to hit note way too early
                }
                else if (hasInput && frame < noteTick - Config.HitHalfRange)
                {
                    // missed note early
                    outEvents.Add(ManiaEvent.MissEvent(note, true));
                    Channels[i].Notes.PopFront();
                }
                else if (hasInput && frame <= noteTick + Config.HitHalfRange)
                {
                    // hit note
                    int diff = Mathsf.Abs(frame - noteTick);
                    outEvents.Add(ManiaEvent.HitEvent(note, diff));
                    Channels[i].Notes.PopFront();
                }
                else if (hasInput && frame <= noteTick + Config.MissTotalRange)
                {
                    // missed note late
                    outEvents.Add(ManiaEvent.MissEvent(note, false));
                    Channels[i].Notes.PopFront();
                }
                else if (frame > noteTick + Config.MissTotalRange)
                {
                    // note was missed automatically (too late)
                    outEvents.Add(ManiaEvent.MissEvent(note, false));
                    Channels[i].Notes.PopFront();
                }
            }
            if (frame == EndFrame)
            {
                outEvents.Add(ManiaEvent.EndEvent());
                EndFrame = Frame.NullFrame;
            }
        }
    }
}
