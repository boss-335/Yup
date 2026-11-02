using UnityEngine;
using VoiceHorror.Gameplay.Player;

namespace VoiceHorror.Gameplay.World
{
    /// <summary>
    /// The nahham's house: creatures cannot hear through its thick coral walls, so it is where the
    /// player may record new weapon songs. While inside, voice noise is muffled to nothing.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    public sealed class SafeRoom : MonoBehaviour
    {
        public static bool PlayerInside { get; private set; }

        void Reset() => GetComponent<BoxCollider>().isTrigger = true;

        void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() != null) PlayerInside = true;
        }

        void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() != null) PlayerInside = false;
        }

        void OnDestroy() => PlayerInside = false;
    }
}
