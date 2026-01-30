using System;
using System.Collections.Generic;
using UnityEngine;
using Utils.SoftFloat;

namespace Design.Animation
{
    [Serializable]
    public enum HitboxKind
    {
        Hurtbox,
        Hitbox,
    }

    [Serializable]
    public struct BoxProps : IEquatable<BoxProps>
    {
        public HitboxKind Kind;

        public int Damage;
        public int HitstunTicks;
        public int BlockstunTicks;
        public bool StartsRhythmCombo;
        public SVector2 Knockback;

        // NOTE: ensure that any new fields added above are added to the equals implementation: otherwise they will not
        // be editable in the move builder
        public bool Equals(BoxProps other) =>
            Kind == other.Kind
            && HitstunTicks == other.HitstunTicks
            && Damage == other.Damage
            && BlockstunTicks == other.BlockstunTicks
            && Knockback == other.Knockback
            && StartsRhythmCombo == other.StartsRhythmCombo;

        public override bool Equals(object obj) => obj is BoxProps other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, HitstunTicks);

        public static bool operator ==(BoxProps a, BoxProps b) => a.Equals(b);

        public static bool operator !=(BoxProps a, BoxProps b) => !a.Equals(b);
    }

    [Serializable]
    public struct BoxData : IEquatable<BoxData>
    {
        public string Name;

        public SVector2 CenterLocal;
        public SVector2 SizeLocal;
        public BoxProps Props;

        public bool Equals(BoxData other)
        {
            return Name == other.Name
                && CenterLocal == other.CenterLocal
                && SizeLocal == other.SizeLocal
                && Props.Equals(other.Props);
        }

        public override bool Equals(object obj) => obj is BoxData other && Equals(other);

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, CenterLocal, SizeLocal, Props);
        }

        public static bool operator ==(BoxData left, BoxData right) => left.Equals(right);

        public static bool operator !=(BoxData left, BoxData right) => !left.Equals(right);
    }

    [Serializable]
    public class FrameData
    {
        public List<BoxData> Boxes = new List<BoxData>();
    }

    [CreateAssetMenu(menuName = "Hypermania/Character Animation Hitbox Data")]
    [Serializable]
    public class HitboxData : ScriptableObject
    {
        public AnimationClip Clip;
        public int TotalTicks => Frames.Count;
        public List<FrameData> Frames = new List<FrameData>();

        public void EnsureSize(int totalTicks)
        {
            if (totalTicks < 1)
            {
                throw new InvalidOperationException("total ticks must be >= 1");
            }
            while (Frames.Count < totalTicks)
                Frames.Add(new FrameData());
            while (Frames.Count > totalTicks)
                Frames.RemoveAt(Frames.Count - 1);
        }

        public FrameData GetFrame(int tick)
        {
            if (tick < 0 || tick >= TotalTicks)
            {
                return null;
            }
            return Frames[tick];
        }
    }
}
