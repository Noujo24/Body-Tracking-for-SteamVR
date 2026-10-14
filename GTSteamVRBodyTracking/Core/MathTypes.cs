using System;

namespace GTSteamVRBodyTracking.Core
{
    // Unity-independent math so every conversion can be unit-tested outside the game.
    // Conventions intentionally match UnityEngine.Vector3/Quaternion (left-handed, +Y up, +Z forward,
    // Hamilton product, Euler order Z then X then Y). Only fields are public so the types
    // serialize to JSON as plain {X,Y,Z(,W)} objects.

    public struct Vec3
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero => new Vec3(0f, 0f, 0f);
        public static Vec3 Up => new Vec3(0f, 1f, 0f);
        public static Vec3 Forward => new Vec3(0f, 0f, 1f);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);

        public float GetMagnitude() => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

        public Vec3 GetNormalized()
        {
            float m = GetMagnitude();
            return m > 1e-6f ? this * (1f / m) : Zero;
        }

        public bool IsFinite() => MathUtil.IsFinite(X) && MathUtil.IsFinite(Y) && MathUtil.IsFinite(Z);

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) => a + (b - a) * t;

        public static float Distance(Vec3 a, Vec3 b) => (a - b).GetMagnitude();

        public override string ToString() => $"({X:F3}, {Y:F3}, {Z:F3})";
    }

    public struct Quat
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public Quat(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public static Quat Identity => new Quat(0f, 0f, 0f, 1f);

        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y + a.Y * b.W + a.Z * b.X - a.X * b.Z,
            a.W * b.Z + a.Z * b.W + a.X * b.Y - a.Y * b.X,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>Rotates a vector (same result as UnityEngine.Quaternion * Vector3).</summary>
        public static Vec3 operator *(Quat q, Vec3 v)
        {
            var u = new Vec3(q.X, q.Y, q.Z);
            Vec3 t = 2f * Vec3.Cross(u, v);
            return v + q.W * t + Vec3.Cross(u, t);
        }

        public static float Dot(Quat a, Quat b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        public bool IsFinite() =>
            MathUtil.IsFinite(X) && MathUtil.IsFinite(Y) && MathUtil.IsFinite(Z) && MathUtil.IsFinite(W);

        public Quat GetNormalized()
        {
            float m = (float)Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            if (m < 1e-6f || !MathUtil.IsFinite(m)) return Identity;
            float inv = 1f / m;
            return new Quat(X * inv, Y * inv, Z * inv, W * inv);
        }

        public Quat GetInverse()
        {
            float n = X * X + Y * Y + Z * Z + W * W;
            if (n < 1e-9f) return Identity;
            float inv = 1f / n;
            return new Quat(-X * inv, -Y * inv, -Z * inv, W * inv);
        }

        public static Quat AngleAxis(float degrees, Vec3 axis)
        {
            Vec3 n = axis.GetNormalized();
            if (n.GetMagnitude() < 1e-6f) return Identity;
            double half = degrees * MathUtil.Deg2Rad * 0.5;
            float s = (float)Math.Sin(half);
            return new Quat(n.X * s, n.Y * s, n.Z * s, (float)Math.Cos(half));
        }

        /// <summary>Same convention as UnityEngine.Quaternion.Euler: rotate Z, then X, then Y.</summary>
        public static Quat Euler(float x, float y, float z) =>
            AngleAxis(y, Vec3.Up) * AngleAxis(x, new Vec3(1f, 0f, 0f)) * AngleAxis(z, Vec3.Forward);

        public static Quat Euler(Vec3 degrees) => Euler(degrees.X, degrees.Y, degrees.Z);

        /// <summary>Builds a quaternion from a row-major 3x3 rotation matrix (rows r0, r1, r2).</summary>
        public static Quat FromMatrix(
            float m00, float m01, float m02,
            float m10, float m11, float m12,
            float m20, float m21, float m22)
        {
            float trace = m00 + m11 + m22;
            Quat q;
            if (trace > 0f)
            {
                float s = (float)Math.Sqrt(trace + 1f) * 2f;
                q = new Quat((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25f * s);
            }
            else if (m00 > m11 && m00 > m22)
            {
                float s = (float)Math.Sqrt(1f + m00 - m11 - m22) * 2f;
                q = new Quat(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            else if (m11 > m22)
            {
                float s = (float)Math.Sqrt(1f + m11 - m00 - m22) * 2f;
                q = new Quat((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            else
            {
                float s = (float)Math.Sqrt(1f + m22 - m00 - m11) * 2f;
                q = new Quat((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
            }
            return q.GetNormalized();
        }

        public static Quat Slerp(Quat a, Quat b, float t)
        {
            t = MathUtil.Clamp01(t);
            float dot = Dot(a, b);
            if (dot < 0f)
            {
                b = new Quat(-b.X, -b.Y, -b.Z, -b.W);
                dot = -dot;
            }
            if (dot > 0.9995f)
            {
                return new Quat(
                    a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
                    a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t).GetNormalized();
            }
            double theta0 = Math.Acos(dot);
            double theta = theta0 * t;
            double sinTheta0 = Math.Sin(theta0);
            float s0 = (float)(Math.Cos(theta) - dot * Math.Sin(theta) / sinTheta0);
            float s1 = (float)(Math.Sin(theta) / sinTheta0);
            return new Quat(
                a.X * s0 + b.X * s1, a.Y * s0 + b.Y * s1,
                a.Z * s0 + b.Z * s1, a.W * s0 + b.W * s1).GetNormalized();
        }

        /// <summary>Angle in degrees between two orientations (0..180).</summary>
        public static float AngleBetween(Quat a, Quat b)
        {
            // 2·atan2(|v|, |w|) of the relative rotation is accurate for small angles (acos of a dot is not).
            Quat d = a.GetNormalized().GetInverse() * b.GetNormalized();
            double v = Math.Sqrt((double)d.X * d.X + (double)d.Y * d.Y + (double)d.Z * d.Z);
            return (float)(2.0 * Math.Atan2(v, Math.Abs((double)d.W)) * MathUtil.Rad2Deg);
        }

        /// <summary>Rotation angle of this quaternion in degrees (0..180).</summary>
        public float GetAngle() => AngleBetween(Identity, this);

        public Vec3 GetForward() => this * Vec3.Forward;

        /// <summary>Signed yaw (degrees) of the forward vector about +Y; +yaw turns +Z toward +X (Unity convention).</summary>
        public float GetYawDegrees()
        {
            Vec3 f = GetForward();
            if (f.X * f.X + f.Z * f.Z < 1e-6f)
            {
                // Looking straight up/down: derive heading from the up vector instead.
                Vec3 u = this * Vec3.Up;
                f = f.Y > 0f ? -u : u;
            }
            return (float)(Math.Atan2(f.X, f.Z) * MathUtil.Rad2Deg);
        }

        public Quat GetYawOnly() => AngleAxis(GetYawDegrees(), Vec3.Up);

        public override string ToString() => $"({X:F4}, {Y:F4}, {Z:F4}, {W:F4})";
    }

    public struct RigidPose
    {
        public Vec3 Position;
        public Quat Rotation;

        public RigidPose(Vec3 position, Quat rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public static RigidPose Identity => new RigidPose(Vec3.Zero, Quat.Identity);

        /// <summary>Composition: (a * b) applies b first, expressed in a's frame.</summary>
        public static RigidPose operator *(RigidPose a, RigidPose b) =>
            new RigidPose(a.Position + a.Rotation * b.Position, (a.Rotation * b.Rotation).GetNormalized());

        public RigidPose GetInverse()
        {
            Quat inv = Rotation.GetInverse();
            return new RigidPose(inv * -Position, inv);
        }

        public Vec3 TransformPoint(Vec3 local) => Position + Rotation * local;

        public bool IsFinite() => Position.IsFinite() && Rotation.IsFinite();

        public override string ToString() => $"pos {Position} rot {Rotation}";
    }

    /// <summary>Rotation + uniform scale + translation: world = T + R * (S * local).</summary>
    public struct SimilarityTransform
    {
        public Vec3 Translation;
        public Quat Rotation;
        public float Scale;

        public SimilarityTransform(Vec3 translation, Quat rotation, float scale)
        {
            Translation = translation;
            Rotation = rotation;
            Scale = scale;
        }

        public static SimilarityTransform Identity => new SimilarityTransform(Vec3.Zero, Quat.Identity, 1f);

        public Vec3 ApplyPoint(Vec3 p) => Translation + Rotation * (p * Scale);

        public RigidPose Apply(RigidPose pose) =>
            new RigidPose(ApplyPoint(pose.Position), (Rotation * pose.Rotation).GetNormalized());

        /// <summary>
        /// Builds the tracking-space to world mapping from one device seen in both spaces
        /// (e.g. the headset): world = cameraWorld * inverse(hmdTracking), with tracking scale.
        /// </summary>
        public static SimilarityTransform FromAnchor(RigidPose deviceInWorld, RigidPose deviceInTracking, float scale)
        {
            Quat r = (deviceInWorld.Rotation * deviceInTracking.Rotation.GetInverse()).GetNormalized();
            Vec3 t = deviceInWorld.Position - r * (deviceInTracking.Position * scale);
            return new SimilarityTransform(t, r, scale);
        }
    }

    public static class MathUtil
    {
        public const double Deg2Rad = Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / Math.PI;

        public static bool IsFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        public static float Clamp01(float v) => Clamp(v, 0f, 1f);

        /// <summary>Returns a finite value clamped into [min, max]; non-finite input becomes <paramref name="fallback"/>.</summary>
        public static float Sanitize(float v, float min, float max, float fallback) =>
            IsFinite(v) ? Clamp(v, min, max) : fallback;

        public static Vec3 Sanitize(Vec3 v, float min, float max) =>
            new Vec3(Sanitize(v.X, min, max, 0f), Sanitize(v.Y, min, max, 0f), Sanitize(v.Z, min, max, 0f));

        /// <summary>Wraps an angle into (-180, 180].</summary>
        public static float WrapDegrees(float deg)
        {
            if (!IsFinite(deg)) return 0f;
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            if (deg <= -180f) deg += 360f;
            return deg;
        }
    }
}
