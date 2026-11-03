using System;

namespace VoiceHorror.Art
{
    /// <summary>Minimal vector type so character generation runs (and is tested) outside Unity.</summary>
    public readonly struct V3
    {
        public readonly float x, y, z;
        public V3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public static readonly V3 Zero = new V3(0, 0, 0);
        public static readonly V3 One = new V3(1, 1, 1);
        public static readonly V3 Up = new V3(0, 1, 0);
        public static readonly V3 Down = new V3(0, -1, 0);
        public static readonly V3 Forward = new V3(0, 0, 1);
        public static readonly V3 Right = new V3(1, 0, 0);

        public static V3 operator +(V3 a, V3 b) => new V3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static V3 operator -(V3 a) => new V3(-a.x, -a.y, -a.z);
        public static V3 operator *(V3 a, float s) => new V3(a.x * s, a.y * s, a.z * s);
        public static V3 operator *(float s, V3 a) => new V3(a.x * s, a.y * s, a.z * s);
        public static V3 operator /(V3 a, float s) => new V3(a.x / s, a.y / s, a.z / s);

        public static V3 Scale(V3 a, V3 b) => new V3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static float Dot(V3 a, V3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static V3 Cross(V3 a, V3 b) => new V3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static V3 Lerp(V3 a, V3 b, float t) => a + (b - a) * t;

        public float Length => (float)Math.Sqrt(x * x + y * y + z * z);
        public V3 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-8f ? this / l : Zero;
            }
        }

        public bool IsFinite => !(float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z));

        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
    }

    /// <summary>Quaternion with Unity's conventions (left-handed, Euler order Z then X then Y).</summary>
    public readonly struct Quat
    {
        public readonly float x, y, z, w;
        public Quat(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }

        public static readonly Quat Identity = new Quat(0, 0, 0, 1);

        public static Quat AxisAngle(V3 axis, float degrees)
        {
            axis = axis.Normalized;
            double h = degrees * Math.PI / 360.0;
            float s = (float)Math.Sin(h);
            return new Quat(axis.x * s, axis.y * s, axis.z * s, (float)Math.Cos(h));
        }

        /// <summary>Same result as UnityEngine.Quaternion.Euler(x, y, z).</summary>
        public static Quat Euler(float xDeg, float yDeg, float zDeg)
            => AxisAngle(V3.Up, yDeg) * AxisAngle(V3.Right, xDeg) * AxisAngle(V3.Forward, zDeg);

        public static Quat Euler(V3 e) => Euler(e.x, e.y, e.z);

        public Quat Inverse => new Quat(-x, -y, -z, w);

        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        public static V3 operator *(Quat q, V3 v)
        {
            var u = new V3(q.x, q.y, q.z);
            V3 t = 2f * V3.Cross(u, v);
            return v + q.w * t + V3.Cross(u, t);
        }

        /// <summary>Rotation taking direction a onto direction b.</summary>
        public static Quat FromTo(V3 a, V3 b)
        {
            a = a.Normalized; b = b.Normalized;
            float d = V3.Dot(a, b);
            if (d > 0.99999f) return Identity;
            if (d < -0.99999f)
            {
                V3 axis = V3.Cross(V3.Right, a);
                if (axis.Length < 1e-4f) axis = V3.Cross(V3.Up, a);
                return AxisAngle(axis, 180f);
            }
            V3 c = V3.Cross(a, b);
            var q = new Quat(c.x, c.y, c.z, 1f + d);
            float n = (float)Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return new Quat(q.x / n, q.y / n, q.z / n, q.w / n);
        }
    }

    /// <summary>Deterministic value noise / hashing for organic surface variation.</summary>
    public static class ArtNoise
    {
        static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 2147483647 + seed * 144665;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
            }
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>Smooth 3D value noise in 0..1.</summary>
        public static float Value(V3 p, int seed = 0)
        {
            int x0 = (int)Math.Floor(p.x), y0 = (int)Math.Floor(p.y), z0 = (int)Math.Floor(p.z);
            float tx = Smooth(p.x - x0), ty = Smooth(p.y - y0), tz = Smooth(p.z - z0);
            float L(float a, float b, float t) => a + (b - a) * t;
            float c00 = L(Hash(x0, y0, z0, seed), Hash(x0 + 1, y0, z0, seed), tx);
            float c10 = L(Hash(x0, y0 + 1, z0, seed), Hash(x0 + 1, y0 + 1, z0, seed), tx);
            float c01 = L(Hash(x0, y0, z0 + 1, seed), Hash(x0 + 1, y0, z0 + 1, seed), tx);
            float c11 = L(Hash(x0, y0 + 1, z0 + 1, seed), Hash(x0 + 1, y0 + 1, z0 + 1, seed), tx);
            return L(L(c00, c10, ty), L(c01, c11, ty), tz);
        }

        /// <summary>Two-octave noise centred on 0 (range about -0.5..0.5).</summary>
        public static float Signed(V3 p, int seed = 0) => Value(p, seed) * 0.66f + Value(p * 2.3f, seed + 7) * 0.34f - 0.5f;

        public static float Rand(int i, int seed) => Hash(i, i * 3 + 1, i * 7 + 2, seed);
    }
}
