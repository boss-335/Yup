using System;
using System.Collections.Generic;

namespace VoiceHorror.UI
{
    /// <summary>
    /// Narrative content for the vertical slice "جزيرة الصمّة" (see docs/GAME_DESIGN.md).
    /// Characters: Reem (the player), her grandfather Abdullah the nahham (tapes),
    /// Mubarak the lighthouse keeper (radio).
    /// </summary>
    public static class Story
    {
        public sealed class Tape
        {
            public string id;
            public string titleAr, titleEn;
            public string speakerAr, speakerEn;
            public string bodyAr, bodyEn;
        }

        public static readonly List<Tape> Tapes = new List<Tape>
        {
            new Tape
            {
                id = "tape_01",
                titleAr = "الشريط الأول — الصوت له وزن", titleEn = "Tape 1 — A voice has weight",
                speakerAr = "عبدالله النهّام", speakerEn = "Abdullah the Nahham",
                bodyAr = "يا ريم، إذا لقيتي هالشريط فأنا ما رجعت.\nفي البحر تعلّمنا شي واحد: الصوت له وزن. النهّام ما يصارخ على الغاصة، النهّام يوزن صوته. اللي يصارخ يتعب ويتعّب اللي حوله.\nغنّي بدقة، مو بقوة. حتى الهمس إذا كان صادق… يوصل.",
                bodyEn = "Reem, if you found this tape, I didn't come back.\nThe sea taught us one thing: a voice has weight. The nahham never shouts at the divers — he weighs his voice. Whoever shouts tires himself and everyone around him.\nSing true, not loud. Even a whisper, if it is honest, arrives.",
            },
            new Tape
            {
                id = "tape_02",
                titleAr = "الشريط الثاني — المساكيت", titleEn = "Tape 2 — The Silenced",
                speakerAr = "عبدالله النهّام", speakerEn = "Abdullah the Nahham",
                bodyAr = "اللي تشوفينهم في السكيك كانوا جيرانّا. أم ناصر، وولد الخبّاز، وسالم اللي يبيع الشباك.\nالسكتة سرقت أصواتهم وخيّطت أفواههم. ما يشوفون… بس يسمعون نفَسك.\nإذا سمعوك، يروحون للمكان اللي سمعوك فيه — مو لمكانك الحين. تكلّمي، وتحرّكي.",
                bodyEn = "The ones you see in the alleys were our neighbours. Umm Nasser, the baker's boy, Salem who sold the nets.\nThe Hush stole their voices and sewed their mouths shut. They cannot see… but they hear your breath.\nWhen they hear you, they go to where they heard you — not to where you are now. Speak, then move.",
            },
            new Tape
            {
                id = "tape_03",
                titleAr = "الشريط الثالث — النَّفَس", titleEn = "Tape 3 — The Breath",
                speakerAr = "عبدالله النهّام", speakerEn = "Abdullah the Nahham",
                bodyAr = "في الصندوق تحت الدرج تركت لك آخر ذخيرة: النَّفَس.\nمو أغنية، كلمة. كلمة قصيرة تقولينها وتدفعهم عنك لحظة.\nاختاري كلمة ما ينساها لسانك وأنتي خايفة. اسم أمك، أو دعاء، أو شي يضحكك.",
                bodyEn = "In the chest under the stairs I left you the last relic: the Breath.\nNot a song — a word. A short word you say that pushes them away for a moment.\nChoose one your tongue won't forget when you're afraid. Your mother's name, a prayer, something that makes you laugh.",
            },
            new Tape
            {
                id = "tape_04",
                titleAr = "الشريط الرابع — السعلوة", titleEn = "Tape 4 — The Si'luwa",
                speakerAr = "عبدالله النهّام", speakerEn = "Abdullah the Nahham",
                bodyAr = "في السوق شي أسرع من المساكيت. العجايز يسمّونها السعلوة، وكنا نضحك عليهم.\nلا تركضين قدامها، خطوات الركض تنسمع. إذا قربت منك تنقز.\nإذا جرحتيها جرح زين تهرب… وترجع. دايم ترجع.",
                bodyEn = "In the market there is something faster than the Silenced. The old women called it the Si'luwa, and we laughed at them.\nDon't run in front of her — running footsteps carry. When she's close, she leaps.\nWound her badly and she flees… and comes back. She always comes back.",
            },
            new Tape
            {
                id = "tape_05",
                titleAr = "الشريط الخامس — مبارك", titleEn = "Tape 5 — Mubarak",
                speakerAr = "عبدالله النهّام", speakerEn = "Abdullah the Nahham",
                bodyAr = "مبارك، حارس المنارة، كان يسجّل كل شي. كل أغنية، كل عرس، كل نهمة على اللنج.\nقال لي: «لو حفظنا أصوات الجزيرة، ما تموت أبد.»\nأنا خايف يا ريم. خايف إنه حفظها… أكثر من اللازم.",
                bodyEn = "Mubarak, the lighthouse keeper, recorded everything. Every song, every wedding, every work-chant on the dhow.\nHe told me: \"If we keep the island's voices, it can never die.\"\nI'm afraid, Reem. Afraid he kept them… more than he should have.",
            },
            new Tape
            {
                id = "tape_06",
                titleAr = "الشريط الأخير — الطار", titleEn = "Last tape — The Drum",
                speakerAr = "عبدالله النهّام", speakerEn = "Abdullah the Nahham",
                bodyAr = "الصمّة ما تنكسر بكلمة ولا بنغمة قصيرة.\nتنكسر بأغنية كاملة — الأغنية اللي سجّلتيها للطار.\nغنّيها كلها، للآخر، ولو صاحت الجزيرة كلها عليك. هذا وزن صوتك.",
                bodyEn = "The Hush won't break for a word or a short tune.\nIt breaks for a whole song — the one you recorded for the Drum.\nSing all of it, to the end, even if the whole island screams at you. That is the weight of your voice.",
            },
        };

        public static Tape FindTape(string id) => Tapes.Find(t => t.id == id);

        /// <summary>Objectives, radio lines and intro/outro cards.</summary>
        public static readonly Dictionary<string, string[]> Lines = new Dictionary<string, string[]>
        {
            { "intro.card", new[] {
                "جزيرة الصمّة، ١٩٧٩.\nقبل أربعين ليلة سكتت الجزيرة كلها في ليلة وحدة. آخر بث طلع من منارتها كان صوت جدّك النهّام يغنّي… ثم انقطع.\nجيتي في قارب صغير تدورين عليه.",
                "Al-Summah Island, 1979.\nForty nights ago the whole island fell silent in a single night. The last broadcast from its lighthouse was your grandfather, the nahham, singing… then nothing.\nYou came in a small boat to find him." } },

            { "obj.reach_alleys", new[] { "ادخل الفريج من المرسى", "Enter the quarter from the dock" } },
            { "obj.find_house", new[] { "اوصل بيت جدّك النهّام", "Reach your grandfather's house" } },
            { "obj.take_breath", new[] { "خذ «النَّفَس» من الصندوق تحت الدرج", "Take the Breath from the chest under the stairs" } },
            { "obj.cross_market", new[] { "اعبر السوق — وانتبه للبير", "Cross the market — beware the well" } },
            { "obj.reach_dhow", new[] { "اوصل اللنج في آخر الرصيف", "Reach the dhow at the end of the pier" } },
            { "obj.break_hush", new[] { "اكسر الصمّة بأغنية الطار", "Break the Hush with the Drum's song" } },

            { "radio.name", new[] { "مبارك — المنارة", "Mubarak — Lighthouse" } },
            { "radio.arrive", new[] { "ريم؟ إذا تسمعيني لا تردّين بصوت عالي. هنا مبارك، من المنارة. اللي في الجزيرة يسمعون… بس ما يشوفون.", "Reem? If you can hear me, don't answer out loud. It's Mubarak, at the lighthouse. The things on this island hear… but they don't see." } },
            { "radio.alleys", new[] { "السكيك ضيقة، والجدران تبلع شوي من الصوت. إذا قرب منك واحد، ادخلي دولاب واكتمي نفَسك.", "The alleys are narrow; the walls swallow some sound. If one comes close, get in a closet and hold your breath." } },
            { "radio.house", new[] { "بيت جدّك قدامك. جدرانه من حجر البحر، ما يطلع منها صوت. ارتاحي هناك.", "Your grandfather's house is ahead. Its walls are sea-stone; no sound gets out. Rest there." } },
            { "radio.market", new[] { "عند البير يقعد بو درياه. ما يتحرك، بس إذا سمعك يصيح… وكل اللي في الجزيرة يجون لصوته.", "Bu Daryah sits by the well. He doesn't move, but if he hears you he wails… and everything on the island comes to his call." } },
            { "radio.dhow", new[] { "اللنج في آخر الرصيف. الصمّة هناك. أغاني جدّك القصيرة ما تكفي… تحتاجين الطار.", "The dhow is at the end of the pier. The Hush is there. Your grandfather's short songs won't be enough… you need the Drum." } },
            { "radio.threat", new[] { "…ريم، صوتك واصل لين المنارة. اهدي.", "…Reem, I can hear you from the lighthouse. Quiet." } },
            { "radio.win", new[] { "…ريم؟ أسمع الموج. الجزيرة تتكلم.\n…بس الصوت اللي في آخر بث — مو صوت جدّك. هذا صوتي أنا. أنا اللي سجّلته.", "…Reem? I can hear the waves. The island is speaking.\n…But the voice in that last broadcast — it wasn't your grandfather. It was mine. I'm the one who recorded it." } },
        };
    }

    /// <summary>Collected tapes this session.</summary>
    public static class Journal
    {
        static readonly List<string> Collected = new List<string>();
        public static IReadOnlyList<string> Items => Collected;
        public static event Action<string> TapeCollected;

        public static void Collect(string id)
        {
            if (string.IsNullOrEmpty(id) || Collected.Contains(id)) return;
            Collected.Add(id);
            TapeCollected?.Invoke(id);
        }

        public static void Reset() => Collected.Clear();
    }

    /// <summary>Current objective (Loc key).</summary>
    public static class Objectives
    {
        public static string CurrentKey { get; private set; } = "";
        public static float ChangedAt { get; private set; } = -100f;
        public static event Action<string> Changed;

        public static void Set(string key)
        {
            if (CurrentKey == key) return;
            CurrentKey = key;
            ChangedAt = UnityEngine.Time.time;
            Changed?.Invoke(key);
        }

        public static void Reset()
        {
            CurrentKey = "";
            ChangedAt = -100f;
        }
    }
}
