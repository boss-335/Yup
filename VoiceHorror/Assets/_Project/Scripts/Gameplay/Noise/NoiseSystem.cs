using System.Collections.Generic;
using UnityEngine;

namespace VoiceHorror.Gameplay.Noise
{
    public enum NoiseSource
    {
        PlayerVoice = 0,
        PlayerFootsteps = 1,
        /// <summary>A creature alerting others (Sentinel wail). Does not raise the player's threat.</summary>
        CreatureAlert = 2,
        /// <summary>World objects (thrown items, doors). Reserved.</summary>
        World = 3,
    }

    /// <summary>Spec §2.4.</summary>
    public struct NoiseEvent
    {
        public Vector3 position;
        public float radius;
        public float loudness01;
        public float time;
        public NoiseSource source;
        /// <summary>Where listeners should go (for alerts this is the heard position, not the wailer).</summary>
        public Vector3 target;
    }

    public interface INoiseListener
    {
        Vector3 ListenerPosition { get; }
        /// <summary>Scales every event radius for this listener (sharp-eared creatures hear further).</summary>
        float HearingMultiplier { get; }
        /// <param name="effectiveRadius">Event radius after occlusion between source and this listener.</param>
        void OnNoise(in NoiseEvent e, float effectiveRadius);
    }

    /// <summary>
    /// Broadcasts noise events to registered listeners (spec §2.4). Walls with an <see cref="Occluder"/>
    /// between source and listener shrink the radius for that listener.
    /// </summary>
    public static class NoiseSystem
    {
        static readonly List<INoiseListener> Listeners = new List<INoiseListener>();
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        public static float WallOcclusion = 0.6f;
        public static int MaxOccluders = 3;

        /// <summary>Most recent player noise (for HUD / debug).</summary>
        public static NoiseEvent LastPlayerNoise;

        public static void Register(INoiseListener l)
        {
            if (!Listeners.Contains(l)) Listeners.Add(l);
        }

        public static void Unregister(INoiseListener l) => Listeners.Remove(l);

        public static void Emit(in NoiseEvent e)
        {
            if (e.source == NoiseSource.PlayerVoice || e.source == NoiseSource.PlayerFootsteps)
            {
                if (e.source == NoiseSource.PlayerVoice || e.radius > LastPlayerNoise.radius) LastPlayerNoise = e;
            }
            if (e.radius <= 0.01f) return;

            for (int i = Listeners.Count - 1; i >= 0; i--)
            {
                if (i >= Listeners.Count) continue;
                var l = Listeners[i];
                Vector3 lp = l.ListenerPosition;
                float dist = Vector3.Distance(lp, e.position);
                float r = e.radius * l.HearingMultiplier;
                if (dist > r) continue; // cheap reject before raycasts
                float eff = r * OcclusionFactor(e.position, lp);
                if (dist <= eff) l.OnNoise(e, eff);
            }
        }

        public static float OcclusionFactor(Vector3 from, Vector3 to)
        {
            Vector3 a = from + Vector3.up * 1.2f, b = to + Vector3.up * 1.2f;
            Vector3 dir = b - a;
            float len = dir.magnitude;
            if (len < 0.01f) return 1f;
            int n = Physics.RaycastNonAlloc(a, dir / len, Hits, len, ~0, QueryTriggerInteraction.Ignore);
            int walls = 0;
            for (int i = 0; i < n; i++)
            {
                if (Hits[i].collider != null && Hits[i].collider.GetComponentInParent<Occluder>() != null) walls++;
            }
            walls = Mathf.Min(walls, MaxOccluders);
            return Mathf.Pow(WallOcclusion, walls);
        }

        public static void Clear() => Listeners.Clear();
    }
}
