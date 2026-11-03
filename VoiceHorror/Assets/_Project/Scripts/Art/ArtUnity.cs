using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoiceHorror.Art;

namespace VoiceHorror.Characters
{
    /// <summary>Bridges the engine-free art library (VoiceArt) to Unity types.</summary>
    public static class ArtUnity
    {
        public static Vector3 ToUnity(this V3 v) => new Vector3(v.x, v.y, v.z);
        public static V3 ToArt(this Vector3 v) => new V3(v.x, v.y, v.z);
        public static Quaternion ToUnity(this Quat q) => new Quaternion(q.x, q.y, q.z, q.w);

        /// <summary>Builds one Unity mesh with a submesh per slot (slots listed in <paramref name="slots"/>).</summary>
        public static Mesh ToMesh(string name, IList<MeshData> parts, out List<Slot> slots, IList<Slot> partSlots)
        {
            var bySlot = new SortedDictionary<int, MeshData>();
            for (int i = 0; i < parts.Count; i++)
            {
                int key = (int)partSlots[i];
                if (!bySlot.TryGetValue(key, out var m)) bySlot[key] = m = new MeshData();
                m.Append(parts[i]);
            }
            slots = new List<Slot>();
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangleLists = new List<int[]>();
            foreach (var kv in bySlot)
            {
                int offset = verts.Count;
                var m = kv.Value;
                for (int i = 0; i < m.VertexCount; i++)
                {
                    verts.Add(m.Vertices[i].ToUnity());
                    normals.Add(m.Normals[i].ToUnity());
                    uvs.Add(new Vector2(m.U[i], m.V[i]));
                }
                var tris = new int[m.Triangles.Count];
                for (int i = 0; i < tris.Length; i++) tris[i] = m.Triangles[i] + offset;
                triangleLists.Add(tris);
                slots.Add((Slot)kv.Key);
            }
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = triangleLists.Count;
            for (int i = 0; i < triangleLists.Count; i++) mesh.SetTriangles(triangleLists[i], i, true);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
