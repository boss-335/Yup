using UnityEngine;
using VoiceHorror.Characters;
using VoiceHorror.UI;

namespace VoiceHorror.Gameplay.World
{
    /// <summary>
    /// Mubarak at the top of the lighthouse: walks the gallery with his lantern raised, and
    /// flashes it every time he speaks on the radio, so the voice in your ear has a body far away.
    /// </summary>
    [RequireComponent(typeof(ProceduralCharacter))]
    public sealed class MubarakFigure : MonoBehaviour
    {
        public float galleryRadius = 2.6f;
        public float walkDegreesPerSecond = 9f;
        public float lanternIntensity = 3f;

        ProceduralCharacter _ch;
        Transform _legL, _legR, _armL, _armR, _head, _spine;
        Light _lantern;
        Vector3 _center;
        float _angle;

        void Start()
        {
            _ch = GetComponent<ProceduralCharacter>();
            _ch.Build();
            _center = transform.position;
            _legL = _ch.Bone("ThighL"); _legR = _ch.Bone("ThighR");
            _armL = _ch.Role("armL"); _armR = _ch.Role("armR");
            _head = _ch.Role("head"); _spine = _ch.Role("spine");
            var hand = _ch.Role("lantern");
            var lg = new GameObject("Mubarak Lantern");
            lg.transform.SetParent(hand != null ? hand : transform, false);
            lg.transform.localPosition = new Vector3(0f, -0.15f, 0.05f);
            _lantern = lg.AddComponent<Light>();
            _lantern.type = LightType.Point;
            _lantern.range = 12f;
            _lantern.color = new Color(1f, 0.75f, 0.45f);
            var glass = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(glass.GetComponent<Collider>());
            glass.transform.SetParent(lg.transform, false);
            glass.transform.localScale = Vector3.one * 0.14f;
            glass.GetComponent<Renderer>().sharedMaterial = _ch.palette.For(Art.Slot.Glow);
        }

        void LateUpdate()
        {
            if (_ch == null || !_ch.Built) return;
            float t = Time.time;
            bool speaking = RadioView.IsSpeaking;

            // Walk around the gallery; stop and face the island while speaking.
            if (!speaking) _angle += walkDegreesPerSecond * Time.deltaTime;
            float rad = _angle * Mathf.Deg2Rad;
            transform.position = _center + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * galleryRadius;
            transform.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad)));

            float step = speaking ? 0f : Mathf.Sin(t * 3.2f);
            Rotate(_legL, new Vector3(-step * 22f, 0f, 0f));
            Rotate(_legR, new Vector3(step * 22f, 0f, 0f));
            Rotate(_armL, new Vector3(step * 14f, 0f, 0f));
            Rotate(_armR, new Vector3(Mathf.Sin(t * 1.3f) * 5f + (speaking ? -20f : 0f), 0f, 0f));
            Rotate(_spine, new Vector3(2f, 0f, step * 2f));
            Rotate(_head, new Vector3(speaking ? 8f : 0f, speaking ? -25f : Mathf.Sin(t * 0.4f) * 20f, 0f));

            // Signal flashes while his words are on screen.
            float flash = speaking ? (Mathf.Sin(t * 9f) > 0.2f ? 1.8f : 0.4f) : 1f;
            _lantern.intensity = lanternIntensity * flash * (0.9f + 0.1f * Mathf.PerlinNoise(t * 5f, 0f));
        }

        void Rotate(Transform bone, Vector3 euler)
        {
            if (bone != null) bone.localRotation = _ch.RestLocal(bone) * Quaternion.Euler(euler);
        }
    }
}
