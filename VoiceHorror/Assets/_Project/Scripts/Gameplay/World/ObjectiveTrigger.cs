using UnityEngine;
using VoiceHorror.Gameplay.Player;
using VoiceHorror.UI;

namespace VoiceHorror.Gameplay.World
{
    /// <summary>
    /// Trigger volume that advances the objective, optionally shows a radio line from Mubarak,
    /// and stores a checkpoint.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ObjectiveTrigger : MonoBehaviour
    {
        [Tooltip("Loc key of the new objective (empty = unchanged).")]
        public string objectiveKey = "";
        [Tooltip("Loc key of a radio message (empty = none).")]
        public string radioKey = "";
        public bool setCheckpoint = true;
        public bool once = true;
        bool _done;

        void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (_done && once) return;
            var pc = other.GetComponentInParent<PlayerController>();
            if (pc == null) return;
            _done = true;
            if (!string.IsNullOrEmpty(objectiveKey)) Objectives.Set(objectiveKey);
            if (!string.IsNullOrEmpty(radioKey)) RadioView.Say(radioKey);
            if (setCheckpoint) Checkpoint.Store(pc.transform.position, pc.transform.rotation);
        }
    }

    /// <summary>Last checkpoint position (process-wide so it survives a scene reload).</summary>
    public static class Checkpoint
    {
        public static bool Has { get; private set; }
        public static Vector3 Position { get; private set; }
        public static Quaternion Rotation { get; private set; }
        public static string ObjectiveKey { get; private set; }
        public static string SceneName { get; private set; }

        public static void Store(Vector3 p, Quaternion r)
        {
            Has = true;
            Position = p;
            Rotation = r;
            ObjectiveKey = Objectives.CurrentKey;
            SceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        public static void Clear() { Has = false; SceneName = null; }
    }
}
