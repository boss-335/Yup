using UnityEngine;

namespace VoiceHorror.Gameplay.World
{
    /// <summary>Constant rotation (the lighthouse beam, the Heart's ring of mouths).</summary>
    public sealed class Rotator : MonoBehaviour
    {
        public Vector3 degreesPerSecond = new Vector3(0f, 20f, 0f);

        void Update() => transform.Rotate(degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
