using TMPro;
using UnityEngine;

namespace Project.UI
{
    public sealed class EnemyWorldLabel : MonoBehaviour
    {
        [SerializeField] private TMP_Text label;
        [SerializeField] private Vector3 offset = new Vector3(0f, 2.0f, 0f);

        private Project.Gameplay.EnemyHealth _health;
        private Project.Gameplay.EnemyAI _ai;

        private void Awake()
        {
            _health = GetComponentInParent<Project.Gameplay.EnemyHealth>();
            _ai = GetComponentInParent<Project.Gameplay.EnemyAI>();
        }

        private void LateUpdate()
        {
            if (label == null || _health == null)
                return;

            // Текст HP
            int hp = _health.CurrentHP;
            int max = _health.MaxHP > 0 ? _health.MaxHP : hp;

            bool elite = (_ai != null && _ai.IsElite);

            label.text = elite
                ? $"<b>ELITE</b>\nHP: {hp}/{max}"
                : $"HP: {hp}/{max}";

            // Держим над головой
            transform.position = _health.transform.position + offset;

            // Поворачиваем к камере (простая версия)
            var cam = Camera.main;
            if (cam != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
            }
        }
    }
}