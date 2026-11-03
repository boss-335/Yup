using System;
using System.Collections.Generic;

namespace VoiceHorror.Art
{
    /// <summary>
    /// Organic primitive generators: tapered limbs with muscle bulges, displaced blobs,
    /// swept tubes, cloth/hair ribbons. All return meshes with outward-facing, Unity-wound triangles.
    /// </summary>
    public static class Shapes
    {
        const float Tau = (float)(Math.PI * 2.0);

        /// <summary>Quad grid with wrap-around columns. pos(row, col) for col in [0, cols).</summary>
        static void Grid(MeshData m, int rows, int cols, Func<int, int, V3> pos, int baseRowsV = 1)
        {
            int start = m.VertexCount;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    m.AddVertex(pos(r, c), V3.Up, c / (float)cols, r / (float)Math.Max(1, rows - 1));
            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int c1 = (c + 1) % cols;
                    int a = start + r * cols + c, b = start + r * cols + c1;
                    int d = start + (r + 1) * cols + c, e = start + (r + 1) * cols + c1;
                    m.AddTriangle(a, d, b);
                    m.AddTriangle(b, d, e);
                }
            }
        }

        /// <summary>Closes a ring (row) with a fan to an apex. outward = apex lies beyond the row.</summary>
        static void Cap(MeshData m, int ringStart, int cols, V3 apex, bool atEnd)
        {
            int ap = m.AddVertex(apex, V3.Up, 0.5f, atEnd ? 1f : 0f);
            for (int c = 0; c < cols; c++)
            {
                int a = ringStart + c, b = ringStart + (c + 1) % cols;
                if (atEnd) m.AddTriangle(a, ap, b);
                else m.AddTriangle(b, ap, a);
            }
        }

        /// <summary>
        /// Swept tube along a polyline. Each centre has its own elliptical radius (rx across, rz depth),
        /// with optional domed caps and surface roughness.
        /// </summary>
        public static MeshData Tube(IList<V3> centers, IList<float> rx, IList<float> rz, int sides = 10,
                                    bool capStart = true, bool capEnd = true, float roughness = 0f, int seed = 0, V3? sideHint = null)
        {
            var m = new MeshData();
            int n = centers.Count;
            if (n < 2) return m;
            var tangents = new V3[n];
            for (int i = 0; i < n; i++)
            {
                V3 a = centers[Math.Max(0, i - 1)], b = centers[Math.Min(n - 1, i + 1)];
                tangents[i] = (b - a).Normalized;
            }
            // Parallel-transport frames.
            V3 hint = sideHint ?? (Math.Abs(V3.Dot(tangents[0], V3.Forward)) > 0.9f ? V3.Right : V3.Forward);
            var normals = new V3[n];
            var binormals = new V3[n];
            normals[0] = V3.Cross(tangents[0], V3.Cross(hint, tangents[0])).Normalized;
            for (int i = 0; i < n; i++)
            {
                if (i > 0)
                {
                    Quat q = Quat.FromTo(tangents[i - 1], tangents[i]);
                    normals[i] = (q * normals[i - 1]).Normalized;
                }
                binormals[i] = V3.Cross(tangents[i], normals[i]).Normalized;
            }

            int ringStart = m.VertexCount;
            Grid(m, n, sides, (r, c) =>
            {
                float a = c / (float)sides * Tau;
                float rough = roughness > 0f ? 1f + roughness * ArtNoise.Signed(centers[r] * 6f + new V3(c * 0.37f, 0, 0), seed) : 1f;
                V3 offset = binormals[r] * ((float)Math.Cos(a) * rx[r] * rough) + normals[r] * ((float)Math.Sin(a) * rz[r] * rough);
                return centers[r] + offset;
            });
            if (capStart)
            {
                float r0 = Math.Max(rx[0], rz[0]);
                Cap(m, ringStart, sides, centers[0] - tangents[0] * (r0 * 0.6f), false);
            }
            if (capEnd)
            {
                float r1 = Math.Max(rx[n - 1], rz[n - 1]);
                Cap(m, ringStart + (n - 1) * sides, sides, centers[n - 1] + tangents[n - 1] * (r1 * 0.6f), true);
            }
            m.RecalculateNormals();
            return m;
        }

        /// <summary>
        /// Tapered limb from a to b. bulge adds a muscle swell centred at bulgeAt (0..1);
        /// flatten squashes the depth (0 = round). Ends are domed.
        /// </summary>
        public static MeshData Limb(V3 a, V3 b, float r0, float r1, float bulge = 0.15f, float bulgeAt = 0.35f,
                                    float flatten = 0f, int rings = 7, int sides = 10, float roughness = 0f, int seed = 0)
        {
            var c = new List<V3>();
            var rx = new List<float>();
            var rz = new List<float>();
            for (int i = 0; i < rings; i++)
            {
                float t = i / (float)(rings - 1);
                float baseR = r0 + (r1 - r0) * t;
                float swell = 1f + bulge * (float)Math.Exp(-Math.Pow((t - bulgeAt) / 0.22f, 2));
                // Round the ends so joints read as joints.
                float endRound = (float)Math.Sqrt(Math.Max(0.15, 1.0 - Math.Pow(2 * t - 1, 8)));
                c.Add(V3.Lerp(a, b, t));
                rx.Add(baseR * swell * endRound);
                rz.Add(baseR * swell * endRound * (1f - flatten));
            }
            return Tube(c, rx, rz, sides, true, true, roughness, seed);
        }

        /// <summary>Ellipsoid with optional displacement along the normal (displace(unitDir) in radius units).</summary>
        public static MeshData Blob(V3 center, V3 radii, int lat = 10, int lon = 14, Func<V3, float> displace = null)
        {
            var m = new MeshData();
            int rows = lat + 1;
            // Rows from south pole (excluded) to north pole (excluded); poles capped.
            Grid(m, lat - 1, lon, (r, c) =>
            {
                float phi = (float)Math.PI * (r + 1) / lat;          // 0..pi from bottom
                float theta = c / (float)lon * Tau;
                var dir = new V3((float)(Math.Sin(phi) * Math.Cos(theta)), -(float)Math.Cos(phi), (float)(Math.Sin(phi) * Math.Sin(theta)));
                float k = 1f + (displace?.Invoke(dir) ?? 0f);
                return center + V3.Scale(dir, radii) * k;
            });
            var south = new V3(0, -1, 0);
            var north = new V3(0, 1, 0);
            Cap(m, 0, lon, center + V3.Scale(south, radii) * (1f + (displace?.Invoke(south) ?? 0f)), false);
            Cap(m, (lat - 2) * lon, lon, center + V3.Scale(north, radii) * (1f + (displace?.Invoke(north) ?? 0f)), true);
            _ = rows;
            m.RecalculateNormals();
            return m;
        }

        /// <summary>Axis-aligned box with flat normals (24 vertices).</summary>
        public static MeshData Box(V3 center, V3 size)
        {
            var m = new MeshData();
            V3 h = size * 0.5f;
            void Face(V3 n, V3 u, V3 v)
            {
                // Corners ordered so that (b-a)x(c-a) points along n (Unity clockwise front).
                V3 c0 = center + V3.Scale(n, h) - V3.Scale(u, h) - V3.Scale(v, h);
                V3 c1 = center + V3.Scale(n, h) - V3.Scale(u, h) + V3.Scale(v, h);
                V3 c2 = center + V3.Scale(n, h) + V3.Scale(u, h) + V3.Scale(v, h);
                V3 c3 = center + V3.Scale(n, h) + V3.Scale(u, h) - V3.Scale(v, h);
                int i0 = m.AddVertex(c0, n, 0, 0), i1 = m.AddVertex(c1, n, 0, 1), i2 = m.AddVertex(c2, n, 1, 1), i3 = m.AddVertex(c3, n, 1, 0);
                if (V3.Dot(V3.Cross(c1 - c0, c2 - c0), n) > 0) { m.AddTriangle(i0, i1, i2); m.AddTriangle(i0, i2, i3); }
                else { m.AddTriangle(i0, i2, i1); m.AddTriangle(i0, i3, i2); }
            }
            Face(V3.Right, V3.Forward, V3.Up); Face(-V3.Right, V3.Forward, V3.Up);
            Face(V3.Up, V3.Right, V3.Forward); Face(V3.Down, V3.Right, V3.Forward);
            Face(V3.Forward, V3.Right, V3.Up); Face(-V3.Forward, V3.Right, V3.Up);
            return m;
        }

        /// <summary>Double-sided strip through points (cloth rags, hair locks, kelp, stitches).</summary>
        public static MeshData Ribbon(IList<V3> points, IList<float> widths, V3 across)
        {
            var m = new MeshData();
            int n = points.Count;
            if (n < 2) return m;
            for (int side = 0; side < 2; side++)
            {
                int start = m.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    V3 t = (points[Math.Min(n - 1, i + 1)] - points[Math.Max(0, i - 1)]).Normalized;
                    V3 nrm = V3.Cross(across, t).Normalized * (side == 0 ? 1f : -1f);
                    V3 half = across.Normalized * (widths[i] * 0.5f);
                    m.AddVertex(points[i] - half, nrm, 0, i / (float)(n - 1));
                    m.AddVertex(points[i] + half, nrm, 1, i / (float)(n - 1));
                }
                for (int i = 0; i < n - 1; i++)
                {
                    int a = start + i * 2, b = a + 1, c = a + 2, d = a + 3;
                    // Orient each quad so its geometric normal matches the side's normal.
                    V3 geo = V3.Cross(m.Vertices[c] - m.Vertices[a], m.Vertices[b] - m.Vertices[a]);
                    if (V3.Dot(geo, m.Normals[a]) >= 0) { m.AddTriangle(a, c, b); m.AddTriangle(b, c, d); }
                    else { m.AddTriangle(a, b, c); m.AddTriangle(b, d, c); }
                }
            }
            return m;
        }

        /// <summary>A hanging strand curve: starts at root, falls along dir with a slight curl.</summary>
        public static List<V3> Strand(V3 root, V3 dir, float length, int segments, V3 curl)
        {
            var pts = new List<V3>();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                pts.Add(root + dir.Normalized * (length * t) + curl * (t * t));
            }
            return pts;
        }

        public static List<float> Taper(int count, float w0, float w1)
        {
            var l = new List<float>();
            for (int i = 0; i < count; i++) l.Add(w0 + (w1 - w0) * i / Math.Max(1, count - 1));
            return l;
        }
    }
}
