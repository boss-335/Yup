#nullable enable
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VoiceBattle.Session.Text;

namespace VoiceBattle.UnityLayer
{
    /// <summary>
    /// First screen: Training (vs bot, teach moves) or Online (rooms, quick match, Steam friends).
    /// Created automatically in any empty scene (see <see cref="VoiceBattleBootstrap"/>), so pressing Play
    /// in a new project works. Escape returns here. A Steam invite on the command line goes straight online.
    /// </summary>
    public sealed class MainMenu : MonoBehaviour
    {
        [Tooltip("Start Steam with the menu so friends' invites arrive here too")]
        public bool useSteam = true;

        private GUIStyle? _title, _button;
        private SteamBridge? _steam;

        private void Start()
        {
            if (useSteam) _steam = SteamBridge.Get();
        }

        private void Update()
        {
            // A Steam invite (or a join on the command line) goes straight to the online screen.
            if (_steam != null && _steam.HasPendingJoin) StartOnline();
        }

        private void OnGUI()
        {
            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _button ??= new GUIStyle(GUI.skin.button) { fontSize = 22 };
            float w = Mathf.Min(520, Screen.width - 32);
            GUILayout.BeginArea(new Rect((Screen.width - w) / 2, Screen.height * .18f, w, Screen.height * .8f));
            GUILayout.Label("Voice Battle", _title);
            GUILayout.Label(ArabicShaper.ForDisplay("غنّي عشان تضرب"), _title);
            GUILayout.Space(24);
            if (GUILayout.Button("Training  ·  " + ArabicShaper.ForDisplay("تدريب"), _button, GUILayout.Height(64))) StartTraining();
            GUILayout.Space(8);
            if (GUILayout.Button("Online  ·  " + ArabicShaper.ForDisplay("أونلاين"), _button, GUILayout.Height(64))) StartOnline();
            GUILayout.Space(8);
            if (GUILayout.Button("Quit", GUILayout.Height(36))) Application.Quit();
            GUILayout.Space(16);
            GUILayout.Label("Use headphones. Esc = back to this menu.");
            if (_steam != null) GUILayout.Label(_steam.Available ? "Steam: " + ArabicShaper.ForDisplay(_steam.PersonaName) : "Steam: off (" + _steam.Problem + ")");
            GUILayout.EndArea();
        }

        private void StartTraining()
        {
            var go = new GameObject("Training");
            go.AddComponent<TrainingMatch>();   // adds MicCapture + AudioSource
            go.AddComponent<TrainingHud>();
            go.AddComponent<BackToMenu>();
            Destroy(gameObject);
        }

        private void StartOnline()
        {
            var go = new GameObject("Online");
            go.AddComponent<NetworkMatch>();    // adds MicCapture + AudioSource, and SteamBridge when Steam is on
            go.AddComponent<OnlineHud>();
            go.AddComponent<BackToMenu>();
            Destroy(gameObject);
        }
    }

    /// <summary>Escape reloads the scene, which brings the main menu back.</summary>
    public sealed class BackToMenu : MonoBehaviour
    {
        private void Update()
        {
            if (Keys.Escape()) SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().buildIndex : 0);
        }
    }

    public static class SceneBasics
    {
        /// <summary>The main camera, created when the scene has none, and exactly one AudioListener (Unity plays nothing without one).</summary>
        public static Camera EnsureCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.07f, 0.05f, 0.12f);
            }
            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            return cam;
        }
    }

    public static class VoiceBattleBootstrap
    {
        /// <summary>Any scene without a Voice Battle object gets the main menu, so an empty project just works.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot() => SceneManager.sceneLoaded += (_, __) => Ensure();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Ensure()
        {
            SceneBasics.EnsureCamera();
            if (UnityEngine.Object.FindFirstObjectByType<MainMenu>() != null || UnityEngine.Object.FindFirstObjectByType<TrainingMatch>() != null
                || UnityEngine.Object.FindFirstObjectByType<NetworkMatch>() != null) return;
            new GameObject("Voice Battle").AddComponent<MainMenu>();
        }
    }
}
