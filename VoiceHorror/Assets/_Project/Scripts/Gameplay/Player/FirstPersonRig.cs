using UnityEngine;
using VoiceHorror.Art;
using VoiceHorror.Audio;
using VoiceHorror.Characters;
using VoiceHorror.Core;
using VoiceHorror.Dsp;
using VoiceHorror.Gameplay.Noise;
using VoiceHorror.Gameplay.Weapons;
using VoiceHorror.Persistence;

namespace VoiceHorror.Gameplay.Player
{
    /// <summary>
    /// Reem's arms in first person: the lantern in the left hand, the selected relic in the right.
    /// Everything moves with the player: walk bob, mouse sway, fearful trembling with threat,
    /// the lantern breathing with the live voice level, and a gesture per weapon while singing
    /// (shell to the lips, drum raised, open palm for the Breath).
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class FirstPersonRig : MonoBehaviour
    {
        public CharacterPalette palette = new CharacterPalette();
        public Light lanternLight;
        public float lanternIntensity = 1.4f;

        ProceduralCharacter _arms, _lantern, _shell, _drum;
        Transform _armL, _armR;
        Vector3 _restL, _restR;
        Quaternion _restRotL, _restRotR;
        Light _relicLight;
        WeaponController _weapons;
        PlayerController _player;
        PlayerHealth _health;
        Vector2 _sway;
        float _bobPhase, _perform, _lower, _punch, _hidden, _dead, _lastResultSeen = -1f;
        bool _lastWasSuccess;

        void Start()
        {
            _player = GetComponentInParent<PlayerController>();
            _weapons = GetComponentInParent<WeaponController>();
            _health = GetComponentInParent<PlayerHealth>();

            _arms = Spawn(transform, Blueprints.ReemArms, "Arms");
            _armL = _arms.Role("armL");
            _armR = _arms.Role("armR");
            _restL = _armL.localPosition; _restR = _armR.localPosition;
            _restRotL = _armL.localRotation; _restRotR = _armR.localRotation;

            _lantern = Spawn(_arms.Role("propL"), Blueprints.PropLantern, "Lantern");
            var flame = _lantern.Role("flame");
            if (lanternLight == null)
            {
                var lg = new GameObject("Lantern Light");
                lanternLight = lg.AddComponent<Light>();
                lanternLight.type = LightType.Point;
                lanternLight.range = 7f;
                lanternLight.color = new Color(1f, 0.72f, 0.42f);
                lanternLight.shadows = LightShadows.None;
            }
            lanternLight.transform.SetParent(flame != null ? flame : _lantern.transform, false);
            lanternLight.transform.localPosition = Vector3.zero;

            var propR = _arms.Role("propR");
            _shell = Spawn(propR, Blueprints.PropShell, "Shell");
            _drum = Spawn(propR, Blueprints.PropDrum, "Drum");
            _drum.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _drum.transform.localPosition = new Vector3(0f, 0.02f, 0.12f);
            var rl = new GameObject("Relic Light");
            rl.transform.SetParent(propR, false);
            _relicLight = rl.AddComponent<Light>();
            _relicLight.type = LightType.Point;
            _relicLight.range = 3f;
            _relicLight.intensity = 0f;
            _relicLight.shadows = LightShadows.None;
        }

        ProceduralCharacter Spawn(Transform parent, string id, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.SetActive(false); // configure before Awake builds it
            var pc = go.AddComponent<ProceduralCharacter>();
            pc.blueprintId = id;
            pc.palette = palette;
            pc.castShadows = false;
            pc.strands = false;
            go.SetActive(true);
            foreach (var r in pc.Renderers) r.gameObject.layer = gameObject.layer;
            return pc;
        }

        void LateUpdate()
        {
            if (_armL == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float t = Time.time;
            var meter = VoiceLevelMeter.Instance;
            float voice = meter != null ? meter.Loudness01 : 0f;
            float threat = ThreatMeter.Instance != null ? ThreatMeter.Instance.Threat01 : 0f;

            // --- Which relic is in the right hand.
            string relic = _weapons != null && _weapons.Selected != null && _weapons.Selected.unlocked ? _weapons.Selected.def.id : "";
            _shell.gameObject.SetActive(relic == "w_sadafa");
            _drum.gameObject.SetActive(relic == "w_tar");
            Color relicColor = _weapons?.Selected?.def != null ? _weapons.Selected.def.color : Color.white;

            // --- Targets.
            var phase = _weapons != null ? _weapons.Current : WeaponController.Phase.Ready;
            bool performing = phase == WeaponController.Phase.Performing || phase == WeaponController.Phase.Scoring;
            _perform = Mathf.MoveTowards(_perform, performing ? 1f : 0f, dt * 4f);
            _lower = Mathf.MoveTowards(_lower, phase == WeaponController.Phase.Cooldown ? 1f : 0f, dt * 3f);
            _hidden = Mathf.MoveTowards(_hidden, _player != null && _player.IsHidden ? 1f : 0f, dt * 3f);
            _dead = Mathf.MoveTowards(_dead, _health != null && !_health.IsAlive ? 1f : 0f, dt * 1.5f);
            if (_weapons != null && _weapons.LastResultTime != _lastResultSeen)
            {
                _lastResultSeen = _weapons.LastResultTime;
                _lastWasSuccess = _weapons.LastDamage > 0f;
                _punch = 1f;
            }
            _punch = Mathf.MoveTowards(_punch, 0f, dt * 2.5f);

            // --- Mouse sway (arms lag behind the view).
            Vector2 look = GameState.InputBlocked ? Vector2.zero : InputReader.Look * 0.0025f * PlayerSettingsData.Current.mouseSensitivity;
            _sway = Vector2.Lerp(_sway, Vector2.ClampMagnitude(-look, 0.04f), 1f - Mathf.Exp(-dt * 8f));

            // --- Walk bob.
            float speed = _player != null ? _player.Velocity.magnitude : 0f;
            _bobPhase += dt * speed * 2.2f;
            float bobAmp = Mathf.Clamp01(speed / 4.5f) * (_player != null && _player.IsSprinting ? 1.6f : 1f);
            Vector3 bob = new Vector3(Mathf.Sin(_bobPhase) * 0.012f, -Mathf.Abs(Mathf.Cos(_bobPhase)) * 0.014f, 0f) * bobAmp;

            // --- Breathing and fear.
            float breathe = Mathf.Sin(t * (1.3f + threat * 1.5f)) * (0.004f + threat * 0.006f);
            Vector3 tremble = new Vector3(Mathf.PerlinNoise(t * 9f, 1f) - 0.5f, Mathf.PerlinNoise(1f, t * 9f) - 0.5f, 0f) * (0.008f * threat);
            float hurt = _health != null ? _health.HurtFlash : 0f;

            Vector3 common = bob + new Vector3(_sway.x, _sway.y + breathe, 0f) + tremble
                           + Vector3.down * (_hidden * 0.3f + _dead * 0.5f + hurt * 0.04f);

            // --- Left hand: lantern held steady-ish, lifted slightly while singing to light the face.
            _armL.localPosition = _restL + common * 0.8f + new Vector3(0f, 0.03f * _perform, 0f);
            _armL.localRotation = _restRotL * Quaternion.Euler(-6f * _perform + _sway.y * 200f, _sway.x * 150f, 0f);

            // --- Right hand: the gesture for the selected relic.
            Vector3 gesturePos;
            Vector3 gestureRot;
            if (relic == "w_sadafa") { gesturePos = new Vector3(-0.14f, 0.2f, -0.04f); gestureRot = new Vector3(-28f, 22f, 0f); }     // shell to the lips
            else if (relic == "w_tar") { gesturePos = new Vector3(-0.16f, 0.14f, 0.06f); gestureRot = new Vector3(-20f, 30f, -25f); } // drum raised before the face
            else { gesturePos = new Vector3(-0.12f, 0.19f, 0.02f); gestureRot = new Vector3(-35f, 15f, 0f); }                          // open palm before the mouth
            float drumBeat = relic == "w_tar" ? Mathf.Abs(Mathf.Sin(t * 7f)) * 0.012f * _perform * (0.3f + voice) : 0f;
            float strike = _punch * (_lastWasSuccess ? 1f : -0.6f);
            _armR.localPosition = _restR + common + gesturePos * _perform
                                + new Vector3(0f, -0.06f * _lower + drumBeat, 0.09f * Mathf.Max(0f, strike) - 0.03f * Mathf.Max(0f, -strike));
            _armR.localRotation = _restRotR * Quaternion.Euler(gestureRot * _perform + new Vector3(-12f * strike + 10f * _lower + _sway.y * 220f, _sway.x * 160f, 0f));

            // --- Lights: the lantern breathes with your voice; the relic glows as you sing.
            if (lanternLight != null)
            {
                float flicker = 0.9f + 0.1f * Mathf.PerlinNoise(t * 6f, 0.3f);
                lanternLight.intensity = lanternIntensity * flicker * (1f + voice * 0.6f) * (1f - _hidden * 0.7f) * (1f - _dead);
            }
            if (_relicLight != null)
            {
                _relicLight.color = relicColor;
                _relicLight.intensity = _perform * (0.4f + voice * 2.5f) + Mathf.Max(0f, strike) * 3f;
            }
        }
    }
}
