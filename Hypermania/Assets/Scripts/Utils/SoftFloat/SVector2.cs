using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Utils.SoftFloat
{
    [Serializable]
    public struct SVector2 : IEquatable<SVector2>, IFormattable
    {
        public static readonly sfloat Epsilon = (sfloat)1E-5f;
        public static readonly sfloat EpsilonNormalSqrt = (sfloat)1E-15f;

        //
        // Summary:
        //     X component of the vector.
        public sfloat x;

        //
        // Summary:
        //     Y component of the vector.
        public sfloat y;

        private static readonly SVector2 zeroVector = new SVector2(sfloat.Zero, sfloat.Zero);

        private static readonly SVector2 oneVector = new SVector2(sfloat.One, sfloat.One);

        private static readonly SVector2 upVector = new SVector2(sfloat.Zero, sfloat.One);

        private static readonly SVector2 downVector = new SVector2(sfloat.Zero, sfloat.MinusOne);

        private static readonly SVector2 leftVector = new SVector2(sfloat.MinusOne, sfloat.Zero);

        private static readonly SVector2 rightVector = new SVector2(sfloat.One, sfloat.Zero);

        private static readonly SVector2 positiveInfinityVector = new SVector2(
            sfloat.PositiveInfinity,
            sfloat.PositiveInfinity
        );

        private static readonly SVector2 negativeInfinityVector = new SVector2(
            sfloat.NegativeInfinity,
            sfloat.NegativeInfinity
        );

        public sfloat this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return index switch
                {
                    0 => x,
                    1 => y,
                    _ => throw new IndexOutOfRangeException("Invalid SVector2 index!"),
                };
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set
            {
                switch (index)
                {
                    case 0:
                        x = value;
                        break;
                    case 1:
                        y = value;
                        break;
                    default:
                        throw new IndexOutOfRangeException("Invalid SVector2 index!");
                }
            }
        }

        //
        // Summary:
        //     Returns a normalized vector based on the current vector. The normalized vector
        //     has a magnitude of 1 and is in the same direction as the current vector. Returns
        //     a zero vector If the current vector is too small to be normalized.
        public SVector2 normalized
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                SVector2 result = new SVector2(x, y);
                result.Normalize();
                return result;
            }
        }

        //
        // Summary:
        //     Returns the length of this vector (Read Only).
        public sfloat magnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Mathsf.Sqrt(x * x + y * y); }
        }

        //
        // Summary:
        //     Returns the squared length of this vector (Read Only).
        public sfloat sqrMagnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return x * x + y * y; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector2(0, 0).
        public static SVector2 zero
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return zeroVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector2(1, 1).
        public static SVector2 one
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return oneVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector2(0, 1).
        public static SVector2 up
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return upVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector2(0, -1).
        public static SVector2 down
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return downVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector2(-1, 0).
        public static SVector2 left
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return leftVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector2(1, 0).
        public static SVector2 right
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return rightVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector2(sfloat.PositiveInfinity, sfloat.PositiveInfinity).
        public static SVector2 positiveInfinity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return positiveInfinityVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector2(sfloat.NegativeInfinity, sfloat.NegativeInfinity).
        public static SVector2 negativeInfinity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return negativeInfinityVector; }
        }

        //
        // Summary:
        //     Constructs a new vector with given x, y components.
        //
        // Parameters:
        //   x:
        //
        //   y:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SVector2(sfloat x, sfloat y)
        {
            this.x = x;
            this.y = y;
        }

        //
        // Summary:
        //     Set x and y components of an existing SVector2.
        //
        // Parameters:
        //   newX:
        //
        //   newY:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(sfloat newX, sfloat newY)
        {
            x = newX;
            y = newY;
        }

        //
        // Summary:
        //     Linearly interpolates between vectors a and b by t.
        //
        // Parameters:
        //   a:
        //
        //   b:
        //
        //   t:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 Lerp(SVector2 a, SVector2 b, sfloat t)
        {
            t = Mathsf.Clamp01(t);
            return new SVector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        }

        //
        // Summary:
        //     Linearly interpolates between vectors a and b by t.
        //
        // Parameters:
        //   a:
        //
        //   b:
        //
        //   t:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 LerpUnclamped(SVector2 a, SVector2 b, sfloat t)
        {
            return new SVector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        }

        //
        // Summary:
        //     Moves a point current towards target.
        //
        // Parameters:
        //   current:
        //
        //   target:
        //
        //   maxDistanceDelta:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 MoveTowards(SVector2 current, SVector2 target, sfloat maxDistanceDelta)
        {
            sfloat num = target.x - current.x;
            sfloat num2 = target.y - current.y;
            sfloat num3 = num * num + num2 * num2;
            if (num3 == sfloat.Zero || (maxDistanceDelta >= sfloat.Zero && num3 <= maxDistanceDelta * maxDistanceDelta))
            {
                return target;
            }

            sfloat num4 = Mathsf.Sqrt(num3);
            return new SVector2(current.x + num / num4 * maxDistanceDelta, current.y + num2 / num4 * maxDistanceDelta);
        }

        //
        // Summary:
        //     Multiplies two vectors component-wise.
        //
        // Parameters:
        //   a:
        //
        //   b:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 Scale(SVector2 a, SVector2 b)
        {
            return new SVector2(a.x * b.x, a.y * b.y);
        }

        //
        // Summary:
        //     Multiplies every component of this vector by the same component of scale.
        //
        // Parameters:
        //   scale:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Scale(SVector2 scale)
        {
            x *= scale.x;
            y *= scale.y;
        }

        //
        // Summary:
        //     Makes this vector have a magnitude of 1.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Normalize()
        {
            sfloat num = magnitude;
            if (num > Epsilon)
            {
                this /= num;
            }
            else
            {
                this = zero;
            }
        }

        //
        // Summary:
        //     Returns a formatted string for this vector.
        //
        // Parameters:
        //   format:
        //     A numeric format string.
        //
        //   formatProvider:
        //     An object that specifies culture-specific formatting.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString()
        {
            return ToString(null, null);
        }

        //
        // Summary:
        //     Returns a formatted string for this vector.
        //
        // Parameters:
        //   format:
        //     A numeric format string.
        //
        //   formatProvider:
        //     An object that specifies culture-specific formatting.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string format)
        {
            return ToString(format, null);
        }

        //
        // Summary:
        //     Returns a formatted string for this vector.
        //
        // Parameters:
        //   format:
        //     A numeric format string.
        //
        //   formatProvider:
        //     An object that specifies culture-specific formatting.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format))
            {
                format = "F2";
            }

            if (formatProvider == null)
            {
                formatProvider = CultureInfo.InvariantCulture.NumberFormat;
            }

            return $"({x.ToString(format, formatProvider)}, {y.ToString(format, formatProvider)})";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return x.GetHashCode() ^ (y.GetHashCode() << 2);
        }

        //
        // Summary:
        //     Returns true if the given vector is exactly equal to this vector.
        //
        // Parameters:
        //   other:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object other)
        {
            if (other is SVector2 other2)
            {
                return Equals(other2);
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(SVector2 other)
        {
            return x == other.x && y == other.y;
        }

        //
        // Summary:
        //     Reflects a vector off the surface defined by a normal.
        //
        // Parameters:
        //   inDirection:
        //     The direction vector towards the surface.
        //
        //   inNormal:
        //     The normal vector that defines the surface.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 Reflect(SVector2 inDirection, SVector2 inNormal)
        {
            sfloat num = (sfloat)(-2f) * Dot(inNormal, inDirection);
            return new SVector2(num * inNormal.x + inDirection.x, num * inNormal.y + inDirection.y);
        }

        //
        // Summary:
        //     Returns the 2D vector perpendicular to this 2D vector. The result is always rotated
        //     90-degrees in a counter-clockwise direction for a 2D coordinate system where
        //     the positive Y axis goes up.
        //
        // Parameters:
        //   inDirection:
        //     The input direction.
        //
        // Returns:
        //     The perpendicular direction.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 Perpendicular(SVector2 inDirection)
        {
            return new SVector2(sfloat.Zero - inDirection.y, inDirection.x);
        }

        //
        // Summary:
        //     Dot Product of two vectors.
        //
        // Parameters:
        //   lhs:
        //
        //   rhs:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sfloat Dot(SVector2 lhs, SVector2 rhs)
        {
            return lhs.x * rhs.x + lhs.y * rhs.y;
        }

        //
        // Summary:
        //     Gets the unsigned angle in degrees between from and to.
        //
        // Parameters:
        //   from:
        //     The vector from which the angular difference is measured.
        //
        //   to:
        //     The vector to which the angular difference is measured.
        //
        // Returns:
        //     The unsigned angle in degrees between the two vectors.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sfloat Angle(SVector2 from, SVector2 to)
        {
            sfloat num = Mathsf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (num < Epsilon)
            {
                return sfloat.Zero;
            }

            sfloat num2 = Mathsf.Clamp(Dot(from, to) / num, sfloat.MinusOne, sfloat.One);
            return Mathsf.Acos(num2) * (sfloat)57.29578f;
        }

        //
        // Summary:
        //     Gets the signed angle in degrees between from and to.
        //
        // Parameters:
        //   from:
        //     The vector from which the angular difference is measured.
        //
        //   to:
        //     The vector to which the angular difference is measured.
        //
        // Returns:
        //     The signed angle in degrees between the two vectors.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sfloat SignedAngle(SVector2 from, SVector2 to)
        {
            sfloat num = Angle(from, to);
            sfloat num2 = Mathsf.Sign(from.x * to.y - from.y * to.x);
            return num * num2;
        }

        //
        // Summary:
        //     Returns the distance between a and b.
        //
        // Parameters:
        //   a:
        //
        //   b:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sfloat Distance(SVector2 a, SVector2 b)
        {
            sfloat num = a.x - b.x;
            sfloat num2 = a.y - b.y;
            return Mathsf.Sqrt(num * num + num2 * num2);
        }

        //
        // Summary:
        //     Returns a copy of vector with its magnitude clamped to maxLength.
        //
        // Parameters:
        //   vector:
        //
        //   maxLength:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 ClampMagnitude(SVector2 vector, sfloat maxLength)
        {
            sfloat num = vector.sqrMagnitude;
            if (num > maxLength * maxLength)
            {
                sfloat num2 = Mathsf.Sqrt(num);
                sfloat num3 = vector.x / num2;
                sfloat num4 = vector.y / num2;
                return new SVector2(num3 * maxLength, num4 * maxLength);
            }

            return vector;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sfloat SqrMagnitude(SVector2 a)
        {
            return a.x * a.x + a.y * a.y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public sfloat SqrMagnitude()
        {
            return x * x + y * y;
        }

        //
        // Summary:
        //     Returns a vector that is made from the smallest components of two vectors.
        //
        // Parameters:
        //   lhs:
        //
        //   rhs:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 Min(SVector2 lhs, SVector2 rhs)
        {
            return new SVector2(Mathsf.Min(lhs.x, rhs.x), Mathsf.Min(lhs.y, rhs.y));
        }

        //
        // Summary:
        //     Returns a vector that is made from the largest components of two vectors.
        //
        // Parameters:
        //   lhs:
        //
        //   rhs:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 Max(SVector2 lhs, SVector2 rhs)
        {
            return new SVector2(Mathsf.Max(lhs.x, rhs.x), Mathsf.Max(lhs.y, rhs.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 operator +(SVector2 a, SVector2 b)
        {
            return new SVector2(a.x + b.x, a.y + b.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 operator -(SVector2 a, SVector2 b)
        {
            return new SVector2(a.x - b.x, a.y - b.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 operator *(SVector2 a, SVector2 b)
        {
            return new SVector2(a.x * b.x, a.y * b.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 operator /(SVector2 a, SVector2 b)
        {
            return new SVector2(a.x / b.x, a.y / b.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 operator -(SVector2 a)
        {
            return new SVector2(sfloat.Zero - a.x, sfloat.Zero - a.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 operator *(SVector2 a, sfloat d)
        {
            return new SVector2(a.x * d, a.y * d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 operator *(sfloat d, SVector2 a)
        {
            return new SVector2(a.x * d, a.y * d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector2 operator /(SVector2 a, sfloat d)
        {
            return new SVector2(a.x / d, a.y / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(SVector2 lhs, SVector2 rhs)
        {
            sfloat num = lhs.x - rhs.x;
            sfloat num2 = lhs.y - rhs.y;
            return num * num + num2 * num2 < (sfloat)9.9999994E-11f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(SVector2 lhs, SVector2 rhs)
        {
            return !(lhs == rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator SVector2(Vector2 v)
        {
            return new SVector2((sfloat)v.x, (sfloat)v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector2(SVector2 v)
        {
            return new Vector2((float)v.x, (float)v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator SVector2(SVector3 v)
        {
            return new SVector2(v.x, v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator SVector3(SVector2 v)
        {
            return new SVector3(v.x, v.y, sfloat.Zero);
        }
    }
}
