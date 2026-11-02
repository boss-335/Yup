using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Core;
using VoiceHorror.Persistence;

namespace VoiceHorror.UI
{
    /// <summary>
    /// Spec §2.1: no microphone / microphone lost / permission denied → pause with a message and Retry.
    /// The game cannot be played without a microphone, so this blocks everything.
    /// </summary>
    public sealed class MicStatusView : MonoBehaviour
    {
        bool _blocking;

        void Update()
        {
            var mic = MicCapture.Instance;
            bool bad = mic != null && (mic.State == MicCapture.Status.NoDevice || mic.State == MicCapture.Status.DeviceLost || mic.State == MicCapture.Status.PermissionDenied);
            if (bad != _blocking)
            {
                _blocking = bad;
                GameState.Block(this, bad);
                GameState.Pause(this, bad);
            }
        }

        void OnDisable()
        {
            GameState.Block(this, false);
            GameState.Pause(this, false);
        }

        void OnGUI()
        {
            if (!_blocking) return;
            var mic = MicCapture.Instance;
            GUI.depth = -60;
            Ui.Begin();
            Ui.Dim(0.85f);
            var r = Ui.Centered(Mathf.Min(900f, Ui.W - 60f), 440f);
            Ui.Panel(r);
            Ui.Fill(new Rect(r.x, r.y, r.width, 4), Ui.Threat);
            string msg = mic.State == MicCapture.Status.NoDevice ? Loc.T("mic.none")
                : mic.State == MicCapture.Status.PermissionDenied ? Loc.T("mic.permission") : Loc.T("mic.lost");
            float y = r.y + 40;
            y += Ui.Text(new Rect(r.x + 50, y, r.width - 100, 120), msg, 30, TextAnchor.UpperLeft, Ui.Ink, true) + 16;
            if (Application.platform == RuntimePlatform.OSXEditor || Application.platform == RuntimePlatform.OSXPlayer)
                y += Ui.Text(new Rect(r.x + 50, y, r.width - 100, 80), Loc.T("mic.mac_hint"), 20, TextAnchor.UpperLeft, Ui.InkDim) + 10;
            if (!string.IsNullOrEmpty(mic.LastError))
                Ui.Text(new Rect(r.x + 50, y, r.width - 100, 40), mic.LastError, 16, TextAnchor.UpperLeft, Ui.InkDim, false, true, false);
            if (Ui.Button(new Rect(r.center.x - 150, r.yMax - 96, 300, 62), Loc.T("btn.retry"), 28))
                mic.StartCapture(PlayerSettingsData.Current.micDevice);
        }
    }
}
