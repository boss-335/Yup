using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Core;
using VoiceHorror.UI;

namespace VoiceHorror.Gameplay.World
{
    /// <summary>A cassette (story log). Playing it opens the tape reader with its localized text.</summary>
    public sealed class TapeLog : MonoBehaviour, IInteractable
    {
        [Tooltip("Key into Story.Tapes.")]
        public string tapeId = "tape_01";
        public bool consumed;

        public string Prompt => Loc.T("prompt.play_tape");
        public bool CanInteract => !consumed;

        public void Interact(GameObject player)
        {
            consumed = true;
            AudioSource.PlayClipAtPoint(ProceduralAudio.Tape, transform.position, 0.8f);
            Journal.Collect(tapeId);
            TapeReaderView.Open(tapeId);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
            foreach (var l in GetComponentsInChildren<Light>()) l.enabled = false;
        }
    }
}
