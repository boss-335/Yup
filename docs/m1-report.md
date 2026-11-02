# M1 — القتال المحلي: التقرير

**الحالة:** منطق القتال + طبقة الجلسة + وضع التدريب V1 مبنية ومختبرة (61 اختبار). سكربتات Unity مكتوبة وتُترجَم ضد نسخة مبسطة من Unity API، **لكن لم تُشغَّل داخل Unity بعد** (بيئة السحابة ما فيها محرر). أول تجربة حقيقية: افتح `unity/` بـ Unity واتبع `unity/README.md`.

## 1. ما تم بناؤه

| المكوّن | الملف | الاختبارات |
|---|---|---|
| قواعد القتال (HP، الشحن، التبريد، معادلة الضرر §3.3، الضربة المثالية، الضربة القاضية §3.4، الفوز بالضربة/الوقت) | `src/BattleRules.Core/Battle.cs`, `Damage.cs` | 42 |
| إعدادات من JSON (`moves.json` + `battle.json` الجديد) | `src/BattleRules.Core/Config.cs` | ✓ |
| الحركات (Loadout) + التحقق من طول المقطع لكل خانة + اقتراح الخانة حسب الطول (§5.4) | `Content/Loadout.cs` | ✓ |
| تحميل الحزم مع **فحص الترخيص** (§5.2.5): A/C/D مسموحة بشروط، B/E مرفوضة، وضع الستريمر | `Content/PackLoader.cs` | ✓ |
| بوت التدريب V1 (توقيت ثابت، دقة عشوائية بذرة ثابتة، يستخدم الضربة القاضية) | `TrainingBot.cs` | ✓ |
| **طبقة الجلسة** (جديدة): تقطيع المايك إلى أداءات + معايرة الضجيج + Push-to-Sing، التعرّف على الحركة المغنّاة، جلسة التدريب | `src/VoiceBattle.Session/` | 19 |
| محاكاة مباراة كاملة بالسطر: `matcher-lab battle` | `tools/matcher-lab/BattleSim.cs` | — |
| Unity: المايك، مدير المباراة، واجهة مؤقتة | `unity/Assets/VoiceBattle/Scripts/` | فحص ترجمة فقط |

اختبار end-to-end يشغّل مباراة كاملة: صوت صناعي ← قطع 10ms مثل المايك ← تقطيع ← تعرّف ← ضرر ← ضربة قاضية ← فوز.

## 2. قرارات تصميم (تحتاج موافقتك)

1. **التعرّف التلقائي على الحركة:** اللاعب ما يضغط زر — يغني أي مقطع من الثلاثة، والمحرّك يقارنه بالثلاثة ويختار الأقرب. لو أقرب تطابق أقل من 0.35 = "غير معروف" وما ينحسب تبريد (عشان الكحة أو الكلام ما يضيّع حركاتك). خيار بديل `"move_selection": "explicit"` (اختيار بزر) للاختبار أو سهولة الوصول.
2. **لو غنّيت حركة وهي في التبريد:** تُعرف أنها هي، والنتيجة "في التبريد" (ما نختار حركة ثانية بالغلط).
3. **الضربة القاضية:**
   - نافذة الـ 8 ثوانٍ تبدأ من **بداية** أول تكرار.
   - **السلسلة المنكسرة تستهلك الشحن** (مخاطرة/مكافأة) — قابل للتغيير `finisher_break_keeps_charge`.
   - أداء حركة ثانية أثناء السلسلة يكسرها.
   - "KO تحت 50": لو صحة الخصم أقل من 50 = ضربة قاضية فورية، وإلا 50 ضرر.
4. **مقطع جديد للضربة القاضية:** موتيف السيمفونية الخامسة لبيتهوفن (دا-دا-دا-دم، 1.75 ثانية، ملك عام) — ما كان فيه مقطع 1–2 ثانية في الحزمة.
5. **الساعة = ساعة عينات المايك** في Unity، عشان توقيت الأداء ووقت المعركة ما يختلفون.

## 3. ملاحظات توازن من المحاكاة
- **الضربة القاضية ضيقة زمنياً:** 4 تكرارات لمقطع 1.75 ثانية + 250ms صمت لنهاية كل أداء + وقفة طبيعية ≈ 2.3 ثانية للتكرار = **~9 ثوانٍ > 8**. لازم اللاعب يسرّع ~1.3× ووقفات قصيرة. الوثيقة تقول "السرعة مطلوبة" فهذا متوافق، لكن لو حسّيتها صعبة: نافذة 10 ثوانٍ، أو مقاطع قاضية ≤ 1.5 ثانية.
- **بوت التدريب سهل:** فاز اللاعب الصناعي في كل المباريات حتى بمستوى "سيئ" (نصف أداءاته نشاز). مناسب للتدريب؛ كل قيمه في `battle.json` → `bot`.
- **النشاز يخسّرك فعلاً:** نفس المقطع بنشاز ينعرف صح لكن ضرره 0–3 بدل ~14.

## 4. مثال (`matcher-lab battle --seed 3`)
```
   0.0s match started (3:00)
   4.0s BOT    Light    acc 0.69 →  1.4 dmg  (player  98.6 | bot 100.0)
   4.9s PLAYER Heavy    acc 0.85 → 13.6 dmg  (player  98.6 | bot  86.4)
         🎤 sang ode_to_joy_a (correct) → recognised as Heavy  [L=0.34 H=0.85 F=0.00]  108 ms
   9.4s PLAYER Light    acc 0.89 →  5.3 dmg  (player  98.6 | bot  81.2)
         🎤 sang fur_elise_a (correct) → recognised as Light  [L=0.89 H=0.00 F=0.42]  39 ms
  10.0s BOT    Heavy    acc 0.80 → 10.1 dmg  (player  88.5 | bot  81.2)
  15.2s PLAYER Light    acc 0.88 →  5.0 dmg  (player  88.5 | bot  76.2)
         🎤 sang fur_elise_a (correct) → recognised as Light  [L=0.88 H=0.30 F=0.00]  31 ms
  16.0s BOT    Light    acc 0.79 →  3.0 dmg  (player  85.5 | bot  76.2)
  20.5s PLAYER Heavy    acc 0.87 → 14.8 dmg  (player  85.5 | bot  61.4)
         🎤 sang ode_to_joy_a (hum) → recognised as Heavy  [L=0.36 H=0.87 F=0.00]  34 ms
  22.0s BOT    Light    acc 0.81 →  3.4 dmg  (player  82.1 | bot  61.4)
  24.4s PLAYER Light    acc 0.85 →  4.4 dmg  (player  82.1 | bot  57.0)
         🎤 sang fur_elise_a (correct) → recognised as Light  [L=0.85 H=0.00 F=0.38]  25 ms
  28.0s BOT    Heavy    acc 0.64 →  2.2 dmg  (player  79.9 | bot  57.0)
  29.8s PLAYER Light    acc 0.84 →  4.2 dmg  (player  79.9 | bot  52.8)
         🎤 sang fur_elise_a (hum) → recognised as Light  [L=0.84 H=0.39 F=0.38]  38 ms
  34.0s BOT    Light    acc 0.77 →  2.8 dmg  (player  77.1 | bot  52.8)
  34.5s PLAYER Heavy    acc 0.86 → 14.0 dmg  (player  77.1 | bot  38.8)
         🎤 sang ode_to_joy_a (correct) → recognised as Heavy  [L=0.42 H=0.86 F=0.00]  35 ms
  36.5s PLAYER FINISHER chain started
  36.5s PLAYER FINISHER rep 1 acc 0.92
         🎤 sang beethoven5_a (correct) → recognised as Finisher  [L=0.00 H=0.00 F=0.92]  14 ms
  38.4s PLAYER FINISHER rep 2 acc 0.92
         🎤 sang beethoven5_a (correct) → recognised as Finisher  [L=0.00 H=0.00 F=0.92]  15 ms
  40.0s BOT    Light    acc 0.90 →  5.5 dmg ★ perfect  (player  71.6 | bot  38.8)
  40.2s PLAYER FINISHER rep 3 acc 0.81
         🎤 sang beethoven5_a (correct) → recognised as Finisher  [L=0.00 H=0.00 F=0.81]  17 ms
  42.1s PLAYER FINISHER LANDED → 39 dmg  (player  71.6 | bot   0.0)
  42.1s PLAYER K.O.!
  42.1s match over
         🎤 sang beethoven5_a (correct) → recognised as Finisher  [L=0.00 H=0.00 F=0.91]  15 ms

WINNER: player by knockout
  Player        HP  71.6  charge     0
  Training bot  HP   0.0  charge   100
```

## 5. لم يُبنَ بعد
- **الشكل البصري** (سؤال 7 في الوثيقة) — الواجهة الحالية مؤقتة وبالإنجليزي (IMGUI ما يدعم العربي من اليمين لليسار؛ الواجهة النهائية تحتاج TextMeshPro + دعم RTL).
- القطع (Interrupt) ← M3. الشبكة ← M2.
- اختيار الحركات من داخل اللعبة (الحالي: من Inspector في Unity).
