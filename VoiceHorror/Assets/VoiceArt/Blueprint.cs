using System;
using System.Collections.Generic;

namespace VoiceHorror.Art
{
    /// <summary>Material slot a mesh part is drawn with.</summary>
    public enum Slot
    {
        Skin = 0,
        Dark = 1,
        Glow = 2,
        Cloth = 3,
        Hair = 4,
        Wood = 5,
        Metal = 6,
        Ghost = 7,
        Shell = 8,
    }

    /// <summary>A joint. Local transform relative to its parent. Limb bones point down local -Y in rest pose.</summary>
    public sealed class Bone
    {
        public string Name;
        public string Parent;
        public V3 Position;
        /// <summary>Local rotation relative to the parent (Unity convention).</summary>
        public Quat Rotation = Quat.Identity;
        /// <summary>Length to the child joint (used by IK); 0 for end bones.</summary>
        public float Length;
    }

    /// <summary>Mesh attached rigidly to a bone, in the bone's local space.</summary>
    public sealed class Part
    {
        public string Bone;
        public Slot Slot;
        public MeshData Mesh;
    }

    /// <summary>Dynamic strand (hair, kelp, rag) simulated at runtime with verlet physics.</summary>
    public sealed class StrandDef
    {
        public string Bone;
        public V3 Root;          // bone-local
        public V3 Direction;     // model-space rest direction (strands hang, so this is mostly "down")
        public float Length;
        public int Segments = 5;
        public float WidthRoot = 0.05f;
        public float WidthTip = 0.01f;
        public Slot Slot = Slot.Hair;
        public float Stiffness = 0.15f;
    }

    /// <summary>Two-bone IK chain (leg or quadruped front leg).</summary>
    public sealed class LegDef
    {
        public string Upper, Lower, End;
        /// <summary>Gait group: legs in the same group step together, groups alternate.</summary>
        public int Group;
        /// <summary>Which way the middle joint bends, in body space (+Z = knee forward).</summary>
        public V3 Pole = V3.Forward;
    }

    /// <summary>
    /// Complete procedural character: skeleton, rigid mesh parts, strands, legs and named
    /// animation hooks. Built in Unity by RigBuilder, previewed outside Unity by the preview tool.
    /// </summary>
    public sealed class Blueprint
    {
        public string Id;
        public float Height;
        public readonly List<Bone> Bones = new List<Bone>();
        public readonly List<Part> Parts = new List<Part>();
        public readonly List<StrandDef> Strands = new List<StrandDef>();
        public readonly List<LegDef> Legs = new List<LegDef>();
        /// <summary>Animation hooks by role: "hips", "spine", "chest", "neck", "head", "jaw", "armL", "armR", "foreL", "foreR", "ring", ...</summary>
        public readonly Dictionary<string, string> Roles = new Dictionary<string, string>();
        public float StepLength = 0.5f;
        public float StepHeight = 0.12f;

        public Bone AddBone(string name, string parent, V3 pos, V3 euler = default, float length = 0f)
        {
            var b = new Bone { Name = name, Parent = parent, Position = pos, Rotation = Quat.Euler(euler), Length = length };
            Bones.Add(b);
            return b;
        }

        public void Add(string bone, Slot slot, MeshData mesh) => Parts.Add(new Part { Bone = bone, Slot = slot, Mesh = mesh });

        public Bone Find(string name) => Bones.Find(b => b.Name == name);

        /// <summary>
        /// Poses a limb bone so its local -Y axis points along a model-space direction
        /// (keeps posing readable: "upper arm points out and forward").
        /// </summary>
        public void Aim(string bone, V3 modelDirection)
        {
            var b = Find(bone);
            if (b == null) return;
            Quat parentRot = Quat.Identity;
            if (!string.IsNullOrEmpty(b.Parent) && RestPose().TryGetValue(b.Parent, out var p)) parentRot = p.rot;
            V3 local = parentRot.Inverse * modelDirection.Normalized;
            b.Rotation = Quat.FromTo(V3.Down, local);
        }

        /// <summary>World-space (model-space) transform of each bone in the rest pose.</summary>
        public Dictionary<string, (V3 pos, Quat rot)> RestPose()
        {
            var result = new Dictionary<string, (V3 pos, Quat rot)>();
            foreach (var b in Bones)
            {
                if (string.IsNullOrEmpty(b.Parent) || !result.TryGetValue(b.Parent, out var p))
                {
                    result[b.Name] = (b.Position, b.Rotation);
                    continue;
                }
                result[b.Name] = (p.pos + p.rot * b.Position, p.rot * b.Rotation);
            }
            return result;
        }

        /// <summary>All parts merged into model space, grouped by slot (for previews and bounds).</summary>
        public Dictionary<Slot, MeshData> BakeRestPose()
        {
            var pose = RestPose();
            var bySlot = new Dictionary<Slot, MeshData>();
            foreach (var part in Parts)
            {
                if (!pose.TryGetValue(part.Bone, out var t)) continue;
                if (!bySlot.TryGetValue(part.Slot, out var m)) bySlot[part.Slot] = m = new MeshData();
                m.Append(part.Mesh, t.pos, t.rot, V3.One);
            }
            foreach (var s in Strands)
            {
                if (!pose.TryGetValue(s.Bone, out var t)) continue;
                V3 root = t.pos + t.rot * s.Root;
                var pts = Shapes.Strand(root, s.Direction, s.Length, s.Segments, V3.Zero);
                var ribbon = Shapes.Ribbon(pts, Shapes.Taper(pts.Count, s.WidthRoot, s.WidthTip), StrandAcross(s.Direction));
                if (!bySlot.TryGetValue(s.Slot, out var m)) bySlot[s.Slot] = m = new MeshData();
                m.Append(ribbon);
            }
            return bySlot;
        }

        /// <summary>Width direction of a ribbon hanging along dir (horizontal, facing the viewer).</summary>
        public static V3 StrandAcross(V3 dir)
        {
            V3 a = V3.Cross(dir, V3.Forward);
            return a.Length > 0.2f ? a.Normalized : V3.Right;
        }

        /// <summary>Checks the skeleton and meshes are well-formed. Returns problems (empty = OK).</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var seen = new HashSet<string>();
            foreach (var b in Bones)
            {
                if (!seen.Add(b.Name)) problems.Add("duplicate bone " + b.Name);
                if (!string.IsNullOrEmpty(b.Parent) && !seen.Contains(b.Parent)) problems.Add($"bone {b.Name} declared before parent {b.Parent}");
            }
            foreach (var p in Parts)
            {
                if (!seen.Contains(p.Bone)) problems.Add("part on missing bone " + p.Bone);
                if (!p.Mesh.IsValid(out var why)) problems.Add($"part on {p.Bone}: {why}");
            }
            foreach (var s in Strands) if (!seen.Contains(s.Bone)) problems.Add("strand on missing bone " + s.Bone);
            foreach (var l in Legs)
                foreach (var n in new[] { l.Upper, l.Lower, l.End })
                    if (!seen.Contains(n)) problems.Add("leg uses missing bone " + n);
            foreach (var r in Roles) if (!seen.Contains(r.Value)) problems.Add($"role {r.Key} -> missing bone {r.Value}");
            return problems;
        }
    }
}
