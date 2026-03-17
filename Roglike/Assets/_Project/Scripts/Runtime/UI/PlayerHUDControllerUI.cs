using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    public sealed class PlayerHUDControllerUI : MonoBehaviour
    {
        [Header("HP UI")]
        [SerializeField] private UnityEngine.UI.Slider hpSlider;
        [SerializeField] private Text hpText;      // HP_Text

        [Header("Skill Slots (future)")]
        [SerializeField] private Button[] skillButtons; // 6 кнопок

        [Header("Update")]
        [SerializeField] private float refreshInterval = 0.1f;

        private float _t;
        private Project.Gameplay.PlayerHealth _hp;
        private Project.Player.PlayerStats _stats;

        private void Awake()
        {
            if (hpSlider != null)
            {
                hpSlider.minValue = 0f;
                hpSlider.maxValue = 1f;
                hpSlider.value = 1f;
            }
            if (hpText != null) hpText.text = "HP -/-";

            // На будущее: клики по слотам
            if (skillButtons != null)
            {
                for (int i = 0; i < skillButtons.Length; i++)
                {
                    int idx = i; // closure fix
                    if (skillButtons[i] != null)
                    {
                        skillButtons[i].onClick.RemoveAllListeners();
                        skillButtons[i].onClick.AddListener(() => OnSkillSlotClicked(idx));
                    }
                }
            }
        }

        private void Update()
        {
            // хоткеи 1..6 уже сейчас
            HandleSkillHotkeys();

            _t += Time.deltaTime;
            if (_t < refreshInterval) return;
            _t = 0f;

            EnsureLocalPlayerRefs();
            RefreshHP();
        }

        private void EnsureLocalPlayerRefs()
        {
            if (_hp != null && _stats != null) return;

            foreach (var ni in FindObjectsByType<NetworkIdentity>(FindObjectsSortMode.None))
            {
                if (ni != null && ni.isLocalPlayer)
                {
                    _hp = ni.GetComponent<Project.Gameplay.PlayerHealth>();
                    _stats = ni.GetComponent<Project.Player.PlayerStats>();
                    return;
                }
            }
        }

        private void RefreshHP()
        {
            if (_hp == null)
            {
                if (hpText != null) hpText.text = "HP -/-";
                if (hpSlider != null) hpSlider.value = 0f;
                return;
            }

            int cur = _hp.CurrentHP;
            int max = _hp.MaxHP > 0 ? _hp.MaxHP : 1;

            if (hpText != null) hpText.text = $"HP {cur}/{max}";
            if (hpSlider != null)
                hpSlider.value = Mathf.Clamp01(cur / (float)max);
        }

        private void HandleSkillHotkeys()
        {
            // Пока навыков нет — просто логируем.
            // Позже сюда подключим реальные скиллы.
            if (Input.GetKeyDown(KeyCode.Alpha1)) OnSkillSlotClicked(0);
            if (Input.GetKeyDown(KeyCode.Alpha2)) OnSkillSlotClicked(1);
            if (Input.GetKeyDown(KeyCode.Alpha3)) OnSkillSlotClicked(2);
            if (Input.GetKeyDown(KeyCode.Alpha4)) OnSkillSlotClicked(3);
            if (Input.GetKeyDown(KeyCode.Alpha5)) OnSkillSlotClicked(4);
            if (Input.GetKeyDown(KeyCode.Alpha6)) OnSkillSlotClicked(5);
        }

        private void OnSkillSlotClicked(int index)
        {
            // Сейчас — заглушка
            Debug.Log($"[PlayerHUD] Skill slot clicked: {index + 1}");
        }
    }
}