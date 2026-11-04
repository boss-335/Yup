# Voice Battle

لعبة قتال جماعية: **غنّي عشان تضرب**. وثيقة التصميم الكاملة: [`docs/voice-battle-gdd.md`](docs/voice-battle-gdd.md).
المراحل المنجزة: **M0 — محرّك المطابقة** ([تقرير](docs/m0-report.md)) و **M1 — القتال المحلي** ([تقرير](docs/m1-report.md)): مكتبات C# مستقلة عن Unity + أداة سطر أوامر + اختبارات + سكربتات Unity.
اللعب أونلاين (فرق، ساحة المعركة): [`docs/online.md`](docs/online.md) · Steam: [`docs/steam.md`](docs/steam.md) · حركاتي وأصواتي: [`docs/my-moves-and-sounds.md`](docs/my-moves-and-sounds.md) · الشكل البصري والشخصيات: [`docs/art-direction.md`](docs/art-direction.md) — ومعاينة تفاعلية تفتحها بأي متصفح: [`docs/preview/roster.html`](docs/preview/roster.html).

> كل شي يشتغل **محلياً على جهازك بدون إنترنت** بعد أول `restore` (يحمّل حزم NuGet مرة وحدة).

### البداية السريعة (نقل المشروع لجهازك)
1. ثبّت **.NET SDK 8** و **git** و **Unity Hub** + **Unity 2022.3 LTS** (أو أحدث). (اختياري: عميل **Steam**.)
2. انسخ المستودع (أو فك الـ zip) وشغّل من مجلده:
   - Windows: `.\setup.ps1`
   - macOS / Linux: `./setup.sh`

   يتأكد من الأدوات، يبني ويختبر كل شي، ويجهّز مشروع Unity.
3. Unity Hub → **Add project from disk** → اختر مجلد **`unity/`**.
4. في Unity: القائمة **Voice Battle → Create main scene** ثم ▶ **Play** → القائمة الرئيسية (تدريب / أونلاين).
5. أونلاين مع صديق: `dotnet run --project tools/voice-battle-server -c Release` واتصلوا على IP جهازك، منفذ 7777.

---

## 1. التشغيل على جهازك

### المتطلبات
| | |
|---|---|
| **.NET SDK 8** (أو أحدث) | https://dotnet.microsoft.com/download/dotnet/8.0 — Windows / macOS / Linux |
| Git | لنسخ المستودع |
| (اختياري) VS Code + إضافة C# Dev Kit، أو Visual Studio 2022، أو JetBrains Rider | للتطوير |
| Unity 2022.3 LTS أو أحدث (عبر Unity Hub) | اللعبة نفسها — مجلد `unity/` مشروع جاهز |
| (اختياري) عميل Steam | الاسم وتسجيل الدخول ودعوة الأصدقاء (`docs/steam.md`) |

تأكد من التثبيت:
```bash
dotnet --version
```

### نسخ المستودع
```bash
git clone https://github.com/boss-335/Yup.git
```
```bash
cd Yup
git checkout claude/stoic-faraday-or66ig
```

### البناء والاختبار
macOS / Linux:
```bash
./build.sh
```
Windows (PowerShell):
```powershell
.\build.ps1
```
أو مباشرة على أي نظام:
```bash
dotnet test VoiceBattle.sln
```

`build.sh all` / `.\build.ps1 all` يبني ويختبر ويشغّل اختبار القبول ويكتب النتائج في `docs/m0-synthetic-results.md`.

### الشغل بدون إنترنت
بعد أول `dotnet restore` الحزم تنحفظ في كاش NuGet على جهازك (`~/.nuget/packages`)، وبعدها كل الأوامر تشتغل أوفلاين.
ما فيه أي خدمة سحابية أو مفتاح API أو سيرفر في هذه المرحلة.

---

## 2. أداة `matcher-lab`

```bash
dotnet run --project tools/matcher-lab -- <command>
```

| الأمر | الوظيفة |
|---|---|
| `match --ref data/midi/ode_to_joy_a.mid --try take.wav` | يقارن تسجيلك بالمرجع ويطبع تفاصيل الدرجة (pitch / rhythm / coverage / tempo) |
| `match ... --max-speed 1.6` | نفس الشي بسماحية سرعة الضربة القاضية |
| `analyze take.wav` | يطبع منحنى اللحن المستخرج كرسم نصي |
| `make-midi --all` | يبني ملفات `data/midi/*.mid` من `data/melodies/*.json` |
| `synth --midi ref.mid --out take.wav --performer man --kind hum` | يولّد "مغنّي" صناعي للتجربة |
| `eval` | اختبار القبول على أصوات صناعية |
| `eval --manifest recordings/takes.csv` | **نفس الاختبار على تسجيلات حقيقية** (انظر القسم 4) |
| `gen-takes --out recordings/humanized` | يولّد 240 أداء أصعب (حروف ساكنة، ضجيج، صدى، قص…) + `takes.csv` |
| `battle --skill average` | مباراة تدريب كاملة: مغنّي صناعي ضد البوت عبر كل المراحل الحقيقية |
| `teach --slot heavy --name <name> --takes a.wav b.wav` | علّم حركة بالغناء (تنحفظ على جهازك فقط) |
| `moves` / `moves --hum <id> --out x.wav` | حركاتك المحفوظة / اسمعها مدندنة |
| `upload --file x.wav --title <title> --attest` | رفع صوت مع كل فحوصات §5.4 (محلي للتجربة) |
| `analyze take.wav --frames` | يطبع مخرجات YIN الخام إطاراً بإطار (لتشخيص أخطاء الطبقة) |

سجّل نفسك بأي برنامج (Audacity مثلاً) واحفظ WAV، ثم:
```bash
dotnet run --project tools/matcher-lab -- match --ref data/midi/ode_to_joy_a.mid --try my_take.wav
```

---

## 3. هيكل المستودع (GDD §8.5)

```
src/VoiceMatch.Core/         مكتبة المطابقة (netstandard2.1 — تنفع في Unity) بدون أي اعتماد على Unity
  Dsp/                       FFT، YIN، كشف البدايات (SuperFlux)، تطبيع، Endpointer للمايك الحي
  Matching/                  DTW + حساب الدرجة
  Midi/  Audio/              قراءة/كتابة MIDI و WAV
  Synthesis/                 مولّد أصوات صناعية للاختبارات
src/VoiceMatch.Core.Tests/   اختبارات xUnit
src/BattleRules.Core/        منطق القتال (HP، الشحن، التبريد، الضرر، الحزم والتراخيص، بوت التدريب) بدون Unity
src/VoiceBattle.Session/     يربط المكتبتين: تقطيع المايك، التعرّف على الحركة المغنّاة، جلسة التدريب، تعليم الحركات
src/VoiceBattle.Sounds/      أصوات اللاعبين (§5.4): الرفع والفحوصات، المكتبة، البلاغات، الإزالة
src/VoiceBattle.Net/         اللعب أونلاين: البروتوكول، السيرفر الحاكم (غرف، لعب سريع، بوتات)، TCP
tools/voice-battle-server/   برنامج السيرفر
unity/                       مشروع Unity جاهز: السكربتات، أدوات المحرر، الحزم (انظر unity/README.md)
tools/steam/                 رفع النسخ على Steam (SteamPipe)
Dockerfile                   صورة السيرفر لـ VPS
setup.sh / setup.ps1         أول تشغيل على جهاز جديد
tools/matcher-lab/           أداة سطر الأوامر
data/match_config.json       كل العتبات والأوزان (لا شيء ثابت في الكود)
data/moves.json, battle.json إعدادات الحركات والمباراة والبوت
data/characters.json         الشخصيات (شكل فقط، بدون قوة)
data/visuals.json            ألوان المسرح وتوقيتات المؤثرات
data/modes.json              الأوضاع: مبارزة، فرق ٢×٢ و٤×٤، الكل ضد الكل، ساحة المعركة
data/server.json, voice.json إعدادات السيرفر والدردشة الصوتية
data/steam.json              رقم اللعبة في Steam وهوية تذكرة الدخول
tools/preview/               يبني docs/preview/roster.html من ملفات data
data/melodies/*.json         ألحان الملك العام مكتوبة يدوياً (المصدر)
data/midi/*.mid              المراجع المولّدة منها
data/Packs/                  ملفات الحزم مع حقل الترخيص
docs/                        وثيقة التصميم + تقرير M0
legacy/                      مشروع قديم غير مستخدم
```

`legacy/cordova-2016/` — مشروع Cordova قديم (2016) ما له علاقة باللعبة، محفوظ بتاريخه فقط.

---

## 4. الخطوة الجاية: تسجيلات حقيقية (شرط إغلاق M0)

النتائج الحالية على **أصوات صناعية**. عشان نقفل M0 لازم مجموعة الاختبار الحقيقية (GDD §11):
5 ألحان × 10 مؤدين (رجال، نساء، أطفال) × (صحيح + لحن مختلف + همهمة + نشاز متعمد).

1. سجّل في مجلد `recordings/` (موجود في `.gitignore` — **ممنوع رفع أصوات للمستودع**).
2. انسخ `docs/takes-manifest.example.csv` إلى `recordings/takes.csv` وعبّيه.
3. شغّل:
   ```bash
   dotnet run --project tools/matcher-lab -- eval --manifest recordings/takes.csv --report docs/m0-real-results.md
   ```

تفاصيل التسجيل في `docs/m0-report.md` §5.

---

## 5. Unity و Steam
مجلد `unity/` **مشروع Unity كامل** — افتحه مباشرة من Unity Hub. أداة المحرر تبني المحرّك وتنسخ `data/` تلقائياً أول ما يفتح (ولما يتغيّر `src/` أو `data/`)، وقائمة **Voice Battle** فيها: مزامنة، إنشاء المشهد، بناء Windows/macOS/Linux، تشغيل سيرفر محلي.
التفاصيل: [`unity/README.md`](unity/README.md) · Steam والسيرفر والرفع للمتجر: [`docs/steam.md`](docs/steam.md).

## 6. الإكمال مع Claude Code على جهازك
ثبّت Claude Code محلياً وافتحه داخل مجلد المستودع — ملف [`CLAUDE.md`](CLAUDE.md) يشرح له المشروع ووين وقفنا وقواعد العمل.
