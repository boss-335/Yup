using UnityEngine;
using VoiceHorror.Core;
using VoiceHorror.Gameplay.Noise;
using VoiceHorror.Gameplay.Weapons;

namespace VoiceHorror.Gameplay.Player
{
    /// <summary>
    /// Camera layer under the player's head pivot: walk bob, fear tremor (threat), hit kicks,
    /// a narrowed focus while singing, and the fall to the ground on death.
    /// </summary>
    [DefaultExecutionOrder(140)]
    public sealed class CameraFx : MonoBehaviour
    {
        public float baseFov = 70f;
        public float performFov = 62f;
        public float bobAmount = 0.035f;

        Camera _cam;
        PlayerController _player;
        PlayerHealth _health;
        WeaponController _weapons;
        float _bobPhase, _kickPitch, _kickRoll, _deathT = -1f, _fovVel;

        void Start()
        {
            _cam = GetComponent<Camera>();
            _player = GetComponentInParent<PlayerController>();
            _health = GetComponentInParent<PlayerHealth>();
            _weapons = GetComponentInParent<WeaponController>();
            if (_health != null)
            {
                _health.Damaged += amount =>
                {
                    _kickPitch -= Mathf.Clamp(amount * 0.25f, 3f, 12f);
                    _kickRoll += Random.Range(-1f, 1f) * Mathf.Clamp(amount * 0.3f, 4f, 14f);
                };
                _health.Died += () => _deathT = 0f;
            }
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (_deathT >= 0f)
            {
                // Collapse: drop to the floor and roll onto the side.
                _deathT += dt;
                float f = Mathf.SmoothStep(0f, 1f, _deathT / 1.1f);
                float eye = _player != null ? _player.standHeight - _player.eyeOffset : 1.6f;
                transform.localPosition = new Vector3(0.1f * f, -(eye - 0.15f) * f, 0.15f * f);
                transform.localRotation = Quaternion.Euler(-15f * f, 10f * f, 72f * f);
                return;
            }
            if (Time.timeScale <= 0f) return;
            dt = Time.deltaTime;

            float speed = _player != null ? _player.Velocity.magnitude : 0f;
            _bobPhase += dt * speed * 2.2f;
            float amp = bobAmount * Mathf.Clamp01(speed / 4.5f) * (_player != null && _player.IsSprinting ? 1.5f : _player != null && _player.IsCrouching ? 0.5f : 1f);
            Vector3 bob = new Vector3(Mathf.Sin(_bobPhase) * amp * 0.5f, -Mathf.Abs(Mathf.Cos(_bobPhase)) * amp, 0f);

            float threat = ThreatMeter.Instance != null ? ThreatMeter.Instance.Threat01 : 0f;
            float fear = Mathf.Clamp01((threat - 0.45f) / 0.55f);
            float t = Time.time;
            float shakePitch = (Mathf.PerlinNoise(t * 7f, 0.1f) - 0.5f) * 1.6f * fear;
            float shakeYaw = (Mathf.PerlinNoise(0.7f, t * 7f) - 0.5f) * 1.6f * fear;

            _kickPitch = Mathf.Lerp(_kickPitch, 0f, 1f - Mathf.Exp(-dt * 7f));
            _kickRoll = Mathf.Lerp(_kickRoll, 0f, 1f - Mathf.Exp(-dt * 5f));

            transform.localPosition = bob;
            transform.localRotation = Quaternion.Euler(shakePitch + _kickPitch, shakeYaw, Mathf.Sin(_bobPhase * 0.5f) * amp * 25f + _kickRoll);

            if (_cam != null)
            {
                bool performing = _weapons != null && _weapons.Current == WeaponController.Phase.Performing;
                _cam.fieldOfView = Mathf.SmoothDamp(_cam.fieldOfView, performing ? performFov : baseFov, ref _fovVel, 0.35f);
            }
        }
    }
}
