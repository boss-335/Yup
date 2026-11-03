using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VoiceHorror.Art;
using VoiceHorror.Audio;
using VoiceHorror.Characters;
using VoiceHorror.Config;
using VoiceHorror.Core;
using VoiceHorror.Gameplay.Creatures;
using VoiceHorror.Gameplay.Noise;
using VoiceHorror.Gameplay.Player;
using VoiceHorror.Gameplay.Weapons;
using VoiceHorror.Gameplay.World;
using VoiceHorror.UI;

namespace VoiceHorror.EditorTools
{
    /// <summary>Building blocks shared by every scene builder.</summary>
    public sealed class SceneKit
    {
        public const string SceneDir = "Assets/_Project/Scenes";

        public readonly ConfigAssets.Bundle Config;
        public readonly Material Stone, Coral, Wood, DarkWood, Sand, Sea, Cloth, Metal, Lamp, CreatureDark, TapeMat, RelicMat, Fx;
        public readonly Material Hair, Bone, Ghost, ReemSkin, Abaya, ThobeWhite, CharGlow;
        readonly Dictionary<string, Material> _skins = new Dictionary<string, Material>();
        Transform _geometry;

        public SceneKit(ConfigAssets.Bundle config)
        {
            Config = config;
            Stone = ConfigAssets.Lit("Stone", new Color(0.42f, 0.4f, 0.37f));
            Coral = ConfigAssets.Lit("CoralStone", new Color(0.62f, 0.56f, 0.46f));
            Wood = ConfigAssets.Lit("Wood", new Color(0.36f, 0.25f, 0.16f));
            DarkWood = ConfigAssets.Lit("DarkWood", new Color(0.2f, 0.14f, 0.1f));
            Sand = ConfigAssets.Lit("Sand", new Color(0.48f, 0.43f, 0.34f));
            Sea = ConfigAssets.Lit("Sea", new Color(0.03f, 0.08f, 0.11f), 0.9f);
            Cloth = ConfigAssets.Lit("Cloth", new Color(0.35f, 0.12f, 0.1f));
            Metal = ConfigAssets.Lit("Metal", new Color(0.3f, 0.3f, 0.32f), 0.5f);
            Lamp = ConfigAssets.Lit("LampGlass", new Color(1f, 0.8f, 0.5f), 0.3f, new Color(1.6f, 1.1f, 0.6f));
            CreatureDark = ConfigAssets.Lit("CreatureDark", new Color(0.05f, 0.04f, 0.04f));
            TapeMat = ConfigAssets.Lit("Tape", new Color(0.15f, 0.15f, 0.16f), 0.4f, new Color(0.2f, 0.5f, 0.5f));
            RelicMat = ConfigAssets.Lit("Relic", new Color(0.9f, 0.85f, 0.75f), 0.6f, new Color(0.6f, 0.6f, 0.5f));
            Fx = ConfigAssets.Effect();
            Hair = ConfigAssets.Lit("Hair", new Color(0.025f, 0.02f, 0.02f), 0.55f);
            Bone = ConfigAssets.Lit("BoneShell", new Color(0.86f, 0.82f, 0.72f), 0.35f);
            Ghost = ConfigAssets.Transparent("Ghost", new Color(0.55f, 0.85f, 0.95f, 0.38f));
            ReemSkin = ConfigAssets.Lit("ReemSkin", new Color(0.72f, 0.55f, 0.42f), 0.3f);
            Abaya = ConfigAssets.Lit("Abaya", new Color(0.05f, 0.045f, 0.05f), 0.25f);
            ThobeWhite = ConfigAssets.Lit("Thobe", new Color(0.7f, 0.68f, 0.62f), 0.15f);
            CharGlow = ConfigAssets.Lit("CharGlow", new Color(1f, 0.85f, 0.55f), 0.4f, new Color(1.6f, 1.2f, 0.7f));
        }

        // ------------------------------------------------------------ Scene lifecycle

        public static bool PrepareNewScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return true;
        }

        public static string Save(string name)
        {
            Directory.CreateDirectory(SceneDir);
            string path = $"{SceneDir}/{name}.unity";
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, path);
            var list = EditorBuildSettings.scenes.ToList();
            if (!list.Any(s => s.path == path)) list.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log($"[VoiceHorror] Built scene {path}");
            return path;
        }

        /// <summary>Puts the named scenes first in Build Settings, in this order (others keep their order after).</summary>
        public static void SetBuildOrder(params string[] names)
        {
            var list = EditorBuildSettings.scenes.ToList();
            var ordered = new List<EditorBuildSettingsScene>();
            foreach (var n in names)
            {
                var s = list.Find(x => Path.GetFileNameWithoutExtension(x.path) == n);
                if (s != null) { ordered.Add(s); list.Remove(s); }
            }
            ordered.AddRange(list);
            EditorBuildSettings.scenes = ordered.ToArray();
        }

        public void NightLighting(float moon = 0.18f)
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.07f, 0.08f, 0.11f);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.05f, 0.07f, 0.09f);
            RenderSettings.fogDensity = 0.035f;
            var go = new GameObject("Moon");
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.55f, 0.65f, 0.85f);
            l.intensity = moon;
            l.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(35f, -30f, 0f);
        }

        public void DimRoomLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.24f, 0.22f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            var go = new GameObject("Key Light");
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 0.8f;
            go.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
        }

        // ------------------------------------------------------------ Systems & UI

        public GameObject Systems(SceneMode mode, bool threat, bool atmosphere, bool navmesh, Bounds? navBounds = null)
        {
            var go = new GameObject("_Systems");
            var cfg = go.AddComponent<GameConfig>();
            cfg.voiceTuning = Config.tuning;
            cfg.balance = Config.balance;
            cfg.weapons = new List<WeaponDefinition> { Config.sadafa, Config.tar, Config.nafas };
            cfg.creatures = new List<CreatureDefinition> { Config.maskut, Config.siluwa, Config.budaryah, Config.summah };
            cfg.effectMaterial = Fx;
            go.AddComponent<MicCapture>();
            go.AddComponent<VoiceLevelMeter>();
            if (threat) go.AddComponent<ThreatMeter>();
            if (atmosphere) go.AddComponent<AtmosphereController>();
            if (navmesh)
            {
                var baker = go.AddComponent<RuntimeNavMeshBaker>();
                if (navBounds.HasValue) { baker.boundsCenter = navBounds.Value.center; baker.boundsSize = navBounds.Value.size; }
            }
            var flow = go.AddComponent<GameFlow>();
            flow.mode = mode;
            return go;
        }

        public GameObject UiRoot(bool hud, bool debugVisible, bool weaponsOnHud = true, bool threatOnHud = true)
        {
            var go = new GameObject("_UI");
            if (hud)
            {
                var h = go.AddComponent<HudView>();
                h.showWeapons = weaponsOnHud;
                h.showThreat = threatOnHud;
                h.showHealth = weaponsOnHud;
                h.showObjective = weaponsOnHud;
            }
            go.AddComponent<DebugOverlay>().visible = debugVisible;
            go.AddComponent<LivePitchTracker>();
            go.AddComponent<MessageView>();
            go.AddComponent<RadioView>();
            go.AddComponent<PauseMenuView>();
            go.AddComponent<MicStatusView>();
            var cal = new GameObject("Calibration");
            cal.transform.SetParent(go.transform);
            cal.AddComponent<CalibrationRunner>();
            cal.AddComponent<CalibrationView>();
            var rec = new GameObject("Recording");
            rec.transform.SetParent(go.transform);
            rec.AddComponent<VoiceRecorder>();
            rec.AddComponent<RecordingView>();
            return go;
        }

        public Camera StaticCamera(Vector3 pos, Vector3 lookAt)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.04f, 0.05f);
            go.AddComponent<AudioListener>();
            go.transform.position = pos;
            go.transform.LookAt(lookAt);
            return cam;
        }

        // ------------------------------------------------------------ Player

        public GameObject Player(Vector3 pos, float yaw)
        {
            var go = new GameObject("Player");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.75f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0f, 0.875f, 0f);
            cc.stepOffset = 0.35f;
            cc.slopeLimit = 45f;

            // Head pivot (look + height, driven by PlayerController) → camera (bob/shake/death, CameraFx).
            var head = new GameObject("Head").transform;
            head.SetParent(go.transform, false);
            head.localPosition = new Vector3(0f, 1.65f, 0f);
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(head, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 250f;
            cam.fieldOfView = 70f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.03f, 0.04f);
            camGo.AddComponent<AudioListener>();

            var flashGo = new GameObject("Lantern");
            flashGo.transform.SetParent(camGo.transform, false);
            flashGo.transform.localPosition = new Vector3(0.25f, -0.2f, 0.1f);
            var flash = flashGo.AddComponent<Light>();
            flash.type = LightType.Spot;
            flash.range = 16f;
            flash.spotAngle = 60f;
            flash.intensity = 1.7f;
            flash.color = new Color(1f, 0.86f, 0.65f);
            flash.shadows = LightShadows.Soft;

            camGo.AddComponent<CameraFx>();
            var rig = camGo.AddComponent<FirstPersonRig>();
            rig.palette = new CharacterPalette { skin = ReemSkin, cloth = Abaya, dark = CreatureDark, glow = CharGlow, metal = Metal, wood = Wood, shell = Bone, hair = Hair, ghost = Ghost };

            var pc = go.AddComponent<PlayerController>();
            pc.cameraPivot = head;
            go.AddComponent<PlayerNoiseEmitter>();
            go.AddComponent<PlayerHealth>();
            go.AddComponent<PlayerInteractor>();
            go.AddComponent<VoiceRecorder>();
            var wc = go.AddComponent<WeaponController>();
            wc.aimOrigin = head;
            return go;
        }

        // ------------------------------------------------------------ Geometry

        public Transform Geometry
        {
            get
            {
                if (_geometry == null) _geometry = new GameObject("_Level").transform;
                return _geometry;
            }
        }

        public GameObject Box(string name, Vector3 center, Vector3 size, Material mat, bool occluder = false, Transform parent = null, float yaw = 0f)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent != null ? parent : Geometry, false);
            go.transform.position = center;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.isStatic = true;
            if (occluder) go.AddComponent<Occluder>();
            return go;
        }

        public GameObject Cylinder(string name, Vector3 center, float radius, float height, Material mat, Transform parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent != null ? parent : Geometry, false);
            go.transform.position = center;
            go.transform.localScale = new Vector3(radius * 2f, height / 2f, radius * 2f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.isStatic = true;
            return go;
        }

        public GameObject Floor(string name, Vector3 center, Vector2 size, Material mat, float thickness = 1f)
            => Box(name, center - Vector3.up * thickness / 2f, new Vector3(size.x, thickness, size.y), mat);

        /// <summary>Wall along X or Z from a to b (on the ground), sound-occluding.</summary>
        public GameObject Wall(Vector3 a, Vector3 b, float height = 3.2f, float thickness = 0.4f, Material mat = null, Transform parent = null)
        {
            Vector3 mid = (a + b) / 2f + Vector3.up * height / 2f;
            Vector3 d = b - a;
            float len = d.magnitude;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            return Box("Wall", mid, new Vector3(thickness, height, len), mat ?? Coral, true, parent, yaw);
        }

        /// <summary>
        /// A closed house: four occluding walls with an optional door gap on one side.
        /// doorSide: 0 = -Z, 1 = +Z, 2 = -X, 3 = +X, -1 = none.
        /// </summary>
        public Transform House(string name, Vector3 center, Vector2 size, float height, int doorSide, float doorWidth = 1.6f, Material mat = null, bool roof = true)
        {
            var root = new GameObject(name).transform;
            root.SetParent(Geometry, false);
            root.position = center;
            mat = mat ?? Coral;
            float hx = size.x / 2f, hz = size.y / 2f;
            Vector3 c = center;
            void Side(Vector3 a, Vector3 b, bool door)
            {
                if (!door) { Wall(a, b, height, 0.4f, mat, root); return; }
                Vector3 mid = (a + b) / 2f;
                Vector3 dir = (b - a).normalized;
                Wall(a, mid - dir * doorWidth / 2f, height, 0.4f, mat, root);
                Wall(mid + dir * doorWidth / 2f, b, height, 0.4f, mat, root);
                Box("Lintel", mid + Vector3.up * (height - 0.4f), new Vector3(dir.x != 0 ? doorWidth : 0.4f, 0.8f, dir.z != 0 ? doorWidth : 0.4f), mat, true, root);
            }
            Side(c + new Vector3(-hx, 0, -hz), c + new Vector3(hx, 0, -hz), doorSide == 0);
            Side(c + new Vector3(-hx, 0, hz), c + new Vector3(hx, 0, hz), doorSide == 1);
            Side(c + new Vector3(-hx, 0, -hz), c + new Vector3(-hx, 0, hz), doorSide == 2);
            Side(c + new Vector3(hx, 0, -hz), c + new Vector3(hx, 0, hz), doorSide == 3);
            if (roof) Box("Roof", c + Vector3.up * (height + 0.15f), new Vector3(size.x + 0.6f, 0.3f, size.y + 0.6f), DarkWood, true, root);
            return root;
        }

        /// <summary>Solid block building (no interior) for alleys.</summary>
        public GameObject Block(string name, Vector3 center, Vector3 size, Material mat = null)
            => Box(name, center + Vector3.up * size.y / 2f, size, mat ?? Coral, true);

        public GameObject LampPost(Vector3 pos, float intensity = 1.4f, float range = 9f, Color? color = null)
        {
            var root = new GameObject("Lamp");
            root.transform.SetParent(Geometry, false);
            root.transform.position = pos;
            var pole = Box("Pole", pos + Vector3.up * 1.2f, new Vector3(0.1f, 2.4f, 0.1f), DarkWood, false, root.transform);
            Object.DestroyImmediate(pole.GetComponent<Collider>());
            var glass = Box("Glass", pos + Vector3.up * 2.5f, new Vector3(0.28f, 0.36f, 0.28f), Lamp, false, root.transform);
            Object.DestroyImmediate(glass.GetComponent<Collider>());
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.position = pos + Vector3.up * 2.5f;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = range;
            l.intensity = intensity;
            l.color = color ?? new Color(1f, 0.72f, 0.42f);
            var f = lightGo.AddComponent<FlickerLight>();
            f.baseIntensity = intensity;
            return root;
        }

        public HidingSpot Closet(Vector3 pos, float yaw)
        {
            var root = new GameObject("Closet (Hiding Spot)");
            root.transform.SetParent(Geometry, false);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            body.transform.localScale = new Vector3(1.1f, 2.2f, 0.9f);
            body.GetComponent<Renderer>().sharedMaterial = Wood;
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "Door";
            Object.DestroyImmediate(door.GetComponent<Collider>());
            door.transform.SetParent(root.transform, false);
            door.transform.localPosition = new Vector3(0f, 1.1f, 0.46f);
            door.transform.localScale = new Vector3(1.0f, 2.0f, 0.04f);
            door.GetComponent<Renderer>().sharedMaterial = DarkWood;
            var view = new GameObject("View").transform;
            view.SetParent(root.transform, false);
            view.localPosition = new Vector3(0f, 0f, 0f);
            view.localRotation = Quaternion.identity;
            var exit = new GameObject("Exit").transform;
            exit.SetParent(root.transform, false);
            exit.localPosition = new Vector3(0f, 0.05f, 1.2f);
            exit.localRotation = Quaternion.identity;
            var spot = root.AddComponent<HidingSpot>();
            spot.viewPoint = view;
            spot.exitPoint = exit;
            // The player stands inside the closet volume while hidden: make the body a trigger shell.
            body.GetComponent<Collider>().isTrigger = true;
            return spot;
        }

        public TapeLog Tape(string id, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Tape " + id;
            go.transform.SetParent(Geometry, false);
            go.transform.position = pos + Vector3.up * 0.06f;
            go.transform.localScale = new Vector3(0.3f, 0.06f, 0.2f);
            go.GetComponent<Renderer>().sharedMaterial = TapeMat;
            go.GetComponent<Collider>().isTrigger = true;
            var stand = Box("Stand", pos - Vector3.up * 0.45f, new Vector3(0.6f, 0.9f, 0.6f), DarkWood);
            stand.transform.SetParent(go.transform.parent, true);
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = Vector3.up * 4f;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 2.2f;
            l.intensity = 0.8f;
            l.color = new Color(0.4f, 0.9f, 0.9f);
            var t = go.AddComponent<TapeLog>();
            t.tapeId = id;
            return t;
        }

        public WeaponPickup Relic(string weaponId, Vector3 pos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Relic " + weaponId;
            go.transform.SetParent(Geometry, false);
            go.transform.position = pos + Vector3.up * 0.35f;
            go.transform.localScale = new Vector3(0.35f, 0.25f, 0.35f);
            go.GetComponent<Renderer>().sharedMaterial = RelicMat;
            go.GetComponent<Collider>().isTrigger = true;
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(go.transform, false);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 3f;
            l.intensity = 1.5f;
            l.color = new Color(0.95f, 0.9f, 0.7f);
            var p = go.AddComponent<WeaponPickup>();
            p.weaponId = weaponId;
            return p;
        }

        public ObjectiveTrigger Trigger(string name, Vector3 center, Vector3 size, string objectiveKey, string radioKey, bool checkpoint = true)
        {
            var go = new GameObject("Trigger " + name);
            go.transform.SetParent(Geometry, false);
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            var t = go.AddComponent<ObjectiveTrigger>();
            t.objectiveKey = objectiveKey;
            t.radioKey = radioKey;
            t.setCheckpoint = checkpoint;
            return t;
        }

        public SafeRoom SafeRoomVolume(Vector3 center, Vector3 size)
        {
            var go = new GameObject("SafeRoom");
            go.transform.SetParent(Geometry, false);
            go.transform.position = center;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
            return go.AddComponent<SafeRoom>();
        }

        public Transform Point(string name, Vector3 pos, Transform parent = null)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent != null ? parent : Geometry, false);
            t.position = pos;
            return t;
        }

        // ------------------------------------------------------------ Creatures

        Material SkinFor(CreatureDefinition def)
        {
            if (_skins.TryGetValue(def.id, out var m)) return m;
            m = ConfigAssets.Lit("Skin_" + def.id, def.skinColor, 0.25f);
            _skins[def.id] = m;
            return m;
        }

        Material GlowFor(CreatureDefinition def)
        {
            string key = "glow_" + def.id;
            if (_skins.TryGetValue(key, out var m)) return m;
            m = ConfigAssets.Lit("Glow_" + def.id, def.glowColor, 0.5f, def.glowColor * 2.2f);
            _skins[key] = m;
            return m;
        }

        public CharacterPalette CreaturePalette(CreatureDefinition def) => new CharacterPalette
        {
            skin = SkinFor(def), dark = CreatureDark, glow = GlowFor(def), cloth = Cloth, hair = Hair,
            wood = Wood, metal = Metal, ghost = Ghost, shell = Bone,
        };

        public CreatureBrain Creature(CreatureDefinition def, Vector3 pos, float yaw, Transform[] patrol = null, bool active = true, string name = null, int seed = 1, bool dormant = false)
        {
            var go = new GameObject(name ?? def.displayNameEn);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var col = go.AddComponent<CapsuleCollider>();
            col.height = def.height;
            col.radius = Mathf.Max(0.3f, def.radius);
            col.center = new Vector3(0f, def.height / 2f, 0f);
            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = def.radius;
            agent.height = def.height;
            agent.speed = def.wanderSpeed;
            agent.enabled = false; // enabled at runtime after the NavMesh is baked
            var brain = go.AddComponent<CreatureBrain>();
            brain.definition = def;
            brain.patrol = patrol;
            brain.startDormant = dormant;
            CreatureBody.Build(go, def, CreaturePalette(def), seed);
            if (def.archetype == CreatureArchetype.Heart)
            {
                var lightGo = new GameObject("HeartLight");
                lightGo.transform.SetParent(go.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, def.height * 0.6f, 0f);
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 14f;
                l.intensity = 2.5f;
                l.color = new Color(0.75f, 0.8f, 1f);
            }
            go.SetActive(active);
            return brain;
        }
    
        // ------------------------------------------------------------ Story characters

        public CharacterPalette HumanPalette(Material skin, Material cloth) => new CharacterPalette
        {
            skin = skin, cloth = cloth, dark = CreatureDark, glow = CharGlow, hair = Hair,
            wood = Wood, metal = Metal, ghost = Ghost, shell = Bone,
        };

        static ProceduralCharacter Character(string name, string blueprint, Vector3 pos, float yaw, CharacterPalette palette, float scale = 1f)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var ch = go.AddComponent<ProceduralCharacter>();
            ch.blueprintId = blueprint;
            ch.palette = palette;
            ch.scale = scale;
            return ch;
        }

        /// <summary>The grandfather's singing echo.</summary>
        public NahhamEcho Nahham(Vector3 pos, float yaw)
        {
            var ch = Character("Echo of Abdullah the Nahham", Blueprints.Nahham, pos, yaw, HumanPalette(ReemSkin, ThobeWhite));
            ch.castShadows = false;
            return ch.gameObject.AddComponent<NahhamEcho>();
        }

        /// <summary>Mubarak walking the lighthouse gallery.</summary>
        public MubarakFigure Mubarak(Vector3 galleryCenter, float radius)
        {
            var ch = Character("Mubarak (lighthouse)", Blueprints.Mubarak, galleryCenter, 0f, HumanPalette(ReemSkin, ThobeWhite));
            var f = ch.gameObject.AddComponent<MubarakFigure>();
            f.galleryRadius = radius;
            return f;
        }

        /// <summary>A gallery exhibit: a creature driven by GalleryPuppet instead of AI.</summary>
        public GameObject Exhibit(CreatureDefinition def, Vector3 pos, float yaw, bool mobile, int seed = 1)
        {
            var go = new GameObject(def.displayNameEn + " (exhibit)");
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            CreatureBody.Build(go, def, CreaturePalette(def), seed);
            var p = go.AddComponent<GalleryPuppet>();
            p.definition = def;
            p.mobile = mobile;
            return go;
        }

        public GameObject StaticHuman(string name, string blueprint, Vector3 pos, float yaw, Material skin, Material cloth)
            => Character(name, blueprint, pos, yaw, HumanPalette(skin, cloth)).gameObject;
    }
}
