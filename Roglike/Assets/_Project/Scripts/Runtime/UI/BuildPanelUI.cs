using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    public sealed class BuildPanelUI : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject root; // BuildPanelRoot

        [Header("Stats UI")]
        [SerializeField] private Text statsText;

        [Header("Input")]
        [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
        [SerializeField] private KeyCode closeKey = KeyCode.Escape;

        [Header("Update")]
        [SerializeField] private float refreshInterval = 0.15f;

        [SerializeField] private EquipmentPanelUI equipmentPanel;

        private float _t;

        private Project.Gameplay.PlayerHealth _hp;
        private Project.Player.PlayerStats _stats;

        [SerializeField] private InventoryGridUI inventoryGrid;
        [SerializeField] private ItemTooltipUI itemTooltip;

        private void Awake()
        {
            if (root != null) root.SetActive(false);
            ResolveUiRefs();
        }

        public void RefreshAllPanels()
        {
            ResolveUiRefs();
            EnsureLocalRefs();

            inventoryGrid?.BindToLocalPlayer(itemTooltip);
            equipmentPanel?.BindToLocalPlayer(itemTooltip);
            itemTooltip?.Bind(this, GetLocalEquipmentController());
            RefreshStats();
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                if (IsOpen()) Close();
                else Open();
            }

            if (IsOpen() && Input.GetKeyDown(closeKey))
            {
                Close();
            }

            if (!IsOpen()) return;

            _t += Time.deltaTime;
            if (_t < refreshInterval) return;
            _t = 0f;

            RefreshAllPanels();
        }

        private bool IsOpen()
        {
            return root != null && root.activeSelf;
        }

        public void Open()
        {
            if (root == null) return;

            ResolveUiRefs();
            root.SetActive(true);
            UIStateManager.Instance?.Push("BuildPanel");
            inventoryGrid?.BindToLocalPlayer(itemTooltip);

            equipmentPanel?.BindToLocalPlayer(itemTooltip);

            var equipController = GetLocalEquipmentController();
            if (itemTooltip != null)
                itemTooltip.Bind(this, equipController);

            RefreshAllPanels();
        }

        public void Close()
        {
            if (root == null) return;

            itemTooltip?.Hide();
            root.SetActive(false);
            UIStateManager.Instance?.Pop("BuildPanel");
        }

        private void ResolveUiRefs()
        {
            if (inventoryGrid == null)
                inventoryGrid = GetComponentInChildren<InventoryGridUI>(true);

            if (equipmentPanel == null)
                equipmentPanel = GetComponentInChildren<EquipmentPanelUI>(true);

            if (itemTooltip == null)
            {
                var tooltips = Resources.FindObjectsOfTypeAll<ItemTooltipUI>();
                for (int i = 0; i < tooltips.Length; i++)
                {
                    if (tooltips[i] != null)
                    {
                        itemTooltip = tooltips[i];
                        break;
                    }
                }
            }
        }

        private void EnsureLocalRefs()
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

        private void RefreshStats()
        {
            if (statsText == null)
                return;

            if (_hp == null || _stats == null)
            {
                statsText.text = "No local player.";
                return;
            }

            // --- базовые значения ---
            int cur = _hp.CurrentHP;
            int max = _hp.MaxHP;

            float dmgMul = _stats.DamageMultiplier;
            float spdMul = _stats.MoveSpeedMultiplier;

            int rewardHpBonus = _stats.BonusMaxHp;
            int equipmentHpBonus = _hp.EquipmentHpBonus;

            int rewardSpeedBonus = _stats.BonusMoveSpeedPct;
            int equipmentSpeedBonus = _stats.EquipmentMoveSpeedPctBonus;

            int lifesteal = _stats.LifestealPct;
            int thorns = _stats.ThornsFlat;
            int regen = _stats.RegenPer10s;
            int bossDmg = _stats.BossDamagePct;

            // --- бонус оружия ---
            int weaponDamageBonus = 0;
            string weaponName = "-";

            var equip = GetLocalEquipment();
            if (equip != null && equip.WeaponItemId > 0)
            {
                var all = Resources.LoadAll<Project.Items.ItemData>("Items");
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].Id == equip.WeaponItemId)
                    {
                        weaponDamageBonus = all[i].DamageBonus;
                        weaponName = all[i].ItemName;
                        break;
                    }
                }
            }

            statsText.text =
                $"HP: {cur}/{max}\n" +
                $"Reward HP Bonus: +{rewardHpBonus}\n" +
                $"Equipment HP Bonus: +{equipmentHpBonus}\n\n" +

                $"Damage Multiplier: x{dmgMul:0.00} (+{_stats.BonusDamagePct}%)\n" +
                $"Weapon: {weaponName}\n" +
                $"Weapon Damage Bonus: +{weaponDamageBonus}\n\n" +

                $"Move Speed Multiplier: x{spdMul:0.00}\n" +
                $"Reward Speed Bonus: +{rewardSpeedBonus}%\n" +
                $"Equipment Speed Bonus: +{equipmentSpeedBonus}%\n\n" +

                $"Lifesteal: {lifesteal}%\n" +
                $"Thorns: {thorns}\n" +
                $"Regen: {regen} / 10s\n" +
                $"Boss Damage: +{bossDmg}%";
        }

        private Project.Player.PlayerEquipment GetLocalEquipment()
        {
            foreach (var ni in FindObjectsByType<Mirror.NetworkIdentity>(FindObjectsSortMode.None))
            {
                if (ni != null && ni.isLocalPlayer)
                    return ni.GetComponent<Project.Player.PlayerEquipment>();
            }

            return null;
        }

        private Project.Player.PlayerEquipmentController GetLocalEquipmentController()
        {
            foreach (var ni in FindObjectsByType<Mirror.NetworkIdentity>(FindObjectsSortMode.None))
            {
                if (ni != null && ni.isLocalPlayer)
                    return ni.GetComponent<Project.Player.PlayerEquipmentController>();
            }

            return null;
        }

    }
}
