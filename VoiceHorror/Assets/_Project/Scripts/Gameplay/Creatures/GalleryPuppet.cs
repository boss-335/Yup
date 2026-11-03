using UnityEngine;
using VoiceHorror.Config;

namespace VoiceHorror.Gameplay.Creatures
{
    /// <summary>
    /// Character gallery: drives a <see cref="CreatureBody"/> through every animation in a loop
    /// (walk, listen, hunt, attack, recoil, wail, dormant, waking, death) without any AI.
    /// </summary>
    [RequireComponent(typeof(CreatureBody))]
    public sealed class GalleryPuppet : MonoBehaviour
    {
        public CreatureDefinition definition;
        public bool mobile = true;
        public float walkRadius = 1.6f;
        public float stepSeconds = 3.2f;
        public bool paused;

        public enum Act { Idle, Walk, Listen, Hunt, Attack, Stagger, Wail, Dormant, Waking, Death }
        static readonly Act[] MobileCycle = { Act.Idle, Act.Walk, Act.Listen, Act.Hunt, Act.Attack, Act.Stagger, Act.Dormant, Act.Waking, Act.Death };
        static readonly Act[] StaticCycle = { Act.Idle, Act.Listen, Act.Wail, Act.Attack, Act.Stagger, Act.Death };

        public Act Current { get; private set; }
        public float ActProgress => _timer / stepSeconds;

        CreatureBody _body;
        Vector3 _center;
        Quaternion _startRot;
        float _timer, _angle, _strikeTimer;
        int _index;

        void Start()
        {
            _body = GetComponent<CreatureBody>();
            _body.externalControl = true;
            _center = transform.position;
            _startRot = transform.rotation;
            Enter(0);
        }

        Act[] Cycle => mobile ? MobileCycle : StaticCycle;

        public void Next() => Enter((_index + 1) % Cycle.Length);
        public void Previous() => Enter((_index - 1 + Cycle.Length) % Cycle.Length);

        void Enter(int i)
        {
            if (Current == Act.Death) _body.Revive();
            _index = i;
            Current = Cycle[i];
            _timer = 0f;
            _strikeTimer = 0f;
            transform.SetPositionAndRotation(_center, _startRot);
            _angle = 0f;
            switch (Current)
            {
                case Act.Listen: _body.externalPose = CreatureBody.Pose.Listen; break;
                case Act.Hunt: _body.externalPose = CreatureBody.Pose.Hunt; break;
                case Act.Stagger: _body.externalPose = CreatureBody.Pose.Stagger; _body.Flinch(0.6f); break;
                case Act.Wail: _body.externalPose = CreatureBody.Pose.Wail; break;
                case Act.Dormant: _body.externalPose = CreatureBody.Pose.Dormant; break;
                case Act.Waking: _body.externalPose = CreatureBody.Pose.Waking; break;
                case Act.Death: _body.externalPose = CreatureBody.Pose.Dead; break;
                case Act.Attack: _body.externalPose = mobile ? CreatureBody.Pose.Hunt : CreatureBody.Pose.Listen; break;
                default: _body.externalPose = CreatureBody.Pose.Idle; break;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (!paused)
            {
                _timer += dt;
                if (_timer >= stepSeconds) { Next(); return; }
            }

            // A sound source that circles the creature, so the head keeps turning toward it.
            float a = Time.time * 0.9f;
            _body.externalLookTarget = _center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 4f;

            if (Current == Act.Attack)
            {
                _strikeTimer -= dt;
                if (_strikeTimer <= 0f) { _body.Strike(); _strikeTimer = 1.1f; }
            }

            bool moving = mobile && (Current == Act.Walk || Current == Act.Hunt || Current == Act.Listen);
            if (moving)
            {
                float speed = Current == Act.Hunt ? 3.2f : Current == Act.Walk ? 1.2f : 0.6f;
                _angle += speed / walkRadius * dt;
                Vector3 pos = _center + new Vector3(Mathf.Sin(_angle), 0f, 1f - Mathf.Cos(_angle)) * walkRadius;
                Vector3 tangent = new Vector3(Mathf.Cos(_angle), 0f, Mathf.Sin(_angle));
                transform.SetPositionAndRotation(pos, Quaternion.LookRotation(tangent));
            }
        }
    }
}
