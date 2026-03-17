using Mirror;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    public sealed class InventoryGridUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Button[] slotButtons;
        [SerializeField] private Text[] slotLabels;
        [SerializeField] private Image[] slotIcons;
        [SerializeField] private Image[] slotBorders;

        private Project.Player.PlayerInventoryItem _inv;
        private Project.Player.PlayerEquipmentController _equipController;
        private ItemTooltipUI _tooltip;

        public void BindToLocalPlayer(ItemTooltipUI tooltip = null)
        {
            // отписка от старого инвентаря
            if (_inv != null)
                _inv.ItemIds.Callback -= OnInvChanged;

            _inv = null;
            _equipController = null;
            _tooltip = tooltip;
            EnsureTooltipRef();

            foreach (var ni in FindObjectsByType<NetworkIdentity>(FindObjectsSortMode.None))
            {
                if (ni != null && ni.isLocalPlayer)
                {
                    _inv = ni.GetComponent<Project.Player.PlayerInventoryItem>();
                    _equipController = ni.GetComponent<Project.Player.PlayerEquipmentController>();
                    break;
                }
            }

            // Подписываем кнопки
            BindButtons();

            if (_inv != null)
                _inv.ItemIds.Callback += OnInvChanged;

            Refresh();
        }

        private void OnDestroy()
        {
            if (_inv != null)
                _inv.ItemIds.Callback -= OnInvChanged;
        }

        private void BindButtons()
        {
            if (slotButtons == null)
                return;

            for (int i = 0; i < slotButtons.Length; i++)
            {
                int slotIndex = i; // важно для closure
                if (slotButtons[i] != null)
                {
                    slotButtons[i].onClick.RemoveAllListeners();
                    slotButtons[i].onClick.AddListener(() => OnSlotClicked(slotIndex));
                }
            }
        }

        private void OnInvChanged(SyncList<int>.Operation op, int index, int oldItem, int newItem)
        {
            Refresh();
        }

        private void OnSlotClicked(int slotIndex)
        {
            if (_inv == null)
                return;

            if (slotIndex < 0 || slotIndex >= _inv.ItemIds.Count)
                return;

            int itemId = _inv.ItemIds[slotIndex];
            if (itemId <= 0)
                return;

            Debug.Log($"[InventoryGridUI] Click slot={slotIndex} itemId={itemId}");

            var all = Resources.LoadAll<Project.Items.ItemData>("Items");
            Project.Items.ItemData picked = null;

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].Id == itemId)
                {
                    picked = all[i];
                    break;
                }
            }

            if (picked == null)
                return;
            Debug.Log($"[InventoryGridUI] Tooltip ref is {(_tooltip != null ? "OK" : "NULL")}, picked={(picked != null ? picked.ItemName : "NULL")}");
            if (_tooltip != null)
            {
                _tooltip.Show(picked);
            }
            else
            {
                Debug.LogWarning("[InventoryGridUI] Tooltip is null. Assign ItemTooltipUI in BuildPanelUI or keep tooltip object in scene.");
            }
        }

        private void EnsureTooltipRef()
        {
            if (_tooltip != null)
                return;

            var allTooltips = Resources.FindObjectsOfTypeAll<ItemTooltipUI>();
            for (int i = 0; i < allTooltips.Length; i++)
            {
                if (allTooltips[i] != null)
                {
                    _tooltip = allTooltips[i];
                    break;
                }
            }
        }

        private void RefreshPanels()
        {
            Refresh();

            // если есть BuildPanelUI в сцене — попросим его обновить всё
            var build = FindFirstObjectByType<BuildPanelUI>();
            if (build != null)
                build.RefreshAllPanels();
        }

        public void Refresh()
        {
            if (slotLabels == null)
                return;

            for (int i = 0; i < slotLabels.Length; i++)
            {
                string txt = "-";
                Color textColor = Color.white;
                Color borderColor = new Color(0.2f, 0.2f, 0.2f, 1f);
                Sprite icon = null;
                bool hasItem = false;

                if (_inv != null && i < _inv.ItemIds.Count)
                {
                    int id = _inv.ItemIds[i];

                    var all = Resources.LoadAll<Project.Items.ItemData>("Items");
                    Project.Items.ItemData data = null;

                    for (int j = 0; j < all.Length; j++)
                    {
                        if (all[j] != null && all[j].Id == id)
                        {
                            data = all[j];
                            break;
                        }
                    }

                    if (data != null)
                    {
                        txt = data.ItemName;
                        textColor = Project.Items.ItemRarityView.GetColor(data.Rarity);
                        borderColor = Project.Items.ItemRarityView.GetColor(data.Rarity);
                        icon = data.Icon;
                        hasItem = true;
                    }
                    else
                    {
                        txt = $"Item {id}";
                    }
                }

                if (slotLabels[i] != null)
                {
                    slotLabels[i].text = txt;
                    slotLabels[i].color = textColor;
                }

                if (slotBorders != null && i < slotBorders.Length && slotBorders[i] != null)
                {
                    slotBorders[i].color = hasItem ? borderColor : new Color(0.2f, 0.2f, 0.2f, 1f);
                }

                if (slotIcons != null && i < slotIcons.Length && slotIcons[i] != null)
                {
                    slotIcons[i].sprite = icon;
                    slotIcons[i].enabled = (icon != null);
                    slotIcons[i].color = Color.white;
                }
            }
        }
    }
}
