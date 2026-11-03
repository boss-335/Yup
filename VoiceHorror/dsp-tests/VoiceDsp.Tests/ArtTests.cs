using System;
using VoiceHorror.Art;
using Xunit;
using Xunit.Abstractions;

namespace VoiceHorror.Dsp.Tests
{
    public class ArtTests
    {
        readonly ITestOutputHelper _out;
        public ArtTests(ITestOutputHelper o) { _out = o; }

        public static TheoryData<string> AllBlueprints()
        {
            var d = new TheoryData<string>();
            foreach (var id in Blueprints.All) d.Add(id);
            return d;
        }

        [Theory]
        [MemberData(nameof(AllBlueprints))]
        public void Blueprint_IsWellFormed(string id)
        {
            var bp = Blueprints.Create(id, 1);
            var problems = bp.Validate();
            Assert.True(problems.Count == 0, string.Join("; ", problems));
            int verts = 0;
            foreach (var m in bp.BakeRestPose().Values) verts += m.VertexCount;
            _out.WriteLine($"{id}: {bp.Bones.Count} bones, {verts} verts");
            Assert.InRange(verts, 50, 12000); // cheap enough for several on screen
        }

        [Theory]
        [InlineData(Blueprints.Maskut, 1.8f, 2.2f, 0f)]
        [InlineData(Blueprints.Siluwa, 1.2f, 1.8f, 0f)]
        [InlineData(Blueprints.BuDaryah, 2.6f, 3.6f, 0.35f)] // grows out of the ground: the mound is half buried
        [InlineData(Blueprints.Summah, 3.4f, 4.6f, 0f)]
        [InlineData(Blueprints.Nahham, 0.9f, 1.4f, 0f)]
        [InlineData(Blueprints.Mubarak, 1.7f, 2.3f, 0f)]
        public void Characters_HaveTheirDesignedHeight_AndStandOnTheGround(string id, float minH, float maxH, float buried)
        {
            var all = new MeshData();
            foreach (var m in Blueprints.Create(id).BakeRestPose().Values) all.Append(m);
            var (min, max) = all.Bounds();
            _out.WriteLine($"{id}: min {min} max {max}");
            Assert.InRange(max.y - min.y, minH, maxH);
            Assert.InRange(min.y, -0.25f - buried, 0.25f);
        }

        [Fact]
        public void Variants_DifferBySeed()
        {
            var a = Blueprints.Create(Blueprints.Maskut, 1).Find("Hips").Position.y;
            var b = Blueprints.Create(Blueprints.Maskut, 4).Find("Hips").Position.y;
            Assert.NotEqual(a, b);
        }

        static void AssertOutward(MeshData m, V3 center, string what)
        {
            int bad = 0;
            for (int i = 0; i < m.Triangles.Count; i += 3)
            {
                V3 a = m.Vertices[m.Triangles[i]], b = m.Vertices[m.Triangles[i + 1]], c = m.Vertices[m.Triangles[i + 2]];
                V3 n = V3.Cross(b - a, c - a); // Unity front-face normal for clockwise winding
                V3 mid = (a + b + c) / 3f;
                if (V3.Dot(n, mid - center) < 0) bad++;
            }
            Assert.True(bad <= m.TriangleCount / 50, $"{what}: {bad}/{m.TriangleCount} triangles face inward");
        }

        [Fact]
        public void Blob_FacesOutward()
        {
            var m = Shapes.Blob(new V3(1, 2, 3), new V3(0.5f, 1f, 0.7f), 10, 14);
            AssertOutward(m, new V3(1, 2, 3), "blob");
            Assert.True(m.IsValid(out var why), why);
        }

        [Fact]
        public void Limb_FacesOutward_AndNormalsMatchWinding()
        {
            var m = Shapes.Limb(V3.Zero, new V3(0, -1, 0), 0.1f, 0.06f);
            AssertOutward(m, new V3(0, -0.5f, 0), "limb");
            // Smooth normals agree with the geometric orientation.
            int agree = 0;
            for (int i = 0; i < m.VertexCount; i++)
            {
                var radial = new V3(m.Vertices[i].x, 0, m.Vertices[i].z);
                if (radial.Length < 1e-4f || V3.Dot(m.Normals[i], radial) > 0) agree++;
            }
            Assert.True(agree >= m.VertexCount * 0.95f);
        }

        [Fact]
        public void Box_FacesOutward() => AssertOutward(Shapes.Box(V3.Zero, new V3(1, 2, 3)), V3.Zero, "box");

        [Fact]
        public void Quat_MatchesUnityConventions()
        {
            // Unity: Quaternion.Euler(0,90,0) * forward = right; Euler(90,0,0) * forward = down.
            V3 r = Quat.Euler(0, 90, 0) * V3.Forward;
            Assert.Equal(1f, r.x, 4); Assert.Equal(0f, r.z, 4);
            V3 d = Quat.Euler(90, 0, 0) * V3.Forward;
            Assert.Equal(-1f, d.y, 4);
            // Order Z, X, Y: rolling first, then pitching, then yawing.
            V3 v = Quat.Euler(30, 40, 50) * new V3(0.2f, 0.5f, 0.8f);
            V3 manual = Quat.AxisAngle(V3.Up, 40) * (Quat.AxisAngle(V3.Right, 30) * (Quat.AxisAngle(V3.Forward, 50) * new V3(0.2f, 0.5f, 0.8f)));
            Assert.Equal(manual.x, v.x, 4); Assert.Equal(manual.y, v.y, 4); Assert.Equal(manual.z, v.z, 4);
        }

        [Fact]
        public void Aim_PointsLimbAlongModelDirection()
        {
            var bp = Blueprints.Create(Blueprints.Nahham);
            var pose = bp.RestPose();
            V3 down = pose["UpperArmR"].rot * V3.Down;
            V3 want = new V3(0.75f, 0.05f, 0.62f).Normalized;
            Assert.True(V3.Dot(down, want) > 0.999f, $"upper arm points {down}, wanted {want}");
        }

        [Fact]
        public void NahhamHand_ReachesHisEar()
        {
            var pose = Blueprints.Create(Blueprints.Nahham).RestPose();
            V3 hand = pose["HandR"].pos;
            V3 head = pose["Head"].pos;
            _out.WriteLine($"hand {hand} head {head} dist {(hand - head).Length:0.00}");
            Assert.True((hand - head).Length < 0.32f, "the nahham's hand should be cupped near his head");
            Assert.True(hand.x > head.x, "right hand on the right side");
        }

        [Fact]
        public void FirstPersonHands_AreInsideTheView()
        {
            var pose = Blueprints.Create(Blueprints.ReemArms).RestPose();
            foreach (var hand in new[] { "HandL", "HandR" })
            {
                V3 p = pose[hand].pos;
                // 70° vertical FOV at 16:9: |y| < z·tan(35°) and |x| < z·tan(51°).
                Assert.True(p.z > 0.15f, hand + " in front of the camera");
                Assert.True(Math.Abs(p.y) < p.z * Math.Tan(35 * Math.PI / 180), $"{hand} vertically visible ({p})");
                Assert.True(Math.Abs(p.x) < p.z * Math.Tan(51 * Math.PI / 180), $"{hand} horizontally visible ({p})");
            }
        }
    }
}
