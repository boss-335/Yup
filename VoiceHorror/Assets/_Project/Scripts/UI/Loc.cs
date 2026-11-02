using System.Collections.Generic;
using VoiceHorror.Persistence;

namespace VoiceHorror.UI
{
    /// <summary>Two-language string table (Arabic default, English). Keys are stable; text may change.</summary>
    public static class Loc
    {
        public static bool IsArabic => PlayerSettingsData.Current.language != "en";

        public static string T(string key)
        {
            if (Strings.TryGetValue(key, out var s)) return IsArabic ? s[0] : s[1];
            if (Story.Lines.TryGetValue(key, out s)) return IsArabic ? s[0] : s[1];
            return key;
        }

        public static string F(string key, params object[] args) => string.Format(T(key), args);

        static readonly Dictionary<string, string[]> Strings = new Dictionary<string, string[]>
        {
            // ---------------- Common
            { "game.title", new[] { "الصمّة", "AL-SUMMAH" } },
            { "game.tagline", new[] { "صوتك سلاحك… وصوتك يكشفك", "Your voice is your weapon… and your voice gives you away" } },
            { "btn.continue", new[] { "متابعة", "Continue" } },
            { "btn.start", new[] { "ابدأ", "Start" } },
            { "btn.retry", new[] { "إعادة", "Retry" } },
            { "btn.back", new[] { "رجوع", "Back" } },
            { "btn.close", new[] { "إغلاق", "Close" } },
            { "btn.accept", new[] { "فهمت، ابدأ", "I understand" } },
            { "btn.quit", new[] { "خروج", "Quit" } },
            { "btn.resume", new[] { "استئناف", "Resume" } },
            { "btn.restart_checkpoint", new[] { "من آخر نقطة", "From checkpoint" } },
            { "btn.restart_level", new[] { "إعادة المستوى", "Restart level" } },
            { "btn.record", new[] { "سجّل", "Record" } },
            { "btn.stop", new[] { "إيقاف", "Stop" } },
            { "btn.skip", new[] { "تخطّي", "Skip" } },
            { "btn.save", new[] { "احفظ", "Save" } },
            { "btn.melody", new[] { "لحن / دندنة", "Melody / hum" } },
            { "btn.phrase", new[] { "عبارة منطوقة", "Spoken phrase" } },

            // ---------------- Privacy notice (spec §9)
            { "privacy.title", new[] { "قبل ما نبدأ: اللعبة تسمعك", "Before we start: this game listens to you" } },
            { "privacy.body", new[] {
                "هذي اللعبة تستخدم الميكروفون طول وقت اللعب. أي صوت عالي تسويه، المخلوقات تسمعه. والأسلحة تنطلق لما تغني أو تقول الأغنية أو العبارة اللي سجّلتها.\n\n• كل معالجة الصوت تصير على جهازك فقط. ما نرفع أي صوت ولا أي بيانات.\n• نحفظ ملامح رقمية من تسجيلاتك (شكل اللحن والإيقاع) مو الصوت نفسه، إلا إذا فعّلت \"احفظ تسجيلاتي\" من الإعدادات.\n• تقدر تحذف كل شي من الإعدادات في أي وقت.\n• اضغط مطوّلاً على V للكتم الفوري.",
                "This game uses your microphone the whole time you play. Any loud sound you make, the creatures hear. Weapons fire when you sing or say the song or phrase you recorded.\n\n• All audio processing happens on your device. Nothing is ever uploaded.\n• We keep numeric features of your recordings (melody shape and rhythm), not the audio itself, unless you enable \"Keep my recordings\" in Settings.\n• You can delete everything from Settings at any time.\n• Hold V for instant mute." } },

            // ---------------- Microphone
            { "mic.none", new[] { "ما فيه ميكروفون. وصّل ميكروفون ثم اضغط إعادة.", "No microphone found. Connect one, then press Retry." } },
            { "mic.lost", new[] { "انفصل الميكروفون. اللعبة متوقفة.", "Microphone disconnected. The game is paused." } },
            { "mic.permission", new[] { "ما قدرنا نفتح الميكروفون. تأكد من إذن الميكروفون في إعدادات النظام.", "Could not open the microphone. Check the OS microphone permission." } },
            { "mic.mac_hint", new[] { "على الماك: إعدادات النظام ← الخصوصية والأمان ← الميكروفون ← فعّل Unity.", "On macOS: System Settings → Privacy & Security → Microphone → enable Unity." } },
            { "mic.device", new[] { "الميكروفون", "Microphone" } },

            // ---------------- Calibration (spec §2.2)
            { "cal.title", new[] { "معايرة الصوت", "Voice calibration" } },
            { "cal.intro", new[] { "نحتاج نعرف هدوء غرفتك وصوتك العادي والعالي، عشان نحكم على صوتك بعدل. ثلاث خطوات، عشر ثواني.", "We need to learn your room's silence and your normal and loud voice so we can judge you fairly. Three steps, ten seconds." } },
            { "cal.silence", new[] { "اسكت تماماً…", "Stay completely silent…" } },
            { "cal.talk", new[] { "اقرأ هذي الجملة بصوتك العادي:", "Read this sentence in your normal voice:" } },
            { "cal.talk_line", new[] { "«البحر هادي الليلة، والمنارة تنادي من بعيد.»", "\"The sea is calm tonight, and the lighthouse calls from afar.\"" } },
            { "cal.loud", new[] { "الحين قلها بصوت عالي (مو صراخ):", "Now say it loudly (not a scream):" } },
            { "cal.loud_line", new[] { "«أنا هنا!»", "\"I am here!\"" } },
            { "cal.get_ready", new[] { "استعد…", "Get ready…" } },
            { "cal.done", new[] { "تمت المعايرة.", "Calibration complete." } },
            { "cal.result", new[] { "الهدوء {0:F0} dB · العادي {1:F0} dB · العالي {2:F0} dB", "Silence {0:F0} dB · Normal {1:F0} dB · Loud {2:F0} dB" } },
            { "cal.noisy", new[] { "البيئة مزعجة أو الميكروفون بعيد. قرّب الميكروفون أو قلل الإزعاج ثم أعد.", "The room is noisy or the microphone is far. Move closer or reduce noise, then retry." } },
            { "cal.nodata", new[] { "ما وصلنا صوت من الميكروفون.", "No audio arrived from the microphone." } },
            { "cal.loud_warn", new[] { "صوتك العالي قريب من العادي. بنكمل، بس جرّب ترفع صوتك أكثر لو أعدت.", "Your loud voice is close to your normal one. Continuing, but try louder if you redo it." } },

            // ---------------- Recording (spec §2.6)
            { "rec.title", new[] { "مسجّل جدّك", "Grandfather's tape recorder" } },
            { "rec.intro", new[] { "كل ذخيرة من ذخائر النهّام تحتاج صوتك. سجّل لكل سلاح أغنية أو دندنة أو عبارة — مرتين. بتحتاج تعيدها وأنت خايف، فاختار شي تحفظه.", "Each of the nahham's relics needs your voice. Record a song, hum or phrase for each weapon — twice. You'll have to repeat it while afraid, so pick something you know by heart." } },
            { "rec.choose_type", new[] { "وش تبي تسجّل لهذا السلاح؟", "What will you record for this weapon?" } },
            { "rec.length", new[] { "الطول المسموح: {0:F1}–{1:F1} ثانية", "Allowed length: {0:F1}–{1:F1} s" } },
            { "rec.take", new[] { "التسجيل {0} من 2", "Take {0} of 2" } },
            { "rec.press_record", new[] { "اضغط «سجّل»، ثم غنِّ/قل مقطعك، واسكت لما تخلص.", "Press Record, sing/say your piece, then go silent when done." } },
            { "rec.recording", new[] { "يسجّل… اسكت لما تخلص", "Recording… go silent when finished" } },
            { "rec.analyzing", new[] { "نحلّل…", "Analyzing…" } },
            { "rec.ok", new[] { "تسجيل ممتاز.", "Good take." } },
            { "rec.too_short", new[] { "قصير جداً. خلّه أطول.", "Too short. Make it longer." } },
            { "rec.too_long", new[] { "طويل جداً. اختصره.", "Too long. Shorten it." } },
            { "rec.too_quiet", new[] { "هادي جداً. ارفع صوتك شوي أو قرّب الميكروفون.", "Too quiet. Sing a little louder or move closer." } },
            { "rec.clipped", new[] { "الصوت مقطوع (عالي على الميكروفون). اخفض صوتك أو بعّد الميكروفون.", "Clipped (too loud for the mic). Lower your voice or move back." } },
            { "rec.not_voiced", new[] { "ما سمعنا لحن واضح. دندن أو غنِّ بنغمة.", "We couldn't hear a clear melody. Hum or sing with a tune." } },
            { "rec.differ", new[] { "التسجيلين مختلفين ({0:F0}٪). أعد التسجيلين بنفس الطريقة.", "The two takes differ ({0:F0}%). Redo both the same way." } },
            { "rec.saved", new[] { "انحفظ! تشابه التسجيلين {0:F0}٪", "Saved! Takes match {0:F0}%" } },
            { "rec.need_safe", new[] { "ما تقدر تسجّل إلا في بيت النهّام (المكان الآمن).", "You can only record in the nahham's house (safe room)." } },
            { "rec.streamer", new[] { "وضع البث: أغاني محمية قد تسبب مطالبات حقوق على البث. ننصح بدندنة أو عبارة خاصة.", "Streamer mode: copyrighted songs may trigger claims on your stream. Prefer a hum or a personal phrase." } },
            { "rec.weapon_n", new[] { "السلاح {0} من {1}", "Weapon {0} of {1}" } },

            // ---------------- HUD
            { "hud.level.Silent", new[] { "صامت", "Silent" } },
            { "hud.level.Whisper", new[] { "همس", "Whisper" } },
            { "hud.level.Talk", new[] { "كلام", "Talk" } },
            { "hud.level.Loud", new[] { "عالي", "Loud" } },
            { "hud.level.Scream", new[] { "صراخ", "Scream" } },
            { "hud.threat", new[] { "الخطر", "Threat" } },
            { "hud.heard", new[] { "يُسمع حتى {0:F0} م", "Heard up to {0:F0} m" } },
            { "hud.muted", new[] { "مكتوم", "MUTED" } },
            { "hud.hidden", new[] { "مختبئ — صوتك مكتوم للنص", "Hidden — your voice is halved" } },
            { "hud.safe", new[] { "مكان آمن — لا أحد يسمعك هنا", "Safe — nothing can hear you here" } },
            { "hud.perform", new[] { "غنِّ الآن…", "Sing now…" } },
            { "hud.perform_phrase", new[] { "قلها الآن…", "Say it now…" } },
            { "hud.perform_cancel", new[] { "زر يمين للإلغاء", "Right-click to cancel" } },
            { "hud.scoring", new[] { "…", "…" } },
            { "hud.score", new[] { "{0:F0}", "{0:F0}" } },
            { "hud.failed", new[] { "فشل — ما وصل للحد", "Failed — below threshold" } },
            { "hud.no_voice", new[] { "ما سمعناك", "We didn't hear you" } },
            { "hud.cooldown", new[] { "يبرد", "cooling" } },
            { "hud.needs_rec", new[] { "يحتاج تسجيل", "needs recording" } },
            { "hud.locked", new[] { "مقفل", "locked" } },
            { "hud.hint_fire", new[] { "زر يسار: أدِّ أغنية السلاح · 1/2/3 تبديل · V كتم", "LMB: perform weapon song · 1/2/3 switch · V mute" } },
            { "hud.objective", new[] { "الهدف", "Objective" } },

            // ---------------- Prompts
            { "prompt.hide", new[] { "[E] اختبئ في الدولاب", "[E] Hide in the closet" } },
            { "prompt.leave_hiding", new[] { "[E] اطلع", "[E] Leave" } },
            { "prompt.play_tape", new[] { "[E] شغّل الشريط", "[E] Play the tape" } },
            { "prompt.take_relic", new[] { "[E] خذ الذخيرة", "[E] Take the relic" } },
            { "prompt.record_here", new[] { "[E] سجّل أغاني الأسلحة", "[E] Record weapon songs" } },

            // ---------------- Pause / settings (spec §5)
            { "pause.title", new[] { "متوقف", "Paused" } },
            { "set.title", new[] { "الإعدادات", "Settings" } },
            { "set.recalibrate", new[] { "إعادة المعايرة", "Recalibrate" } },
            { "set.rerecord", new[] { "إعادة تسجيل الأسلحة", "Re-record weapons" } },
            { "set.sensitivity", new[] { "حساسية إضافية: {0:+0;-0;0} dB", "Extra sensitivity: {0:+0;-0;0} dB" } },
            { "set.streamer", new[] { "وضع البث (إخفاء أسماء الأغاني)", "Streamer mode (hide song names)" } },
            { "set.keep_raw", new[] { "احفظ تسجيلاتي (ملفات صوت على جهازي)", "Keep my recordings (audio files on my device)" } },
            { "set.delete_all", new[] { "احذف كل تسجيلاتي", "Delete all my recordings" } },
            { "set.deleted", new[] { "انحذفت كل التسجيلات. بتحتاج تسجّل من جديد.", "All recordings deleted. You'll need to record again." } },
            { "set.language", new[] { "اللغة: العربية", "Language: English" } },
            { "set.debug", new[] { "شاشة التصحيح (F3)", "Debug overlay (F3)" } },
            { "set.mouse", new[] { "حساسية الماوس: {0:F1}", "Mouse sensitivity: {0:F1}" } },
            { "set.volume", new[] { "الصوت: {0:P0}", "Volume: {0:P0}" } },
            { "set.mute_hint", new[] { "كتم فوري: اضغط مطوّلاً على V (يُحتسب صمت).", "Instant mute: hold V (counts as silence)." } },

            // ---------------- End screens
            { "end.dead_title", new[] { "سكتّي", "Silenced" } },
            { "end.dead_body", new[] { "صار صوتك واحد من أصوات الجزيرة.", "Your voice became one more voice of the island." } },
            { "end.win_title", new[] { "انكسرت الصمّة", "The Hush is broken" } },
            { "end.win_body", new[] { "الموج يرجع يتكلم. لأول مرة من أربعين ليلة، الجزيرة تسمع صوت غير صوت الخوف.", "The waves speak again. For the first time in forty nights the island hears something other than fear." } },
            { "end.stats", new[] { "أفضل أداء: {0:F0} · أعلى خطر: {1:F0} · أشرطة: {2}/{3}", "Best performance: {0:F0} · Peak threat: {1:F0} · Tapes: {2}/{3}" } },

            // ---------------- Score lab (M1)
            { "lab.title", new[] { "مختبر التقييم", "Score Lab" } },
            { "lab.record_ref", new[] { "سجّل المرجع", "Record reference" } },
            { "lab.record_perf", new[] { "سجّل الأداء", "Record performance" } },
            { "lab.type", new[] { "النوع: {0}", "Type: {0}" } },
            { "lab.no_ref", new[] { "سجّل مرجع أولاً.", "Record a reference first." } },
            { "lab.result", new[] { "الدرجة {0:F1} · اللحن {1:F1} · الإيقاع {2:F1} · النبرة {3:F1} · الطول {4:F2}×", "Score {0:F1} · Melody {1:F1} · Rhythm {2:F1} · Timbre {3:F1} · Length {4:F2}×" } },
            { "lab.legend", new[] { "أزرق: المرجع · برتقالي: أداؤك بعد المحاذاة (نصف نغمات نسبية)", "Blue: reference · Orange: your performance after alignment (relative semitones)" } },

            // ---------------- Weapon range (M2)
            { "range.title", new[] { "ساحة التدريب", "Training yard" } },
            { "range.help", new[] { "الدمى على 6 و12 و20 م. جرّب كل سلاح وشوف الضرر مقابل الدرجة.", "Dummies at 6, 12 and 20 m. Try each weapon and compare damage to score." } },
            { "range.dummy", new[] { "آخر ضرر {0:F0} · المجموع {1:F0} · ضربات {2}", "Last {0:F0} · Total {1:F0} · Hits {2}" } },

            // ---------------- Journal
            { "journal.title", new[] { "الأشرطة", "Tapes" } },
            { "journal.empty", new[] { "ما لقيت أشرطة للحين.", "No tapes found yet." } },
        };
    }
}
