using System;
using System.Globalization;
using System.IO;
using System.Text;
using VoiceHorror.Art;

// Usage: dotnet run --project tools/CharacterPreview -- <outDir>
// Writes <id>.obj (one group per material slot). Z is flipped to convert Unity's left-handed space.
string outDir = args.Length > 0 ? args[0] : "character-preview";
Directory.CreateDirectory(outDir);
var inv = CultureInfo.InvariantCulture;
foreach (var id in Blueprints.All)
{
    var bp = Blueprints.Create(id, 1);
    var problems = bp.Validate();
    if (problems.Count > 0) Console.WriteLine($"{id}: {string.Join("; ", problems)}");
    var sb = new StringBuilder();
    int offset = 1, verts = 0, tris = 0;
    foreach (var kv in bp.BakeRestPose())
    {
        var m = kv.Value;
        sb.AppendLine("o " + kv.Key);
        sb.AppendLine("usemtl " + kv.Key);
        foreach (var v in m.Vertices) sb.AppendLine(string.Format(inv, "v {0} {1} {2}", v.x, v.y, -v.z));
        foreach (var n in m.Normals) sb.AppendLine(string.Format(inv, "vn {0} {1} {2}", n.x, n.y, -n.z));
        for (int i = 0; i < m.Triangles.Count; i += 3)
        {
            int a = m.Triangles[i] + offset, b = m.Triangles[i + 1] + offset, c = m.Triangles[i + 2] + offset;
            sb.AppendLine($"f {a}//{a} {c}//{c} {b}//{b}"); // mirroring Z reverses winding
        }
        offset += m.VertexCount;
        verts += m.VertexCount;
        tris += m.TriangleCount;
    }
    File.WriteAllText(Path.Combine(outDir, id + ".obj"), sb.ToString());
    var (min, max) = bp.BakeRestPose().Count > 0 ? Bounds(bp) : (V3.Zero, V3.Zero);
    Console.WriteLine($"{id,-14} bones {bp.Bones.Count,3}  verts {verts,6}  tris {tris,6}  strands {bp.Strands.Count,2}  size {max - min}");
}

// First-person composite: arms + lantern (left) + shell (right), as the player sees them.
{
    var arms = Blueprints.Create(Blueprints.ReemArms);
    var pose = arms.RestPose();
    var parts = arms.BakeRestPose();
    void Attach(string bone, string propId, V3 offset, Quat extra)
    {
        var (p, r) = pose[bone];
        foreach (var kv in Blueprints.Create(propId).BakeRestPose())
        {
            if (!parts.TryGetValue(kv.Key, out var m)) parts[kv.Key] = m = new MeshData();
            m.Append(kv.Value, p + r * offset, r * extra, V3.One);
        }
    }
    Attach("PropL", Blueprints.PropLantern, V3.Zero, Quat.Identity);
    Attach("PropR", Blueprints.PropShell, V3.Zero, Quat.Identity);
    var sb2 = new StringBuilder();
    int off2 = 1;
    foreach (var kv in parts)
    {
        sb2.AppendLine("o " + kv.Key); sb2.AppendLine("usemtl " + kv.Key);
        foreach (var v in kv.Value.Vertices) sb2.AppendLine(string.Format(inv, "v {0} {1} {2}", v.x, v.y, -v.z));
        foreach (var n in kv.Value.Normals) sb2.AppendLine(string.Format(inv, "vn {0} {1} {2}", n.x, n.y, -n.z));
        for (int i = 0; i < kv.Value.Triangles.Count; i += 3)
        {
            int a = kv.Value.Triangles[i] + off2, b = kv.Value.Triangles[i + 1] + off2, c = kv.Value.Triangles[i + 2] + off2;
            sb2.AppendLine($"f {a}//{a} {c}//{c} {b}//{b}");
        }
        off2 += kv.Value.VertexCount;
    }
    File.WriteAllText(Path.Combine(outDir, "fp_view.obj"), sb2.ToString());
}

static (V3, V3) Bounds(Blueprint bp)
{
    var all = new MeshData();
    foreach (var m in bp.BakeRestPose().Values) all.Append(m);
    return all.Bounds();
}
