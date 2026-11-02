using UnityEngine;
using VoiceHorror.Audio;
using VoiceHorror.Config;

namespace VoiceHorror.Gameplay.Weapons
{
    /// <summary>
    /// Procedural visual + audio feedback for a fired weapon. Brightness, size and the chord
    /// reflect the score, so the player hears and sees how well they sang (spec §2.6 step 4).
    /// </summary>
    public sealed class WeaponEffect : MonoBehaviour
    {
        float _life, _age;
        Vector3 _startScale, _endScale;
        Light _light;
        float _lightPeak;
        Renderer _renderer;
        Color _color;

        public static void Spawn(WeaponDefinition def, Vector3 origin, Vector3 dir, float quality, bool success, Material material)
        {
            if (def.effectPrefab != null)
            {
                var p = Instantiate(def.effectPrefab, origin + dir * 1f, Quaternion.LookRotation(dir));
                Destroy(p, 4f);
            }

            var root = new GameObject("WeaponEffect_" + def.id);
            var fx = root.AddComponent<WeaponEffect>();
            Color color = success ? Color.Lerp(def.color * 0.6f, def.color, quality) : new Color(0.5f, 0.15f, 0.1f);
            fx._color = color;
            fx._life = success ? 0.7f : 0.45f;

            GameObject shape;
            switch (def.delivery)
            {
                case WeaponDelivery.Beam:
                    shape = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    float len = success ? def.range : 3f;
                    root.transform.SetPositionAndRotation(origin + dir * (len / 2f + 0.5f), Quaternion.FromToRotation(Vector3.up, dir));
                    float w = success ? Mathf.Lerp(0.15f, 0.6f, quality) : 0.08f;
                    fx._startScale = new Vector3(w, len / 2f, w);
                    fx._endScale = new Vector3(w * 0.1f, len / 2f, w * 0.1f);
                    break;
                case WeaponDelivery.Cone:
                    shape = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    root.transform.SetPositionAndRotation(origin + dir * 1.5f, Quaternion.LookRotation(dir));
                    float r = success ? def.range * Mathf.Lerp(0.6f, 1f, quality) : 1.5f;
                    fx._startScale = new Vector3(0.5f, 0.5f, 0.5f);
                    fx._endScale = new Vector3(r * 0.9f, r * 0.6f, r * 1.4f);
                    break;
                default:
                    shape = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    root.transform.position = origin - Vector3.up * 0.6f;
                    float s = success ? def.range * 2f * Mathf.Lerp(0.7f, 1f, quality) : 2f;
                    fx._startScale = Vector3.one * 0.5f;
                    fx._endScale = new Vector3(s, s * 0.35f, s);
                    break;
            }
            Destroy(shape.GetComponent<Collider>());
            shape.transform.SetParent(root.transform, false);
            fx._renderer = shape.GetComponent<Renderer>();
            if (material != null) fx._renderer.sharedMaterial = new Material(material);
            fx._renderer.material.color = color;
            fx._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            root.transform.localScale = fx._startScale;

            var lightGo = new GameObject("Flash");
            lightGo.transform.position = origin + dir * 2f;
            lightGo.transform.SetParent(root.transform, true);
            fx._light = lightGo.AddComponent<Light>();
            fx._light.type = LightType.Point;
            fx._light.color = color;
            fx._light.range = success ? def.range * 1.2f : 4f;
            fx._lightPeak = success ? Mathf.Lerp(2f, 8f, quality) : 1.5f;
            fx._light.intensity = fx._lightPeak;

            var src = ProceduralAudio.MakeSource(root, success ? ProceduralAudio.Hit : ProceduralAudio.Fail, false, success ? Mathf.Lerp(0.5f, 1f, quality) : 0.7f, 0.3f, 60f);
            src.pitch = success ? Mathf.Lerp(0.8f, 1.25f, quality) : 0.9f;
            src.Play();

            Destroy(root, 2.5f);
        }

        void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / _life);
            float ease = 1f - (1f - t) * (1f - t);
            transform.localScale = Vector3.Lerp(_startScale, _endScale, ease);
            if (_light != null) _light.intensity = _lightPeak * (1f - t);
            if (_renderer != null)
            {
                var c = _color;
                c.a = 0.6f * (1f - t);
                _renderer.material.color = c;
                if (t >= 1f) _renderer.enabled = false;
            }
        }
    }
}
