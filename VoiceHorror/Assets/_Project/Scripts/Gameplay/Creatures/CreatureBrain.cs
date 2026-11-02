using System;
using UnityEngine;
using UnityEngine.AI;
using VoiceHorror.Audio;
using VoiceHorror.Config;
using VoiceHorror.Core;
using VoiceHorror.Gameplay.Noise;
using VoiceHorror.Gameplay.Player;

namespace VoiceHorror.Gameplay.Creatures
{
    /// <summary>
    /// Spec §2.5. Creatures are blind: they act only on what they hear.
    /// Idle → Investigate(last heard position) → Hunt → Search → Idle, with Stagger on damage.
    /// Archetypes add: Hunter lunge/flee, Sentinel wail-alert, Heart summoning.
    /// </summary>
    public sealed class CreatureBrain : MonoBehaviour, INoiseListener, IDamageable
    {
        public enum State { Idle, Investigate, Hunt, Search, Stagger, Flee, Wail, Dead }

        public CreatureDefinition definition;
        [Tooltip("Optional patrol points for Idle; otherwise wanders around its spawn.")]
        public Transform[] patrol;

        public State Current { get; private set; } = State.Idle;
        public float Health { get; private set; }
        public float Health01 => definition != null && definition.maxHealth > 0 ? Health / definition.maxHealth : 0f;
        public bool IsAlive => Current != State.Dead;
        public Vector3 LastHeardPosition { get; private set; }
        public float HearingAccum { get; private set; }
        public float TimeSinceHeard => Time.time - _lastHeardTime;
        public bool IsMobile => definition != null && (definition.archetype == CreatureArchetype.Stalker || definition.archetype == CreatureArchetype.Hunter);
        public bool IsLunging => _lungeTimer > 0f;

        public Vector3 ListenerPosition => transform.position;
        public float HearingMultiplier => definition != null ? definition.hearingMultiplier : 1f;

        public static event Action<CreatureBrain> AnyDied;
        public event Action<State> StateChanged;

        NavMeshAgent _agent;
        Vector3 _home;
        float _stateTimer;
        float _lastHeardTime = -100f;
        float _attackTimer;
        float _lungeTimer, _lungeCooldown;
        float _alertCooldown;
        float _wanderTimer;
        float _summonTimer = 6f;
        int _patrolIndex;
        readonly System.Collections.Generic.List<CreatureBrain> _summons = new System.Collections.Generic.List<CreatureBrain>();
        AudioSource _breath, _voice;
        Transform _player;
        PlayerHealth _playerHealth;

        void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            if (definition == null) definition = ScriptableObject.CreateInstance<CreatureDefinition>();
            Health = definition.maxHealth;
            _home = transform.position;
            _breath = ProceduralAudio.MakeSource(gameObject, ProceduralAudio.Breath, true, 0.5f, 1f, 12f);
            _voice = ProceduralAudio.MakeSource(gameObject, ProceduralAudio.Wail, false, 1f, 0.85f, 80f);
        }

        void OnEnable() => NoiseSystem.Register(this);
        void OnDisable() => NoiseSystem.Unregister(this);

        void Start()
        {
            var pc = PlayerController.Instance != null ? PlayerController.Instance : FindFirstObjectByType<PlayerController>();
            if (pc != null) { _player = pc.transform; _playerHealth = pc.GetComponent<PlayerHealth>(); }
            _breath.time = UnityEngine.Random.Range(0f, 2.5f);
            _breath.Play();

            if (_agent != null)
            {
                if (IsMobile)
                {
                    // The NavMesh is baked at runtime in Awake; agents are enabled only once it exists.
                    if (NavMesh.SamplePosition(transform.position, out var hit, 4f, NavMesh.AllAreas))
                    {
                        transform.position = hit.position; // place first so enabling the agent finds the mesh
                        _agent.enabled = true;
                        _home = hit.position;
                    }
                    else Debug.LogWarning($"[Creature] {name} is not near the NavMesh.");
                    _agent.radius = definition.radius;
                    _agent.height = definition.height;
                    _agent.angularSpeed = 360f;
                    _agent.acceleration = 12f;
                    _agent.stoppingDistance = 0.4f;
                }
                else _agent.enabled = false;
            }
            SetState(State.Idle);
        }

        bool AgentReady => _agent != null && _agent.enabled && _agent.isOnNavMesh;

        void SetState(State s)
        {
            if (Current == State.Dead) return;
            Current = s;
            _stateTimer = 0f;
            _wanderTimer = 0f;
            if (AgentReady)
            {
                _agent.isStopped = s == State.Stagger || s == State.Wail;
                _agent.speed = SpeedFor(s);
            }
            StateChanged?.Invoke(s);
        }

        float SpeedFor(State s)
        {
            switch (s)
            {
                case State.Investigate: return definition.investigateSpeed;
                case State.Hunt: return definition.huntSpeed;
                case State.Flee: return definition.huntSpeed * 1.1f;
                case State.Search: return definition.investigateSpeed * 0.8f;
                default: return definition.wanderSpeed;
            }
        }

        // ------------------------------------------------------------ Hearing

        public void OnNoise(in NoiseEvent e, float effectiveRadius)
        {
            if (Current == State.Dead) return;

            if (e.source == NoiseSource.CreatureAlert)
            {
                if (!IsMobile) return;
                if (Current == State.Idle || Current == State.Search)
                {
                    LastHeardPosition = e.target;
                    _lastHeardTime = Time.time;
                    SetState(State.Investigate);
                }
                return;
            }

            LastHeardPosition = e.position;
            _lastHeardTime = Time.time;
            HearingAccum += Time.deltaTime * (0.5f + e.loudness01);
            bool scream = e.radius > GameConfig.Tuning.loudRadius * 1.5f;

            switch (definition.archetype)
            {
                case CreatureArchetype.Sentinel:
                case CreatureArchetype.Heart:
                    if (Current == State.Idle && _alertCooldown <= 0f && (HearingAccum >= definition.huntConfirmSec * 0.5f || scream))
                        SetState(State.Wail);
                    return;
            }

            switch (Current)
            {
                case State.Idle:
                case State.Search:
                    SetState(scream ? State.Hunt : State.Investigate);
                    break;
                case State.Investigate:
                    if (scream || HearingAccum >= definition.huntConfirmSec) SetState(State.Hunt);
                    break;
            }
        }

        // ------------------------------------------------------------ Damage

        public void TakeDamage(float amount, Vector3 from, int tier, float staggerSec)
        {
            if (Current == State.Dead) return;
            float dealt = amount * (tier > 0 ? definition.TierMultiplier(tier) : 1f);
            Health -= dealt;
            var body = GetComponent<CreatureBody>();
            if (body != null) body.Flinch(dealt / Mathf.Max(1f, definition.maxHealth));

            if (Health <= 0f) { Die(); return; }

            LastHeardPosition = from;
            _lastHeardTime = Time.time;

            if (definition.archetype == CreatureArchetype.Hunter && Health01 <= definition.fleeHealthFraction && dealt >= definition.maxHealth * 0.2f)
            {
                SetState(State.Flee);
                return;
            }
            float stagger = staggerSec * definition.staggerFactor;
            if (stagger > 0.05f)
            {
                SetState(State.Stagger);
                _stateTimer = -stagger; // counts up to 0
            }
            else if (IsMobile) SetState(State.Hunt);
        }

        void Die()
        {
            Health = 0f;
            Current = State.Dead;
            if (AgentReady) _agent.isStopped = true;
            if (_agent != null) _agent.enabled = false;
            _breath.Stop();
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            var body = GetComponent<CreatureBody>();
            if (body != null) body.Dissolve();
            StateChanged?.Invoke(State.Dead);
            AnyDied?.Invoke(this);
            Destroy(gameObject, 3f);
        }

        // ------------------------------------------------------------ Update

        void Update()
        {
            if (Current == State.Dead || GameState.Paused) return;
            float dt = Time.deltaTime;
            _stateTimer += dt;
            _attackTimer -= dt;
            _lungeTimer -= dt;
            _lungeCooldown -= dt;
            _alertCooldown -= dt;
            HearingAccum = Mathf.Max(0f, HearingAccum - definition.hearingDecayPerSec * dt * (TimeSinceHeard > 0.2f ? 1f : 0f));

            if (_player == null)
            {
                var pc = PlayerController.Instance;
                if (pc != null) { _player = pc.transform; _playerHealth = pc.GetComponent<PlayerHealth>(); }
            }

            CloseSense();
            TryAttack();
            if (definition.archetype == CreatureArchetype.Heart) UpdateSummons(dt);

            switch (Current)
            {
                case State.Idle: UpdateIdle(dt); break;
                case State.Investigate: UpdateInvestigate(); break;
                case State.Hunt: UpdateHunt(); break;
                case State.Search: UpdateSearch(dt); break;
                case State.Stagger: if (_stateTimer >= 0f) SetState(IsMobile ? State.Hunt : State.Idle); break;
                case State.Flee: UpdateFlee(); break;
                case State.Wail: UpdateWail(); break;
            }
        }

        void CloseSense()
        {
            if (_player == null || definition.closeSenseRadius <= 0f || !IsMobile) return;
            var pc = PlayerController.Instance;
            if (pc != null && pc.IsHidden) return;
            if (Vector3.Distance(transform.position, _player.position) > definition.closeSenseRadius) return;
            LastHeardPosition = _player.position;
            _lastHeardTime = Time.time;
            if (Current == State.Idle || Current == State.Search || Current == State.Investigate) SetState(State.Hunt);
        }

        void TryAttack()
        {
            if (_player == null || _playerHealth == null || !_playerHealth.IsAlive || _attackTimer > 0f) return;
            if (Current == State.Stagger || Current == State.Flee) return;
            float dist = Vector3.Distance(transform.position, _player.position);
            if (dist > definition.attackRange) return;
            var pc = PlayerController.Instance;
            // A hidden player is only found if they gave themselves away within the last second.
            if (pc != null && pc.IsHidden && TimeSinceHeard > 1f) return;
            _attackTimer = definition.attackCooldown;
            _playerHealth.TakeDamage(definition.attackDamage, transform.position, 0, 0f);
            var body = GetComponent<CreatureBody>();
            if (body != null) body.Strike();
        }

        void UpdateIdle(float dt)
        {
            if (!AgentReady) return;
            _wanderTimer -= dt;
            if (_wanderTimer > 0f && _agent.remainingDistance > 0.6f) return;
            _wanderTimer = UnityEngine.Random.Range(3f, 7f);
            Vector3 target;
            if (patrol != null && patrol.Length > 0)
            {
                _patrolIndex = (_patrolIndex + 1) % patrol.Length;
                target = patrol[_patrolIndex].position;
            }
            else target = _home + UnityEngine.Random.insideUnitSphere * definition.wanderRadius;
            MoveTo(target);
        }

        void UpdateInvestigate()
        {
            if (!AgentReady) return;
            MoveTo(LastHeardPosition);
            if (!_agent.pathPending && _agent.remainingDistance < 1f) SetState(State.Search);
        }

        void UpdateHunt()
        {
            if (!AgentReady) return;
            float speed = definition.huntSpeed;
            if (definition.archetype == CreatureArchetype.Hunter && _player != null)
            {
                float d = Vector3.Distance(transform.position, LastHeardPosition);
                if (_lungeCooldown <= 0f && d < definition.lungeRange && TimeSinceHeard < 0.5f)
                {
                    _lungeTimer = definition.lungeDuration;
                    _lungeCooldown = definition.lungeCooldown;
                }
                if (_lungeTimer > 0f) speed *= definition.lungeSpeedMultiplier;
            }
            _agent.speed = speed;
            MoveTo(LastHeardPosition);
            bool arrived = !_agent.pathPending && _agent.remainingDistance < 1f;
            if (arrived && TimeSinceHeard > 1.5f) SetState(State.Search);
        }

        void UpdateSearch(float dt)
        {
            if (!AgentReady) return;
            _wanderTimer -= dt;
            if (_wanderTimer <= 0f || _agent.remainingDistance < 0.6f)
            {
                _wanderTimer = UnityEngine.Random.Range(2f, 4f);
                MoveTo(LastHeardPosition + UnityEngine.Random.insideUnitSphere * definition.searchRadius);
            }
            if (TimeSinceHeard > definition.searchTimeout) SetState(State.Idle);
        }

        void UpdateFlee()
        {
            if (AgentReady && _player != null && (_stateTimer < 0.1f || _agent.remainingDistance < 1f))
            {
                Vector3 away = (transform.position - _player.position).normalized;
                MoveTo(transform.position + away * 12f + UnityEngine.Random.insideUnitSphere * 3f);
            }
            if (_stateTimer > definition.fleeDuration) SetState(State.Search);
        }

        void UpdateWail()
        {
            if (_stateTimer < 0.02f && !_voice.isPlaying) _voice.Play();
            if (_stateTimer < definition.wailDuration) return;
            NoiseSystem.Emit(new NoiseEvent
            {
                position = transform.position,
                target = LastHeardPosition,
                radius = definition.alertRadius,
                loudness01 = 1f,
                time = Time.time,
                source = NoiseSource.CreatureAlert,
            });
            _alertCooldown = definition.alertCooldown;
            HearingAccum = 0f;
            SetState(State.Idle);
        }

        /// <summary>The Heart calls Maskut out of the dark while the player is near it.</summary>
        void UpdateSummons(float dt)
        {
            _summons.RemoveAll(c => c == null || !c.IsAlive);
            if (_player == null || Vector3.Distance(transform.position, _player.position) > definition.alertRadius) return;
            _summonTimer -= dt;
            if (_summonTimer > 0f || _summons.Count >= definition.maxSummons) return;
            _summonTimer = definition.summonInterval;
            var dir = CreatureDirector.Instance;
            if (dir == null) return;
            var c = dir.Spawn(dir.stalkerTemplate, true, transform.position);
            if (c != null)
            {
                _summons.Add(c);
                if (!_voice.isPlaying) _voice.Play();
            }
        }

        void MoveTo(Vector3 target)
        {
            if (!AgentReady) return;
            if (NavMesh.SamplePosition(target, out var hit, 3f, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
        }

        /// <summary>Used by the director when it spawns a creature that should head for the player's noise.</summary>
        public void SendTo(Vector3 position, bool hunt)
        {
            LastHeardPosition = position;
            _lastHeardTime = Time.time;
            SetState(hunt ? State.Hunt : State.Investigate);
        }

        public float CurrentSpeed => AgentReady ? _agent.velocity.magnitude : 0f;
    }
}
