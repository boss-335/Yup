using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;
using VoiceHorror.Art;
using VoiceHorror.Characters;
using VoiceHorror.Core;
using VoiceHorror.Gameplay.Creatures;
using VoiceHorror.Gameplay.Weapons;
using VoiceHorror.Gameplay.World;
using VoiceHorror.UI;

namespace VoiceHorror.EditorTools
{
    /// <summary>The five milestone scenes (spec §8). Every scene is generated; nothing is hand-placed.</summary>
    public static class LevelBuilders
    {
        static SceneKit Begin(bool prompt)
        {
            if (prompt)
            {
                if (!SceneKit.PrepareNewScene()) return null;
            }
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return new SceneKit(ConfigAssets.EnsureAll());
        }

        // ================================================================ M0 — Mic Test

        public static void MicTest(bool prompt)
        {
            var k = Begin(prompt);
            if (k == null) return;
            k.DimRoomLighting();
            k.Floor("Floor", Vector3.zero, new Vector2(12, 12), k.Stone);
            k.StaticCamera(new Vector3(0, 2.2f, -6f), new Vector3(0, 1f, 0));
            var sys = k.Systems(SceneMode.MicTest, false, false, false);
            sys.GetComponent<GameFlow>().requireRecording = false;
            k.UiRoot(true, true, false, false);
            SceneKit.Save("M0_MicTest");
        }

        // ================================================================ M1 — Score Lab

        public static void ScoreLab(bool prompt)
        {
            var k = Begin(prompt);
            if (k == null) return;
            k.DimRoomLighting();
            k.StaticCamera(new Vector3(0, 2f, -6f), Vector3.zero);
            var sys = k.Systems(SceneMode.ScoreLab, false, false, false);
            sys.GetComponent<GameFlow>().requireRecording = false;
            k.UiRoot(false, false);
            var lab = new GameObject("ScoreLab");
            lab.AddComponent<Audio.VoiceRecorder>();
            lab.AddComponent<ScoreLab>();
            SceneKit.Save("M1_ScoreLab");
        }

        // ================================================================ M2 — Weapon Range

        public static void WeaponRange(bool prompt)
        {
            var k = Begin(prompt);
            if (k == null) return;
            k.NightLighting(0.4f);
            RenderSettings.fogDensity = 0.012f;
            k.Floor("Yard", new Vector3(0, 0, 12), new Vector2(30, 44), k.Sand);
            k.Wall(new Vector3(-15, 0, -10), new Vector3(15, 0, -10));
            k.Wall(new Vector3(-15, 0, 34), new Vector3(15, 0, 34));
            k.Wall(new Vector3(-15, 0, -10), new Vector3(-15, 0, 34));
            k.Wall(new Vector3(15, 0, -10), new Vector3(15, 0, 34));
            for (int i = 0; i < 4; i++) k.LampPost(new Vector3(i % 2 == 0 ? -6f : 6f, 0, i * 8f), 1.8f, 12f);

            float[] distances = { 6f, 12f, 20f };
            float[] xs = { -2.5f, 0f, 2.5f };
            for (int i = 0; i < distances.Length; i++) Dummy(k, new Vector3(xs[i], 0, distances[i]));
            // A wall with a dummy behind it: beams stop at walls.
            k.Box("Cover Wall", new Vector3(7f, 1.5f, 10f), new Vector3(4f, 3f, 0.4f), k.Coral, true);
            Dummy(k, new Vector3(7f, 0, 12f));

            k.Relic("w_nafas", new Vector3(-3f, 0, 2f));
            k.Systems(SceneMode.WeaponRange, true, false, false);
            k.UiRoot(true, false);
            k.Player(new Vector3(0, 0.05f, -4f), 0f);
            SceneKit.Save("M2_WeaponRange");
        }

        static void Dummy(SceneKit k, Vector3 pos)
        {
            var root = new GameObject("Target Dummy");
            root.transform.position = pos;
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 1f, 0);
            body.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            body.GetComponent<Renderer>().sharedMaterial = k.Cloth;
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0, 2.2f, 0);
            head.transform.localScale = Vector3.one * 0.45f;
            head.GetComponent<Renderer>().sharedMaterial = k.Wood;
            root.AddComponent<TargetDummy>();
        }

        // ================================================================ M3 — Creature Greybox

        public static void CreatureGreybox(bool prompt)
        {
            var k = Begin(prompt);
            if (k == null) return;
            k.NightLighting(0.25f);
            k.Floor("Floor", Vector3.zero, new Vector2(44, 44), k.Stone);
            // Outer walls.
            k.Wall(new Vector3(-22, 0, -22), new Vector3(22, 0, -22));
            k.Wall(new Vector3(-22, 0, 22), new Vector3(22, 0, 22));
            k.Wall(new Vector3(-22, 0, -22), new Vector3(-22, 0, 22));
            k.Wall(new Vector3(22, 0, -22), new Vector3(22, 0, 22));
            // Interior: two rooms and a corridor so occlusion matters.
            k.Wall(new Vector3(-22, 0, 4), new Vector3(-6, 0, 4));
            k.Wall(new Vector3(-2, 0, 4), new Vector3(8, 0, 4));
            k.Wall(new Vector3(8, 0, 4), new Vector3(8, 0, 14));
            k.Wall(new Vector3(8, 0, 18), new Vector3(8, 0, 22));
            k.Wall(new Vector3(-8, 0, -22), new Vector3(-8, 0, -10));
            k.Wall(new Vector3(-8, 0, -6), new Vector3(-8, 0, 4));
            k.Box("Crates", new Vector3(10, 0.6f, -8), new Vector3(3, 1.2f, 2), k.Wood);
            k.Box("Crates", new Vector3(-14, 0.6f, 12), new Vector3(2, 1.2f, 3), k.Wood);
            k.Closet(new Vector3(-20.5f, 0, -12f), 90f);
            k.Closet(new Vector3(16f, 0, 20.4f), 180f);
            k.Closet(new Vector3(0f, 0, 3.3f), 180f);
            k.LampPost(new Vector3(-15, 0, -15));
            k.LampPost(new Vector3(15, 0, -15));
            k.LampPost(new Vector3(-15, 0, 15));
            k.LampPost(new Vector3(15, 0, 12));
            k.LampPost(new Vector3(0, 0, -4));

            var patrol = new List<Transform>
            {
                k.Point("Patrol A", new Vector3(14, 0, -14)),
                k.Point("Patrol B", new Vector3(14, 0, 0)),
                k.Point("Patrol C", new Vector3(-2, 0, -2)),
                k.Point("Patrol D", new Vector3(0, 0, -16)),
            };
            k.Creature(k.Config.maskut, new Vector3(14, 0, -10), 0f, patrol.ToArray());
            // A dormant one slumped against the west wall: whispering past it is safe, talking wakes it.
            k.Creature(k.Config.maskut, new Vector3(-21.3f, 0, -2f), 90f, null, true, "Maskut (dormant)", 2, true);

            var sys = k.Systems(SceneMode.CreatureGreybox, true, true, true, new Bounds(Vector3.zero, new Vector3(60, 20, 60)));
            sys.GetComponent<AtmosphereController>().playWaves = false;
            k.UiRoot(true, true);
            k.Player(new Vector3(-17f, 0.05f, 17f), 135f);
            SceneKit.Save("M3_CreatureGreybox");
        }

        // ================================================================ M4 — Vertical Slice: جزيرة الصمّة

        public static void VerticalSlice(bool prompt)
        {
            var k = Begin(prompt);
            if (k == null) return;
            k.NightLighting(0.15f);

            // ---------------- Sea, land, pier
            var sea = k.Box("Sea", new Vector3(0, -1.6f, 80), new Vector3(700, 1, 700), k.Sea);
            Object.DestroyImmediate(sea.GetComponent<Collider>());
            k.Floor("Island", new Vector3(0, 0, 77.5f), new Vector2(70, 125), k.Sand);
            k.Box("Pier (dock)", new Vector3(0, -0.15f, 5f), new Vector3(4, 0.3f, 20), k.Wood);
            k.Box("Boat", new Vector3(3.6f, -0.5f, 0f), new Vector3(2f, 0.8f, 5f), k.DarkWood);
            k.Box("Pier (to dhow)", new Vector3(24, -0.15f, 155f), new Vector3(4, 0.3f, 30), k.Wood);
            k.Box("Dhow deck", new Vector3(24, -0.15f, 176f), new Vector3(8, 0.3f, 12), k.Wood);
            k.Box("Dhow hull L", new Vector3(19.8f, 0.2f, 176f), new Vector3(0.4f, 1f, 12), k.DarkWood);
            k.Box("Dhow hull R", new Vector3(28.2f, 0.2f, 176f), new Vector3(0.4f, 1f, 12), k.DarkWood);
            k.Box("Dhow bow", new Vector3(24, 0.2f, 182.2f), new Vector3(8.4f, 1f, 0.4f), k.DarkWood);
            k.Cylinder("Mast", new Vector3(24, 5f, 179f), 0.2f, 10f, k.DarkWood);

            // Invisible boundaries (not sound occluders).
            Boundary(k, new Vector3(-35, 0, 15), new Vector3(-35, 0, 140));
            Boundary(k, new Vector3(35, 0, 15), new Vector3(35, 0, 140));
            Boundary(k, new Vector3(-35, 0, 15), new Vector3(-2, 0, 15));
            Boundary(k, new Vector3(2, 0, 15), new Vector3(35, 0, 15));
            Boundary(k, new Vector3(-2.1f, 0, -5), new Vector3(-2.1f, 0, 15));
            Boundary(k, new Vector3(2.1f, 0, -5), new Vector3(2.1f, 0, 15));
            Boundary(k, new Vector3(-2.1f, 0, -5), new Vector3(2.1f, 0, -5));
            Boundary(k, new Vector3(-35, 0, 140), new Vector3(22, 0, 140));
            Boundary(k, new Vector3(26, 0, 140), new Vector3(35, 0, 140));
            Boundary(k, new Vector3(21.9f, 0, 140), new Vector3(21.9f, 0, 170));
            Boundary(k, new Vector3(26.1f, 0, 140), new Vector3(26.1f, 0, 170));
            Boundary(k, new Vector3(19.6f, 0, 170), new Vector3(21.9f, 0, 170));
            Boundary(k, new Vector3(26.1f, 0, 170), new Vector3(28.4f, 0, 170));
            Boundary(k, new Vector3(19.6f, 0, 170), new Vector3(19.6f, 0, 182.4f));
            Boundary(k, new Vector3(28.4f, 0, 170), new Vector3(28.4f, 0, 182.4f));

            // ---------------- Zone A: the quarter (الفريج) — alleys
            // Blocks run wall-to-wall (x = ±35) so the only routes are the alleys.
            k.Block("Houses W1", new Vector3(-18.4f, 0, 30), new Vector3(33.2f, 4.2f, 20));
            k.Block("Houses E1", new Vector3(18.4f, 0, 30), new Vector3(33.2f, 4.6f, 20));
            k.Block("Houses W2", new Vector3(-23.5f, 0, 53), new Vector3(23f, 3.8f, 18));
            k.Block("Houses C2", new Vector3(-1.25f, 0, 53), new Vector3(14.5f, 4.4f, 18));
            k.Block("Houses E2", new Vector3(22.25f, 0, 53), new Vector3(25.5f, 4f, 18));
            k.Box("Barrels", new Vector3(-24f, 0.5f, 41f), new Vector3(1.2f, 1f, 1.2f), k.Wood);
            k.Box("Crates", new Vector3(12f, 0.6f, 43f), new Vector3(2f, 1.2f, 1.4f), k.Wood);
            k.Closet(new Vector3(-34.4f, 0, 42f), 90f);
            k.Closet(new Vector3(34.4f, 0, 64f), -90f);
            k.Closet(new Vector3(9.0f, 0, 47f), -90f);

            // ---------------- Zone B: the nahham's house (safe room) — the only way to the market
            k.Block("Houses W3", new Vector3(-24f, 0, 75), new Vector3(22f, 4.4f, 18));
            k.Block("Houses E3", new Vector3(18f, 0, 75), new Vector3(34f, 4.2f, 18));
            // Front door faces the street, back door opens onto the market.
            var house = new GameObject("Nahham's House").transform;
            house.SetParent(k.Geometry, false);
            WallWithDoor(k, new Vector3(-13, 0, 66), new Vector3(1, 0, 66), -6f, house);
            WallWithDoor(k, new Vector3(-13, 0, 84), new Vector3(1, 0, 84), -6f, house);
            k.Wall(new Vector3(-13, 0, 66), new Vector3(-13, 0, 84), 4f, 0.4f, k.Coral, house);
            k.Wall(new Vector3(1, 0, 66), new Vector3(1, 0, 84), 4f, 0.4f, k.Coral, house);
            k.Box("Roof", new Vector3(-6, 4.15f, 75), new Vector3(14.6f, 0.3f, 18.6f), k.DarkWood, true, house);
            k.Box("Stairs 1", new Vector3(-11.5f, 0.25f, 78f), new Vector3(2f, 0.5f, 1f), k.Wood, false, house);
            k.Box("Stairs 2", new Vector3(-11.5f, 0.75f, 79f), new Vector3(2f, 0.5f, 1f), k.Wood, false, house);
            k.Box("Stairs 3", new Vector3(-11.5f, 1.25f, 80f), new Vector3(2f, 0.5f, 1f), k.Wood, false, house);
            k.Box("Chest", new Vector3(-9.6f, 0.35f, 79.5f), new Vector3(1.2f, 0.7f, 0.8f), k.DarkWood, false, house);
            k.Box("Mattress", new Vector3(-1.5f, 0.15f, 70f), new Vector3(2f, 0.3f, 3f), k.Cloth, false, house);
            k.SafeRoomVolume(new Vector3(-6, 1.5f, 75), new Vector3(13f, 3f, 17f));
            k.Relic("w_nafas", new Vector3(-9.6f, 0.7f, 79.5f));
            k.Tape("tape_03", new Vector3(-1.2f, 0.9f, 77.6f));
            k.LampPost(new Vector3(-6, 0, 74), 1.6f, 10f);
            // The grandfather's echo sits by his radio, singing; he fades when you come close.
            k.Nahham(new Vector3(-3.2f, 0, 79.5f), 200f);

            // ---------------- Zone C: the market (السوق) and the well
            for (int i = 0; i < 6; i++)
            {
                float x = i % 2 == 0 ? -16f : 14f;
                float z = 94f + i * 6f;
                k.Box("Stall", new Vector3(x + (i % 3) * 2f, 0.5f, z), new Vector3(3f, 1f, 1.4f), k.Wood);
                var canopy = k.Box("Canopy", new Vector3(x + (i % 3) * 2f, 2.4f, z), new Vector3(3.4f, 0.1f, 2f), k.Cloth);
                Object.DestroyImmediate(canopy.GetComponent<Collider>());
            }
            k.Box("Stall", new Vector3(-6f, 0.5f, 120f), new Vector3(3f, 1f, 1.4f), k.Wood);
            k.Box("Stall", new Vector3(8f, 0.5f, 98f), new Vector3(1.4f, 1f, 3f), k.Wood);
            k.Cylinder("Well", new Vector3(0, 0.5f, 108f), 1.4f, 1f, k.Stone);
            k.Closet(new Vector3(-34.4f, 0, 100f), 90f);
            k.Closet(new Vector3(34.4f, 0, 112f), -90f);
            k.Closet(new Vector3(-12f, 0, 129.4f), 180f);
            k.Tape("tape_04", new Vector3(-18f, 0.9f, 89f));
            k.Tape("tape_05", new Vector3(28f, 0.9f, 126f));
            // Market back wall with the gate to the pier.
            k.Wall(new Vector3(-35, 0, 136), new Vector3(22, 0, 136), 3.6f);
            k.Wall(new Vector3(26, 0, 136), new Vector3(35, 0, 136), 3.6f);

            // ---------------- Zone D: the pier and the dhow
            k.Tape("tape_06", new Vector3(25.4f, 0.9f, 143f));

            // ---------------- Lights
            k.LampPost(new Vector3(-1.6f, 0, 12f));
            k.LampPost(new Vector3(1.4f, 0, 30f), 1.2f);
            k.LampPost(new Vector3(-15f, 0, 41f));
            k.LampPost(new Vector3(7.8f, 0, 55f), 1.1f);
            k.LampPost(new Vector3(-3f, 0, 64f), 1.5f);
            k.LampPost(new Vector3(-10f, 0, 100f));
            k.LampPost(new Vector3(12f, 0, 115f));
            k.LampPost(new Vector3(-22f, 0, 122f));
            k.LampPost(new Vector3(24f, 0, 133f), 1.5f);
            k.LampPost(new Vector3(25.6f, 0, 152f), 1.2f, 9f, new Color(0.7f, 0.8f, 1f));
            k.LampPost(new Vector3(22.4f, 0, 165f), 1.2f, 9f, new Color(0.7f, 0.8f, 1f));

            // Lighthouse (Mubarak's) on its rock — a landmark.
            k.Cylinder("Lighthouse rock", new Vector3(62, -0.5f, 150f), 6f, 2f, k.Stone);
            k.Cylinder("Lighthouse", new Vector3(62, 11f, 150f), 2.2f, 22f, k.Coral);
            k.Cylinder("Lighthouse gallery", new Vector3(62, 22.1f, 150f), 3.4f, 0.3f, k.DarkWood);
            k.Cylinder("Lighthouse lamp room", new Vector3(62, 23.6f, 150f), 1.4f, 2.6f, k.Lamp);
            k.Mubarak(new Vector3(62, 22.25f, 150f), 2.7f);
            var beamPivot = new GameObject("Lighthouse beam").transform;
            beamPivot.position = new Vector3(62, 24.2f, 150f);
            var beam = beamPivot.gameObject.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.range = 140f;
            beam.spotAngle = 18f;
            beam.intensity = 4f;
            beam.color = new Color(1f, 0.92f, 0.75f);
            beamPivot.rotation = Quaternion.Euler(8f, -90f, 0f);
            beamPivot.gameObject.AddComponent<Rotator>().degreesPerSecond = new Vector3(0f, 18f, 0f);

            // ---------------- Story triggers
            k.Tape("tape_01", new Vector3(1.3f, 0.9f, 9f));
            k.Tape("tape_02", new Vector3(-31.5f, 0.9f, 43.4f));
            k.Trigger("dock end", new Vector3(0, 1.5f, 17f), new Vector3(4f, 3f, 2f), "obj.find_house", "radio.alleys");
            k.Trigger("house door", new Vector3(-6, 1.5f, 67.2f), new Vector3(3f, 3f, 1.5f), "obj.take_breath", "radio.house");
            k.Trigger("house back", new Vector3(-6, 1.5f, 85.5f), new Vector3(3f, 3f, 1.5f), "obj.cross_market", "radio.market");
            k.Trigger("market gate", new Vector3(24, 1.5f, 138f), new Vector3(4f, 3f, 2f), "obj.reach_dhow", "radio.dhow");
            k.Trigger("dhow", new Vector3(24, 1.5f, 169f), new Vector3(4f, 3f, 2f), "obj.break_hush", "");

            // ---------------- Creatures
            k.Creature(k.Config.maskut, new Vector3(-20, 0, 42), 90f, new[]
            {
                k.Point("P1a", new Vector3(-26, 0, 42)), k.Point("P1b", new Vector3(26, 0, 42)),
                k.Point("P1c", new Vector3(7.7f, 0, 52)), k.Point("P1d", new Vector3(-10.2f, 0, 52)),
            }, true, "Maskut (quarter)");
            k.Creature(k.Config.maskut, new Vector3(18, 0, 64), -90f, new[]
            {
                k.Point("P2a", new Vector3(-10, 0, 64)), k.Point("P2b", new Vector3(26, 0, 64)), k.Point("P2c", new Vector3(-10.2f, 0, 48)),
            }, true, "Maskut (street)", 2);
            k.Creature(k.Config.maskut, new Vector3(-20, 0, 96), 0f, new[]
            {
                k.Point("P3a", new Vector3(-22, 0, 92)), k.Point("P3b", new Vector3(-22, 0, 128)),
                k.Point("P3c", new Vector3(18, 0, 128)), k.Point("P3d", new Vector3(18, 0, 92)),
            }, true, "Maskut (market)", 5);
            k.Creature(k.Config.siluwa, new Vector3(22, 0, 104), 180f, new[]
            {
                k.Point("P4a", new Vector3(24, 0, 96)), k.Point("P4b", new Vector3(6, 0, 122)), k.Point("P4c", new Vector3(-26, 0, 116)),
            }, true, "Si'luwa (market)");
            k.Creature(k.Config.budaryah, new Vector3(0, 0, 111f), 180f, null, true, "Bu Daryah (well)");
            // Dormant villagers: slumped against walls. Sneak past in silence or they wake.
            k.Creature(k.Config.maskut, new Vector3(-1.3f, 0, 33f), 90f, null, true, "Maskut (dormant, alley)", 3, true);
            k.Creature(k.Config.maskut, new Vector3(-12f, 0, 116f), 180f, null, true, "Maskut (dormant, market)", 4, true);
            k.Creature(k.Config.summah, new Vector3(24, 0, 178f), 180f, null, true, "Al-Summah (dhow)");

            var templates = new GameObject("_Templates").transform;
            var maskutT = k.Creature(k.Config.maskut, new Vector3(0, -50, 0), 0f, null, false, "Template_Maskut");
            var siluwaT = k.Creature(k.Config.siluwa, new Vector3(0, -50, 0), 0f, null, false, "Template_Siluwa");
            maskutT.transform.SetParent(templates, true);
            siluwaT.transform.SetParent(templates, true);

            // ---------------- Systems
            var sys = k.Systems(SceneMode.VerticalSlice, true, true, true, new Bounds(new Vector3(10, 0, 90), new Vector3(110, 20, 220)));
            var flow = sys.GetComponent<GameFlow>();
            flow.introKey = "intro.card";
            flow.startObjectiveKey = "obj.reach_alleys";
            flow.startRadioKey = "radio.arrive";
            var director = sys.AddComponent<CreatureDirector>();
            director.stalkerTemplate = maskutT.gameObject;
            director.hunterTemplate = siluwaT.gameObject;
            var spawns = new GameObject("_SpawnPoints").transform;
            Vector3[] sp =
            {
                new Vector3(-28, 0, 42), new Vector3(28, 0, 42), new Vector3(-28, 0, 64), new Vector3(28, 0, 64),
                new Vector3(-10.2f, 0, 46), new Vector3(7.7f, 0, 46), new Vector3(-28, 0, 88), new Vector3(28, 0, 88),
                new Vector3(-28, 0, 130), new Vector3(16, 0, 130), new Vector3(24, 0, 150), new Vector3(24, 0, 160),
            };
            var points = new List<Transform>();
            for (int i = 0; i < sp.Length; i++) points.Add(k.Point("Spawn " + i, sp[i], spawns));
            director.spawnPoints = points.ToArray();

            k.UiRoot(true, false);
            k.Player(new Vector3(0, 0.05f, 0f), 0f);
            SceneKit.Save("M4_VerticalSlice_AlSummah");
        }

        // ================================================================ Character gallery

        public static void CharacterGallery(bool prompt)
        {
            var k = Begin(prompt);
            if (k == null) return;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.17f, 0.2f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.05f, 0.06f, 0.08f);
            RenderSettings.fogDensity = 0.02f;
            k.Floor("Floor", new Vector3(21, 0, 0), new Vector2(70, 24), k.Stone);
            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.intensity = 0.35f;
            moon.color = new Color(0.6f, 0.7f, 0.9f);
            moon.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            var cam = k.StaticCamera(new Vector3(0, 2, -5), new Vector3(0, 1, 0));
            var view = new GameObject("_Gallery").AddComponent<GalleryView>();
            view.cam = cam;

            void Spot(Vector3 at, float height)
            {
                var l = new GameObject("Spot").AddComponent<Light>();
                l.type = LightType.Spot;
                l.range = height * 4f + 6f;
                l.spotAngle = 55f;
                l.intensity = 3.2f;
                l.color = new Color(1f, 0.88f, 0.72f);
                l.shadows = LightShadows.Soft;
                l.transform.position = at + new Vector3(1.5f, height + 3f, -2.5f);
                l.transform.LookAt(at + Vector3.up * height * 0.5f);
                k.Cylinder("Plinth", at + Vector3.down * 0.02f, 2.2f, 0.06f, k.DarkWood);
            }

            void Add(Transform target, string ar, string en, string loreAr, string loreEn, float height, float dist)
                => view.exhibits.Add(new GalleryView.Exhibit { target = target, nameAr = ar, nameEn = en, loreAr = loreAr, loreEn = loreEn, viewHeight = height, viewDistance = dist });

            var c = k.Config;
            var maskut = k.Exhibit(c.maskut, new Vector3(0, 0, 0), 180f, true);
            Spot(Vector3.zero, 2.1f);
            Add(maskut.transform, c.maskut.displayNameAr, c.maskut.displayNameEn, c.maskut.loreAr, c.maskut.loreEn, 1.4f, 4.2f);
            var siluwa = k.Exhibit(c.siluwa, new Vector3(7, 0, 0), 180f, true);
            Spot(new Vector3(7, 0, 0), 1.5f);
            Add(siluwa.transform, c.siluwa.displayNameAr, c.siluwa.displayNameEn, c.siluwa.loreAr, c.siluwa.loreEn, 1.1f, 4.2f);
            var bu = k.Exhibit(c.budaryah, new Vector3(15, 0, 0), 180f, false);
            Spot(new Vector3(15, 0, 0), 3.2f);
            Add(bu.transform, c.budaryah.displayNameAr, c.budaryah.displayNameEn, c.budaryah.loreAr, c.budaryah.loreEn, 2.2f, 8f);
            var summah = k.Exhibit(c.summah, new Vector3(25, 0, 0), 180f, false);
            Spot(new Vector3(25, 0, 0), 4f);
            var heartLight = new GameObject("HeartLight").AddComponent<Light>();
            heartLight.transform.position = new Vector3(25, 2.6f, -0.8f);
            heartLight.range = 10f; heartLight.intensity = 2f; heartLight.color = new Color(0.75f, 0.8f, 1f);
            Add(summah.transform, c.summah.displayNameAr, c.summah.displayNameEn, c.summah.loreAr, c.summah.loreEn, 2.6f, 10f);

            var echo = k.Nahham(new Vector3(34, 0, 0), 180f);
            echo.alwaysVisible = true;
            echo.maxAlpha = 0.55f;
            Spot(new Vector3(34, 0, 0), 1.2f);
            Add(echo.transform, "عبدالله النهّام (صدى)", "Abdullah the Nahham (echo)",
                "جد ريم وآخر نهّام في الجزيرة. يجلس متربّع ويده خلف أذنه — هكذا يغني النهّام للبحّارة. يظهر في بيته كذكرى، ويتلاشى إذا قربت.",
                "Reem's grandfather and the island's last nahham. He sits cross-legged with his hand cupped behind his ear — how a nahham sings to the sailors. He appears in his house as a memory and fades when you come close.", 0.8f, 3.2f);

            var mub = k.Mubarak(new Vector3(41, 0, 0), 1.2f);
            Spot(new Vector3(41, 0, 0), 1.8f);
            Add(mub.transform, "مبارك — حارس المنارة", "Mubarak — the lighthouse keeper",
                "صوته في الراديو طول الليل. يمشي حول شرفة المنارة رافع فانوسه، ويومض به كل ما تكلم. سجّل أصوات الجزيرة كلها… أكثر من اللازم.",
                "His voice is on the radio all night. He walks the lighthouse gallery with his lantern raised and flashes it whenever he speaks. He recorded every voice on the island… more than he should have.", 1.2f, 4.5f);

            SceneKit.Save("CharacterGallery");
        }

        // ================================================================ Title screen

        public static void TitleScreen(bool prompt)
        {
            var k = Begin(prompt);
            if (k == null) return;
            k.NightLighting(0.14f);
            RenderSettings.fogDensity = 0.022f;
            var sea = k.Box("Sea", new Vector3(0, -1.2f, 80), new Vector3(600, 1, 600), k.Sea);
            Object.DestroyImmediate(sea.GetComponent<Collider>());
            k.Box("Pier", new Vector3(0, -0.15f, 22f), new Vector3(3.5f, 0.3f, 40f), k.Wood);
            for (int i = 0; i < 8; i++) k.Cylinder("Pile", new Vector3(i % 2 == 0 ? -1.8f : 1.8f, -0.6f, 4f + i * 5f), 0.12f, 1.4f, k.DarkWood);
            k.LampPost(new Vector3(1.5f, 0, 38f), 1.4f, 9f);
            k.Box("Boat", new Vector3(3.2f, -0.6f, 10f), new Vector3(1.8f, 0.7f, 4.5f), k.DarkWood);
            // A villager stands at the end of the pier, listening for you.
            var watcher = k.Exhibit(k.Config.maskut, new Vector3(0, 0, 39f), 180f, false, 6);
            Object.DestroyImmediate(watcher.GetComponent<GalleryPuppet>());
            var body = watcher.GetComponent<CreatureBody>();
            body.externalControl = true;
            body.externalPose = CreatureBody.Pose.Listen;
            body.externalLookTarget = new Vector3(0, 0, 0);
            // The lighthouse with Mubarak on its gallery.
            k.Cylinder("Rock", new Vector3(30, -0.6f, 90f), 6f, 2f, k.Stone);
            k.Cylinder("Lighthouse", new Vector3(30, 11f, 90f), 2.2f, 22f, k.Coral);
            k.Cylinder("Gallery", new Vector3(30, 22.1f, 90f), 3.4f, 0.3f, k.DarkWood);
            k.Cylinder("Lamp room", new Vector3(30, 23.6f, 90f), 1.4f, 2.6f, k.Lamp);
            k.Mubarak(new Vector3(30, 22.25f, 90f), 2.7f);
            var beam = new GameObject("Beam").AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.range = 160f; beam.spotAngle = 16f; beam.intensity = 5f; beam.color = new Color(1f, 0.92f, 0.75f);
            beam.transform.position = new Vector3(30, 24.2f, 90f);
            beam.transform.rotation = Quaternion.Euler(6f, -120f, 0f);
            beam.gameObject.AddComponent<Rotator>().degreesPerSecond = new Vector3(0f, 14f, 0f);

            var camRig = k.StaticCamera(new Vector3(-1.2f, 1.9f, 1f), new Vector3(20f, 8f, 80f));
            camRig.farClipPlane = 400f;
            var atm = new GameObject("_Atmosphere").AddComponent<AtmosphereController>();
            atm.calmFogDensity = 0.022f;
            var ui = new GameObject("_UI");
            var title = ui.AddComponent<TitleMenu>();
            title.cameraRig = camRig.transform;
            title.lookAt = new Vector3(18f, 7f, 80f);
            ui.AddComponent<MessageView>();
            ui.AddComponent<PauseMenuView>();
            new GameObject("GameConfig").AddComponent<GameConfig>().voiceTuning = k.Config.tuning;
            SceneKit.Save("Title");
        }

        static void Boundary(SceneKit k, Vector3 a, Vector3 b)
        {
            var w = k.Wall(a, b, 4f, 0.3f);
            w.name = "Boundary";
            Object.DestroyImmediate(w.GetComponent<Gameplay.Noise.Occluder>());
            Object.DestroyImmediate(w.GetComponent<MeshRenderer>());
        }

        /// <summary>Wall along X from a to b with a door gap centred at doorX.</summary>
        static void WallWithDoor(SceneKit k, Vector3 a, Vector3 b, float doorX, Transform parent, float doorWidth = 1.8f, float height = 4f)
        {
            k.Wall(a, new Vector3(doorX - doorWidth / 2f, a.y, a.z), height, 0.4f, k.Coral, parent);
            k.Wall(new Vector3(doorX + doorWidth / 2f, b.y, b.z), b, height, 0.4f, k.Coral, parent);
            k.Box("Lintel", new Vector3(doorX, height - 0.5f, a.z), new Vector3(doorWidth, 1f, 0.4f), k.Coral, true, parent);
        }
    }
}
