using UnityEngine;
using UnityEngine.SceneManagement;

namespace VoiceHorror.UI
{
    /// <summary>Title screen: start the story, the test scenes, the gallery, settings, quit.</summary>
    public sealed class TitleMenu : MonoBehaviour
    {
        [System.Serializable]
        public struct Entry
        {
            public string labelAr, labelEn, scene;
        }

        public Entry[] entries =
        {
            new Entry { labelAr = "ابدأ القصة", labelEn = "Begin the story", scene = "M4_VerticalSlice_AlSummah" },
            new Entry { labelAr = "ساحة التدريب", labelEn = "Training yard", scene = "M2_WeaponRange" },
            new Entry { labelAr = "معرض الشخصيات", labelEn = "Character gallery", scene = "CharacterGallery" },
            new Entry { labelAr = "اختبار الميكروفون", labelEn = "Microphone test", scene = "M0_MicTest" },
            new Entry { labelAr = "مختبر التقييم", labelEn = "Score lab", scene = "M1_ScoreLab" },
        };

        public Transform cameraRig;
        public Vector3 lookAt = new Vector3(40f, 8f, 140f);
        float _fade = 1f;
        Vector3 _camStart;

        void Start()
        {
            Core.GameState.Reset();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (cameraRig != null) _camStart = cameraRig.position;
        }

        void Update()
        {
            _fade = Mathf.MoveTowards(_fade, 0f, Time.deltaTime * 0.5f);
            if (cameraRig != null)
            {
                float t = Time.time;
                cameraRig.position = _camStart + new Vector3(Mathf.Sin(t * 0.05f) * 3f, Mathf.Sin(t * 0.2f) * 0.15f, Mathf.Sin(t * 0.03f) * 2f);
                cameraRig.rotation = Quaternion.Slerp(cameraRig.rotation, Quaternion.LookRotation(lookAt - cameraRig.position) * Quaternion.Euler(Mathf.Sin(t * 0.3f) * 0.6f, 0f, Mathf.Sin(t * 0.25f) * 0.8f), Time.deltaTime);
            }
        }

        void OnGUI()
        {
            var pause = FindFirstObjectByType<PauseMenuView>();
            if (pause != null && pause.IsOpen) return;
            Ui.Begin();
            bool ar = Loc.IsArabic;
            float x = Ui.Rtl ? Ui.W - 620f : 90f;
            Ui.Text(new Rect(x, 140f, 540f, 140f), Loc.T("game.title"), 120, TextAnchor.UpperLeft, new Color(0.95f, 0.9f, 0.8f), true, false);
            Ui.Text(new Rect(x, 290f, 540f, 40f), Loc.T("game.tagline"), 24, TextAnchor.UpperLeft, Ui.InkDim);
            float y = 380f;
            foreach (var e in entries)
            {
                if (Ui.Button(new Rect(x, y, 420f, 58f), ar ? e.labelAr : e.labelEn, 26))
                {
                    if (Application.CanStreamedLevelBeLoaded(e.scene)) SceneManager.LoadScene(e.scene);
                    else Debug.LogWarning($"[Title] Scene '{e.scene}' is not in Build Settings — run Tools ▸ VoiceHorror ▸ Build All Scenes.");
                }
                y += 70f;
            }
            if (Ui.Button(new Rect(x, y, 420f, 58f), Loc.T("set.title"), 24) && pause != null) pause.OpenSettingsOnly();
            y += 70f;
            if (Ui.Button(new Rect(x, y, 420f, 58f), Loc.T("btn.quit"), 24))
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }
            Ui.Text(new Rect(30, Ui.H - 44, Ui.W - 60, 30), ar ? "العب بالسماعات · الميكروفون إلزامي" : "Play with headphones · a microphone is required", 18, TextAnchor.UpperCenter, Ui.InkDim);
            if (_fade > 0f) Ui.Fill(new Rect(0, 0, Ui.W, Ui.H), new Color(0, 0, 0, _fade));
        }
    }
}
