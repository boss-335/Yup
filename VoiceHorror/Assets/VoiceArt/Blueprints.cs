using System;
using System.Collections.Generic;

namespace VoiceHorror.Art
{
    /// <summary>
    /// Every character of Al-Summah as a procedural blueprint (see docs/GAME_DESIGN.md §3–4).
    /// Model space: feet at y = 0, facing +Z. Limb bones hang along local -Y; spine bones rise along +Y.
    /// </summary>
    public static class Blueprints
    {
        public const string Maskut = "maskut";
        public const string Siluwa = "siluwa";
        public const string BuDaryah = "budaryah";
        public const string Summah = "summah";
        public const string Nahham = "nahham";
        public const string Mubarak = "mubarak";
        public const string ReemArms = "reem_arms";
        public const string PropLantern = "prop_lantern";
        public const string PropShell = "prop_shell";
        public const string PropDrum = "prop_drum";

        public static readonly string[] All = { Maskut, Siluwa, BuDaryah, Summah, Nahham, Mubarak, ReemArms, PropLantern, PropShell, PropDrum };

        public static Blueprint Create(string id, int seed = 1)
        {
            switch (id)
            {
                case Maskut: return BuildMaskut(seed);
                case Siluwa: return BuildSiluwa(seed);
                case BuDaryah: return BuildBuDaryah(seed);
                case Summah: return BuildSummah(seed);
                case Nahham: return BuildNahham(seed);
                case Mubarak: return BuildMubarak(seed);
                case ReemArms: return BuildReemArms(seed);
                case PropLantern: return BuildLantern();
                case PropShell: return BuildShell(seed);
                case PropDrum: return BuildDrum();
                default: throw new ArgumentException("Unknown blueprint " + id);
            }
        }

        // =====================================================================================
        // Shared builders
        // =====================================================================================

        static void LimbPart(Blueprint bp, string bone, float length, float r0, float r1, Slot slot = Slot.Skin,
                             float bulge = 0.12f, float bulgeAt = 0.35f, float flatten = 0f, float rough = 0f, int seed = 0)
            => bp.Add(bone, slot, Shapes.Limb(V3.Zero, new V3(0, -length, 0), r0, r1, bulge, bulgeAt, flatten, 7, 10, rough, seed));

        /// <summary>Hand with fingers hanging along -Y. curl 0 = straight, 1 = fist. claw makes long dark nails.</summary>
        static void Hand(Blueprint bp, string bone, float scale, float curl, bool claw, Slot skin, int fingers = 4, bool mirror = false)
        {
            float s = scale;
            bp.Add(bone, skin, Shapes.Blob(new V3(0, -0.055f * s, 0.005f * s), new V3(0.042f * s, 0.06f * s, 0.018f * s), 8, 10));
            for (int i = 0; i < fingers; i++)
            {
                float x = (-0.027f + 0.018f * i) * s * (fingers == 4 ? 1f : 4f / fingers);
                float len = (claw ? 0.15f : 0.085f) * s * (i == 1 || i == 2 ? 1f : 0.86f);
                var pts = new List<V3>();
                var rx = new List<float>();
                int seg = 5;
                for (int k = 0; k < seg; k++)
                {
                    float t = k / (float)(seg - 1);
                    float bend = curl * t * t * 1.4f;
                    pts.Add(new V3(x, -0.105f * s - len * t * (float)Math.Cos(bend), 0.004f * s + len * (float)Math.Sin(bend) * 0.9f));
                    rx.Add((0.011f - 0.004f * t) * s);
                }
                bp.Add(bone, skin, Shapes.Tube(pts, rx, rx, 6));
                if (claw)
                {
                    V3 tip = pts[pts.Count - 1];
                    V3 dir = (pts[pts.Count - 1] - pts[pts.Count - 2]).Normalized;
                    bp.Add(bone, Slot.Dark, Shapes.Limb(tip, tip + dir * (0.05f * s), 0.008f * s, 0.001f * s, 0f, 0.5f, 0.3f, 4, 5));
                }
            }
            // Thumb.
            float tx = (mirror ? 0.045f : -0.045f) * s;
            bp.Add(bone, skin, Shapes.Limb(new V3(tx * 0.6f, -0.03f * s, 0.012f * s), new V3(tx, -0.09f * s, 0.035f * s + curl * 0.02f * s), 0.012f * s, 0.009f * s, 0f, 0.5f, 0f, 5, 6));
        }

        static void Foot(Blueprint bp, string bone, float scale, Slot skin, bool claws)
        {
            bp.Add(bone, skin, Shapes.Blob(new V3(0, -0.02f * scale, 0.06f * scale), new V3(0.05f * scale, 0.035f * scale, 0.12f * scale), 7, 10));
            for (int i = 0; i < 4; i++)
            {
                float x = (-0.03f + 0.02f * i) * scale;
                var a = new V3(x, -0.035f * scale, 0.16f * scale);
                var b = a + new V3(0, -0.01f, claws ? 0.09f : 0.04f) * scale;
                bp.Add(bone, claws ? Slot.Dark : skin, Shapes.Limb(a, b, 0.012f * scale, 0.005f * scale, 0f, 0.5f, 0.3f, 4, 6));
            }
        }

        /// <summary>Knuckle-like joint balls so limbs read as one body instead of separate segments.</summary>
        static void Joints(Blueprint bp, string side, float shoulder, float elbow, float hip, float knee, Slot slot = Slot.Skin)
        {
            bp.Add("UpperArm" + side, slot, Shapes.Blob(V3.Zero, new V3(shoulder, shoulder, shoulder), 6, 8));
            bp.Add("Forearm" + side, slot, Shapes.Blob(V3.Zero, new V3(elbow, elbow, elbow), 6, 8));
            bp.Add("Thigh" + side, slot, Shapes.Blob(V3.Zero, new V3(hip, hip, hip), 6, 8));
            bp.Add("Shin" + side, slot, Shapes.Blob(V3.Zero, new V3(knee, knee, knee), 6, 8));
        }

        /// <summary>Gaunt ribcage: an ellipsoid with rib ridges on the front and sides, sunken belly below.</summary>
        static MeshData Ribcage(V3 center, V3 radii, int ribs, float depth, int seed)
            => Shapes.Blob(center, radii, 16, 18, d =>
            {
                float band = (float)Math.Abs(Math.Sin(d.y * ribs * 1.6f));
                float ridge = d.z > -0.35f && d.y < 0.75f && d.y > -0.7f ? depth * (band * band * band - 0.3f) : 0f;
                float sternum = Math.Abs(d.x) < 0.12f && d.z > 0.5f ? -0.05f : 0f;
                return ridge + sternum + 0.03f * ArtNoise.Signed(d * 3f, seed);
            });

        static void SpineKnobs(Blueprint bp, string bone, float length, float back, int count, float size, Slot slot)
        {
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                bp.Add(bone, slot, Shapes.Blob(new V3(0, length * t, -back), new V3(size, size * 0.8f, size), 5, 6));
            }
        }

        static void Stitches(Blueprint bp, string bone, V3 center, float width, int count, float height, float tilt = 0f)
        {
            for (int i = 0; i < count; i++)
            {
                float x = center.x - width / 2f + width * i / Math.Max(1, count - 1);
                var c = new V3(x, center.y + (float)Math.Sin(i * 1.7f) * 0.003f, center.z - Math.Abs(x - center.x) * 0.25f);
                bp.Add(bone, Slot.Dark, Shapes.Box(c, new V3(0.006f, height, 0.008f)).Transformed(V3.Zero, Quat.Euler(0, 0, tilt * (i % 2 == 0 ? 1 : -1)), V3.One));
            }
            // The thread running across the stitches.
            bp.Add(bone, Slot.Dark, Shapes.Box(center + new V3(0, 0, 0.002f), new V3(width + 0.01f, 0.004f, 0.004f)));
        }

        static void Strand(Blueprint bp, string bone, V3 root, V3 dir, float length, float w0, float w1, Slot slot, int segments = 5, float stiffness = 0.15f)
            => bp.Strands.Add(new StrandDef { Bone = bone, Root = root, Direction = dir, Length = length, WidthRoot = w0, WidthTip = w1, Slot = slot, Segments = segments, Stiffness = stiffness });

        /// <summary>Standard biped skeleton (hips → spine → chest → neck → head, two arms, two legs).</summary>
        static void BipedSkeleton(Blueprint bp, float hipY, float spineLen, float chestLen, float neckLen, float shoulderW,
                                  float upperArm, float foreArm, float thigh, float shin, float hipW,
                                  V3 spineTilt, V3 chestTilt, V3 neckTilt, V3 headTilt, float armSpread, float armForward)
        {
            bp.AddBone("Hips", null, new V3(0, hipY, 0));
            bp.AddBone("Spine", "Hips", new V3(0, 0.06f, 0), spineTilt, spineLen);
            bp.AddBone("Chest", "Spine", new V3(0, spineLen, 0), chestTilt, chestLen);
            bp.AddBone("Neck", "Chest", new V3(0, chestLen, 0.01f), neckTilt, neckLen);
            bp.AddBone("Head", "Neck", new V3(0, neckLen, 0), headTilt);
            bp.AddBone("Jaw", "Head", new V3(0, 0.03f, 0.04f));
            float compensate = -(spineTilt.x + chestTilt.x);
            foreach (var side in new[] { "L", "R" })
            {
                float sx = side == "L" ? -1f : 1f;
                bp.AddBone("Shoulder" + side, "Chest", new V3(sx * shoulderW, chestLen * 0.86f, -0.01f));
                bp.AddBone("UpperArm" + side, "Shoulder" + side, V3.Zero, new V3(compensate + armForward, 0, sx * armSpread), upperArm);
                bp.AddBone("Forearm" + side, "UpperArm" + side, new V3(0, -upperArm, 0), new V3(-14, 0, 0), foreArm);
                bp.AddBone("Hand" + side, "Forearm" + side, new V3(0, -foreArm, 0), new V3(-6, 0, 0));
                bp.AddBone("Thigh" + side, "Hips", new V3(sx * hipW, -0.02f, 0), new V3(-12, 0, sx * 3), thigh);
                bp.AddBone("Shin" + side, "Thigh" + side, new V3(0, -thigh, 0), new V3(20, 0, 0), shin);
                bp.AddBone("Foot" + side, "Shin" + side, new V3(0, -shin, 0), new V3(-8, 0, 0));
                bp.Legs.Add(new LegDef { Upper = "Thigh" + side, Lower = "Shin" + side, End = "Foot" + side, Group = side == "L" ? 0 : 1, Pole = V3.Forward });
            }
            bp.Roles["hips"] = "Hips"; bp.Roles["spine"] = "Spine"; bp.Roles["chest"] = "Chest";
            bp.Roles["neck"] = "Neck"; bp.Roles["head"] = "Head"; bp.Roles["jaw"] = "Jaw";
            bp.Roles["armL"] = "UpperArmL"; bp.Roles["armR"] = "UpperArmR";
            bp.Roles["foreL"] = "ForearmL"; bp.Roles["foreR"] = "ForearmR";
            bp.Roles["handL"] = "HandL"; bp.Roles["handR"] = "HandR";
        }

        // =====================================================================================
        // المسكوت — The Silenced
        // =====================================================================================

        static Blueprint BuildMaskut(int seed)
        {
            var bp = new Blueprint { Id = Maskut, Height = 2.1f, StepLength = 0.55f, StepHeight = 0.1f };
            float var = 0.94f + 0.12f * ArtNoise.Rand(1, seed); // each villager slightly different
            BipedSkeleton(bp, 1.02f * var, 0.30f, 0.30f, 0.14f, 0.19f, 0.46f * var, 0.44f * var, 0.5f * var, 0.48f * var, 0.11f,
                new V3(14, 0, 0), new V3(16, 0, 0), new V3(20, 0, 0), new V3(-42, 0, 0), 7f, -8f);

            // Pelvis and belly.
            bp.Add("Hips", Slot.Skin, Shapes.Blob(new V3(0, 0.02f, 0), new V3(0.15f, 0.1f, 0.1f), 8, 12, d => 0.04f * ArtNoise.Signed(d * 2f, seed)));
            bp.Add("Spine", Slot.Skin, Shapes.Limb(V3.Zero, new V3(0, 0.32f, 0), 0.11f, 0.13f, -0.1f, 0.5f, 0.25f, 6, 12, 0.08f, seed));
            SpineKnobs(bp, "Spine", 0.3f, 0.095f, 4, 0.022f, Slot.Skin);
            bp.Add("Chest", Slot.Skin, Ribcage(new V3(0, 0.14f, 0.0f), new V3(0.18f, 0.19f, 0.125f), 6, 0.1f, seed));
            SpineKnobs(bp, "Chest", 0.28f, 0.12f, 4, 0.024f, Slot.Skin);
            // Collarbones.
            bp.Add("Chest", Slot.Skin, Shapes.Limb(new V3(-0.19f, 0.26f, 0.02f), new V3(0.19f, 0.26f, 0.02f), 0.018f, 0.018f, 0f, 0.5f, 0f, 6, 6));
            bp.Add("Neck", Slot.Skin, Shapes.Limb(new V3(0, -0.02f, 0), new V3(0, 0.15f, 0.01f), 0.052f, 0.044f, 0.05f, 0.5f, 0f, 5, 10));

            // Skull: long, eyeless (skin stretched over the sockets), hollow cheeks, brow ridge.
            bp.Add("Head", Slot.Skin, Shapes.Blob(new V3(0, 0.12f, 0.015f), new V3(0.105f, 0.15f, 0.12f), 14, 18, d =>
            {
                float sockets = 0f;
                if (d.z > 0.55f && d.y > 0.0f && d.y < 0.45f && Math.Abs(d.x) > 0.15f && Math.Abs(d.x) < 0.6f) sockets = -0.09f;
                float brow = d.z > 0.5f && d.y > 0.42f && d.y < 0.6f ? 0.05f : 0f;
                float cheek = d.z > 0.2f && d.y < -0.05f && d.y > -0.5f && Math.Abs(d.x) > 0.5f ? -0.08f : 0f;
                return sockets + brow + cheek + 0.025f * ArtNoise.Signed(d * 4f, seed + 3);
            }));
            bp.Add("Jaw", Slot.Skin, Shapes.Blob(new V3(0, -0.035f, 0.02f), new V3(0.072f, 0.048f, 0.075f), 8, 12));
            Stitches(bp, "Head", new V3(0, 0.012f, 0.128f), 0.075f, 6, 0.05f, 8f);
            // Ears — the only lit thing about them: they glow when they listen.
            bp.Add("Head", Slot.Skin, Shapes.Blob(new V3(-0.104f, 0.12f, 0f), new V3(0.016f, 0.042f, 0.028f), 6, 8));
            bp.Add("Head", Slot.Skin, Shapes.Blob(new V3(0.104f, 0.12f, 0f), new V3(0.016f, 0.042f, 0.028f), 6, 8));
            bp.Add("Head", Slot.Glow, Shapes.Blob(new V3(-0.115f, 0.12f, 0f), new V3(0.006f, 0.02f, 0.012f), 4, 6));
            bp.Add("Head", Slot.Glow, Shapes.Blob(new V3(0.115f, 0.12f, 0f), new V3(0.006f, 0.02f, 0.012f), 4, 6));
            for (int i = 0; i < 6; i++)
            {
                float a = -1.2f + i * 0.48f;
                Strand(bp, "Head", new V3((float)Math.Sin(a) * 0.09f, 0.2f, -0.06f + (float)Math.Cos(a) * 0.02f), new V3((float)Math.Sin(a) * 0.3f, -1f, -0.35f), 0.28f + 0.1f * ArtNoise.Rand(i, seed), 0.016f, 0.004f, Slot.Hair, 4);
            }

            foreach (var s in new[] { "L", "R" })
            {
                LimbPart(bp, "UpperArm" + s, bp.Find("UpperArm" + s).Length, 0.05f, 0.04f, Slot.Skin, 0.1f, 0.3f, 0.1f, 0.05f, seed);
                LimbPart(bp, "Forearm" + s, bp.Find("Forearm" + s).Length, 0.042f, 0.03f, Slot.Skin, 0.08f, 0.25f, 0.15f, 0.05f, seed);
                Hand(bp, "Hand" + s, 1.25f, 0.35f, true, Slot.Skin, 4, s == "R");
                LimbPart(bp, "Thigh" + s, bp.Find("Thigh" + s).Length, 0.07f, 0.05f, Slot.Skin, 0.1f, 0.3f, 0.05f, 0.05f, seed);
                LimbPart(bp, "Shin" + s, bp.Find("Shin" + s).Length, 0.05f, 0.035f, Slot.Skin, 0.12f, 0.22f, 0.1f, 0.05f, seed);
                Foot(bp, "Foot" + s, 1.05f, Slot.Skin, false);
                Joints(bp, s, 0.045f, 0.04f, 0.06f, 0.045f);
                // Knee caps stick out on the emaciated legs.
                bp.Add("Shin" + s, Slot.Skin, Shapes.Blob(new V3(0, -0.01f, 0.035f), new V3(0.04f, 0.04f, 0.03f), 6, 8));
            }

            // Torn wizar (loincloth) and shoulder rags.
            var skirt = new List<V3> { new V3(0, 0.08f, 0), new V3(0, -0.12f, 0.01f), new V3(0, -0.34f, 0.02f) };
            bp.Add("Hips", Slot.Cloth, Shapes.Tube(skirt, new List<float> { 0.165f, 0.19f, 0.22f }, new List<float> { 0.12f, 0.15f, 0.18f }, 14, false, false, 0.28f, seed));
            for (int i = 0; i < 5; i++)
            {
                float x = -0.16f + 0.08f * i;
                Strand(bp, "Hips", new V3(x, -0.3f, 0.12f - Math.Abs(x) * 0.3f), new V3(0, -1, 0.05f), 0.14f + 0.12f * ArtNoise.Rand(i + 10, seed), 0.05f, 0.02f, Slot.Cloth, 3, 0.25f);
            }
            Strand(bp, "Chest", new V3(-0.15f, 0.28f, -0.05f), new V3(0.1f, -1f, -0.2f), 0.45f, 0.07f, 0.03f, Slot.Cloth, 4, 0.2f);
            Strand(bp, "Chest", new V3(0.12f, 0.29f, -0.07f), new V3(-0.15f, -1f, -0.25f), 0.38f, 0.06f, 0.02f, Slot.Cloth, 4, 0.2f);
            return bp;
        }

        // =====================================================================================
        // السعلوة — The Si'luwa (quadruped)
        // =====================================================================================

        static Blueprint BuildSiluwa(int seed)
        {
            var bp = new Blueprint { Id = Siluwa, Height = 1.5f, StepLength = 0.6f, StepHeight = 0.12f };
            bp.AddBone("Hips", null, new V3(0, 0.8f, -0.35f));
            bp.AddBone("Spine", "Hips", new V3(0, 0.04f, 0), new V3(68, 0, 0), 0.42f);
            bp.AddBone("Chest", "Spine", new V3(0, 0.42f, 0), new V3(12, 0, 0), 0.34f);
            bp.AddBone("Neck", "Chest", new V3(0, 0.34f, 0), new V3(-45, 0, 0), 0.16f);
            bp.AddBone("Head", "Neck", new V3(0, 0.16f, 0), new V3(-30, 0, 0));
            bp.AddBone("Jaw", "Head", new V3(0, 0.0f, 0.06f));
            float chestTilt = 80f;
            foreach (var s in new[] { "L", "R" })
            {
                float sx = s == "L" ? -1f : 1f;
                // Front legs (her arms).
                bp.AddBone("Shoulder" + s, "Chest", new V3(sx * 0.15f, 0.28f, 0.02f));
                bp.AddBone("UpperArm" + s, "Shoulder" + s, V3.Zero, new V3(-chestTilt + 18f, 0, sx * 10f), 0.5f);
                bp.AddBone("Forearm" + s, "UpperArm" + s, new V3(0, -0.5f, 0), new V3(-34, 0, 0), 0.5f);
                bp.AddBone("Hand" + s, "Forearm" + s, new V3(0, -0.5f, 0), new V3(30, 0, 0));
                // Hind legs, folded like a crouching animal.
                bp.AddBone("Thigh" + s, "Hips", new V3(sx * 0.13f, -0.02f, 0), new V3(-40, 0, sx * 6f), 0.46f);
                bp.AddBone("Shin" + s, "Thigh" + s, new V3(0, -0.46f, 0), new V3(75, 0, 0), 0.46f);
                bp.AddBone("Foot" + s, "Shin" + s, new V3(0, -0.46f, 0), new V3(-35, 0, 0));
            }
            // Diagonal gait: left-hind with right-front.
            bp.Legs.Add(new LegDef { Upper = "ThighL", Lower = "ShinL", End = "FootL", Group = 0, Pole = V3.Forward });
            bp.Legs.Add(new LegDef { Upper = "ThighR", Lower = "ShinR", End = "FootR", Group = 1, Pole = V3.Forward });
            bp.Legs.Add(new LegDef { Upper = "UpperArmL", Lower = "ForearmL", End = "HandL", Group = 1, Pole = -V3.Forward });
            bp.Legs.Add(new LegDef { Upper = "UpperArmR", Lower = "ForearmR", End = "HandR", Group = 0, Pole = -V3.Forward });
            bp.Roles["hips"] = "Hips"; bp.Roles["spine"] = "Spine"; bp.Roles["chest"] = "Chest";
            bp.Roles["neck"] = "Neck"; bp.Roles["head"] = "Head"; bp.Roles["jaw"] = "Jaw";
            bp.Roles["quadruped"] = "Hips";

            bp.Add("Hips", Slot.Skin, Shapes.Blob(new V3(0, 0.02f, 0), new V3(0.15f, 0.12f, 0.13f), 8, 12));
            bp.Add("Spine", Slot.Skin, Shapes.Limb(V3.Zero, new V3(0, 0.44f, 0), 0.11f, 0.12f, -0.15f, 0.5f, 0.2f, 7, 12, 0.06f, seed));
            SpineKnobs(bp, "Spine", 0.42f, 0.1f, 6, 0.024f, Slot.Skin);
            bp.Add("Chest", Slot.Skin, Ribcage(new V3(0, 0.17f, 0.01f), new V3(0.16f, 0.21f, 0.15f), 7, 0.06f, seed));
            SpineKnobs(bp, "Chest", 0.32f, 0.14f, 4, 0.026f, Slot.Skin);
            bp.Add("Neck", Slot.Skin, Shapes.Limb(V3.Zero, new V3(0, 0.17f, 0), 0.06f, 0.05f, 0f, 0.5f, 0f, 5, 10));
            bp.Add("Head", Slot.Skin, Shapes.Blob(new V3(0, 0.08f, 0.04f), new V3(0.1f, 0.12f, 0.13f), 12, 16, d => 0.03f * ArtNoise.Signed(d * 4f, seed + 2)));
            bp.Add("Jaw", Slot.Skin, Shapes.Blob(new V3(0, -0.02f, 0.06f), new V3(0.07f, 0.04f, 0.09f), 8, 10));
            // Teeth she cannot hide: a row of needles under the hair.
            for (int i = 0; i < 9; i++)
            {
                float x = -0.05f + i * 0.0125f;
                bp.Add("Jaw", Slot.Shell, Shapes.Limb(new V3(x, 0.0f, 0.13f - Math.Abs(x) * 0.4f), new V3(x, 0.03f, 0.14f - Math.Abs(x) * 0.4f), 0.004f, 0.001f, 0f, 0.5f, 0f, 3, 4));
            }
            // Two pale slits where eyes should be (she is blind too — they glow, they do not see).
            bp.Add("Head", Slot.Glow, Shapes.Blob(new V3(-0.045f, 0.11f, 0.155f), new V3(0.03f, 0.006f, 0.01f), 4, 6));
            bp.Add("Head", Slot.Glow, Shapes.Blob(new V3(0.045f, 0.11f, 0.155f), new V3(0.03f, 0.006f, 0.01f), 4, 6));
            // Hair: a heavy mass on the skull plus long dynamic locks that hide the face.
            bp.Add("Head", Slot.Hair, Shapes.Blob(new V3(0, 0.14f, -0.01f), new V3(0.125f, 0.12f, 0.14f), 10, 12, d => d.y < -0.2f ? 0.1f : 0.04f * ArtNoise.Signed(d * 6f, seed)));
            for (int i = 0; i < 12; i++)
            {
                float a = (i / 11f - 0.5f) * 2.6f;
                var root = new V3((float)Math.Sin(a) * 0.11f, 0.19f, 0.06f + (float)Math.Cos(a) * 0.05f);
                float len = 0.55f + 0.35f * ArtNoise.Rand(i, seed + 5);
                Strand(bp, "Head", root, new V3((float)Math.Sin(a) * 0.25f, -1f, 0.35f), len, 0.035f, 0.008f, Slot.Hair, 6, 0.08f);
            }
            foreach (var s in new[] { "L", "R" })
            {
                LimbPart(bp, "UpperArm" + s, 0.5f, 0.05f, 0.038f, Slot.Skin, 0.12f, 0.3f, 0.1f, 0.05f, seed);
                LimbPart(bp, "Forearm" + s, 0.5f, 0.038f, 0.026f, Slot.Skin, 0.1f, 0.2f, 0.15f, 0.05f, seed);
                Hand(bp, "Hand" + s, 1.2f, 0.55f, true, Slot.Skin, 4, s == "R");
                LimbPart(bp, "Thigh" + s, 0.46f, 0.08f, 0.05f, Slot.Skin, 0.18f, 0.3f, 0.1f, 0.05f, seed);
                LimbPart(bp, "Shin" + s, 0.46f, 0.045f, 0.03f, Slot.Skin, 0.1f, 0.2f, 0.1f, 0.05f, seed);
                Foot(bp, "Foot" + s, 1.1f, Slot.Skin, true);
                Joints(bp, s, 0.05f, 0.04f, 0.075f, 0.05f);
            }
            // A scrap of an old abaya caught on her back.
            Strand(bp, "Chest", new V3(0, 0.1f, -0.15f), new V3(0.1f, -1f, -0.3f), 0.5f, 0.18f, 0.08f, Slot.Cloth, 4, 0.2f);
            return bp;
        }

        // =====================================================================================
        // بو درياه — Bu Daryah (rooted sentinel)
        // =====================================================================================

        static Blueprint BuildBuDaryah(int seed)
        {
            var bp = new Blueprint { Id = BuDaryah, Height = 3.4f };
            bp.AddBone("Base", null, V3.Zero);
            bp.AddBone("Spine", "Base", new V3(0, 0.25f, 0), new V3(6, 0, 0), 1.25f);
            bp.AddBone("Chest", "Spine", new V3(0, 1.25f, 0), new V3(10, 0, 0), 0.7f);
            bp.AddBone("Head", "Chest", new V3(0, 0.62f, 0.3f), new V3(14, 0, 0));
            bp.AddBone("Jaw", "Head", new V3(0, -0.12f, 0.12f));
            bp.AddBone("FinL", "Head", new V3(-0.42f, 0.08f, -0.05f), new V3(0, 0, 35));
            bp.AddBone("FinR", "Head", new V3(0.42f, 0.08f, -0.05f), new V3(0, 0, -35));
            foreach (var s in new[] { "L", "R" })
            {
                float sx = s == "L" ? -1f : 1f;
                bp.AddBone("Shoulder" + s, "Chest", new V3(sx * 0.72f, 0.45f, 0));
                bp.AddBone("UpperArm" + s, "Shoulder" + s, V3.Zero, new V3(-30, 0, sx * 40f), 1.0f);
                bp.AddBone("Forearm" + s, "UpperArm" + s, new V3(0, -1.0f, 0), new V3(-50, 0, 0), 0.95f);
                bp.AddBone("Hand" + s, "Forearm" + s, new V3(0, -0.95f, 0), new V3(-10, 0, 0));
            }
            bp.Roles["spine"] = "Spine"; bp.Roles["chest"] = "Chest"; bp.Roles["head"] = "Head"; bp.Roles["jaw"] = "Jaw";
            bp.Roles["finL"] = "FinL"; bp.Roles["finR"] = "FinR";
            bp.Roles["armL"] = "UpperArmL"; bp.Roles["armR"] = "UpperArmR"; bp.Roles["foreL"] = "ForearmL"; bp.Roles["foreR"] = "ForearmR";

            // Mound where the body grows out of the ground.
            bp.Add("Base", Slot.Skin, Shapes.Blob(new V3(0, 0.08f, 0), new V3(1.35f, 0.42f, 1.15f), 10, 18, d => 0.12f * ArtNoise.Signed(d * 3f, seed)));
            bp.Add("Spine", Slot.Skin, Shapes.Limb(new V3(0, -0.1f, 0), new V3(0, 1.35f, 0), 0.78f, 0.86f, 0.18f, 0.6f, 0.18f, 8, 16, 0.22f, seed));
            bp.Add("Chest", Slot.Skin, Shapes.Blob(new V3(0, 0.3f, -0.05f), new V3(0.92f, 0.62f, 0.76f), 12, 16, d =>
                (d.z < -0.3f && d.y > 0f ? 0.18f : 0f) + 0.16f * ArtNoise.Signed(d * 3f, seed + 1)));
            // Skin folds where the neck should be.
            for (int i = 0; i < 3; i++)
                bp.Add("Chest", Slot.Skin, Shapes.Tube(new List<V3> { new V3(0, 0.62f + i * 0.08f, 0.12f + i * 0.05f), new V3(0, 0.66f + i * 0.08f, 0.14f + i * 0.05f) },
                    new List<float> { 0.62f - i * 0.06f, 0.6f - i * 0.06f }, new List<float> { 0.52f - i * 0.05f, 0.5f - i * 0.05f }, 16, true, true, 0.05f, seed + i));
            // Wide frog-like head with a slack jaw and a glowing throat sac.
            bp.Add("Head", Slot.Skin, Shapes.Blob(new V3(0, 0.05f, 0.05f), new V3(0.55f, 0.36f, 0.5f), 12, 16, d => d.z > 0.6f && d.y > 0.1f ? -0.06f : 0.04f * ArtNoise.Signed(d * 5f, seed)));
            bp.Add("Jaw", Slot.Skin, Shapes.Blob(new V3(0, -0.1f, 0.05f), new V3(0.5f, 0.14f, 0.42f), 10, 14));
            bp.Add("Jaw", Slot.Glow, Shapes.Blob(new V3(0, -0.2f, 0.12f), new V3(0.26f, 0.16f, 0.24f), 8, 10));
            // Dead, milky eyes on top of the head — he does not use them.
            bp.Add("Head", Slot.Shell, Shapes.Blob(new V3(-0.24f, 0.3f, 0.3f), new V3(0.085f, 0.07f, 0.07f), 8, 10));
            bp.Add("Head", Slot.Shell, Shapes.Blob(new V3(0.24f, 0.3f, 0.3f), new V3(0.085f, 0.07f, 0.07f), 8, 10));
            bp.Add("Head", Slot.Skin, Shapes.Blob(new V3(-0.24f, 0.34f, 0.29f), new V3(0.1f, 0.05f, 0.08f), 6, 8));
            bp.Add("Head", Slot.Skin, Shapes.Blob(new V3(0.24f, 0.34f, 0.29f), new V3(0.1f, 0.05f, 0.08f), 6, 8));
            for (int i = 0; i < 14; i++)
            {
                float x = -0.4f + i * (0.8f / 13f);
                bp.Add("Jaw", Slot.Shell, Shapes.Limb(new V3(x, -0.02f, 0.4f - x * x * 0.5f), new V3(x, 0.07f, 0.42f - x * x * 0.5f), 0.018f, 0.002f, 0f, 0.5f, 0f, 3, 5));
            }
            foreach (var fin in new[] { "FinL", "FinR" })
            {
                float sx = fin == "FinL" ? -1f : 1f;
                bp.Add(fin, Slot.Dark, Shapes.Blob(new V3(sx * 0.32f, 0.2f, -0.05f), new V3(0.34f, 0.32f, 0.03f), 8, 10, d => 0.15f * (float)Math.Sin(Math.Atan2(d.y, d.x) * 6)));
            }
            // Barnacles and kelp.
            for (int i = 0; i < 26; i++)
            {
                float a = ArtNoise.Rand(i, seed) * 6.283f, h = ArtNoise.Rand(i + 40, seed);
                var p = new V3((float)Math.Cos(a) * 0.82f, h * 1.3f, (float)Math.Sin(a) * 0.7f);
                float r = 0.04f + 0.05f * ArtNoise.Rand(i + 80, seed);
                bp.Add("Spine", Slot.Shell, Shapes.Blob(p, new V3(r, r * 0.6f, r), 5, 7, d => d.y > 0.6f ? -0.5f : 0f));
            }
            for (int i = 0; i < 10; i++)
            {
                float a = i * 0.628f;
                Strand(bp, "Chest", new V3((float)Math.Cos(a) * 0.7f, 0.65f, (float)Math.Sin(a) * 0.55f), new V3((float)Math.Cos(a) * 0.3f, -1f, (float)Math.Sin(a) * 0.3f), 0.9f + 0.6f * ArtNoise.Rand(i, seed + 9), 0.12f, 0.04f, Slot.Hair, 6, 0.1f);
            }
            for (int i = 0; i < 5; i++)
                Strand(bp, "Head", new V3(-0.3f + 0.15f * i, 0.32f, -0.15f), new V3(0, -1f, -0.6f), 0.7f, 0.08f, 0.02f, Slot.Hair, 5, 0.1f);
            foreach (var s in new[] { "L", "R" })
            {
                LimbPart(bp, "UpperArm" + s, 1.0f, 0.2f, 0.15f, Slot.Skin, 0.2f, 0.3f, 0.1f, 0.1f, seed);
                LimbPart(bp, "Forearm" + s, 0.95f, 0.15f, 0.11f, Slot.Skin, 0.15f, 0.25f, 0.15f, 0.1f, seed);
                Hand(bp, "Hand" + s, 3.2f, 0.4f, true, Slot.Skin, 4, s == "R");
                // Webbing between the fingers.
                bp.Add("Hand" + s, Slot.Dark, Shapes.Blob(new V3(0, -0.36f, 0.02f), new V3(0.14f, 0.16f, 0.012f), 6, 8));
            }
            return bp;
        }

        // =====================================================================================
        // الصمّة — Al-Summah, the Heart of the Hush
        // =====================================================================================

        static Blueprint BuildSummah(int seed)
        {
            var bp = new Blueprint { Id = Summah, Height = 4.2f };
            bp.AddBone("Core", null, V3.Zero);
            bp.AddBone("Mass", "Core", new V3(0, 2.3f, 0));
            bp.AddBone("Ring", "Mass", V3.Zero);
            bp.AddBone("Crown", "Mass", new V3(0, 1.2f, 0));
            bp.Roles["chest"] = "Mass"; bp.Roles["ring"] = "Ring"; bp.Roles["head"] = "Crown";

            bp.Add("Mass", Slot.Dark, Shapes.Blob(V3.Zero, new V3(1.3f, 1.2f, 1.3f), 18, 22, d =>
                0.22f * ArtNoise.Signed(d * 2.2f, seed) + 0.08f * ArtNoise.Signed(d * 7f, seed + 1)));
            // The glow inside shows through cracks.
            bp.Add("Mass", Slot.Glow, Shapes.Blob(new V3(0, 0, 0.15f), new V3(0.9f, 0.85f, 0.95f), 10, 12));
            for (int i = 0; i < 16; i++)
            {
                float a = ArtNoise.Rand(i, seed) * 6.283f, b = (ArtNoise.Rand(i + 9, seed) - 0.5f) * 1.8f;
                V3 dir = new V3((float)(Math.Cos(a) * Math.Cos(b)), (float)Math.Sin(b), (float)(Math.Sin(a) * Math.Cos(b)));
                var tangent = V3.Cross(dir, V3.Up).Normalized;
                var bit = V3.Cross(dir, tangent).Normalized;
                var crack = new List<V3>();
                for (int k = 0; k < 5; k++)
                {
                    float t = k / 4f - 0.5f;
                    V3 q = (dir + tangent * (t * 0.5f) + bit * (0.12f * (float)Math.Sin(k * 2.1f + i))).Normalized;
                    crack.Add(V3.Scale(q, new V3(1.36f, 1.27f, 1.36f)));
                }
                bp.Add("Mass", Slot.Glow, Shapes.Tube(crack, new List<float> { 0.01f, 0.05f, 0.07f, 0.04f, 0.01f }, new List<float> { 0.01f, 0.03f, 0.04f, 0.03f, 0.01f }, 6, true, true));
            }
            // Ring of stolen mouths, each with its own lips (animated open/closed).
            for (int i = 0; i < 9; i++)
            {
                float yaw = i * 40f;
                string m = "Mouth" + i;
                bp.AddBone(m, "Ring", Quat.Euler(0, yaw, 0) * new V3(0, (float)Math.Sin(i * 1.9f) * 0.35f, 1.85f), new V3(0, yaw, 0));
                bp.AddBone(m + "Up", m, new V3(0, 0.03f, 0));
                bp.AddBone(m + "Lo", m, new V3(0, -0.03f, 0));
                bp.Add(m + "Up", Slot.Skin, Shapes.Blob(new V3(0, 0.05f, 0), new V3(0.24f, 0.08f, 0.14f), 8, 12));
                bp.Add(m + "Lo", Slot.Skin, Shapes.Blob(new V3(0, -0.05f, 0), new V3(0.22f, 0.08f, 0.13f), 8, 12));
                bp.Add(m, Slot.Dark, Shapes.Blob(new V3(0, 0, -0.03f), new V3(0.2f, 0.06f, 0.1f), 6, 8));
                Stitches(bp, m, new V3(0, 0, 0.13f), 0.3f, 7, 0.17f, 6f);
                // Tendon tethering each mouth to the mass.
                bp.Add(m, Slot.Dark, Shapes.Limb(new V3(0, 0, -0.1f), new V3(0, -(float)Math.Sin(i * 1.9f) * 0.35f * 0.6f, -0.75f), 0.06f, 0.12f, 0.1f, 0.5f, 0.2f, 5, 8, 0.2f, seed + i));
            }
            // Tendrils dragging on the deck.
            for (int i = 0; i < 12; i++)
            {
                float a = i * 0.5236f;
                Strand(bp, "Mass", new V3((float)Math.Cos(a) * 1.0f, -0.9f, (float)Math.Sin(a) * 1.0f), new V3((float)Math.Cos(a) * 0.35f, -1f, (float)Math.Sin(a) * 0.35f), 1.25f + 0.15f * ArtNoise.Rand(i, seed), 0.16f, 0.03f, Slot.Dark, 7, 0.06f);
            }
            return bp;
        }

        // =====================================================================================
        // عبدالله النهّام — the grandfather's echo (seated, hand cupped to his ear as nahhams sing)
        // =====================================================================================

        static Blueprint BuildNahham(int seed)
        {
            var bp = new Blueprint { Id = Nahham, Height = 1.2f };
            bp.AddBone("Hips", null, new V3(0, 0.2f, 0));
            bp.AddBone("Spine", "Hips", new V3(0, 0.05f, 0), new V3(4, 0, 0), 0.25f);
            bp.AddBone("Chest", "Spine", new V3(0, 0.25f, 0), new V3(6, 0, 0), 0.26f);
            bp.AddBone("Neck", "Chest", new V3(0, 0.27f, 0.01f), new V3(-4, 0, 0), 0.1f);
            bp.AddBone("Head", "Neck", new V3(0, 0.1f, 0), new V3(-12, 0, 12));
            bp.AddBone("Jaw", "Head", new V3(0, 0.02f, 0.05f));
            // Right hand to the right ear; left hand resting on the knee.
            bp.AddBone("ShoulderR", "Chest", new V3(0.19f, 0.23f, 0));
            bp.AddBone("UpperArmR", "ShoulderR", V3.Zero, new V3(-10, -10, 125), 0.29f);
            bp.AddBone("ForearmR", "UpperArmR", new V3(0, -0.29f, 0), new V3(0, 0, -150), 0.27f);
            bp.AddBone("HandR", "ForearmR", new V3(0, -0.27f, 0), new V3(0, 90, 0));
            bp.AddBone("ShoulderL", "Chest", new V3(-0.19f, 0.23f, 0));
            bp.AddBone("UpperArmL", "ShoulderL", V3.Zero, new V3(-35, 0, -12), 0.29f);
            bp.AddBone("ForearmL", "UpperArmL", new V3(0, -0.29f, 0), new V3(-45, 0, 0), 0.27f);
            bp.AddBone("HandL", "ForearmL", new V3(0, -0.27f, 0), new V3(-20, 0, 0));
            // Crossed legs.
            foreach (var s in new[] { "L", "R" })
            {
                float sx = s == "L" ? -1f : 1f;
                bp.AddBone("Thigh" + s, "Hips", new V3(sx * 0.1f, 0, 0.02f), new V3(-85, sx * 35f, 0), 0.42f);
                bp.AddBone("Shin" + s, "Thigh" + s, new V3(0, -0.42f, 0), new V3(0, 0, sx * -140f), 0.4f);
                bp.AddBone("Foot" + s, "Shin" + s, new V3(0, -0.4f, 0));
            }
            bp.Roles["spine"] = "Spine"; bp.Roles["chest"] = "Chest"; bp.Roles["head"] = "Head"; bp.Roles["jaw"] = "Jaw";
            bp.Roles["armR"] = "UpperArmR"; bp.Roles["armL"] = "UpperArmL";
            // Elbow out and forward, hand cupped behind the right ear (how a nahham sings over the sea).
            bp.Aim("UpperArmR", new V3(0.75f, 0.05f, 0.62f));
            bp.Aim("ForearmR", new V3(-0.55f, 0.72f, -0.42f));
            bp.Aim("HandR", new V3(-0.35f, 0.9f, -0.2f));
            // Left hand rests on the knee.
            bp.Aim("UpperArmL", new V3(-0.3f, -0.85f, 0.42f));
            bp.Aim("ForearmL", new V3(-0.1f, -0.45f, 1f));
            bp.Aim("HandL", new V3(0f, -0.7f, 0.7f));
            // Crossed legs.
            bp.Aim("ThighL", new V3(-0.78f, -0.1f, 0.62f));
            bp.Aim("ShinL", new V3(0.85f, -0.12f, 0.35f));
            bp.Aim("ThighR", new V3(0.78f, -0.05f, 0.62f));
            bp.Aim("ShinR", new V3(-0.85f, -0.05f, 0.4f));

            // Thobe body + a bisht (cloak) over the shoulders.
            bp.Add("Chest", Slot.Ghost, Shapes.Limb(new V3(0, -0.02f, 0), new V3(0, 0.27f, 0), 0.17f, 0.19f, 0.05f, 0.6f, 0.3f, 6, 14));
            bp.Add("Spine", Slot.Ghost, Shapes.Limb(new V3(0, -0.06f, 0), new V3(0, 0.27f, 0), 0.2f, 0.17f, 0.1f, 0.2f, 0.3f, 6, 14));
            bp.Add("Hips", Slot.Ghost, Shapes.Blob(new V3(0, -0.08f, 0.14f), new V3(0.44f, 0.13f, 0.34f), 10, 18, d => 0.08f * ArtNoise.Signed(d * 4f, seed)));
            bp.Add("Chest", Slot.Ghost, Shapes.Tube(new List<V3> { new V3(0, 0.26f, -0.01f), new V3(0, 0.1f, -0.04f), new V3(0, -0.3f, -0.07f) },
                new List<float> { 0.2f, 0.25f, 0.3f }, new List<float> { 0.13f, 0.19f, 0.25f }, 16, false, false, 0.05f, seed + 1));
            bp.Add("Neck", Slot.Ghost, Shapes.Limb(V3.Zero, new V3(0, 0.11f, 0), 0.05f, 0.045f, 0f, 0.5f, 0f, 4, 8));
            bp.Add("Head", Slot.Ghost, Shapes.Blob(new V3(0, 0.1f, 0.01f), new V3(0.085f, 0.11f, 0.1f), 10, 14));
            // Ghutra (head cloth) draped down the back, agal ring, and a beard.
            bp.Add("Head", Slot.Ghost, Shapes.Blob(new V3(0, 0.15f, -0.005f), new V3(0.105f, 0.09f, 0.115f), 8, 12));
            bp.Add("Head", Slot.Ghost, Shapes.Tube(new List<V3> { new V3(0, 0.19f, -0.01f), new V3(0, 0.2f, -0.01f) }, new List<float> { 0.1f, 0.1f }, new List<float> { 0.105f, 0.105f }, 14, false, false));
            Strand(bp, "Head", new V3(0, 0.12f, -0.1f), new V3(0, -1, -0.3f), 0.35f, 0.2f, 0.24f, Slot.Ghost, 4, 0.3f);
            Strand(bp, "Head", new V3(-0.1f, 0.12f, -0.02f), new V3(-0.2f, -1, 0f), 0.3f, 0.07f, 0.09f, Slot.Ghost, 4, 0.3f);
            Strand(bp, "Head", new V3(0.1f, 0.12f, -0.02f), new V3(0.2f, -1, 0f), 0.3f, 0.07f, 0.09f, Slot.Ghost, 4, 0.3f);
            bp.Add("Jaw", Slot.Ghost, Shapes.Blob(new V3(0, -0.05f, 0.02f), new V3(0.06f, 0.07f, 0.05f), 8, 10, d => d.y < -0.3f ? 0.25f : 0f));
            foreach (var s in new[] { "L", "R" })
            {
                LimbPart(bp, "UpperArm" + s, 0.29f, 0.06f, 0.055f, Slot.Ghost, 0.05f, 0.5f, 0f);
                LimbPart(bp, "Forearm" + s, 0.27f, 0.055f, 0.05f, Slot.Ghost, 0.03f, 0.5f, 0f);
                Hand(bp, "Hand" + s, 1f, s == "R" ? 0.6f : 0.3f, false, Slot.Ghost, 4, s == "R");
                LimbPart(bp, "Thigh" + s, 0.42f, 0.09f, 0.075f, Slot.Ghost, 0.05f, 0.4f, 0f);
                LimbPart(bp, "Shin" + s, 0.4f, 0.07f, 0.055f, Slot.Ghost, 0.05f, 0.4f, 0f);
                Foot(bp, "Foot" + s, 0.9f, Slot.Ghost, false);
            }
            return bp;
        }

        // =====================================================================================
        // مبارك — Mubarak at the lighthouse (seen from afar: thobe, ghutra, raised lantern)
        // =====================================================================================

        static Blueprint BuildMubarak(int seed)
        {
            var bp = new Blueprint { Id = Mubarak, Height = 1.8f, StepLength = 0.45f };
            BipedSkeleton(bp, 0.95f, 0.28f, 0.3f, 0.1f, 0.19f, 0.3f, 0.28f, 0.47f, 0.46f, 0.1f,
                new V3(2, 0, 0), new V3(4, 0, 0), new V3(-2, 0, 0), new V3(-4, 0, 0), 6f, 0f);
            // Raise the right arm with the lantern.
            bp.Aim("UpperArmR", new V3(0.55f, 0.45f, 0.45f));
            bp.Aim("ForearmR", new V3(0.1f, 1f, 0.15f));
            bp.Roles["lantern"] = "HandR";
            var thobe = new List<V3> { new V3(0, 0.6f, 0), new V3(0, 0.2f, 0), new V3(0, -0.4f, 0.01f), new V3(0, -0.92f, 0.02f) };
            bp.Add("Hips", Slot.Cloth, Shapes.Tube(thobe, new List<float> { 0.19f, 0.2f, 0.23f, 0.27f }, new List<float> { 0.13f, 0.15f, 0.18f, 0.21f }, 16, false, false, 0.04f, seed));
            bp.Add("Neck", Slot.Skin, Shapes.Limb(V3.Zero, new V3(0, 0.1f, 0), 0.05f, 0.045f, 0f, 0.5f, 0f, 4, 8));
            bp.Add("Head", Slot.Skin, Shapes.Blob(new V3(0, 0.1f, 0.01f), new V3(0.09f, 0.11f, 0.1f), 10, 14));
            bp.Add("Head", Slot.Cloth, Shapes.Blob(new V3(0, 0.15f, -0.005f), new V3(0.11f, 0.09f, 0.12f), 8, 12));
            Strand(bp, "Head", new V3(0, 0.12f, -0.1f), new V3(0, -1, -0.2f), 0.4f, 0.2f, 0.26f, Slot.Cloth, 4, 0.3f);
            Strand(bp, "Chest", new V3(0, 0.25f, -0.12f), new V3(0, -1, -0.15f), 0.9f, 0.4f, 0.5f, Slot.Dark, 5, 0.3f); // bisht
            foreach (var s in new[] { "L", "R" })
            {
                LimbPart(bp, "UpperArm" + s, 0.3f, 0.06f, 0.055f, Slot.Cloth, 0.04f);
                LimbPart(bp, "Forearm" + s, 0.28f, 0.055f, 0.05f, Slot.Cloth, 0.03f);
                Hand(bp, "Hand" + s, 1f, s == "R" ? 0.8f : 0.3f, false, Slot.Skin, 4, s == "R");
                LimbPart(bp, "Shin" + s, 0.46f, 0.05f, 0.045f, Slot.Skin, 0.05f);
                Foot(bp, "Foot" + s, 0.95f, Slot.Dark, false);
            }
            return bp;
        }

        // =====================================================================================
        // ريم — the player's own arms (first person). ViewRoot = camera.
        // =====================================================================================

        static Blueprint BuildReemArms(int seed)
        {
            var bp = new Blueprint { Id = ReemArms, Height = 0.6f };
            bp.AddBone("ViewRoot", null, V3.Zero);
            // Forearms reach forward from below the view: local -Y mapped to +Z by -90° pitch.
            bp.AddBone("ArmL", "ViewRoot", new V3(-0.19f, -0.13f, -0.08f), new V3(-88, 14, -6), 0.34f);
            bp.AddBone("HandL", "ArmL", new V3(0, -0.34f, 0), new V3(-10, 0, 0));
            bp.AddBone("ArmR", "ViewRoot", new V3(0.2f, -0.14f, -0.09f), new V3(-87, -16, 6), 0.34f);
            bp.AddBone("HandR", "ArmR", new V3(0, -0.34f, 0), new V3(-12, 0, 0));
            bp.AddBone("PropL", "HandL", new V3(0, -0.1f, 0.03f), new V3(85, 0, 0));
            bp.AddBone("PropR", "HandR", new V3(0, -0.1f, 0.035f), new V3(80, 0, 0));
            bp.Roles["armL"] = "ArmL"; bp.Roles["armR"] = "ArmR"; bp.Roles["handL"] = "HandL"; bp.Roles["handR"] = "HandR";
            bp.Roles["propL"] = "PropL"; bp.Roles["propR"] = "PropR";
            foreach (var s in new[] { "L", "R" })
            {
                // Black abaya sleeve over the forearm, then the wrist and hand.
                bp.Add("Arm" + s, Slot.Cloth, Shapes.Limb(new V3(0, 0.05f, 0), new V3(0, -0.29f, 0), 0.06f, 0.05f, 0.04f, 0.4f, 0.15f, 6, 12, 0.06f, seed));
                bp.Add("Arm" + s, Slot.Skin, Shapes.Limb(new V3(0, -0.26f, 0), new V3(0, -0.345f, 0), 0.03f, 0.028f, 0f, 0.5f, 0.2f, 4, 10));
                Hand(bp, "Hand" + s, 0.95f, 0.75f, false, Slot.Skin, 4, s == "R");
            }
            return bp;
        }

        // =====================================================================================
        // Props
        // =====================================================================================

        static Blueprint BuildLantern()
        {
            var bp = new Blueprint { Id = PropLantern, Height = 0.3f };
            bp.AddBone("Root", null, V3.Zero);
            bp.AddBone("Flame", "Root", new V3(0, -0.13f, 0));
            bp.Roles["flame"] = "Flame";
            bp.Add("Root", Slot.Metal, Shapes.Tube(new List<V3> { new V3(0, 0.0f, 0), new V3(0, 0.02f, 0) }, new List<float> { 0.012f, 0.012f }, new List<float> { 0.06f, 0.06f }, 8)); // handle
            bp.Add("Root", Slot.Metal, Shapes.Limb(new V3(0, -0.02f, 0), new V3(0, -0.05f, 0), 0.03f, 0.055f, 0f, 0.5f, 0f, 4, 10));
            bp.Add("Root", Slot.Glow, Shapes.Limb(new V3(0, -0.05f, 0), new V3(0, -0.2f, 0), 0.05f, 0.05f, 0.25f, 0.5f, 0f, 6, 12));
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.5708f + 0.785f;
                var x = new V3((float)Math.Cos(a) * 0.055f, 0, (float)Math.Sin(a) * 0.055f);
                bp.Add("Root", Slot.Metal, Shapes.Limb(x + new V3(0, -0.05f, 0), x + new V3(0, -0.2f, 0), 0.005f, 0.005f, 0f, 0.5f, 0f, 3, 4));
            }
            bp.Add("Root", Slot.Metal, Shapes.Limb(new V3(0, -0.2f, 0), new V3(0, -0.23f, 0), 0.06f, 0.05f, 0f, 0.5f, 0.3f, 3, 10));
            return bp;
        }

        static Blueprint BuildShell(int seed)
        {
            var bp = new Blueprint { Id = PropShell, Height = 0.18f };
            bp.AddBone("Root", null, V3.Zero);
            // Spiral conch: a tapering tube wound around an axis.
            var pts = new List<V3>();
            var r = new List<float>();
            for (int i = 0; i < 18; i++)
            {
                float t = i / 17f;
                float a = t * 9.5f;
                float rad = 0.055f * (1f - t * 0.85f);
                pts.Add(new V3((float)Math.Cos(a) * rad, -0.02f - t * 0.14f, (float)Math.Sin(a) * rad));
                r.Add(0.05f * (float)Math.Pow(1f - t, 1.3f) + 0.004f);
            }
            bp.Add("Root", Slot.Shell, Shapes.Tube(pts, r, r, 10, true, true, 0.05f, seed));
            bp.Add("Root", Slot.Glow, Shapes.Blob(new V3(0.04f, -0.01f, 0.01f), new V3(0.03f, 0.012f, 0.03f), 6, 8));
            return bp;
        }

        static Blueprint BuildDrum()
        {
            var bp = new Blueprint { Id = PropDrum, Height = 0.4f };
            bp.AddBone("Root", null, V3.Zero);
            bp.AddBone("Skin", "Root", V3.Zero);
            bp.Roles["skin"] = "Skin";
            // Frame drum (طار): a wide wooden ring with a hide membrane and small cymbals.
            var ring = new List<V3> { new V3(0, 0.03f, 0), new V3(0, -0.03f, 0) };
            bp.Add("Root", Slot.Wood, Shapes.Tube(ring, new List<float> { 0.2f, 0.2f }, new List<float> { 0.2f, 0.2f }, 24, false, false));
            bp.Add("Root", Slot.Wood, Shapes.Tube(ring, new List<float> { 0.185f, 0.185f }, new List<float> { 0.185f, 0.185f }, 24, false, false).Transformed(V3.Zero, Quat.Euler(180, 0, 0), V3.One));
            bp.Add("Skin", Slot.Skin, Shapes.Blob(new V3(0, 0.028f, 0), new V3(0.19f, 0.004f, 0.19f), 4, 24));
            for (int i = 0; i < 4; i++)
            {
                float a = i * 1.5708f;
                bp.Add("Root", Slot.Metal, Shapes.Blob(new V3((float)Math.Cos(a) * 0.2f, 0, (float)Math.Sin(a) * 0.2f), new V3(0.025f, 0.006f, 0.025f), 4, 8));
            }
            return bp;
        }
    }
}
