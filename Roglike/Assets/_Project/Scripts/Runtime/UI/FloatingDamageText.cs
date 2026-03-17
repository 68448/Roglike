using TMPro;
using UnityEngine;

namespace Project.UI
{
    public sealed class FloatingDamageText : MonoBehaviour
    {
        [SerializeField] private float lifetime = 0.8f;
        [SerializeField] private float riseSpeed = 1.8f;
        [SerializeField] private float horizontalDrift = 0.5f;
        [SerializeField] private float startScale = 1f;
        [SerializeField] private float endScale = 1.2f;
        [SerializeField] private float critScaleMultiplier = 1.28f;
        [SerializeField] private string critPrefix = "CRIT ";

        private TextMeshPro _text;
        private Vector3 _velocity;
        private Color _startColor;
        private float _t;

        public static void Spawn(Vector3 worldPos, int amount, Color color, bool isCritical = false)
        {
            GameObject go = new GameObject("FloatingDamageText");
            go.transform.position = worldPos;

            var text = go.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            text.fontSize = 5.5f;
            text.outlineWidth = 0.18f;
            text.outlineColor = new Color(0f, 0f, 0f, 0.9f);

            if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;

            var fx = go.AddComponent<FloatingDamageText>();
            fx.Init(text, amount, color, isCritical);
        }

        private void Init(TextMeshPro text, int amount, Color color, bool isCritical)
        {
            _text = text;
            _text.text = isCritical ? $"{critPrefix}{amount}" : amount.ToString();
            _text.color = color;

            _startColor = color;
            _t = 0f;

            float drift = Random.Range(-horizontalDrift, horizontalDrift);
            _velocity = new Vector3(drift, riseSpeed, 0f);

            if (isCritical)
            {
                _velocity.y *= 1.12f;
                startScale *= critScaleMultiplier;
                endScale *= critScaleMultiplier;
                lifetime *= 1.12f;
            }

            transform.localScale = Vector3.one * startScale;
        }

        private void Update()
        {
            if (_text == null)
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.deltaTime;
            _t += dt;
            float normalized = Mathf.Clamp01(_t / lifetime);

            transform.position += _velocity * dt;
            transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, normalized);

            Color c = _startColor;
            c.a = 1f - normalized;
            _text.color = c;

            var cam = Camera.main;
            if (cam != null)
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);

            if (_t >= lifetime)
                Destroy(gameObject);
        }
    }
}
