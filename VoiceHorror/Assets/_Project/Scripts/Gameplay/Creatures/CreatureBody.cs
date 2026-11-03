using System.Collections.Generic;
using UnityEngine;
using VoiceHorror.Art;
using VoiceHorror.Audio;
using VoiceHorror.Characters;
using VoiceHorror.Config;
using VoiceHorror.Core;

namespace VoiceHorror.Gameplay.Creatures
{
    /// <summary>
    /// Procedural animation for every creature: foot-planting IK gait, per-state body language
    /// (blind listening, reaching hunt, recoil, wail, death collapse, dormant/waking), archetype
    /// extras (Bu Daryah's fins and slam, the Heart's ring of mouths) and footstep sounds.
    /// Driven by <see cref="CreatureBrain"/>, or externally (character gallery).
    /// </summary>
    [RequireComponent(typeof(ProceduralCharacter))]
    [DefaultExecutionOrder(100)]
    public sealed class CreatureBody : MonoBehaviour
    {
        public enum Pose { Idle, Listen, Hunt, Stagger, Flee, Wail, Dead, Dormant, Waking }

        [Header("External control (gallery / cutscenes)")]
        public bool externalControl;
        public Pose externalPose = Pose.Idle;
        public Vector3 externalLookTarget;

        [Header("Feel")]
        public float twitchiness = 1f;
        public float footstepVolume = 0.5f;

        sealed class Leg
        {
            public Transform upper, lower, end;
            public int group;
            public Vector3 pole;          // body space
            public Vector3 homeLocal;     // body space, on the ground
            public Vector3 foot;          // world
            public Vector3 stepFrom;
            public float stepT = 1f;
            public bool frontLeg;
        }

        ProceduralCharacter _ch;
        CreatureBrain _brain;
        CreatureArchetype _archetype;
        bool _quadruped;
        readonly List<Leg> _legs = new List<Leg>();
        Transform _hips, _spine, _chest, _neck, _head, _jaw, _armL, _armR, _foreL, _foreR, _finL, _finR, _ring;
        readonly List<(Transform up, Transform lo)> _mouths = new List<(Transform, Transform)>();
        Vector3 _hipsRestPos;
        float _scale = 1f;
        float _stepLength, _stepHeight;

        // Smoothed pose weights.
        float _wHunt, _wListen, _wWail, _wStagger, _wFlee, _wDormant;
        float _flinch, _strike, _deathT = -1f, _wakeT;
        float _twitchTimer, _twitchYaw, _twitchRoll;
        float _gaitPhase;
        Vector3 _lastPos, _velocity;
        Vector3 _deathAxis;
        AudioSource _stepAudio;
        bool _initialized;
        Pose _pose;

        public Pose CurrentPose => _pose;
        readonly bool[] _groupStepping = new bool[4];

        // ------------------------------------------------------------ Construction (scene builders)

        public static string BlueprintFor(CreatureArchetype a)
        {
            switch (a)
            {
                case CreatureArchetype.Hunter: return Blueprints.Siluwa;
                case CreatureArchetype.Sentinel: return Blueprints.BuDaryah;
                case CreatureArchetype.Heart: return Blueprints.Summah;
                default: return Blueprints.Maskut;
            }
        }

        /// <summary>Adds the procedural character and this animator to a creature root.</summary>
        public static CreatureBody Build(GameObject root, CreatureDefinition def, CharacterPalette palette, int seed = 1)
        {
            var ch = root.GetComponent<ProceduralCharacter>();
            if (ch == null) ch = root.AddComponent<ProceduralCharacter>();
            ch.blueprintId = BlueprintFor(def.archetype);
            ch.seed = seed;
            ch.palette = palette;
            float bpHeight = def.archetype == CreatureArchetype.Hunter ? 1.5f : def.archetype == CreatureArchetype.Sentinel ? 3.2f : def.archetype == CreatureArchetype.Heart ? 4.2f : 2.0f;
            ch.scale = def.height / bpHeight;
            var body = root.GetComponent<CreatureBody>();
            if (body == null) body = root.AddComponent<CreatureBody>();
            return body;
        }

        // ------------------------------------------------------------ Events from the brain

        public void Flinch(float amount01) => _flinch = Mathf.Clamp01(0.45f + amount01 * 2f);
        public void Strike() => _strike = 1f;
        public void Dissolve()
        {
            if (_deathT >= 0f) return;
            _deathT = 0f;
            _deathAxis = _quadruped ? Vector3.forward : Vector3.right;
        }

        /// <summary>Undo a death animation (gallery loops).</summary>
        public void Revive()
        {
            _deathT = -1f;
            var root = _ch != null ? _ch.VisualRoot : null;
            if (root == null) return;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one * _scale;
            if (_hips != null) _hips.localPosition = _hipsRestPos;
            SnapFeet();
        }

        // ------------------------------------------------------------ Setup

        void Awake()
        {
            _ch = GetComponent<ProceduralCharacter>();
            _brain = GetComponent<CreatureBrain>();
        }

        void Start() => Init();

        void Init()
        {
            if (_initialized) return;
            _ch.Build();
            var bp = _ch.Blueprint;
            _scale = _ch.scale;
            _archetype = _brain != null && _brain.definition != null ? _brain.definition.archetype : ArchetypeFor(bp.Id);
            _quadruped = bp.Roles.ContainsKey("quadruped");
            _hips = _ch.Role("hips"); _spine = _ch.Role("spine"); _chest = _ch.Role("chest");
            _neck = _ch.Role("neck"); _head = _ch.Role("head"); _jaw = _ch.Role("jaw");
            _armL = _ch.Role("armL"); _armR = _ch.Role("armR"); _foreL = _ch.Role("foreL"); _foreR = _ch.Role("foreR");
            _finL = _ch.Role("finL"); _finR = _ch.Role("finR"); _ring = _ch.Role("ring");
            if (_hips != null) _hipsRestPos = _hips.localPosition;
            for (int i = 0; i < 16; i++)
            {
                var up = _ch.Bone("Mouth" + i + "Up");
                if (up == null) break;
                _mouths.Add((up, _ch.Bone("Mouth" + i + "Lo")));
            }
            _stepLength = bp.StepLength * _scale;
            _stepHeight = bp.StepHeight * _scale;

            foreach (var ld in bp.Legs)
            {
                var leg = new Leg
                {
                    upper = _ch.Bone(ld.Upper), lower = _ch.Bone(ld.Lower), end = _ch.Bone(ld.End),
                    group = ld.Group, pole = ld.Pole.ToUnity(), frontLeg = ld.Upper.StartsWith("UpperArm"),
                };
                if (leg.upper == null || leg.lower == null || leg.end == null) continue;
                Vector3 endLocal = transform.InverseTransformPoint(leg.end.position);
                Vector3 hipLocal = transform.InverseTransformPoint(leg.upper.position);
                // Feet rest under (and slightly forward of) their hip, on the ground.
                leg.homeLocal = new Vector3(Mathf.Lerp(hipLocal.x, endLocal.x, 0.5f), 0f, Mathf.Lerp(hipLocal.z, endLocal.z, 0.6f));
                leg.foot = transform.TransformPoint(leg.homeLocal);
                _legs.Add(leg);
            }

            _stepAudio = ProceduralAudio.MakeSource(gameObject, ProceduralAudio.Step, false, footstepVolume, 1f, 18f);
            _lastPos = transform.position;
            _initialized = true;
        }

        static CreatureArchetype ArchetypeFor(string id)
        {
            switch (id)
            {
                case Blueprints.Siluwa: return CreatureArchetype.Hunter;
                case Blueprints.BuDaryah: return CreatureArchetype.Sentinel;
                case Blueprints.Summah: return CreatureArchetype.Heart;
                default: return CreatureArchetype.Stalker;
            }
        }

        Pose PoseFromBrain()
        {
            if (_brain == null) return Pose.Idle;
            switch (_brain.Current)
            {
                case CreatureBrain.State.Investigate:
                case CreatureBrain.State.Search: return Pose.Listen;
                case CreatureBrain.State.Hunt: return Pose.Hunt;
                case CreatureBrain.State.Stagger: return Pose.Stagger;
                case CreatureBrain.State.Flee: return Pose.Flee;
                case CreatureBrain.State.Wail: return Pose.Wail;
                case CreatureBrain.State.Dead: return Pose.Dead;
                case CreatureBrain.State.Dormant: return Pose.Dormant;
                case CreatureBrain.State.Waking: return Pose.Waking;
                default: return Pose.Idle;
            }
        }

        // ------------------------------------------------------------ Per-frame

        void LateUpdate()
        {
            if (!_initialized) Init();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            var pose = externalControl ? externalPose : PoseFromBrain();
            if (pose == Pose.Waking && _pose != Pose.Waking) _wakeT = 0f;
            _pose = pose;
            if (pose == Pose.Dead && _deathT < 0f) Dissolve();

            Vector3 rawVel = (transform.position - _lastPos) / dt;
            _lastPos = transform.position;
            if (rawVel.magnitude > 20f) { rawVel = Vector3.zero; SnapFeet(); } // teleport
            _velocity = Vector3.Lerp(_velocity, rawVel, 1f - Mathf.Exp(-dt * 10f));
            float speed = new Vector3(_velocity.x, 0f, _velocity.z).magnitude;

            float k = 1f - Mathf.Exp(-dt * 6f);
            _wHunt = Mathf.Lerp(_wHunt, pose == Pose.Hunt ? 1f : 0f, k);
            _wListen = Mathf.Lerp(_wListen, pose == Pose.Listen ? 1f : 0f, k);
            _wWail = Mathf.Lerp(_wWail, pose == Pose.Wail ? 1f : 0f, 1f - Mathf.Exp(-dt * 9f));
            _wStagger = Mathf.Lerp(_wStagger, pose == Pose.Stagger ? 1f : 0f, 1f - Mathf.Exp(-dt * 14f));
            _wFlee = Mathf.Lerp(_wFlee, pose == Pose.Flee ? 1f : 0f, k);
            if (pose == Pose.Waking) _wakeT += dt;
            float dormantTarget = pose == Pose.Dormant ? 1f : pose == Pose.Waking ? 1f - Mathf.SmoothStep(0f, 1f, _wakeT / 1.6f) : 0f;
            _wDormant = pose == Pose.Dormant ? 1f : Mathf.MoveTowards(_wDormant, dormantTarget, dt * 2f);
            _flinch = Mathf.MoveTowards(_flinch, 0f, dt * 2.2f);
            _strike = Mathf.MoveTowards(_strike, 0f, dt / 0.5f);

            UpdateTwitch(dt, pose);

            if (_deathT >= 0f)
            {
                AnimateDeath(dt);
                return;
            }

            switch (_archetype)
            {
                case CreatureArchetype.Sentinel: AnimateSentinel(dt); break;
                case CreatureArchetype.Heart: AnimateHeart(dt); break;
                default:
                    AnimateBody(dt, speed);
                    AnimateLegs(dt, speed);
                    break;
            }
        }

        Vector3 LookTarget => externalControl ? externalLookTarget : (_brain != null ? _brain.LastHeardPosition : transform.position + transform.forward);

        float SoundYaw()
        {
            Vector3 to = LookTarget - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) return 0f;
            return Mathf.Clamp(Vector3.SignedAngle(transform.forward, to, Vector3.up), -80f, 80f);
        }

        void UpdateTwitch(float dt, Pose pose)
        {
            // Unnatural, sudden head snaps: the tell that these used to be people.
            _twitchTimer -= dt;
            if (_twitchTimer <= 0f)
            {
                _twitchTimer = Random.Range(1.2f, 4f) / Mathf.Max(0.2f, twitchiness * (pose == Pose.Hunt ? 2f : 1f));
                _twitchYaw = Random.Range(-25f, 25f) * twitchiness;
                _twitchRoll = Random.Range(-30f, 30f) * twitchiness;
            }
            float decay = 1f - Mathf.Exp(-dt * 7f);
            _twitchYaw = Mathf.Lerp(_twitchYaw, 0f, decay);
            _twitchRoll = Mathf.Lerp(_twitchRoll, 0f, decay);
        }

        void Additive(Transform bone, Vector3 euler)
        {
            if (bone == null) return;
            bone.localRotation = _ch.RestLocal(bone) * Quaternion.Euler(euler);
        }

        float StrikeCurve()
        {
            // 1 → 0 over the attack: wind up (negative), then a fast swipe (positive peak), then recover.
            float t = 1f - _strike;
            if (_strike <= 0f) return 0f;
            if (t < 0.35f) return -Mathf.Sin(t / 0.35f * Mathf.PI * 0.5f);
            if (t < 0.55f) return Mathf.Lerp(-1f, 1.4f, (t - 0.35f) / 0.2f);
            return Mathf.Lerp(1.4f, 0f, (t - 0.55f) / 0.45f);
        }

        // ------------------------------------------------------------ Bipeds & quadrupeds

        void AnimateBody(float dt, float speed)
        {
            float t = Time.time + GetInstanceID() * 0.37f;
            float breathe = Mathf.Sin(t * 1.4f);
            float strike = StrikeCurve();
            float yaw = SoundYaw();
            float gaitSwing = Mathf.Sin(_gaitPhase * Mathf.PI) * Mathf.Clamp01(speed / 2f);

            // Hips: crouch for hunt/flee, sit when dormant, bob with the gait.
            if (_hips != null)
            {
                float crouch = (_wHunt * 0.06f + _wFlee * 0.1f + _wStagger * 0.04f) * _ch.Blueprint.Height;
                float sit = _wDormant * (_quadruped ? 0.45f : 0.62f);
                float bob = Mathf.Abs(gaitSwing) * 0.03f;
                Vector3 p = _hipsRestPos;
                p.y = Mathf.Max(0.15f, p.y - crouch - bob - sit * _hipsRestPos.y);
                _hips.localPosition = p;
                _hips.localRotation = _ch.RestLocal(_hips) * Quaternion.Euler(-_wDormant * 18f, 0f, Mathf.Sin(t * 0.9f) * 2f);
            }

            float lean = _wHunt * 14f - _wListen * 4f - _wStagger * 28f - _flinch * 24f + strike * 12f + _wDormant * 25f;
            Additive(_spine, new Vector3(lean * 0.55f + breathe * 1.2f, yaw * 0.15f * _wListen, Mathf.Sin(t * 0.7f) * 3f));
            Additive(_chest, new Vector3(lean * 0.45f + breathe * 2f, yaw * 0.2f * _wListen + _wStagger * Mathf.Sin(t * 30f) * 6f, 0f));

            // Head: blind — it turns an ear toward the sound and tilts; it thrusts forward when hunting.
            float headYaw = yaw * (0.55f * _wListen + 0.35f * _wHunt) + _twitchYaw;
            float headRoll = -Mathf.Sign(yaw) * Mathf.Min(Mathf.Abs(yaw), 40f) * 0.6f * _wListen + _twitchRoll * (1f - _wDormant);
            float headPitch = -_wHunt * 8f + _wDormant * 30f - _wStagger * 20f + Mathf.Sin(t * 0.5f) * 3f;
            Additive(_neck, new Vector3(headPitch * 0.4f, headYaw * 0.35f, headRoll * 0.3f));
            Additive(_head, new Vector3(headPitch * 0.6f, headYaw * 0.65f, headRoll * 0.7f));

            // Jaw strains against the stitches.
            float jaw = _wHunt * 0.35f + _wStagger * 0.6f + Mathf.Max(0f, strike) * 0.4f + _wListen * 0.1f * Mathf.Max(0f, Mathf.Sin(t * 9f));
            Additive(_jaw, new Vector3(jaw * 22f, 0f, 0f));

            // Arms: hang and swing; feel the air ahead when listening; reach when hunting; claw on attack.
            if (!_quadruped)
            {
                float reach = _wListen * 35f + _wHunt * 65f;
                float flail = _wStagger * Mathf.Sin(t * 22f) * 25f;
                float strikeL = strike * 80f, strikeR = strike * 95f;
                Additive(_armL, new Vector3(-reach - strikeL - gaitSwing * 18f + flail - _wDormant * 30f, 0f, -_wStagger * 20f));
                Additive(_armR, new Vector3(-reach * 0.85f - strikeR + gaitSwing * 18f - flail - _wDormant * 25f, 0f, _wStagger * 20f));
                Additive(_foreL, new Vector3(-_wHunt * 15f - _wListen * 10f + Mathf.Max(0f, -strike) * 50f, 0f, 0f));
                Additive(_foreR, new Vector3(-_wHunt * 15f - _wListen * 20f + Mathf.Max(0f, -strike) * 50f, 0f, 0f));
            }
        }

        void AnimateLegs(float dt, float speed)
        {
            if (_legs.Count == 0) return;
            Vector3 fwd = transform.forward;
            float ground = transform.position.y;
            float lead = Mathf.Clamp(speed * 0.12f, 0f, _stepLength * 0.8f);
            Vector3 flatVel = new Vector3(_velocity.x, 0f, _velocity.z);
            Vector3 leadOffset = speed > 0.05f ? flatVel.normalized * lead : Vector3.zero;
            float threshold = speed > 0.2f ? _stepLength * 0.5f : _stepLength * 0.18f;
            float duration = Mathf.Clamp(_stepLength / Mathf.Max(speed, 0.6f) * 0.55f, 0.11f, 0.32f);

            var groupStepping = _groupStepping;
            System.Array.Clear(groupStepping, 0, groupStepping.Length);
            foreach (var l in _legs) if (l.stepT < 1f) groupStepping[Mathf.Clamp(l.group, 0, 3)] = true;

            foreach (var leg in _legs)
            {
                Vector3 home = transform.TransformPoint(leg.homeLocal) + leadOffset;
                if (_wDormant > 0.01f && !_quadruped)
                {
                    // Sitting: feet out in front with the knees drawn up.
                    home = Vector3.Lerp(home, transform.TransformPoint(leg.homeLocal + new Vector3(0f, 0f, 0.55f * _scale)), _wDormant);
                }
                home.y = ground;

                if (leg.stepT < 1f)
                {
                    leg.stepT = Mathf.Min(1f, leg.stepT + dt / duration);
                    float s = Mathf.SmoothStep(0f, 1f, leg.stepT);
                    leg.foot = Vector3.Lerp(leg.stepFrom, home, s) + Vector3.up * (Mathf.Sin(leg.stepT * Mathf.PI) * _stepHeight);
                    if (leg.stepT >= 1f) Footstep(leg, speed);
                }
                else
                {
                    bool otherGroupStepping = false;
                    for (int g = 0; g < groupStepping.Length; g++) if (g != leg.group && groupStepping[g]) otherGroupStepping = true;
                    float d = Vector3.Distance(new Vector3(leg.foot.x, 0f, leg.foot.z), new Vector3(home.x, 0f, home.z));
                    if (d > threshold && !otherGroupStepping)
                    {
                        leg.stepFrom = leg.foot;
                        leg.stepT = 0f;
                        groupStepping[Mathf.Clamp(leg.group, 0, 3)] = true;
                        _gaitPhase = leg.group == 0 ? 0f : 1f;
                    }
                    leg.foot.y = ground;
                }

                Vector3 pole = transform.TransformDirection(leg.pole);
                TwoBoneIK.Solve(leg.upper, leg.lower, leg.end, leg.foot + Vector3.up * (0.04f * _scale), pole, fwd);
                // Plant the foot (or front paw) flat on the ground, facing forward.
                leg.end.rotation = leg.frontLeg ? transform.rotation * Quaternion.Euler(-80f, 0f, 0f) : transform.rotation;
            }
            if (speed > 0.05f) _gaitPhase = Mathf.Repeat(_gaitPhase + dt / Mathf.Max(0.15f, duration * 2f) * 2f, 2f);
        }

        void SnapFeet()
        {
            foreach (var l in _legs)
            {
                l.foot = transform.TransformPoint(l.homeLocal);
                l.stepT = 1f;
            }
        }

        void Footstep(Leg leg, float speed)
        {
            if (_stepAudio == null || speed < 0.3f) return;
            _stepAudio.transform.position = leg.foot;
            _stepAudio.pitch = Random.Range(0.55f, 0.75f) * (_quadruped ? 1.25f : 1f);
            _stepAudio.volume = footstepVolume * Mathf.Clamp01(speed / 3f + 0.25f);
            _stepAudio.Play();
        }

        // ------------------------------------------------------------ بو درياه

        void AnimateSentinel(float dt)
        {
            float t = Time.time;
            float yaw = SoundYaw();
            float strike = StrikeCurve();
            float listen = Mathf.Max(_wListen, _wHunt);
            Additive(_spine, new Vector3(Mathf.Sin(t * 0.5f) * 2f - _wWail * 8f - _flinch * 12f, Mathf.Sin(t * 0.3f) * 4f + yaw * 0.15f * listen, Mathf.Sin(t * 0.4f) * 2f));
            Additive(_chest, new Vector3(Mathf.Sin(t * 1.1f) * 1.5f - _wWail * 10f, yaw * 0.2f * listen, 0f));
            Additive(_head, new Vector3(-_wWail * 35f + Mathf.Sin(t * 0.7f) * 3f, yaw * 0.5f * listen + _twitchYaw * 0.5f, -Mathf.Sign(yaw) * 12f * listen));
            float jaw = _wWail * (0.8f + 0.2f * Mathf.Sin(t * 25f)) + 0.08f + 0.05f * Mathf.Sin(t * 0.8f);
            Additive(_jaw, new Vector3(jaw * 35f, 0f, 0f));
            if (_jaw != null)
            {
                // The throat sac swells during the wail.
                float sac = 1f + _wWail * 0.35f * (0.8f + 0.2f * Mathf.Sin(t * 18f));
                _jaw.localScale = new Vector3(sac, 1f + _wWail * 0.15f, sac);
            }
            float flare = 25f * listen + 40f * _wWail + Mathf.Sin(t * 2.2f) * 6f;
            Additive(_finL, new Vector3(0f, -flare * 0.4f, flare));
            Additive(_finR, new Vector3(0f, flare * 0.4f, -flare));
            // Arms: rest on the ground; rise and spread with the wail; slam toward the target on attack.
            bool leftSlams = yaw < 0f;
            float slam = strike * 70f;
            Additive(_armL, new Vector3(-_wWail * 55f - (leftSlams ? slam : 0f) + Mathf.Sin(t * 0.6f) * 3f, 0f, -_wWail * 25f));
            Additive(_armR, new Vector3(-_wWail * 55f - (!leftSlams ? slam : 0f) + Mathf.Sin(t * 0.6f + 1f) * 3f, 0f, _wWail * 25f));
            Additive(_foreL, new Vector3(_wWail * 30f, 0f, 0f));
            Additive(_foreR, new Vector3(_wWail * 30f, 0f, 0f));
        }

        // ------------------------------------------------------------ الصمّة

        void AnimateHeart(float dt)
        {
            float t = Time.time;
            float agitation = Mathf.Max(_wHunt, _wWail) + _flinch;
            if (_ring != null) _ring.localRotation *= Quaternion.Euler(0f, (12f + 45f * agitation) * dt, 0f);
            if (_chest != null)
            {
                float beat = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * (2.2f + agitation * 2f))), 8f);
                _chest.localScale = Vector3.one * (1f + beat * 0.04f + _flinch * 0.06f);
                _chest.localPosition = _ch.RestLocalPosition(_chest) + Vector3.up * (Mathf.Sin(t * 0.6f) * 0.12f * _scale);
            }
            // Stolen mouths: they mumble, and gape when the player's voice reaches them.
            float voice = 0f;
            var meter = VoiceLevelMeter.Instance;
            var player = Player.PlayerController.Instance;
            if (meter != null && player != null)
            {
                float d = Vector3.Distance(player.transform.position, transform.position);
                voice = meter.Loudness01 * Mathf.Clamp01(1f - d / 40f);
            }
            for (int i = 0; i < _mouths.Count; i++)
            {
                float flutter = Mathf.PerlinNoise(i * 3.1f, t * 2.5f);
                float open = Mathf.Clamp01(0.1f + flutter * 0.25f + voice * 1.2f + _wWail * 0.8f);
                var (up, lo) = _mouths[i];
                if (up != null) up.localPosition = _ch.RestLocalPosition(up) + Vector3.up * (open * 0.07f);
                if (lo != null) lo.localPosition = _ch.RestLocalPosition(lo) - Vector3.up * (open * 0.07f);
            }
        }

        // ------------------------------------------------------------ Death

        void AnimateDeath(float dt)
        {
            _deathT += dt;
            var root = _ch.VisualRoot;
            if (root == null) return;
            switch (_archetype)
            {
                case CreatureArchetype.Sentinel:
                    root.localPosition = Vector3.down * (Mathf.SmoothStep(0f, 1f, _deathT / 2.6f) * 3f * _scale);
                    Additive(_head, new Vector3(-40f, 0f, 0f));
                    Additive(_jaw, new Vector3(40f, 0f, 0f));
                    break;
                case CreatureArchetype.Heart:
                    float s = 1f - Mathf.SmoothStep(0f, 1f, _deathT / 2.6f);
                    root.localScale = new Vector3(_scale * (1f + (1f - s) * 0.4f), _scale * s, _scale * (1f + (1f - s) * 0.4f));
                    if (_ring != null) _ring.localRotation *= Quaternion.Euler(0f, 200f * dt, 0f);
                    foreach (var (up, lo) in _mouths)
                    {
                        if (up != null) up.localPosition = _ch.RestLocalPosition(up) + Vector3.up * 0.09f;
                        if (lo != null) lo.localPosition = _ch.RestLocalPosition(lo) - Vector3.up * 0.09f;
                    }
                    break;
                default:
                    // Knees buckle, the body topples, then it sinks into the ground.
                    float fall = Mathf.SmoothStep(0f, 1f, _deathT / 1.1f);
                    root.localRotation = Quaternion.AngleAxis(fall * 82f, _deathAxis);
                    root.localPosition = Vector3.down * (Mathf.Max(0f, _deathT - 1.4f) * 0.5f * _scale);
                    if (_hips != null) _hips.localPosition = Vector3.Lerp(_hipsRestPos, _hipsRestPos * 0.45f, fall);
                    Additive(_head, new Vector3(30f * fall, 0f, 25f * fall));
                    Additive(_jaw, new Vector3(18f, 0f, 0f));
                    Additive(_armL, new Vector3(-60f * fall, 0f, -30f * fall));
                    Additive(_armR, new Vector3(-20f * fall, 0f, 40f * fall));
                    break;
            }
        }
    }
}
