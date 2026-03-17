using UnityEngine;
using UnityEngine.UI;

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

        [Header("Buttons")]
        [SerializeField] private Button equipButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button unequipButton;

        private Project.Items.ItemData _currentItem;
        private Project.Player.PlayerEquipmentController _equipController;
        private BuildPanelUI _buildPanel;

        private Project.Items.EquipmentSlotType _slot;
        private bool _fromEquipment;

        private GameObject RootOrSelf => root != null ? root : gameObject;

        private void Awake()
        {
            Hide();

            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                closeButton.onClick.AddListener(Hide);
            }
        }

        public void Bind(BuildPanelUI buildPanel, Project.Player.PlayerEquipmentController equipController)
        {
            _buildPanel = buildPanel;
            _equipController = equipController;
        }

        public void Show(Project.Items.ItemData item, bool fromEquipment = false)
        {
            if (item == null)
            {
                Debug.LogWarning("[ItemTooltipUI] Show called with null item");
                return;
            }

            _currentItem = item;
            _fromEquipment = fromEquipment;
            _slot = item.EquipSlot;

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

            if (bonusesText != null)
                bonusesText.text = BuildBonusText(_currentItem);

            if (equipButton != null){
                equipButton?.onClick.RemoveAllListeners();
                equipButton?.onClick.AddListener(OnEquipClicked);
                equipButton.gameObject.SetActive(!fromEquipment);
            }
                
            if (unequipButton != null){
                unequipButton?.onClick.RemoveAllListeners();
                unequipButton?.onClick.AddListener(OnUnequipClicked);
                unequipButton.gameObject.SetActive(fromEquipment);
            }
                
        }

        public void Hide()
        {
            _currentItem = null;

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

            if (sb.Length == 0)
                sb.Append("-");

            return sb.ToString();
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
