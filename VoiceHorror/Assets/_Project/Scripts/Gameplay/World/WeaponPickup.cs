using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Core;
using VoiceHorror.Gameplay.Weapons;
using VoiceHorror.UI;

namespace VoiceHorror.Gameplay.World
{
    /// <summary>One of the nahham's relics. Unlocks a weapon; the recording screen then asks for its song.</summary>
    public sealed class WeaponPickup : MonoBehaviour, IInteractable
    {
        public string weaponId = "w_nafas";
        public string tapeIdOnPickup = "";
        bool _taken;

        public string Prompt => Loc.T("prompt.take_relic");
        public bool CanInteract => !_taken;

        public void Interact(GameObject player)
        {
            var wc = player.GetComponentInChildren<WeaponController>();
            if (wc == null) return;
            _taken = true;
            AudioSource.PlayClipAtPoint(ProceduralAudio.Pickup, transform.position, 0.9f);
            wc.Unlock(weaponId);
            if (!string.IsNullOrEmpty(tapeIdOnPickup)) Journal.Collect(tapeIdOnPickup);
            gameObject.SetActive(false);
        }

        void Update()
        {
            transform.Rotate(0f, 40f * Time.deltaTime, 0f, Space.World);
        }
    }
}
