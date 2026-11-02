using UnityEngine;
using VoiceHorror.Config;

namespace VoiceHorror.Gameplay.Creatures
{
    /// <summary>
    /// Builds each archetype's greybox body from primitives and animates it procedurally
    /// (breathing, listening head-tilt, gait, lunge stretch, flinch, dissolve). No rig or clips needed.
    /// </summary>
    public sealed class CreatureBody : MonoBehaviour
    {
        public Transform torso, head, armL, armR, legL, legR, extra;
        public Renderer[] glowRenderers;

        CreatureBrain _brain;
        Vector3[] _glowBase;
        float _flinch, _strike, _dissolve = -1f, _phase;
        Vector3 _baseScale;

        void Awake()
        {
            _brain = GetComponent<CreatureBrain>();
            _baseScale = transform.localScale;
            _phase = Random.Range(0f, 10f);
            if (glowRenderers != null)
            {
                _glowBase = new Vector3[glowRenderers.Length];
                for (int i = 0; i < glowRenderers.Length; i++)
                    if (glowRenderers[i] != null) _glowBase[i] = glowRenderers[i].transform.localScale;
            }
        }

        public void Flinch(float amount01) => _flinch = Mathf.Clamp01(0.4f + amount01 * 2f);
        public void Strike() => _strike = 1f;
        public void Dissolve() => _dissolve = 0f;

        void Update()
        {
            float dt = Time.deltaTime;
            _flinch = Mathf.MoveTowards(_flinch, 0f, dt * 2.5f);
            _strike = Mathf.MoveTowards(_strike, 0f, dt * 3f);

            if (_dissolve >= 0f)
            {
                _dissolve += dt / 2.5f;
                float s = Mathf.Clamp01(1f - _dissolve);
                transform.localScale = new Vector3(_baseScale.x * (1f + _dissolve * 0.5f), _baseScale.y * s, _baseScale.z * (1f + _dissolve * 0.5f));
                return;
            }

            float t = Time.time + _phase;
            float speed = _brain != null ? _brain.CurrentSpeed : 0f;
            var state = _brain != null ? _brain.Current : CreatureBrain.State.Idle;
            bool listening = state == CreatureBrain.State.Investigate || state == CreatureBrain.State.Search;
            float gait = Mathf.Clamp01(speed / 3f);

            if (torso != null)
            {
                float breathe = Mathf.Sin(t * 1.6f) * 0.02f;
                float hunch = state == CreatureBrain.State.Hunt ? 18f : 8f;
                if (_brain != null && _brain.IsLunging) hunch = 35f;
                torso.localRotation = Quaternion.Euler(hunch + _flinch * -25f + _strike * 20f, Mathf.Sin(t * 0.7f) * 4f, Mathf.Sin(t * 5f * gait) * 4f * gait);
                torso.localScale = new Vector3(1f, 1f + breathe, 1f);
            }
            if (head != null)
            {
                // Blind creatures turn an ear toward sound.
                float tilt = listening ? Mathf.Sin(t * 0.9f) * 25f : Mathf.Sin(t * 0.4f) * 6f;
                head.localRotation = Quaternion.Euler(listening ? -10f : 5f, Mathf.Sin(t * 0.6f) * (listening ? 30f : 10f), tilt);
            }
            float swing = Mathf.Sin(t * (4f + 6f * gait)) * (10f + 35f * gait);
            if (armL != null) armL.localRotation = Quaternion.Euler(swing - _strike * 80f, 0f, 8f);
            if (armR != null) armR.localRotation = Quaternion.Euler(-swing - _strike * 80f, 0f, -8f);
            if (legL != null) legL.localRotation = Quaternion.Euler(-swing * 0.8f, 0f, 0f);
            if (legR != null) legR.localRotation = Quaternion.Euler(swing * 0.8f, 0f, 0f);
            if (extra != null) extra.localRotation = Quaternion.Euler(0f, t * 25f, Mathf.Sin(t) * 10f);

            if (glowRenderers != null && _glowBase != null)
            {
                float pulse = state == CreatureBrain.State.Hunt || state == CreatureBrain.State.Wail ? 1f : 0.35f + 0.15f * Mathf.Sin(t * 2f);
                for (int i = 0; i < glowRenderers.Length && i < _glowBase.Length; i++)
                    if (glowRenderers[i] != null) glowRenderers[i].transform.localScale = _glowBase[i] * Mathf.Lerp(0.8f, 1.35f, pulse);
            }
        }

        // ------------------------------------------------------------ Construction

        static GameObject Part(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(euler);
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Transform Pivot(Transform parent, string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            return go.transform;
        }

        /// <summary>Creates the body under <paramref name="root"/> and returns the configured component.</summary>
        public static CreatureBody Build(GameObject root, CreatureDefinition def, Material skin, Material dark, Material glow)
        {
            var body = root.GetComponent<CreatureBody>();
            if (body == null) body = root.AddComponent<CreatureBody>();
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            float h = def.height;

            switch (def.archetype)
            {
                case CreatureArchetype.Hunter: BuildHunter(body, visual, h, skin, dark, glow); break;
                case CreatureArchetype.Sentinel: BuildSentinel(body, visual, h, skin, dark, glow); break;
                case CreatureArchetype.Heart: BuildHeart(body, visual, h, skin, dark, glow); break;
                default: BuildStalker(body, visual, h, skin, dark, glow); break;
            }
            return body;
        }

        // المسكوت: tall, thin, hunched villager; no eyes; mouth sewn shut with dark stitches.
        static void BuildStalker(CreatureBody b, Transform v, float h, Material skin, Material dark, Material glow)
        {
            b.legL = Pivot(v, "LegL", new Vector3(-0.13f, h * 0.45f, 0f));
            Part(PrimitiveType.Capsule, b.legL, "Shin", new Vector3(0f, -h * 0.22f, 0f), new Vector3(0.14f, h * 0.23f, 0.14f), skin);
            b.legR = Pivot(v, "LegR", new Vector3(0.13f, h * 0.45f, 0f));
            Part(PrimitiveType.Capsule, b.legR, "Shin", new Vector3(0f, -h * 0.22f, 0f), new Vector3(0.14f, h * 0.23f, 0.14f), skin);

            b.torso = Pivot(v, "Torso", new Vector3(0f, h * 0.45f, 0f));
            Part(PrimitiveType.Capsule, b.torso, "Chest", new Vector3(0f, h * 0.18f, 0f), new Vector3(0.38f, h * 0.2f, 0.24f), skin);
            Part(PrimitiveType.Cube, b.torso, "Rags", new Vector3(0f, h * 0.06f, 0f), new Vector3(0.45f, h * 0.22f, 0.3f), dark);

            b.armL = Pivot(b.torso, "ArmL", new Vector3(-0.25f, h * 0.33f, 0f));
            Part(PrimitiveType.Capsule, b.armL, "Arm", new Vector3(0f, -h * 0.25f, 0.02f), new Vector3(0.09f, h * 0.27f, 0.09f), skin);
            b.armR = Pivot(b.torso, "ArmR", new Vector3(0.25f, h * 0.33f, 0f));
            Part(PrimitiveType.Capsule, b.armR, "Arm", new Vector3(0f, -h * 0.25f, 0.02f), new Vector3(0.09f, h * 0.27f, 0.09f), skin);

            b.head = Pivot(b.torso, "Head", new Vector3(0f, h * 0.42f, 0.04f));
            Part(PrimitiveType.Sphere, b.head, "Skull", new Vector3(0f, 0.1f, 0f), new Vector3(0.26f, 0.32f, 0.27f), skin);
            for (int i = 0; i < 4; i++)
                Part(PrimitiveType.Cube, b.head, "Stitch" + i, new Vector3(-0.06f + i * 0.04f, 0.0f, 0.13f), new Vector3(0.012f, 0.06f, 0.01f), dark);
            Part(PrimitiveType.Cube, b.head, "Mouth", new Vector3(0f, 0.0f, 0.128f), new Vector3(0.14f, 0.01f, 0.01f), dark);
            var g1 = Part(PrimitiveType.Sphere, b.head, "Ear", new Vector3(-0.13f, 0.1f, 0f), Vector3.one * 0.1f, glow);
            var g2 = Part(PrimitiveType.Sphere, b.head, "Ear", new Vector3(0.13f, 0.1f, 0f), Vector3.one * 0.1f, glow);
            b.glowRenderers = new[] { g1.GetComponent<Renderer>(), g2.GetComponent<Renderer>() };
        }

        // السعلوة: low, long-limbed, long dark hair that hides the face, two pale slits.
        static void BuildHunter(CreatureBody b, Transform v, float h, Material skin, Material dark, Material glow)
        {
            b.legL = Pivot(v, "LegL", new Vector3(-0.2f, h * 0.4f, -0.2f));
            Part(PrimitiveType.Capsule, b.legL, "Leg", new Vector3(0f, -h * 0.2f, 0f), new Vector3(0.13f, h * 0.22f, 0.13f), skin);
            b.legR = Pivot(v, "LegR", new Vector3(0.2f, h * 0.4f, -0.2f));
            Part(PrimitiveType.Capsule, b.legR, "Leg", new Vector3(0f, -h * 0.2f, 0f), new Vector3(0.13f, h * 0.22f, 0.13f), skin);

            b.torso = Pivot(v, "Torso", new Vector3(0f, h * 0.42f, -0.15f));
            Part(PrimitiveType.Capsule, b.torso, "Spine", new Vector3(0f, 0.1f, 0.35f), new Vector3(0.34f, 0.5f, 0.3f), skin, new Vector3(70f, 0f, 0f));
            b.armL = Pivot(b.torso, "ArmL", new Vector3(-0.22f, 0.2f, 0.7f));
            Part(PrimitiveType.Capsule, b.armL, "Arm", new Vector3(0f, -h * 0.25f, 0.1f), new Vector3(0.1f, h * 0.3f, 0.1f), skin);
            b.armR = Pivot(b.torso, "ArmR", new Vector3(0.22f, 0.2f, 0.7f));
            Part(PrimitiveType.Capsule, b.armR, "Arm", new Vector3(0f, -h * 0.25f, 0.1f), new Vector3(0.1f, h * 0.3f, 0.1f), skin);

            b.head = Pivot(b.torso, "Head", new Vector3(0f, 0.3f, 0.85f));
            Part(PrimitiveType.Sphere, b.head, "Skull", Vector3.zero, new Vector3(0.28f, 0.3f, 0.3f), skin);
            for (int i = 0; i < 7; i++)
            {
                float x = -0.15f + i * 0.05f;
                Part(PrimitiveType.Capsule, b.head, "Hair" + i, new Vector3(x, -0.25f, 0.1f + (i % 2) * 0.03f), new Vector3(0.05f, 0.35f + (i % 3) * 0.08f, 0.05f), dark);
            }
            var e1 = Part(PrimitiveType.Sphere, b.head, "Slit", new Vector3(-0.06f, 0.04f, 0.15f), new Vector3(0.07f, 0.015f, 0.02f), glow);
            var e2 = Part(PrimitiveType.Sphere, b.head, "Slit", new Vector3(0.06f, 0.04f, 0.15f), new Vector3(0.07f, 0.015f, 0.02f), glow);
            b.glowRenderers = new[] { e1.GetComponent<Renderer>(), e2.GetComponent<Renderer>() };
        }

        // بو درياه: a huge kelp-draped torso rising from the water/well, fin-like ears, wide jaw.
        static void BuildSentinel(CreatureBody b, Transform v, float h, Material skin, Material dark, Material glow)
        {
            b.torso = Pivot(v, "Torso", new Vector3(0f, 0f, 0f));
            Part(PrimitiveType.Capsule, b.torso, "Bulk", new Vector3(0f, h * 0.4f, 0f), new Vector3(1.3f, h * 0.45f, 1.0f), skin);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                Part(PrimitiveType.Capsule, b.torso, "Kelp" + i, new Vector3(Mathf.Cos(a) * 0.55f, h * 0.45f, Mathf.Sin(a) * 0.45f), new Vector3(0.12f, h * 0.35f, 0.08f), dark, new Vector3(Mathf.Sin(a) * 12f, 0f, Mathf.Cos(a) * 12f));
            }
            b.head = Pivot(b.torso, "Head", new Vector3(0f, h * 0.85f, 0.1f));
            Part(PrimitiveType.Sphere, b.head, "Skull", Vector3.zero, new Vector3(0.9f, 0.6f, 0.8f), skin);
            Part(PrimitiveType.Cube, b.head, "Jaw", new Vector3(0f, -0.18f, 0.32f), new Vector3(0.7f, 0.08f, 0.2f), dark);
            b.armL = Pivot(b.head, "FinL", new Vector3(-0.45f, 0.1f, 0f));
            Part(PrimitiveType.Cube, b.armL, "Fin", new Vector3(-0.3f, 0.15f, 0f), new Vector3(0.6f, 0.5f, 0.04f), dark, new Vector3(0f, 0f, 25f));
            b.armR = Pivot(b.head, "FinR", new Vector3(0.45f, 0.1f, 0f));
            Part(PrimitiveType.Cube, b.armR, "Fin", new Vector3(0.3f, 0.15f, 0f), new Vector3(0.6f, 0.5f, 0.04f), dark, new Vector3(0f, 0f, -25f));
            var g = Part(PrimitiveType.Sphere, b.head, "Throat", new Vector3(0f, -0.25f, 0.3f), Vector3.one * 0.1f, glow);
            b.glowRenderers = new[] { g.GetComponent<Renderer>() };
        }

        // الصمّة: a floating black mass ringed by sewn-shut mouths, pulsing pale light.
        static void BuildHeart(CreatureBody b, Transform v, float h, Material skin, Material dark, Material glow)
        {
            b.torso = Pivot(v, "Core", new Vector3(0f, h * 0.55f, 0f));
            Part(PrimitiveType.Sphere, b.torso, "Mass", Vector3.zero, Vector3.one * h * 0.55f, dark);
            var core = Part(PrimitiveType.Sphere, b.torso, "Glow", new Vector3(0f, 0f, h * 0.2f), Vector3.one * h * 0.18f, glow);
            b.extra = Pivot(b.torso, "Ring", Vector3.zero);
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                var mouth = Pivot(b.extra, "Mouth" + i, new Vector3(Mathf.Cos(a) * h * 0.45f, Mathf.Sin(a * 2f) * 0.3f, Mathf.Sin(a) * h * 0.45f));
                Part(PrimitiveType.Sphere, mouth, "Lips", Vector3.zero, new Vector3(0.4f, 0.22f, 0.25f), skin);
                Part(PrimitiveType.Cube, mouth, "Seam", new Vector3(0f, 0f, 0.1f), new Vector3(0.32f, 0.02f, 0.06f), dark);
            }
            b.head = Pivot(b.torso, "Crown", new Vector3(0f, h * 0.3f, 0f));
            b.glowRenderers = new[] { core.GetComponent<Renderer>() };
        }
    }
}
