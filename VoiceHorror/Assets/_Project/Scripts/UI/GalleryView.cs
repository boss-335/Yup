using System.Collections.Generic;
using UnityEngine;
using VoiceHorror.Gameplay.Creatures;

namespace VoiceHorror.UI
{
    /// <summary>
    /// Character gallery camera + overlay: orbits the focused character, shows its name, lore and
    /// the animation playing. ←/→ switch character, ↑/↓ switch animation, Space pauses the cycle.
    /// </summary>
    public sealed class GalleryView : MonoBehaviour
    {
        [System.Serializable]
        public sealed class Exhibit
        {
            public Transform target;
            public string nameAr, nameEn;
            [TextArea] public string loreAr, loreEn;
            public float viewHeight = 1.2f;
            public float viewDistance = 4f;
        }

        public List<Exhibit> exhibits = new List<Exhibit>();
        public Camera cam;
        int _index;
        float _orbit;
        Vector3 _camPos;

        static readonly Dictionary<GalleryPuppet.Act, string[]> ActNames = new Dictionary<GalleryPuppet.Act, string[]>
        {
            { GalleryPuppet.Act.Idle, new[] { "سكون", "Idle" } },
            { GalleryPuppet.Act.Walk, new[] { "مشي", "Walk" } },
            { GalleryPuppet.Act.Listen, new[] { "يتسمّع (يلف أذنه للصوت)", "Listening (turns an ear to the sound)" } },
            { GalleryPuppet.Act.Hunt, new[] { "مطاردة", "Hunt" } },
            { GalleryPuppet.Act.Attack, new[] { "هجوم", "Attack" } },
            { GalleryPuppet.Act.Stagger, new[] { "ترنّح (أصابته أغنية)", "Stagger (hit by a song)" } },
            { GalleryPuppet.Act.Wail, new[] { "صيحة", "Wail" } },
            { GalleryPuppet.Act.Dormant, new[] { "نايم عند الجدار", "Dormant against a wall" } },
            { GalleryPuppet.Act.Waking, new[] { "يصحى", "Waking" } },
            { GalleryPuppet.Act.Death, new[] { "موت", "Death" } },
        };

        void Start()
        {
            if (cam == null) cam = Camera.main;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (exhibits.Count > 0) _camPos = TargetCamPos(exhibits[0]);
        }

        Vector3 TargetCamPos(Exhibit e)
        {
            Vector3 c = e.target.position;
            return c + new Vector3(Mathf.Sin(_orbit) * e.viewDistance, e.viewHeight + 0.4f, -Mathf.Cos(_orbit) * e.viewDistance);
        }

        void LateUpdate()
        {
            if (exhibits.Count == 0 || cam == null) return;
            var e = exhibits[_index];
            if (e.target == null) return;
            _orbit += Time.deltaTime * 0.18f;
            _camPos = Vector3.Lerp(_camPos, TargetCamPos(e), 1f - Mathf.Exp(-Time.deltaTime * 2.5f));
            cam.transform.position = _camPos;
            cam.transform.LookAt(e.target.position + Vector3.up * e.viewHeight * 0.85f);
        }

        void OnGUI()
        {
            var ev = Event.current;
            if (ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.RightArrow || ev.keyCode == KeyCode.D) { _index = (_index + 1) % exhibits.Count; ev.Use(); }
                else if (ev.keyCode == KeyCode.LeftArrow || ev.keyCode == KeyCode.A) { _index = (_index - 1 + exhibits.Count) % exhibits.Count; ev.Use(); }
                else if (ev.keyCode == KeyCode.UpArrow || ev.keyCode == KeyCode.W) { Puppet()?.Next(); ev.Use(); }
                else if (ev.keyCode == KeyCode.DownArrow || ev.keyCode == KeyCode.S) { Puppet()?.Previous(); ev.Use(); }
                else if (ev.keyCode == KeyCode.Space) { var p = Puppet(); if (p != null) p.paused = !p.paused; ev.Use(); }
                else if (ev.keyCode == KeyCode.L) { var s = Persistence.PlayerSettingsData.Current; s.language = s.language == "en" ? "ar" : "en"; Persistence.PlayerSettingsData.Save(); ev.Use(); }
            }
            if (exhibits.Count == 0) return;
            var x = exhibits[_index];
            Ui.Begin();
            bool ar = Loc.IsArabic;
            var panel = new Rect(Ui.Rtl ? Ui.W - 640f : 30f, 30f, 610f, 330f);
            Ui.Panel(panel);
            float y = panel.y + 20f;
            Ui.Text(new Rect(panel.x + 24, y, panel.width - 48, 30), ar ? "معرض الشخصيات" : "Character gallery", 20, TextAnchor.UpperLeft, Ui.InkDim);
            y += 32f;
            Ui.Text(new Rect(panel.x + 24, y, panel.width - 48, 50), ar ? x.nameAr : x.nameEn, 40, TextAnchor.UpperLeft, Ui.Gold, true);
            y += 58f;
            y += Ui.Text(new Rect(panel.x + 24, y, panel.width - 48, 150), ar ? x.loreAr : x.loreEn, 21, TextAnchor.UpperLeft, Ui.Ink) + 10f;
            var puppet = Puppet();
            if (puppet != null && ActNames.TryGetValue(puppet.Current, out var names))
            {
                Ui.Text(new Rect(panel.x + 24, panel.yMax - 62, panel.width - 48, 30), (ar ? "الحركة: " : "Animation: ") + (ar ? names[0] : names[1]) + (puppet.paused ? (ar ? "  (متوقف)" : "  (paused)") : ""), 22, TextAnchor.UpperLeft, Ui.Voice);
                Ui.Bar(new Rect(panel.x + 24, panel.yMax - 24, panel.width - 48, 5), puppet.paused ? 1f : puppet.ActProgress, Ui.Voice);
            }
            string help = ar ? "← → الشخصية · ↑ ↓ الحركة · مسافة إيقاف · L اللغة" : "← → character · ↑ ↓ animation · Space pause · L language";
            Ui.Text(new Rect(30, Ui.H - 50, Ui.W - 60, 30), help + $"   ({_index + 1}/{exhibits.Count})", 20, TextAnchor.UpperCenter, Ui.InkDim);
        }

        GalleryPuppet Puppet() => exhibits.Count > 0 && exhibits[_index].target != null ? exhibits[_index].target.GetComponent<GalleryPuppet>() : null;
    }
}
