using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using VoiceHorror.Gameplay.Creatures;
using VoiceHorror.Gameplay.Player;

namespace VoiceHorror.Gameplay.World
{
    /// <summary>
    /// Bakes a NavMesh from the level's colliders when the scene starts, using only the core
    /// UnityEngine.AI API (no AI Navigation package needed). Creatures enable their agents in Start,
    /// after this has run.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class RuntimeNavMeshBaker : MonoBehaviour
    {
        public Vector3 boundsCenter = Vector3.zero;
        public Vector3 boundsSize = new Vector3(200f, 30f, 200f);
        public int agentTypeId = 0;

        NavMeshDataInstance _instance;

        public static bool Baked { get; private set; }

        void Awake()
        {
            Bake();
        }

        void OnDestroy()
        {
            if (_instance.valid) NavMesh.RemoveNavMeshData(_instance);
            Baked = false;
        }

        public void Bake()
        {
            if (_instance.valid) NavMesh.RemoveNavMeshData(_instance);
            var bounds = new Bounds(boundsCenter, boundsSize);
            var sources = new List<NavMeshBuildSource>();
            var markups = new List<NavMeshBuildMarkup>();
            NavMeshBuilder.CollectSources(bounds, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, markups, sources);

            // Drop anything that moves or should not block: creatures, the player, triggers.
            sources.RemoveAll(s =>
            {
                var c = s.component;
                if (c == null) return false;
                if (c is Collider col && col.isTrigger) return true;
                return c.GetComponentInParent<CreatureBrain>() != null || c.GetComponentInParent<PlayerController>() != null;
            });

            var settings = NavMesh.GetSettingsByID(agentTypeId);
            settings.agentRadius = 0.4f;
            settings.agentClimb = 0.45f;
            settings.agentSlope = 40f;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null)
            {
                Debug.LogError("[NavMesh] Runtime bake failed.");
                return;
            }
            _instance = NavMesh.AddNavMeshData(data);
            Baked = true;
        }
    }
}
