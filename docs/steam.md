# Voice Battle على Steam

Steam في اللعبة يعطي **أربع أشياء** فقط، والباقي (القتال، الغرف، الصوت) على سيرفرنا الخاص (`docs/online.md`):

| | كيف |
|---|---|
| **اسم اللاعب** | اسم حسابك في Steam يصير اسمك في اللعبة تلقائياً |
| **تسجيل دخول موثّق** | اللعبة تطلب "تذكرة" من Steam وترسلها للسيرفر، والسيرفر يتحقق منها عند Steam (Web API) — ما أحد يقدر ينتحل حساب غيره |
| **دعوة الأصدقاء** | لما تفتح غرفة خاصة، اللعبة تفتح Lobby في Steam (للأصدقاء فقط) فيه عنوان السيرفر وكود الغرفة. صديقك يضغط **Join game** من قائمة الأصدقاء أو يقبل دعوتك من الـ Overlay، فتدخله اللعبة غرفتك مباشرة (حتى لو كانت اللعبة مقفلة عنده) |
| **الحالة (Rich presence)** | الأصدقاء يشوفون أنك في Voice Battle وفي أي غرفة |

ما نستخدم شبكة Steam (P2P/Datagram) — السيرفر هو الحَكَم (`CLAUDE.md` → Online rules). واللعبة **تشتغل كاملة بدون Steam** (اسم يدوي، بدون تسجيل دخول).

---

## 1. الملفات

| الملف | الدور |
|---|---|
| `unity/Packages/manifest.json` | يضيف حزمة **Steamworks.NET 2025.164.1** (من GitHub عبر git) |
| `unity/steam_appid.txt` | رقم اللعبة للتجربة داخل Unity (`480`) |
| `data/steam.json` | `app_id`، `ticket_identity`، `relaunch_through_steam`، `max_party_members` |
| `data/server.json` | `auth` (`none`/`steam`)، `steam_app_id`، `steam_identity` — لازم تطابق `steam.json` (فيه اختبار يتأكد) |
| `unity/Assets/VoiceBattle/Scripts/SteamBridge.cs` | المكان الوحيد اللي يكلّم Steam |
| `src/VoiceBattle.Net/Auth.cs` | تحقق السيرفر من التذكرة (`SteamWebApiAuthenticator`) |
| `src/VoiceBattle.Session/Online/JoinInfo.cs` | صيغة الدعوة: `+vb_server host:port +vb_room CODE` |
| `tools/steam/` | رفع النسخة على Steam (SteamPipe) |

## 2. التجربة على جهازك (AppID 480)

`480` هو تطبيق **Spacewar** التجريبي من Valve — أي حساب Steam يقدر يستخدمه بدون حساب مطوّر.

1. ثبّت **git** (Unity يحتاجه عشان ينزّل Steamworks.NET) وافتح **عميل Steam** وسجّل دخول.
2. افتح مجلد `unity/` في Unity. أول مرة ينزّل الحزمة، وأداة `EngineSync` تفعّل الرمز `VB_STEAMWORKS` تلقائياً.
3. ▶ Play: في القائمة الرئيسية بيظهر `Steam: <اسمك>`. وفي Steam بيبين إنك تلعب "Spacewar" (طبيعي مع 480).
4. Online → Connect → **Create room** → زر **Invite Steam friends** يفتح الـ Overlay. *(الـ Overlay ما يشتغل داخل محرر Unity — جرّبه من نسخة مبنية: Voice Battle → Build.)*
5. صديقك (عنده نفس النسخة + Steam) يقبل الدعوة → يدخل غرفتك.

**بدون git أو بدون Steam:** احذف سطر `com.rlabrecque.steamworks.net` من `unity/Packages/manifest.json` — الرمز `VB_STEAMWORKS` ينشال تلقائياً واللعبة تشتغل بدون Steam. وأي نسخة تقدر تنضم لغرفة مباشرة بالأمر:
```bash
VoiceBattle.exe +vb_server 192.168.1.20:7777 +vb_room ABCDE
```

> مع 480 **ما تقدر** تفعّل تحقق السيرفر (`auth: steam`): التحقق يحتاج مفتاح Web API للناشر، وهذا ما ينعطى إلا لصاحب الـ AppID. خلّ السيرفر على `auth: none` لين يصير عندك AppID.

## 3. AppID خاص باللعبة

1. سجّل في **Steamworks** (partner.steamgames.com) وادفع رسوم **Steam Direct** لكل لعبة.
2. أنشئ التطبيق، وسجّل الـ AppID وثلاث Depots (Windows / macOS / Linux).
3. غيّر الرقم في **ثلاثة أماكن**:
   - `data/steam.json` → `"app_id"`
   - `data/server.json` → `"steam_app_id"`
   - `unity/steam_appid.txt`
4. شغّل `unity/sync.sh` (أو افتح Unity — يزامن تلقائياً).
5. مع AppID حقيقي، النسخة المبنية لو انفتحت من خارج Steam تعيد فتح نفسها من خلال Steam (`relaunch_through_steam`). ما يصير هذا داخل Unity ولا مع 480.
6. الحالة: اللعبة تكتب `status` (يظهر في نافذة معلومات اللعبة) و`connect` (زر Join game). عشان تظهر جملة مرتبة تحت اسمك في قائمة الأصدقاء لازم ملف ترجمة Rich Presence في Steamworks ومفتاح `steam_display` — **غير مسوّى بعد**.

## 4. السيرفر مع تسجيل الدخول

1. Steamworks → **Users & Permissions → Manage Groups** → أنشئ مجموعة → **Create WebAPI Key**. هذا مفتاح **ناشر** وسرّي.
2. **لا تحطه أبداً في git ولا في `data/`.** يُعطى للسيرفر كمتغير بيئة فقط:
```bash
VB_AUTH=steam VB_STEAM_WEBAPI_KEY=xxxxxxxx dotnet run --project tools/voice-battle-server -c Release
```
3. السيرفر يرفض أي لاعب بدون تذكرة صحيحة، أو عليه حظر VAC/ناشر، ويسمح بجلسة وحدة لكل حساب (الدخول من جهاز ثاني يطلّع الأول).

### على VPS (Docker)
```bash
docker build -t voice-battle-server .
```
```bash
docker run -d --name vb -p 7777:7777 --restart unless-stopped -e VB_AUTH=steam -e VB_STEAM_WEBAPI_KEY=xxxxxxxx -e VB_STEAM_APP_ID=1234560 voice-battle-server
```
افتح منفذ **TCP 7777** في جدار الحماية. الصورة فيها السيرفر وملفات `data` اللي يحتاجها فقط (بدون صوت أو كلمات).
**لسا ما فيه TLS ولا NAT traversal** (`docs/online.md`): الاتصال غير مشفّر. للتجربة مع الأصدقاء يكفي، أما للإطلاق العام فهذي مهمة لازم تنعمل قبل.

## 5. البناء والرفع على Steam

1. Unity → **Voice Battle → Build → Windows (64-bit)** (أو macOS / Linux) → `unity/Builds/<platform>/`.
   مع 480 ينحط `steam_appid.txt` جنب اللعبة عشان تشتغل من خارج Steam؛ مع AppID حقيقي ينشال (وملف الرفع يستثنيه دائماً).
2. ثبّت **steamcmd** (من Steamworks SDK → `tools/ContentBuilder`).
3. ارفع:
```bash
VB_DEPOT_WINDOWS=1234561 VB_DEPOT_LINUX=1234563 tools/steam/upload.sh <steam_login> "build 1" beta
```
   Windows:
```powershell
$env:VB_DEPOT_WINDOWS = "1234561"; .\tools\steam\upload.ps1 -Login <steam_login> -Branch beta
```
   السكربت يرفض الرفع مع 480، ويرفض يخلي النسخة **live** على الفرع الافتراضي — هذا تسويه بنفسك من موقع Steamworks بعد ما تجرب فرع `beta`.

## 6. قبل صفحة المتجر

- **المايك:** اللعبة تحتاجه (على macOS تطلب الإذن بنص من `ApplyPlayerSettings`). اكتب في الوصف إن الغناء يتحلل على الجهاز وإن **ما يوصل أي صوت للسيرفر** إلا محادثة الأصدقاء (تمر ولا تنحفظ).
- **محتوى اللاعبين:** الحركات المتعلَّمة ما تطلع من الجهاز. رفع الأصوات العام **مقفل** (`public_uploads_enabled: false`) لين مراجعة المحامي (GDD §11 M6) — لا تفعّله لأجل المتجر.
- **الأغاني:** المراجع كلها ملكية عامة ومكتوبة بخط اليد (`data/melodies`). لا تحط أسماء أغاني تجارية في الصفحة ولا في البيانات.
- **التصنيف العمري:** محادثة صوتية بين لاعبين → اذكرها في استبيان المحتوى. أصوات الغرباء تبدأ مكتومة (GDD §9).
