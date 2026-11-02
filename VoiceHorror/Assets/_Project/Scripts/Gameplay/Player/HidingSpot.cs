using UnityEngine;
using VoiceHorror.Core;
using VoiceHorror.UI;

namespace VoiceHorror.Gameplay.Player
{
    /// <summary>
    /// A closet (الدولاب) the player can hide in. Hidden players are muffled (voice radius x0.5)
    /// and cannot be sensed at close range — but loud noise still gives them away.
    /// </summary>
    public sealed class HidingSpot : MonoBehaviour, IInteractable
    {
        public Transform viewPoint;
        public Transform exitPoint;

        public bool Occupied { get; private set; }
        public string Prompt => Occupied ? Loc.T("prompt.leave_hiding") : Loc.T("prompt.hide");
        public bool CanInteract => true;

        Vector3 _returnPos;
        Quaternion _returnRot;

        public void Interact(GameObject playerGo)
        {
            var pc = playerGo.GetComponent<PlayerController>();
            if (pc == null) return;
            if (!Occupied)
            {
                _returnPos = playerGo.transform.position;
                _returnRot = playerGo.transform.rotation;
                Vector3 pos = viewPoint != null ? viewPoint.position : transform.position;
                Quaternion rot = viewPoint != null ? viewPoint.rotation : transform.rotation;
                pc.TeleportTo(new Vector3(pos.x, transform.position.y, pos.z), Quaternion.Euler(0f, rot.eulerAngles.y, 0f));
                pc.CurrentHidingSpot = this;
                Occupied = true;
            }
            else
            {
                Vector3 pos = exitPoint != null ? exitPoint.position : _returnPos;
                Quaternion rot = exitPoint != null ? exitPoint.rotation : _returnRot;
                pc.TeleportTo(pos, rot);
                pc.CurrentHidingSpot = null;
                Occupied = false;
            }
        }
    }
}
