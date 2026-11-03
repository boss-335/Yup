using System;
using System.Collections.Generic;

namespace VoiceHorror.Art
{
    /// <summary>Engine-agnostic triangle mesh (clockwise front faces, like Unity).</summary>
    public sealed class MeshData
    {
        public readonly List<V3> Vertices = new List<V3>();
        public readonly List<V3> Normals = new List<V3>();
        public readonly List<float> U = new List<float>();
        public readonly List<float> V = new List<float>();
        public readonly List<int> Triangles = new List<int>();

        public int VertexCount => Vertices.Count;
        public int TriangleCount => Triangles.Count / 3;

        public int AddVertex(V3 p, V3 n, float u = 0f, float v = 0f)
        {
            Vertices.Add(p);
            Normals.Add(n);
            U.Add(u);
            V.Add(v);
            return Vertices.Count - 1;
        }

        public void AddTriangle(int a, int b, int c)
        {
            Triangles.Add(a); Triangles.Add(b); Triangles.Add(c);
        }

        /// <summary>Appends another mesh after transforming it by (scale, rotation, translation).</summary>
        public void Append(MeshData other, V3 position, Quat rotation, V3 scale)
        {
            int baseIndex = Vertices.Count;
            bool uniform = Math.Abs(scale.x - scale.y) < 1e-5f && Math.Abs(scale.y - scale.z) < 1e-5f;
            for (int i = 0; i < other.Vertices.Count; i++)
            {
                Vertices.Add(rotation * V3.Scale(other.Vertices[i], scale) + position);
                V3 n = uniform ? other.Normals[i] : new V3(other.Normals[i].x / scale.x, other.Normals[i].y / scale.y, other.Normals[i].z / scale.z).Normalized;
                Normals.Add(rotation * n);
                U.Add(other.U[i]);
                V.Add(other.V[i]);
            }
            foreach (int t in other.Triangles) Triangles.Add(baseIndex + t);
        }

        public void Append(MeshData other) => Append(other, V3.Zero, Quat.Identity, V3.One);

        public MeshData Transformed(V3 position, Quat rotation, V3 scale)
        {
            var m = new MeshData();
            m.Append(this, position, rotation, scale);
            return m;
        }

        /// <summary>Area-weighted smooth normals (shared vertices only).</summary>
        public void RecalculateNormals()
        {
            var acc = new V3[Vertices.Count];
            for (int i = 0; i < Triangles.Count; i += 3)
            {
                int a = Triangles[i], b = Triangles[i + 1], c = Triangles[i + 2];
                // Unity is clockwise-front: normal = (b-a) x (c-a).
                V3 n = V3.Cross(Vertices[b] - Vertices[a], Vertices[c] - Vertices[a]);
                acc[a] += n; acc[b] += n; acc[c] += n;
            }
            for (int i = 0; i < acc.Length; i++)
            {
                var n = acc[i].Normalized;
                Normals[i] = n.Length > 0.5f ? n : Normals[i];
            }
        }

        public (V3 min, V3 max) Bounds()
        {
            if (Vertices.Count == 0) return (V3.Zero, V3.Zero);
            float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue;
            float x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
            foreach (var p in Vertices)
            {
                if (p.x < x0) x0 = p.x; if (p.y < y0) y0 = p.y; if (p.z < z0) z0 = p.z;
                if (p.x > x1) x1 = p.x; if (p.y > y1) y1 = p.y; if (p.z > z1) z1 = p.z;
            }
            return (new V3(x0, y0, z0), new V3(x1, y1, z1));
        }

        /// <summary>True when indices are in range and all positions/normals are finite.</summary>
        public bool IsValid(out string problem)
        {
            problem = null;
            if (Triangles.Count % 3 != 0) { problem = "triangle list not a multiple of 3"; return false; }
            foreach (int t in Triangles) if (t < 0 || t >= Vertices.Count) { problem = "index out of range"; return false; }
            for (int i = 0; i < Vertices.Count; i++)
            {
                if (!Vertices[i].IsFinite) { problem = "non-finite vertex"; return false; }
                if (!Normals[i].IsFinite) { problem = "non-finite normal"; return false; }
            }
            if (U.Count != Vertices.Count || V.Count != Vertices.Count || Normals.Count != Vertices.Count) { problem = "attribute count mismatch"; return false; }
            return true;
        }
    }
}
