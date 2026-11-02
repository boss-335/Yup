using UnityEngine;
using VoiceHorror.Core;

namespace VoiceHorror.Gameplay.Player
{
    /// <summary>Looks for an IInteractable in front of the camera; E uses it. HUD reads <see cref="Focus"/>.</summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        public float reach = 2.4f;
        public IInteractable Focus { get; private set; }

        Transform _cam;
        readonly RaycastHit[] _hits = new RaycastHit[6];

        void Start()
        {
            var pc = GetComponent<PlayerController>();
            _cam = pc != null && pc.cameraPivot != null ? pc.cameraPivot : (Camera.main != null ? Camera.main.transform : transform);
        }

        void Update()
        {
            Focus = null;
            if (GameState.Paused || GameState.InputBlocked) return;

            var pc = PlayerController.Instance;
            if (pc != null && pc.IsHidden)
            {
                Focus = pc.CurrentHidingSpot;
            }
            else
            {
                int n = Physics.SphereCastNonAlloc(_cam.position, 0.15f, _cam.forward, _hits, reach, ~0, QueryTriggerInteraction.Collide);
                float best = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    var it = _hits[i].collider.GetComponentInParent<IInteractable>();
                    if (it == null || !it.CanInteract) continue;
                    if (_hits[i].distance < best) { best = _hits[i].distance; Focus = it; }
                }
            }

            if (Focus != null && InputReader.WasPressed(InputReader.Action.Interact)) Focus.Interact(gameObject);
        }
    }
}
