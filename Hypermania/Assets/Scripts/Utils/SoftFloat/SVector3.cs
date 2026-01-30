using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Utils.SoftFloat
{
    [Serializable]
    public struct SVector3 : IEquatable<SVector3>, IFormattable
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

        //
        // Summary:
        //     Z component of the vector.
        public sfloat z;

        private static readonly SVector3 zeroVector = new SVector3(sfloat.Zero, sfloat.Zero, sfloat.Zero);

        private static readonly SVector3 oneVector = new SVector3(sfloat.One, sfloat.One, sfloat.One);

        private static readonly SVector3 upVector = new SVector3(sfloat.Zero, sfloat.One, sfloat.Zero);

        private static readonly SVector3 downVector = new SVector3(sfloat.Zero, sfloat.MinusOne, sfloat.Zero);

        private static readonly SVector3 leftVector = new SVector3(sfloat.MinusOne, sfloat.Zero, sfloat.Zero);

        private static readonly SVector3 rightVector = new SVector3(sfloat.One, sfloat.Zero, sfloat.Zero);

        private static readonly SVector3 forwardVector = new SVector3(sfloat.Zero, sfloat.Zero, sfloat.One);

        private static readonly SVector3 backVector = new SVector3(sfloat.Zero, sfloat.Zero, sfloat.MinusOne);

        private static readonly SVector3 positiveInfinityVector = new SVector3(
            sfloat.PositiveInfinity,
            sfloat.PositiveInfinity,
            sfloat.PositiveInfinity
        );

        private static readonly SVector3 negativeInfinityVector = new SVector3(
            sfloat.NegativeInfinity,
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
                    2 => z,
                    _ => throw new IndexOutOfRangeException("Invalid SVector3 index!"),
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
                    case 2:
                        z = value;
                        break;
                    default:
                        throw new IndexOutOfRangeException("Invalid SVector3 index!");
                }
            }
        }

        //
        // Summary:
        //     The unit vector in the direction of the current vector.
        public SVector3 normalized
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Normalize(this); }
        }

        //
        // Summary:
        //     Returns the length of this vector (Read Only).
        public sfloat magnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return Mathsf.Sqrt(x * x + y * y + z * z); }
        }

        //
        // Summary:
        //     Returns the squared length of this vector (Read Only).
        public sfloat sqrMagnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return x * x + y * y + z * z; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector3(0, 0, 0).
        public static SVector3 zero
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return zeroVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector3(1, 1, 1).
        public static SVector3 one
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return oneVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector3(0, 0, 1).
        public static SVector3 forward
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return forwardVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector3(0, 0, -1).
        public static SVector3 back
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return backVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector3(0, 1, 0).
        public static SVector3 up
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return upVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector3(0, -1, 0).
        public static SVector3 down
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return downVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector3(-1, 0, 0).
        public static SVector3 left
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return leftVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector3(1, 0, 0).
        public static SVector3 right
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return rightVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector3(sfloat.PositiveInfinity, sfloat.PositiveInfinity,
        //     sfloat.PositiveInfinity).
        public static SVector3 positiveInfinity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return positiveInfinityVector; }
        }

        //
        // Summary:
        //     Shorthand for writing SVector3(sfloat.NegativeInfinity, sfloat.NegativeInfinity,
        //     sfloat.NegativeInfinity).
        public static SVector3 negativeInfinity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return negativeInfinityVector; }
        }

        //
        // Summary:
        //     Interpolates linearly between two points.
        //
        // Parameters:
        //   a:
        //     Start value. This value is returned when t = 0.
        //
        //   b:
        //     End value. This value is returned when t = 1.
        //
        //   t:
        //     Value used to interpolate between a and b. Values greater than one are clamped
        //     to 1. Values less than zero are clamped to 0.
        //
        // Returns:
        //     Interpolated value. This value always lies on a line between points a and b.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 Lerp(SVector3 a, SVector3 b, sfloat t)
        {
            t = Mathsf.Clamp01(t);
            return new SVector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        //
        // Summary:
        //     Interpolates linearly between two vectors, allowing extrapolation beyond the
        //     end points.
        //
        // Parameters:
        //   a:
        //     Start value. This value is returned when t = 0.
        //
        //   b:
        //     End value. This value is returned when t = 1.
        //
        //   t:
        //     Value used to interpolate between or beyond a and b.
        //
        // Returns:
        //     Interpolated value. This value always lies on a line that passes through points
        //     a and b.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 LerpUnclamped(SVector3 a, SVector3 b, sfloat t)
        {
            return new SVector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }

        //
        // Summary:
        //     Moves vector incrementally towards a target point.
        //
        // Parameters:
        //   current:
        //     The position to move from.
        //
        //   target:
        //     The position to move towards.
        //
        //   maxDistanceDelta:
        //     Distance to move current per call.
        //
        // Returns:
        //     The new position.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 MoveTowards(SVector3 current, SVector3 target, sfloat maxDistanceDelta)
        {
            sfloat num = target.x - current.x;
            sfloat num2 = target.y - current.y;
            sfloat num3 = target.z - current.z;
            sfloat num4 = num * num + num2 * num2 + num3 * num3;
            if (num4 == sfloat.Zero || (maxDistanceDelta >= sfloat.Zero && num4 <= maxDistanceDelta * maxDistanceDelta))
            {
                return target;
            }

            sfloat num5 = Mathsf.Sqrt(num4);
            return new SVector3(
                current.x + num / num5 * maxDistanceDelta,
                current.y + num2 / num5 * maxDistanceDelta,
                current.z + num3 / num5 * maxDistanceDelta
            );
        }

        //
        // Summary:
        //     Creates a new vector with given x, y, z components.
        //
        // Parameters:
        //   x:
        //
        //   y:
        //
        //   z:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SVector3(sfloat x, sfloat y, sfloat z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        //
        // Summary:
        //     Creates a new vector with given x, y components and sets z to zero.
        //
        // Parameters:
        //   x:
        //
        //   y:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SVector3(sfloat x, sfloat y)
        {
            this.x = x;
            this.y = y;
            z = sfloat.Zero;
        }

        //
        // Summary:
        //     Set x, y and z components of an existing SVector3.
        //
        // Parameters:
        //   newX:
        //
        //   newY:
        //
        //   newZ:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(sfloat newX, sfloat newY, sfloat newZ)
        {
            x = newX;
            y = newY;
            z = newZ;
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
        public static SVector3 Scale(SVector3 a, SVector3 b)
        {
            return new SVector3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        //
        // Summary:
        //     Multiplies every component of this vector by the same component of scale.
        //
        // Parameters:
        //   scale:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Scale(SVector3 scale)
        {
            x *= scale.x;
            y *= scale.y;
            z *= scale.z;
        }

        //
        // Summary:
        //     Cross Product of two vectors.
        //
        // Parameters:
        //   lhs:
        //
        //   rhs:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 Cross(SVector3 lhs, SVector3 rhs)
        {
            return new SVector3(
                lhs.y * rhs.z - lhs.z * rhs.y,
                lhs.z * rhs.x - lhs.x * rhs.z,
                lhs.x * rhs.y - lhs.y * rhs.x
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
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
            if (other is SVector3 other2)
            {
                return Equals(other2);
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(SVector3 other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        //
        // Summary:
        //     Reflects a vector off the plane defined by a normal vector.
        //
        // Parameters:
        //   inDirection:
        //     The vector to be reflected in the plane.
        //
        //   inNormal:
        //     The normal vector that defines the plane of reflection.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 Reflect(SVector3 inDirection, SVector3 inNormal)
        {
            sfloat num = (sfloat)(-2f) * Dot(inNormal, inDirection);
            return new SVector3(
                num * inNormal.x + inDirection.x,
                num * inNormal.y + inDirection.y,
                num * inNormal.z + inDirection.z
            );
        }

        //
        // Summary:
        //     Obtains the normalized version of an input vector.
        //
        // Parameters:
        //   value:
        //     The vector to be normalized.
        //
        // Returns:
        //     A new vector with the same direction as the original vector but with a magnitude
        //     of 1.0.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 Normalize(SVector3 value)
        {
            sfloat num = Magnitude(value);
            if (num > Epsilon)
            {
                return value / num;
            }

            return zero;
        }

        //
        // Summary:
        //     Normalizes the magnitude of the current vector to 1 while maintaining the direction.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Normalize()
        {
            sfloat num = Magnitude(this);
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
        //     Dot Product of two vectors.
        //
        // Parameters:
        //   lhs:
        //
        //   rhs:
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sfloat Dot(SVector3 lhs, SVector3 rhs)
        {
            return lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z;
        }

        //
        // Summary:
        //     Projects a vector onto another vector.
        //
        // Parameters:
        //   vector:
        //     The vector to project.
        //
        //   onNormal:
        //     The vector to project onto. This vector doesn't need to be normalized.
        //
        // Returns:
        //     The vector that results from the projection of vector. This vector points in
        //     the same direction as onNormal.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 Project(SVector3 vector, SVector3 onNormal)
        {
            sfloat num = Dot(onNormal, onNormal);
            if (num < Epsilon)
            {
                return zero;
            }

            sfloat num2 = Dot(vector, onNormal);
            return new SVector3(onNormal.x * num2 / num, onNormal.y * num2 / num, onNormal.z * num2 / num);
        }

        //
        // Summary:
        //     Projects a vector onto a plane.
        //
        // Parameters:
        //   vector:
        //     The vector to project on the plane.
        //
        //   planeNormal:
        //     The normal which defines the plane to project on.
        //
        // Returns:
        //     The vector that results from projection of vector on the plane.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 ProjectOnPlane(SVector3 vector, SVector3 planeNormal)
        {
            sfloat num = Dot(planeNormal, planeNormal);
            if (num < Epsilon)
            {
                return vector;
            }

            sfloat num2 = Dot(vector, planeNormal);
            return new SVector3(
                vector.x - planeNormal.x * num2 / num,
                vector.y - planeNormal.y * num2 / num,
                vector.z - planeNormal.z * num2 / num
            );
        }

        //
        // Summary:
        //     Calculates the angle between two vectors.
        //
        // Parameters:
        //   from:
        //     The vector from which the angular difference is measured.
        //
        //   to:
        //     The vector to which the angular difference is measured.
        //
        // Returns:
        //     The angle in degrees between the two vectors.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sfloat Angle(SVector3 from, SVector3 to)
        {
            sfloat num = Mathsf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (num < EpsilonNormalSqrt)
            {
                return sfloat.Zero;
            }

            sfloat num2 = Mathsf.Clamp(Dot(from, to) / num, sfloat.MinusOne, sfloat.One);
            return Mathsf.Acos(num2) * (sfloat)57.29578f;
        }

        //
        // Summary:
        //     Calculates the signed angle between two vectors, using a third vector to determine
        //     the sign.
        //
        // Parameters:
        //   from:
        //     The vector from which the angular difference is measured.
        //
        //   to:
        //     The vector to which the angular difference is measured.
        //
        //   axis:
        //     The contextual direction for the calculation.
        //
        // Returns:
        //     Returns the signed angle between from and to in degrees.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sfloat SignedAngle(SVector3 from, SVector3 to, SVector3 axis)
        {
            sfloat num = Angle(from, to);
            sfloat num2 = from.y * to.z - from.z * to.y;
            sfloat num3 = from.z * to.x - from.x * to.z;
            sfloat num4 = from.x * to.y - from.y * to.x;
            sfloat num5 = Mathsf.Sign(axis.x * num2 + axis.y * num3 + axis.z * num4);
            return num * num5;
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
        public static sfloat Distance(SVector3 a, SVector3 b)
        {
            sfloat num = a.x - b.x;
            sfloat num2 = a.y - b.y;
            sfloat num3 = a.z - b.z;
            return Mathsf.Sqrt(num * num + num2 * num2 + num3 * num3);
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
        public static SVector3 ClampMagnitude(SVector3 vector, sfloat maxLength)
        {
            sfloat num = vector.sqrMagnitude;
            if (num > maxLength * maxLength)
            {
                sfloat num2 = Mathsf.Sqrt(num);
                sfloat num3 = vector.x / num2;
                sfloat num4 = vector.y / num2;
                sfloat num5 = vector.z / num2;
                return new SVector3(num3 * maxLength, num4 * maxLength, num5 * maxLength);
            }

            return vector;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sfloat Magnitude(SVector3 vector)
        {
            return Mathsf.Sqrt(vector.x * vector.x + vector.y * vector.y + vector.z * vector.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static sfloat SqrMagnitude(SVector3 vector)
        {
            return vector.x * vector.x + vector.y * vector.y + vector.z * vector.z;
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
        public static SVector3 Min(SVector3 lhs, SVector3 rhs)
        {
            return new SVector3(Mathsf.Min(lhs.x, rhs.x), Mathsf.Min(lhs.y, rhs.y), Mathsf.Min(lhs.z, rhs.z));
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
        public static SVector3 Max(SVector3 lhs, SVector3 rhs)
        {
            return new SVector3(Mathsf.Max(lhs.x, rhs.x), Mathsf.Max(lhs.y, rhs.y), Mathsf.Max(lhs.z, rhs.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 operator +(SVector3 a, SVector3 b)
        {
            return new SVector3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 operator -(SVector3 a, SVector3 b)
        {
            return new SVector3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 operator -(SVector3 a)
        {
            return new SVector3(sfloat.Zero - a.x, sfloat.Zero - a.y, sfloat.Zero - a.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 operator *(SVector3 a, sfloat d)
        {
            return new SVector3(a.x * d, a.y * d, a.z * d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 operator *(sfloat d, SVector3 a)
        {
            return new SVector3(a.x * d, a.y * d, a.z * d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SVector3 operator /(SVector3 a, sfloat d)
        {
            return new SVector3(a.x / d, a.y / d, a.z / d);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(SVector3 lhs, SVector3 rhs)
        {
            sfloat num = lhs.x - rhs.x;
            sfloat num2 = lhs.y - rhs.y;
            sfloat num3 = lhs.z - rhs.z;
            sfloat num4 = num * num + num2 * num2 + num3 * num3;
            return num4 < (sfloat)9.9999994E-11f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(SVector3 lhs, SVector3 rhs)
        {
            return !(lhs == rhs);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Vector3(SVector3 v)
        {
            return new Vector3((float)v.x, (float)v.y, (float)v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator SVector3(Vector3 v)
        {
            return new SVector3((sfloat)v.x, (sfloat)v.y, (sfloat)v.z);
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

            return $"({x.ToString(format, formatProvider)}, {y.ToString(format, formatProvider)}, {z.ToString(format, formatProvider)})";
        }
    }
}
