using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Core;
using VoiceHorror.Gameplay.Noise;
using VoiceHorror.Persistence;

namespace VoiceHorror.Gameplay.Player
{
    /// <summary>First-person movement (CharacterController), look, crouch and sprint footsteps.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        public Transform cameraPivot;
        public float standHeight = 1.75f;
        public float crouchHeight = 1.0f;
        public float eyeOffset = 0.1f;
        public float gravity = 18f;

        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        public bool IsHidden => CurrentHidingSpot != null;
        public HidingSpot CurrentHidingSpot { get; set; }
        public Vector3 Velocity { get; private set; }

        CharacterController _cc;
        float _pitch;
        float _yVel;
        float _stepTimer;
        AudioSource _stepSource;

        void Awake()
        {
            Instance = this;
            GameState.CursorLockAllowed = true;
            _cc = GetComponent<CharacterController>();
            if (cameraPivot == null && Camera.main != null) cameraPivot = Camera.main.transform;
            _stepSource = ProceduralAudio.MakeSource(gameObject, ProceduralAudio.Step, false, 0.35f, 0f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            GameState.CursorLockAllowed = false;
            GameState.ApplyCursor();
        }

        void Start() => GameState.ApplyCursor();

        PlayerHealth _health;

        void Update()
        {
            if (GameState.Paused) return;
            if (_health == null) _health = GetComponent<PlayerHealth>();
            if (_health != null && !_health.IsAlive) { Velocity = Vector3.zero; return; }
            bool blocked = GameState.InputBlocked || IsHidden;
            var b = GameConfig.Balance;

            if (!GameState.InputBlocked)
            {
                float sens = b.mouseSensitivity * PlayerSettingsData.Current.mouseSensitivity;
                Vector2 look = InputReader.Look * sens;
                transform.Rotate(0f, look.x, 0f, Space.Self);
                _pitch = Mathf.Clamp(_pitch - look.y, -85f, 85f);
                if (cameraPivot != null) cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }

            if (IsHidden) { Velocity = Vector3.zero; return; }

            bool wantCrouch = !blocked && InputReader.IsHeld(InputReader.Action.Crouch);
            if (wantCrouch != IsCrouching && (wantCrouch || CanStandUp())) IsCrouching = wantCrouch;
            float targetHeight = IsCrouching ? crouchHeight : standHeight;
            _cc.height = Mathf.MoveTowards(_cc.height, targetHeight, Time.deltaTime * 4f);
            _cc.center = new Vector3(0f, _cc.height / 2f, 0f);
            if (cameraPivot != null)
                cameraPivot.localPosition = new Vector3(0f, _cc.height - eyeOffset, 0f);

            Vector2 move = blocked ? Vector2.zero : InputReader.Move;
            IsSprinting = !IsCrouching && !blocked && InputReader.IsHeld(InputReader.Action.Sprint) && move.y > 0.1f;
            float speed = IsCrouching ? b.crouchSpeed : IsSprinting ? b.sprintSpeed : b.walkSpeed;

            Vector3 wish = (transform.right * move.x + transform.forward * move.y) * speed;
            if (_cc.isGrounded && _yVel < 0f) _yVel = -2f;
            _yVel -= gravity * Time.deltaTime;
            Vector3 vel = wish + Vector3.up * _yVel;
            _cc.Move(vel * Time.deltaTime);
            Velocity = wish;

            UpdateFootsteps(wish.magnitude);
        }

        bool CanStandUp()
        {
            Vector3 bottom = transform.position + Vector3.up * (_cc.radius + 0.05f);
            Vector3 top = transform.position + Vector3.up * (standHeight - _cc.radius);
            return !Physics.CheckCapsule(bottom, top, _cc.radius * 0.9f, ~0, QueryTriggerInteraction.Ignore)
                   || _cc.height >= standHeight - 0.01f;
        }

        void UpdateFootsteps(float speed)
        {
            if (speed < 0.2f || !_cc.isGrounded) { _stepTimer = 0f; return; }
            float interval = IsSprinting ? 0.34f : IsCrouching ? 0.75f : 0.52f;
            _stepTimer += Time.deltaTime;
            if (_stepTimer < interval) return;
            _stepTimer = 0f;
            _stepSource.pitch = Random.Range(0.85f, 1.1f);
            _stepSource.volume = IsSprinting ? 0.5f : IsCrouching ? 0.08f : 0.2f;
            _stepSource.Play();
            if (IsSprinting)
            {
                NoiseSystem.Emit(new NoiseEvent
                {
                    position = transform.position,
                    target = transform.position,
                    radius = GameConfig.Balance.sprintFootstepRadius,
                    loudness01 = 0.3f,
                    time = Time.time,
                    source = NoiseSource.PlayerFootsteps,
                });
            }
        }

        public void TeleportTo(Vector3 position, Quaternion rotation)
        {
            _cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _cc.enabled = true;
            _yVel = 0f;
        }
    }
}
