using Mirror;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Project.UI
{
    public sealed class EquipmentPanelUI : MonoBehaviour
    {
        [SerializeField] private Text weaponText;
        [SerializeField] private Text helmetText;
        [SerializeField] private Text chestText;
        [SerializeField] private Text bootsText;
        [SerializeField] private Text ringText;
        [SerializeField] private Text amuletText;

        [SerializeField] private Button weaponButton;
        [SerializeField] private Button helmetButton;
        [SerializeField] private Button chestButton;
        [SerializeField] private Button bootsButton;
        [SerializeField] private Button ringButton;
        [SerializeField] private Button amuletButton;

        [SerializeField] private Image weaponIcon;
        [SerializeField] private Image helmetIcon;
        [SerializeField] private Image chestIcon;
        [SerializeField] private Image bootsIcon;
        [SerializeField] private Image ringIcon;
        [SerializeField] private Image amuletIcon;

        [SerializeField] private Image weaponBorder;
        [SerializeField] private Image helmetBorder;
        [SerializeField] private Image chestBorder;
        [SerializeField] private Image bootsBorder;
        [SerializeField] private Image ringBorder;
        [SerializeField] private Image amuletBorder;

        private Project.Player.PlayerEquipment _equip;
        private ItemTooltipUI _tooltip;

        public void BindToLocalPlayer(ItemTooltipUI tooltip = null)
        {
            _equip = null;
            _tooltip = tooltip;

            foreach (var ni in FindObjectsByType<NetworkIdentity>(FindObjectsSortMode.None))
            {
                if (ni != null && ni.isLocalPlayer)
                {
                    _equip = ni.GetComponent<Project.Player.PlayerEquipment>();
                    break;
                }
            }

            BindHover(weaponButton, () => OnSlotHoverEnter(_equip != null ? _equip.WeaponItemId : 0));
            BindHover(helmetButton, () => OnSlotHoverEnter(_equip != null ? _equip.HelmetItemId : 0));
            BindHover(chestButton, () => OnSlotHoverEnter(_equip != null ? _equip.ChestItemId : 0));
            BindHover(bootsButton, () => OnSlotHoverEnter(_equip != null ? _equip.BootsItemId : 0));
            BindHover(ringButton, () => OnSlotHoverEnter(_equip != null ? _equip.RingItemId : 0));
            BindHover(amuletButton, () => OnSlotHoverEnter(_equip != null ? _equip.AmuletItemId : 0));

            Refresh();
        }

        public void Refresh()
        {
            if (_equip == null)
            {
                SetText(weaponText, "Weapon: -", Color.white);
                SetText(helmetText, "Helmet: -", Color.white);
                SetText(chestText, "Chest: -", Color.white);
                SetText(bootsText, "Boots: -", Color.white);
                SetText(ringText, "Ring: -", Color.white);
                SetText(amuletText, "Amulet: -", Color.white);

                SetVisual(weaponIcon, weaponBorder, null, Color.white, false);
                SetVisual(helmetIcon, helmetBorder, null, Color.white, false);
                SetVisual(chestIcon, chestBorder, null, Color.white, false);
                SetVisual(bootsIcon, bootsBorder, null, Color.white, false);
                SetVisual(ringIcon, ringBorder, null, Color.white, false);
                SetVisual(amuletIcon, amuletBorder, null, Color.white, false);

                BindButton(weaponButton, 0);
                BindButton(helmetButton, 0);
                BindButton(chestButton, 0);
                BindButton(bootsButton, 0);
                BindButton(ringButton, 0);
                BindButton(amuletButton, 0);

                return;
            }

            RefreshSlot("Weapon", _equip.WeaponItemId, weaponText, weaponIcon, weaponBorder, weaponButton);
            RefreshSlot("Helmet", _equip.HelmetItemId, helmetText, helmetIcon, helmetBorder, helmetButton);
            RefreshSlot("Chest", _equip.ChestItemId, chestText, chestIcon, chestBorder, chestButton);
            RefreshSlot("Boots", _equip.BootsItemId, bootsText, bootsIcon, bootsBorder, bootsButton);
            RefreshSlot("Ring", _equip.RingItemId, ringText, ringIcon, ringBorder, ringButton);
            RefreshSlot("Amulet", _equip.AmuletItemId, amuletText, amuletIcon, amuletBorder, amuletButton);
        }

        private void SetText(Text txt, string value, Color color)
        {
            if (txt != null)
            {
                txt.text = value;
                txt.color = color;
            }
                
        }

        private string GetItemName(int itemId)
        {
            if (itemId <= 0)
                return "-";

            var all = Resources.LoadAll<Project.Items.ItemData>("Items");
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].Id == itemId)
                    return all[i].ItemName;
            }

            return $"Item {itemId}";
        }

        private void SetItemText(Text txt, string slotName, int itemId)
        {
            if (txt == null)
                return;

            if (itemId <= 0)
            {
                txt.text = $"{slotName}: -";
                txt.color = Color.white;
                return;
            }

            var all = Resources.LoadAll<Project.Items.ItemData>("Items");
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].Id == itemId)
                {
                    txt.text = $"{slotName}: {all[i].ItemName}";
                    txt.color = Project.Items.ItemRarityView.GetColor(all[i].Rarity);
                    return;
                }
            }

            txt.text = $"{slotName}: Item {itemId}";
            txt.color = Color.white;
        }

        private void OnSlotClicked(int itemId)
        {
            if (itemId <= 0)
                return;

            var all = Resources.LoadAll<Project.Items.ItemData>("Items");

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].Id == itemId)
                {
                    _tooltip?.Show(all[i], fromEquipment: true, pinned: true);
                    return;
                }
            }
        }

        private void OnSlotHoverEnter(int itemId)
        {
            if (_tooltip == null || itemId <= 0)
                return;

            var all = Resources.LoadAll<Project.Items.ItemData>("Items");
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].Id == itemId)
                {
                    _tooltip.Show(all[i], fromEquipment: true, pinned: false);
                    return;
                }
            }
        }

        private void OnSlotHoverExit()
        {
            _tooltip?.TryHideHover();
        }

        private void BindHover(Button button, System.Action onEnter)
        {
            if (button == null)
                return;

            var trigger = button.GetComponent<EventTrigger>();
            if (trigger == null)
                trigger = button.gameObject.AddComponent<EventTrigger>();

            if (trigger.triggers == null)
                trigger.triggers = new System.Collections.Generic.List<EventTrigger.Entry>();

            RemoveEvent(trigger, EventTriggerType.PointerEnter);
            RemoveEvent(trigger, EventTriggerType.PointerExit);

            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => onEnter?.Invoke());
            trigger.triggers.Add(enter);

            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => OnSlotHoverExit());
            trigger.triggers.Add(exit);
        }

        private static void RemoveEvent(EventTrigger trigger, EventTriggerType type)
        {
            for (int i = trigger.triggers.Count - 1; i >= 0; i--)
            {
                if (trigger.triggers[i] != null && trigger.triggers[i].eventID == type)
                    trigger.triggers.RemoveAt(i);
            }
        }

        private void BindButton(Button button, int itemId)
        {
            if (button == null)
                return;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnSlotClicked(itemId));
        }

        private void RefreshSlot(
            string slotName,
            int itemId,
            Text txt,
            Image iconImage,
            Image borderImage,
            Button button)
        {
            if (itemId <= 0)
            {
                SetText(txt, $"{slotName}: -", Color.white);
                SetVisual(iconImage, borderImage, null, Color.white, false);
                BindButton(button, 0);
                return;
            }

            var all = Resources.LoadAll<Project.Items.ItemData>("Items");
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].Id == itemId)
                {
                    var color = Project.Items.ItemRarityView.GetColor(all[i].Rarity);

                    SetText(txt, $"{slotName}: {all[i].ItemName}", color);
                    SetVisual(iconImage, borderImage, all[i].Icon, color, true);
                    BindButton(button, itemId);
                    return;
                }
            }

            SetText(txt, $"{slotName}: Item {itemId}", Color.white);
            SetVisual(iconImage, borderImage, null, Color.white, false);
            BindButton(button, itemId);
        }

        private void SetVisual(Image iconImage, Image borderImage, Sprite icon, Color borderColor, bool hasItem)
        {
            if (borderImage != null)
                borderImage.color = hasItem ? borderColor : new Color(0.2f, 0.2f, 0.2f, 1f);

            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.enabled = (icon != null);
                iconImage.color = Color.white;
            }
        }

    }
}

