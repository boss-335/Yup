# Voice Battle — محرّك مطابقة الصوت (M0)

لعبة قتال جماعية: **غنّي عشان تضرب**. وثيقة التصميم الكاملة: [`docs/voice-battle-gdd.md`](docs/voice-battle-gdd.md).
هذا المستودع حالياً في مرحلة **M0 — إثبات المطابقة**: مكتبة C# مستقلة عن Unity + أداة سطر أوامر + اختبارات.
التقرير ونتائجه في [`docs/m0-report.md`](docs/m0-report.md).

> كل شي يشتغل **محلياً على جهازك بدون إنترنت** بعد أول `restore` (يحمّل حزم NuGet مرة وحدة).

---

## 1. التشغيل على جهازك

### المتطلبات
| | |
|---|---|
| **.NET SDK 8** (أو أحدث) | https://dotnet.microsoft.com/download/dotnet/8.0 — Windows / macOS / Linux |
| Git | لنسخ المستودع |
| (اختياري) VS Code + إضافة C# Dev Kit، أو Visual Studio 2022، أو JetBrains Rider | للتطوير |
| (لاحقاً، M1) Unity 2022.3 LTS أو أحدث | للمشهد والمايك |

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
tools/matcher-lab/           أداة سطر الأوامر
data/match_config.json       كل العتبات والأوزان (لا شيء ثابت في الكود)
data/moves.json              إعدادات الحركات (يستخدمها BattleRules في M1)
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

## 5. الربط مع Unity لاحقاً (M1)

```bash
dotnet publish src/VoiceMatch.Core -c Release -o out/unity
```
انسخ `VoiceMatch.Core.dll` و`System.Text.Json.dll` و`System.Text.Encodings.Web.dll` و`Microsoft.Bcl.AsyncInterfaces.dll` من `out/unity` إلى `Assets/Plugins/VoiceMatch/` في مشروع Unity.
لو Unity اشتكى من مكتبات مكررة (`System.Memory`، `System.Buffers`، …) لا تنسخها — Unity فيه نسخته منها.
طبقة Unity (MicCapture) تأخذ عينات المايك ← `PerformanceEndpointer` يحدد بداية ونهاية الأداء ← `ContourBuilder.FromAudio` ← `ContourMatcher.Match`.

## 6. الإكمال مع Claude Code على جهازك
ثبّت Claude Code محلياً وافتحه داخل مجلد المستودع — ملف [`CLAUDE.md`](CLAUDE.md) يشرح له المشروع ووين وقفنا وقواعد العمل.
