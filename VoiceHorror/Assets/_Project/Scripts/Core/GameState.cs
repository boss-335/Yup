using System.Collections.Generic;
using UnityEngine;

namespace VoiceHorror.Core
{
    /// <summary>Global flags shared by gameplay and UI (who owns the mouse, is the game paused).</summary>
    public static class GameState
    {
        static readonly HashSet<object> Blockers = new HashSet<object>();
        static readonly HashSet<object> Pausers = new HashSet<object>();

        /// <summary>True while any modal UI is open: player look/move/fire are ignored and the cursor is free.</summary>
        public static bool InputBlocked => Blockers.Count > 0;
        public static bool Paused => Pausers.Count > 0;

        public static void Block(object owner, bool block)
        {
            if (block) Blockers.Add(owner); else Blockers.Remove(owner);
            ApplyCursor();
        }

        public static void Pause(object owner, bool pause)
        {
            if (pause) Pausers.Add(owner); else Pausers.Remove(owner);
            Time.timeScale = Paused ? 0f : 1f;
            AudioListener.pause = Paused;
        }

        /// <summary>Set by the first-person player; scenes without one (Mic Test, Score Lab) keep a free cursor.</summary>
        public static bool CursorLockAllowed { get; set; }

        public static void ApplyCursor()
        {
            bool free = InputBlocked || !CursorLockAllowed;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        /// <summary>Call when a scene loads so stale blockers from the previous scene do not linger.</summary>
        public static void Reset()
        {
            Blockers.Clear();
            Pausers.Clear();
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }

    public interface IDamageable
    {
        bool IsAlive { get; }
        /// <param name="tier">Weapon tier (1 short … 3 long), 0 for non-weapon damage.</param>
        void TakeDamage(float amount, Vector3 from, int tier, float staggerSec);
    }

    public interface IInteractable
    {
        string Prompt { get; }
        bool CanInteract { get; }
        void Interact(GameObject player);
    }
}
