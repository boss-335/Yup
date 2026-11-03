using System;
using UnityEngine;
using UnityEngine.Rendering;
using VoiceHorror.Art;

namespace VoiceHorror.Characters
{
    /// <summary>
    /// Materials for each <see cref="Slot"/>. Built-in RP and URP are both supported; the scene
    /// builder saves these as assets so they ship in builds, and code falls back to creating them.
    /// </summary>
    [Serializable]
    public sealed class CharacterPalette
    {
        public Material skin, dark, glow, cloth, hair, wood, metal, ghost, shell;

        public Material For(Slot slot)
        {
            Material m;
            switch (slot)
            {
                case Slot.Skin: m = skin; break;
                case Slot.Dark: m = dark; break;
                case Slot.Glow: m = glow; break;
                case Slot.Cloth: m = cloth; break;
                case Slot.Hair: m = hair; break;
                case Slot.Wood: m = wood; break;
                case Slot.Metal: m = metal; break;
                case Slot.Ghost: m = ghost; break;
                default: m = shell; break;
            }
            return m != null ? m : Fallback(slot);
        }

        static readonly Material[] Cache = new Material[16];

        /// <summary>Runtime fallback so a character never renders pink or invisible.</summary>
        public static Material Fallback(Slot slot)
        {
            int i = (int)slot;
            if (Cache[i] != null) return Cache[i];
            Color c;
            switch (slot)
            {
                case Slot.Skin: c = new Color(0.6f, 0.56f, 0.5f); break;
                case Slot.Dark: c = new Color(0.05f, 0.04f, 0.04f); break;
                case Slot.Glow: c = new Color(1f, 0.9f, 0.6f); break;
                case Slot.Cloth: c = new Color(0.35f, 0.12f, 0.1f); break;
                case Slot.Hair: c = new Color(0.03f, 0.025f, 0.025f); break;
                case Slot.Wood: c = new Color(0.36f, 0.25f, 0.16f); break;
                case Slot.Metal: c = new Color(0.35f, 0.35f, 0.38f); break;
                case Slot.Ghost: c = new Color(0.55f, 0.85f, 0.95f, 0.35f); break;
                default: c = new Color(0.9f, 0.86f, 0.78f); break;
            }
            Cache[i] = slot == Slot.Ghost ? Transparent(c) : Lit(c, slot == Slot.Glow ? c * 2f : (Color?)null);
            return Cache[i];
        }

        public static Shader LitShader()
        {
            Shader s = GraphicsSettings.currentRenderPipeline != null ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
            return s != null ? s : Shader.Find("Standard");
        }

        public static Material Lit(Color c, Color? emission = null)
        {
            var m = new Material(LitShader()) { color = c };
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
            }
            return m;
        }

        /// <summary>Unlit, alpha-blended (Sprites/Default exists in both pipelines).</summary>
        public static Material Transparent(Color c) => new Material(Shader.Find("Sprites/Default")) { color = c };
    }
}
