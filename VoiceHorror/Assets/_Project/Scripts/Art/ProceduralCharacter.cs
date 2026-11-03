using System.Collections.Generic;
using UnityEngine;
using VoiceHorror.Art;

namespace VoiceHorror.Characters
{
    /// <summary>
    /// Instantiates a <see cref="Blueprint"/> as a bone hierarchy with one mesh per bone.
    /// Built at runtime (Awake) so meshes never need to be stored in scenes; identical
    /// blueprints share meshes through a static cache.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ProceduralCharacter : MonoBehaviour
    {
        public string blueprintId = Blueprints.Maskut;
        [Tooltip("Variation seed (body proportions, noise). Characters with the same id+seed share meshes.")]
        public int seed = 1;
        public CharacterPalette palette = new CharacterPalette();
        public float scale = 1f;
        public bool castShadows = true;
        [Tooltip("Simulate hair, rags and kelp.")]
        public bool strands = true;

        public Blueprint Blueprint { get; private set; }
        public Transform VisualRoot { get; private set; }
        public bool Built => VisualRoot != null;

        readonly Dictionary<string, Transform> _bones = new Dictionary<string, Transform>();
        readonly Dictionary<string, Quaternion> _restLocal = new Dictionary<string, Quaternion>();
        readonly Dictionary<string, Vector3> _restLocalPos = new Dictionary<string, Vector3>();
        readonly List<Renderer> _renderers = new List<Renderer>();

        static readonly Dictionary<string, (Mesh mesh, List<Slot> slots)> MeshCache = new Dictionary<string, (Mesh, List<Slot>)>();
        static readonly Dictionary<string, Blueprint> BlueprintCache = new Dictionary<string, Blueprint>();

        public IReadOnlyList<Renderer> Renderers => _renderers;

        void Awake() => Build();

        public void Build()
        {
            if (Built) return;
            string key = blueprintId + "#" + seed;
            if (!BlueprintCache.TryGetValue(key, out var bp))
            {
                bp = Blueprints.Create(blueprintId, seed);
                BlueprintCache[key] = bp;
            }
            Blueprint = bp;

            VisualRoot = new GameObject("Rig_" + blueprintId).transform;
            VisualRoot.SetParent(transform, false);
            VisualRoot.localScale = Vector3.one * scale;

            // Bones.
            foreach (var b in bp.Bones)
            {
                var t = new GameObject(b.Name).transform;
                Transform parent = !string.IsNullOrEmpty(b.Parent) && _bones.TryGetValue(b.Parent, out var p) ? p : VisualRoot;
                t.SetParent(parent, false);
                t.localPosition = b.Position.ToUnity();
                t.localRotation = b.Rotation.ToUnity();
                _bones[b.Name] = t;
                _restLocal[b.Name] = t.localRotation;
                _restLocalPos[b.Name] = t.localPosition;
            }

            // One mesh per bone (parts grouped into submeshes by slot).
            var byBone = new Dictionary<string, (List<MeshData> meshes, List<Slot> slots)>();
            foreach (var part in bp.Parts)
            {
                if (!byBone.TryGetValue(part.Bone, out var list)) byBone[part.Bone] = list = (new List<MeshData>(), new List<Slot>());
                list.meshes.Add(part.Mesh);
                list.slots.Add(part.Slot);
            }
            foreach (var kv in byBone)
            {
                string meshKey = key + "/" + kv.Key;
                if (!MeshCache.TryGetValue(meshKey, out var cached))
                {
                    var mesh = ArtUnity.ToMesh(meshKey, kv.Value.meshes, out var slots, kv.Value.slots);
                    cached = (mesh, slots);
                    MeshCache[meshKey] = cached;
                }
                var go = new GameObject("Mesh");
                go.transform.SetParent(_bones[kv.Key], false);
                go.AddComponent<MeshFilter>().sharedMesh = cached.mesh;
                var mr = go.AddComponent<MeshRenderer>();
                var mats = new Material[cached.slots.Count];
                for (int i = 0; i < mats.Length; i++) mats[i] = palette.For(cached.slots[i]);
                mr.sharedMaterials = mats;
                mr.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                _renderers.Add(mr);
            }

            if (strands && bp.Strands.Count > 0)
            {
                var vs = gameObject.GetComponent<VerletStrands>();
                if (vs == null) vs = gameObject.AddComponent<VerletStrands>();
                vs.Init(this);
            }
        }

        public Transform Bone(string name) => name != null && _bones.TryGetValue(name, out var t) ? t : null;

        /// <summary>Bone for an animation role ("head", "jaw", "armL"...), or null.</summary>
        public Transform Role(string role) => Blueprint != null && Blueprint.Roles.TryGetValue(role, out var b) ? Bone(b) : null;

        public Quaternion RestLocal(Transform bone) => bone != null && _restLocal.TryGetValue(bone.name, out var q) ? q : Quaternion.identity;
        public Vector3 RestLocalPosition(Transform bone) => bone != null && _restLocalPos.TryGetValue(bone.name, out var p) ? p : Vector3.zero;

        public void SetTint(Color color, float amount)
        {
            // Used for hit flashes: MaterialPropertyBlock keeps shared materials untouched.
            var block = new MaterialPropertyBlock();
            foreach (var r in _renderers)
            {
                r.GetPropertyBlock(block);
                if (amount <= 0.001f) block.Clear();
                else
                {
                    block.SetColor("_EmissionColor", color * amount);
                }
                r.SetPropertyBlock(block);
            }
        }

        void OnDrawGizmos()
        {
            if (Built) return;
            // Placeholder in the editor before Play: the body is generated at runtime.
            Gizmos.color = new Color(0.9f, 0.6f, 0.3f, 0.6f);
            float h = blueprintId == Blueprints.BuDaryah ? 3.2f : blueprintId == Blueprints.Summah ? 4f : blueprintId == Blueprints.Nahham ? 1.1f : 2f;
            Gizmos.DrawWireCube(transform.position + Vector3.up * h * 0.5f * scale, new Vector3(0.6f, h, 0.6f) * scale);
        }
    }
}
