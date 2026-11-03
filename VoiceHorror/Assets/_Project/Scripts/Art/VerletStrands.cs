using System.Collections.Generic;
using UnityEngine;
using VoiceHorror.Art;

namespace VoiceHorror.Characters
{
    /// <summary>
    /// Secondary motion for hair, rags, kelp and tendrils: each strand is a verlet chain pinned to
    /// its bone, pulled by gravity and gently back toward its rest direction, drawn as ribbons.
    /// </summary>
    [DefaultExecutionOrder(200)] // after animation has posed the bones
    public sealed class VerletStrands : MonoBehaviour
    {
        public float gravity = 9.81f;
        public float damping = 0.06f;
        public float cullDistance = 45f;

        sealed class Chain
        {
            public StrandDef def;
            public Transform bone;
            public Vector3[] pos, prev;
            public float segLen;
            public int slotIndex;
        }

        readonly List<Chain> _chains = new List<Chain>();
        readonly List<Mesh> _meshes = new List<Mesh>();
        readonly List<List<Chain>> _bySlot = new List<List<Chain>>();
        Vector3[] _verts = new Vector3[0];
        Vector3[] _normals = new Vector3[0];
        int[] _tris = new int[0];
        Transform _model;
        bool _ready;

        public void Init(ProceduralCharacter character)
        {
            var bp = character.Blueprint;
            _model = character.VisualRoot;
            var slotOrder = new List<Slot>();
            foreach (var s in bp.Strands)
            {
                var bone = character.Bone(s.Bone);
                if (bone == null) continue;
                int si = slotOrder.IndexOf(s.Slot);
                if (si < 0) { slotOrder.Add(s.Slot); si = slotOrder.Count - 1; _bySlot.Add(new List<Chain>()); }
                var c = new Chain { def = s, bone = bone, slotIndex = si, segLen = s.Length / s.Segments * character.scale };
                c.pos = new Vector3[s.Segments + 1];
                c.prev = new Vector3[s.Segments + 1];
                Vector3 root = bone.TransformPoint(s.Root.ToUnity());
                Vector3 dir = _model.TransformDirection(s.Direction.ToUnity().normalized);
                for (int i = 0; i < c.pos.Length; i++) c.pos[i] = c.prev[i] = root + dir * (c.segLen * i);
                _chains.Add(c);
                _bySlot[si].Add(c);
            }
            for (int si = 0; si < slotOrder.Count; si++)
            {
                var go = new GameObject("Strands_" + slotOrder[si]);
                go.transform.SetParent(transform, false);
                var mesh = new Mesh { name = "Strands" };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = character.palette.For(slotOrder[si]);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _meshes.Add(mesh);
            }
            _ready = true;
        }

        void LateUpdate()
        {
            if (!_ready) return;
            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            if (dt <= 0f) return;
            var cam = Camera.main;
            bool far = cam != null && (cam.transform.position - transform.position).sqrMagnitude > cullDistance * cullDistance;

            foreach (var c in _chains)
            {
                Vector3 root = c.bone.TransformPoint(c.def.Root.ToUnity());
                Vector3 restDir = _model.TransformDirection(c.def.Direction.ToUnity().normalized);
                c.pos[0] = root;
                c.prev[0] = root;
                if (far)
                {
                    for (int i = 1; i < c.pos.Length; i++) c.pos[i] = c.prev[i] = root + restDir * (c.segLen * i);
                    continue;
                }
                for (int i = 1; i < c.pos.Length; i++)
                {
                    Vector3 vel = (c.pos[i] - c.prev[i]) * (1f - damping);
                    c.prev[i] = c.pos[i];
                    Vector3 rest = root + restDir * (c.segLen * i);
                    c.pos[i] += vel + Vector3.down * (gravity * dt * dt) + (rest - c.pos[i]) * (c.def.Stiffness * dt * 10f);
                }
                for (int iter = 0; iter < 3; iter++)
                {
                    for (int i = 1; i < c.pos.Length; i++)
                    {
                        Vector3 d = c.pos[i] - c.pos[i - 1];
                        float len = d.magnitude;
                        if (len < 1e-5f) continue;
                        Vector3 corr = d * ((len - c.segLen) / len);
                        if (i == 1) c.pos[i] -= corr;
                        else { c.pos[i] -= corr * 0.5f; c.pos[i - 1] += corr * 0.5f; }
                    }
                }
            }
            for (int si = 0; si < _meshes.Count; si++) Rebuild(_meshes[si], _bySlot[si]);
        }

        void Rebuild(Mesh mesh, List<Chain> chains)
        {
            int verts = 0, tris = 0;
            foreach (var c in chains) { verts += c.pos.Length * 4; tris += (c.pos.Length - 1) * 12; }
            if (_verts.Length < verts) { _verts = new Vector3[verts]; _normals = new Vector3[verts]; }
            if (_tris.Length != tris) _tris = new int[tris];
            int v = 0, t = 0;
            Vector3 fwd = transform.forward;
            foreach (var c in chains)
            {
                int n = c.pos.Length;
                for (int side = 0; side < 2; side++)
                {
                    int start = v;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 along = (c.pos[Mathf.Min(n - 1, i + 1)] - c.pos[Mathf.Max(0, i - 1)]).normalized;
                        Vector3 across = Vector3.Cross(along, fwd);
                        if (across.sqrMagnitude < 0.04f) across = transform.right;
                        across.Normalize();
                        float w = Mathf.Lerp(c.def.WidthRoot, c.def.WidthTip, i / (float)(n - 1)) * 0.5f;
                        Vector3 nrm = Vector3.Cross(across, along).normalized * (side == 0 ? 1f : -1f);
                        _verts[v] = transform.InverseTransformPoint(c.pos[i] - across * w);
                        _normals[v++] = transform.InverseTransformDirection(nrm);
                        _verts[v] = transform.InverseTransformPoint(c.pos[i] + across * w);
                        _normals[v++] = transform.InverseTransformDirection(nrm);
                    }
                    for (int i = 0; i < n - 1; i++)
                    {
                        int a = start + i * 2, b = a + 1, cc = a + 2, d = a + 3;
                        // Front face (Unity: Cross(b-a, c-a)) must agree with the side's normal for correct lighting.
                        if (side == 0) { _tris[t++] = a; _tris[t++] = b; _tris[t++] = cc; _tris[t++] = b; _tris[t++] = d; _tris[t++] = cc; }
                        else { _tris[t++] = a; _tris[t++] = cc; _tris[t++] = b; _tris[t++] = b; _tris[t++] = cc; _tris[t++] = d; }
                    }
                }
            }
            mesh.Clear(false);
            mesh.SetVertices(_verts, 0, v);
            mesh.SetNormals(_normals, 0, v);
            mesh.SetTriangles(_tris, 0, t, 0, true);
        }
    }
}
