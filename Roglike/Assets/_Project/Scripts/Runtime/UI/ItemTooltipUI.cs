using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

namespace Project.UI
{
    public sealed class ItemTooltipUI : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject root;

        [Header("Texts")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text rarityText;
        [SerializeField] private Text slotText;
        [SerializeField] private Text descriptionText;
        [SerializeField] private Text bonusesText;
        [SerializeField] private Text compareText;

        [Header("Buttons")]
        [SerializeField] private Button equipButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button unequipButton;

        private Project.Items.ItemData _currentItem;
        private Project.Player.PlayerEquipmentController _equipController;
        private Project.Player.PlayerEquipment _playerEquipment;
        private BuildPanelUI _buildPanel;

        private Project.Items.EquipmentSlotType _slot;
        private bool _fromEquipment;
        private bool _isPinned;

        private GameObject RootOrSelf => root != null ? root : gameObject;
        private static Dictionary<int, Project.Items.ItemData> _itemById;

        private void Awake()
        {
            Hide();

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Hide);
            }
        }

        public void Bind(
            BuildPanelUI buildPanel,
            Project.Player.PlayerEquipmentController equipController,
            Project.Player.PlayerEquipment playerEquipment)
        {
            _buildPanel = buildPanel;
            _equipController = equipController;
            _playerEquipment = playerEquipment;
        }

        public void Show(Project.Items.ItemData item, bool fromEquipment = false, bool pinned = false)
        {
            if (item == null)
            {
                Debug.LogWarning("[ItemTooltipUI] Show called with null item");
                return;
            }

            _currentItem = item;
            _fromEquipment = fromEquipment;
            _slot = item.EquipSlot;
            _isPinned = pinned;

            var panel = RootOrSelf;
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (!panel.activeSelf)
                panel.SetActive(true);
            panel.transform.SetAsLastSibling();

            if (titleText != null)
            {
                titleText.text = _currentItem.ItemName;
                titleText.color = Project.Items.ItemRarityView.GetColor(_currentItem.Rarity);
            }

            if (rarityText != null)
                rarityText.text = $"Rarity: {Project.Items.ItemRarityView.GetLabel(_currentItem.Rarity)}";

            if (slotText != null)
                slotText.text = $"Slot: {_currentItem.EquipSlot}";

            if (descriptionText != null)
                descriptionText.text = string.IsNullOrWhiteSpace(_currentItem.Description)
                    ? "-"
                    : _currentItem.Description;

            string bonusBlock = BuildBonusText(_currentItem);
            string compareBlock = BuildCompareText(_currentItem, fromEquipment);

            if (bonusesText != null)
            {
                if (compareText == null)
                    bonusesText.text = $"{bonusBlock}\n\n{compareBlock}";
                else
                    bonusesText.text = bonusBlock;
            }

            if (compareText != null)
                compareText.text = compareBlock;

            if (equipButton != null){
                equipButton?.onClick.RemoveAllListeners();
                equipButton?.onClick.AddListener(OnEquipClicked);
                equipButton.gameObject.SetActive(!fromEquipment && _isPinned);
            }
                
            if (unequipButton != null){
                unequipButton?.onClick.RemoveAllListeners();
                unequipButton?.onClick.AddListener(OnUnequipClicked);
                unequipButton.gameObject.SetActive(fromEquipment && _isPinned);
            }
                
        }

        public void TryHideHover()
        {
            if (!_isPinned)
                Hide();
        }

        public void Hide()
        {
            _currentItem = null;
            _isPinned = false;

            RootOrSelf.SetActive(false);
        }

        private void OnEquipClicked()
        {
            if (_currentItem == null || _equipController == null)
                return;

            _equipController.CmdEquipItem(_currentItem.Id);

            Hide();

            if (_buildPanel != null)
                _buildPanel.Invoke(nameof(BuildPanelUI.RefreshAllPanels), 0.15f);
        }

        private string BuildBonusText(Project.Items.ItemData item)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            if (item.DamageBonus != 0)
                sb.AppendLine($"+{item.DamageBonus} Damage");

            if (item.MaxHpBonus != 0)
                sb.AppendLine($"+{item.MaxHpBonus} Max HP");

            if (item.MoveSpeedPctBonus != 0)
                sb.AppendLine($"+{item.MoveSpeedPctBonus}% Move Speed");

            if (item.CritChancePct != 0)
                sb.AppendLine($"+{item.CritChancePct}% Crit Chance");

            if (item.CritDamageBonusPct != 0)
                sb.AppendLine($"+{item.CritDamageBonusPct}% Crit Damage");

            if (sb.Length == 0)
                sb.Append("-");

            return sb.ToString();
        }

        private string BuildCompareText(Project.Items.ItemData hovered, bool fromEquipment)
        {
            if (hovered == null)
                return "-";

            if (fromEquipment)
                return "Equipped item";

            if (hovered.EquipSlot == Project.Items.EquipmentSlotType.None)
                return "Not equippable";

            int equippedId = GetEquippedItemId(hovered.EquipSlot);
            if (equippedId <= 0)
                return "Compare: slot is empty";

            var equipped = GetItemById(equippedId);
            if (equipped == null)
                return "Compare: equipped item data missing";

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine($"Compare vs {equipped.ItemName}:");
            sb.AppendLine(BuildDeltaLine("Damage", hovered.DamageBonus - equipped.DamageBonus));
            sb.AppendLine(BuildDeltaLine("Max HP", hovered.MaxHpBonus - equipped.MaxHpBonus));
            sb.AppendLine(BuildDeltaLine("Move Speed %", hovered.MoveSpeedPctBonus - equipped.MoveSpeedPctBonus));
            sb.AppendLine(BuildDeltaLine("Crit Chance %", hovered.CritChancePct - equipped.CritChancePct));
            sb.Append(BuildDeltaLine("Crit Damage %", hovered.CritDamageBonusPct - equipped.CritDamageBonusPct));
            return sb.ToString();
        }

        private int GetEquippedItemId(Project.Items.EquipmentSlotType slot)
        {
            if (_playerEquipment == null)
                return 0;

            switch (slot)
            {
                case Project.Items.EquipmentSlotType.Weapon: return _playerEquipment.WeaponItemId;
                case Project.Items.EquipmentSlotType.Helmet: return _playerEquipment.HelmetItemId;
                case Project.Items.EquipmentSlotType.Chest: return _playerEquipment.ChestItemId;
                case Project.Items.EquipmentSlotType.Boots: return _playerEquipment.BootsItemId;
                case Project.Items.EquipmentSlotType.Ring: return _playerEquipment.RingItemId;
                case Project.Items.EquipmentSlotType.Amulet: return _playerEquipment.AmuletItemId;
                default: return 0;
            }
        }

        private static string BuildDeltaLine(string label, int delta)
        {
            if (delta > 0)
                return $"{label}: +{delta}";
            if (delta < 0)
                return $"{label}: {delta}";
            return $"{label}: 0";
        }

        private static Project.Items.ItemData GetItemById(int id)
        {
            EnsureItemCache();
            if (_itemById != null && _itemById.TryGetValue(id, out var item))
                return item;
            return null;
        }

        private static void EnsureItemCache()
        {
            if (_itemById != null)
                return;

            var all = Resources.LoadAll<Project.Items.ItemData>("Items");
            _itemById = new Dictionary<int, Project.Items.ItemData>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || all[i].Id <= 0)
                    continue;

                _itemById[all[i].Id] = all[i];
            }
        }

        private void OnUnequipClicked()
        {
            Debug.Log($"[ItemTooltipUI] OnUnequipClicked currentItem={(_currentItem != null ? _currentItem.ItemName : "NULL")} slot={_slot}");

            if (_equipController == null)
            {
                Debug.LogWarning("[ItemTooltipUI] Unequip failed: _equipController is null");
                return;
            }

            _equipController.CmdUnequipSlot(_slot);

            Hide();

            if (_buildPanel != null)
                _buildPanel.Invoke(nameof(BuildPanelUI.RefreshAllPanels), 0.15f);
        }
    }
}
