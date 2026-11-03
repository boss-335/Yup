using UnityEditor;
using UnityEngine;
using VoiceHorror.Persistence;

namespace VoiceHorror.EditorTools
{
    /// <summary>Tools/VoiceHorror menu: one click builds each milestone's test scene (spec §0.2).</summary>
    public static class VoiceHorrorMenu
    {
        const string Root = "Tools/VoiceHorror/";

        [MenuItem(Root + "Build Mic Test (M0)", priority = 1)]
        public static void BuildMicTest() => Run(LevelBuilders.MicTest);

        [MenuItem(Root + "Build Score Lab (M1)", priority = 2)]
        public static void BuildScoreLab() => Run(LevelBuilders.ScoreLab);

        [MenuItem(Root + "Build Weapon Range (M2)", priority = 3)]
        public static void BuildWeaponRange() => Run(LevelBuilders.WeaponRange);

        [MenuItem(Root + "Build Creature Greybox (M3)", priority = 4)]
        public static void BuildGreybox() => Run(LevelBuilders.CreatureGreybox);

        [MenuItem(Root + "Build Vertical Slice - Al-Summah Island (M4)", priority = 5)]
        public static void BuildVerticalSlice() => Run(LevelBuilders.VerticalSlice);

        [MenuItem(Root + "Build Character Gallery", priority = 6)]
        public static void BuildGallery() => Run(LevelBuilders.CharacterGallery);

        [MenuItem(Root + "Build Title Screen", priority = 7)]
        public static void BuildTitle() => Run(LevelBuilders.TitleScreen);

        [MenuItem(Root + "Build All Scenes", priority = 20)]
        public static void BuildAll()
        {
            if (!SceneKit.PrepareNewScene()) return;
            LevelBuilders.CharacterGallery(false);
            LevelBuilders.TitleScreen(false);
            LevelBuilders.MicTest(false);
            LevelBuilders.ScoreLab(false);
            LevelBuilders.WeaponRange(false);
            LevelBuilders.CreatureGreybox(false);
            LevelBuilders.VerticalSlice(false);
            SceneKit.SetBuildOrder("Title", "M4_VerticalSlice_AlSummah", "M2_WeaponRange", "CharacterGallery", "M0_MicTest", "M1_ScoreLab", "M3_CreatureGreybox");
            EditorUtility.DisplayDialog("VoiceHorror", "All scenes were built into Assets/_Project/Scenes and added to Build Settings (Title first).\nThe Vertical Slice is open now — or open CharacterGallery to see every character animate.", "OK");
        }

        [MenuItem(Root + "Create or Update Config Assets", priority = 40)]
        public static void CreateConfig()
        {
            ConfigAssets.EnsureAll();
            EditorUtility.DisplayDialog("VoiceHorror", "Config assets are in " + ConfigAssets.ConfigDir + ".\nExisting assets were kept as they were.", "OK");
        }

        [MenuItem(Root + "Open Saved Data Folder", priority = 60)]
        public static void OpenData() => EditorUtility.RevealInFinder(Storage.Root);

        [MenuItem(Root + "Delete Saved Data (calibration, recordings, settings)", priority = 61)]
        public static void DeleteData()
        {
            if (!EditorUtility.DisplayDialog("VoiceHorror", "Delete calibration, weapon recordings and settings from\n" + Storage.Root + " ?", "Delete", "Cancel")) return;
            Storage.DeleteEverything();
            Debug.Log("[VoiceHorror] Saved data deleted.");
        }

        static void Run(System.Action<bool> build)
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("VoiceHorror", "Stop Play mode before building a scene.", "OK");
                return;
            }
            build(true);
        }
    }
}
