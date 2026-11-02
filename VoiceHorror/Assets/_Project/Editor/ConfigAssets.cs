using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VoiceHorror.Config;
using VoiceHorror.Dsp;

namespace VoiceHorror.EditorTools
{
    /// <summary>
    /// Creates (or tops up) the ScriptableObject config assets and generated materials.
    /// Existing assets are never overwritten, so designer tweaks survive a rebuild.
    /// </summary>
    public static class ConfigAssets
    {
        public const string ConfigDir = "Assets/_Project/Config";
        public const string MaterialDir = "Assets/_Project/Generated/Materials";

        public sealed class Bundle
        {
            public VoiceTuning tuning;
            public GameBalance balance;
            public WeaponDefinition sadafa, tar, nafas;
            public CreatureDefinition maskut, siluwa, budaryah, summah;
        }

        public static Bundle EnsureAll()
        {
            Directory.CreateDirectory(ConfigDir);
            var b = new Bundle
            {
                tuning = Ensure<VoiceTuning>("VoiceTuning", _ => { }),
                balance = Ensure<GameBalance>("GameBalance", _ => { }),
                sadafa = Ensure<WeaponDefinition>("Weapon_Sadafa", SetupSadafa),
                tar = Ensure<WeaponDefinition>("Weapon_Tar", SetupTar),
                nafas = Ensure<WeaponDefinition>("Weapon_Nafas", SetupNafas),
                maskut = Ensure<CreatureDefinition>("Creature_Maskut", SetupMaskut),
                siluwa = Ensure<CreatureDefinition>("Creature_Siluwa", SetupSiluwa),
                budaryah = Ensure<CreatureDefinition>("Creature_BuDaryah", SetupBuDaryah),
                summah = Ensure<CreatureDefinition>("Creature_Summah", SetupSummah),
            };
            AssetDatabase.SaveAssets();
            return b;
        }

        static T Ensure<T>(string name, System.Action<T> setup) where T : ScriptableObject
        {
            string path = $"{ConfigDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var asset = ScriptableObject.CreateInstance<T>();
            setup(asset);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ------------------------------------------------------------ Weapons (relics of the nahham)

        static void SetupSadafa(WeaponDefinition w)
        {
            w.id = "w_sadafa";
            w.displayNameAr = "الصدفة";
            w.displayNameEn = "The Shell";
            w.loreAr = "صدفة محار كبيرة كان النهّام يغني فيها للغاصة تحت الماء. لحن قصير يطلع منها شعاع يخترق المسكوت.";
            w.loreEn = "A great oyster shell the nahham sang into for the divers below. A short tune becomes a beam that pierces the Silenced.";
            w.recordingHintAr = "لحن قصير أو دندنة من ٢ إلى ٤ ثواني. مقطع تعرفه زين.";
            w.recordingHintEn = "A short tune or hum, 2 to 4 seconds. Something you know well.";
            w.referenceType = ReferenceType.Melody;
            w.minDurationSec = 1.5f;
            w.maxDurationSec = 4.5f;
            w.baseDamage = 45f;
            w.minScoreToFire = 35f;
            w.damageCurve = WeaponDefinition.DefaultCurve(35f);
            w.cooldownSec = 5f;
            w.staggerSec = 1.2f;
            w.delivery = WeaponDelivery.Beam;
            w.range = 22f;
            w.spread = 0.9f;
            w.color = new Color(0.45f, 0.9f, 1f);
            w.unlockedAtStart = true;
            w.tier = 2;
        }

        static void SetupTar(WeaponDefinition w)
        {
            w.id = "w_tar";
            w.displayNameAr = "الطار";
            w.displayNameEn = "The Drum";
            w.loreAr = "طار جلده من جلد الحوت، كان يضبط إيقاع المجاديف. أغنية طويلة تطلق موجة تكسر كل اللي حولك — وتسمعها الجزيرة كلها.";
            w.loreEn = "A frame drum skinned with whale hide that kept the oars in time. A long song releases a wave that breaks everything around you — and the whole island hears it.";
            w.recordingHintAr = "أغنية أو مقطع طويل من ٥ إلى ٩ ثواني. هذا أقوى سلاح، وأخطرها.";
            w.recordingHintEn = "A song or long passage, 5 to 9 seconds. The strongest weapon, and the most dangerous.";
            w.referenceType = ReferenceType.Melody;
            w.minDurationSec = 4f;
            w.maxDurationSec = 9f;
            w.baseDamage = 110f;
            w.minScoreToFire = 35f;
            w.damageCurve = WeaponDefinition.DefaultCurve(35f);
            w.cooldownSec = 14f;
            w.staggerSec = 2.5f;
            w.delivery = WeaponDelivery.Shockwave;
            w.range = 12f;
            w.spread = 0f;
            w.color = new Color(1f, 0.72f, 0.3f);
            w.unlockedAtStart = true;
            w.tier = 3;
        }

        static void SetupNafas(WeaponDefinition w)
        {
            w.id = "w_nafas";
            w.displayNameAr = "النَّفَس";
            w.displayNameEn = "The Breath";
            w.loreAr = "مو أداة، كلمة. آخر شي علّمه النهّام لحفيدته: كلمة قصيرة تقولها فتدفعهم عنك لحظة.";
            w.loreEn = "Not an object — a word. The last thing the nahham taught his granddaughter: a short word that pushes them back for a moment.";
            w.recordingHintAr = "كلمة أو عبارة قصيرة (أقل من ثانيتين ونص). اسم، دعاء، أي شي ما تنساه وأنت خايف.";
            w.recordingHintEn = "A short word or phrase (under 2.5 s). A name, a prayer, anything you won't forget when afraid.";
            w.referenceType = ReferenceType.Phrase;
            w.minDurationSec = 0.5f;
            w.maxDurationSec = 2.5f;
            w.baseDamage = 20f;
            w.minScoreToFire = 40f;
            w.damageCurve = WeaponDefinition.DefaultCurve(40f);
            w.cooldownSec = 3f;
            w.staggerSec = 1.8f;
            w.delivery = WeaponDelivery.Cone;
            w.range = 7f;
            w.spread = 40f;
            w.color = new Color(0.85f, 0.95f, 0.85f);
            w.unlockedAtStart = false;
            w.tier = 1;
        }

        // ------------------------------------------------------------ Creatures

        static void SetupMaskut(CreatureDefinition c)
        {
            c.id = "c_maskut";
            c.displayNameAr = "المسكوت";
            c.displayNameEn = "The Silenced";
            c.loreAr = "أهل الجزيرة بعد ليلة السكتة: عيون مطموسة وأفواه مخيّطة. يمشون على صوت أي نفَس.";
            c.loreEn = "The islanders after the Night of the Hush: blind, mouths sewn shut. They walk toward any breath.";
            c.archetype = CreatureArchetype.Stalker;
            c.maxHealth = 100f; c.height = 2.1f; c.radius = 0.4f;
            c.skinColor = new Color(0.6f, 0.56f, 0.5f); c.glowColor = new Color(0.9f, 0.85f, 0.6f);
            c.hearingMultiplier = 1f; c.huntConfirmSec = 0.8f; c.closeSenseRadius = 1.4f;
            c.wanderSpeed = 1.1f; c.investigateSpeed = 2.0f; c.huntSpeed = 3.4f;
            c.searchTimeout = 8f; c.attackRange = 1.6f; c.attackDamage = 34f; c.attackCooldown = 1.6f;
            c.tierDamageMultiplier = new Vector3(1f, 1f, 1f); c.staggerFactor = 1f;
        }

        static void SetupSiluwa(CreatureDefinition c)
        {
            c.id = "c_siluwa";
            c.displayNameAr = "السعلوة";
            c.displayNameEn = "The Si'luwa";
            c.loreAr = "من حكايات العجايز. أسرع من المساكيت، تنقز لما تقرب، وتهرب إذا انجرحت… وترجع.";
            c.loreEn = "From the old women's tales. Faster than the Silenced, she leaps when close, flees when wounded… and returns.";
            c.archetype = CreatureArchetype.Hunter;
            c.maxHealth = 140f; c.height = 1.5f; c.radius = 0.45f;
            c.skinColor = new Color(0.32f, 0.26f, 0.22f); c.glowColor = new Color(0.85f, 0.95f, 1f);
            c.hearingMultiplier = 1.3f; c.huntConfirmSec = 0.5f; c.closeSenseRadius = 1.8f;
            c.wanderSpeed = 1.6f; c.investigateSpeed = 3.0f; c.huntSpeed = 4.6f;
            c.lungeRange = 6f; c.lungeSpeedMultiplier = 2.2f; c.lungeDuration = 0.6f; c.lungeCooldown = 4f;
            c.fleeHealthFraction = 0.35f; c.fleeDuration = 4f;
            c.searchTimeout = 10f; c.attackRange = 1.7f; c.attackDamage = 30f; c.attackCooldown = 1.2f;
            c.tierDamageMultiplier = new Vector3(1f, 1f, 1.2f); c.staggerFactor = 0.7f;
        }

        static void SetupBuDaryah(CreatureDefinition c)
        {
            c.id = "c_budaryah";
            c.displayNameAr = "بو درياه";
            c.displayNameEn = "Bu Daryah";
            c.loreAr = "كان يسحب البحّارة من سفنهم بندائه. الحين ساكن عند البير، ما يتحرك، يسمع أبعد من الكل، وإذا سمعك يصيح فيجون كلهم.";
            c.loreEn = "He used to drag sailors from their ships with his call. Now he sits by the well, motionless, hearing farther than any — and when he hears you, he wails and they all come.";
            c.archetype = CreatureArchetype.Sentinel;
            c.maxHealth = 160f; c.height = 3.2f; c.radius = 1.2f;
            c.skinColor = new Color(0.2f, 0.3f, 0.27f); c.glowColor = new Color(0.4f, 1f, 0.8f);
            c.hearingMultiplier = 2f; c.huntConfirmSec = 0.6f; c.closeSenseRadius = 0f;
            c.alertRadius = 45f; c.wailDuration = 1.8f; c.alertCooldown = 10f;
            c.attackRange = 2.6f; c.attackDamage = 50f; c.attackCooldown = 2f;
            c.tierDamageMultiplier = new Vector3(0.8f, 1f, 1.2f); c.staggerFactor = 0.5f;
        }

        static void SetupSummah(CreatureDefinition c)
        {
            c.id = "c_summah";
            c.displayNameAr = "الصمّة";
            c.displayNameEn = "Al-Summah (The Hush)";
            c.loreAr = "قلب السكوت. كتلة سودا ملفوفة بأفواه مخيّطة، كل فم صوت انسرق من الجزيرة. ما تنكسر إلا بأغنية كاملة.";
            c.loreEn = "The heart of silence. A black mass ringed by sewn mouths, each a voice stolen from the island. Only a whole song breaks it.";
            c.archetype = CreatureArchetype.Heart;
            c.maxHealth = 450f; c.height = 4f; c.radius = 1.6f;
            c.skinColor = new Color(0.5f, 0.42f, 0.4f); c.glowColor = new Color(0.95f, 0.95f, 1f);
            c.hearingMultiplier = 1.5f; c.huntConfirmSec = 0.6f; c.closeSenseRadius = 0f;
            c.alertRadius = 35f; c.wailDuration = 2f; c.alertCooldown = 12f;
            c.summonInterval = 14f; c.maxSummons = 3;
            c.attackRange = 3.4f; c.attackDamage = 40f; c.attackCooldown = 2f;
            c.tierDamageMultiplier = new Vector3(0.2f, 0.5f, 1.5f); c.staggerFactor = 0.3f;
        }

        // ------------------------------------------------------------ Materials

        static bool UsingSrp => GraphicsSettings.currentRenderPipeline != null;

        static Shader LitShader()
        {
            Shader s = UsingSrp ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            return s != null ? s : Shader.Find("Standard");
        }

        public static Material Lit(string name, Color color, float smoothness = 0.1f, Color? emission = null)
        {
            Directory.CreateDirectory(MaterialDir);
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(LitShader());
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                mat.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Alpha-blended unlit material for effects (Sprites/Default exists in built-in and URP).</summary>
        public static Material Effect()
        {
            Directory.CreateDirectory(MaterialDir);
            string path = $"{MaterialDir}/FX_Song.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 1f, 1f, 0.6f) };
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
