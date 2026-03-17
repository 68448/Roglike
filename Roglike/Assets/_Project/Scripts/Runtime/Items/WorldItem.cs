using Mirror;
using UnityEngine;

namespace Project.Items
{
    public sealed class WorldItem : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnItemIdChanged))]
        public int ItemId;

        private ItemData _data;
        private TMPro.TMP_Text _label;

        public override void OnStartClient()
        {
            base.OnStartClient();
            ResolveDataAndApplyVisual();
        }

        [Server]
        public void Init(int itemId)
        {
            ItemId = itemId;
        }

        private void OnItemIdChanged(int oldValue, int newValue)
        {
            ItemId = newValue;
            ResolveDataAndApplyVisual();
        }

        private void ResolveDataAndApplyVisual()
        {

            // 1) найти ItemData по ItemId
            _data = null;
            var all = Resources.LoadAll<ItemData>("Items");
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].Id == ItemId)
                {
                    _data = all[i];
                    break;
                }
            }

            if (_data == null)
                return;

            // 2) применить цвет
            var rend = GetComponentInChildren<Renderer>();
            if (rend != null)
                rend.material.color = _data.ItemColor;

            // 3) применить label
            _label = GetComponentInChildren<TMPro.TMP_Text>();
            if (_label != null)
            {
                string rarity = Project.Items.ItemRarityView.GetLabel(_data.Rarity);
                _label.text = $"{_data.ItemName} [{rarity}]\n+{_data.DamageBonus} DMG";
                _label.color = Project.Items.ItemRarityView.GetColor(_data.Rarity);
            }

        }

        [Server]
        public void Pickup(NetworkIdentity picker)
        {
            if (picker == null)
                return;

            var inv = picker.GetComponent<Project.Player.PlayerInventoryItem>();
            if (inv != null)
                inv.ServerAddItem(ItemId);
            else
                Debug.LogWarning("[WorldItem] Picker has no PlayerInventoryItem!");

            NetworkServer.Destroy(gameObject);
        }
        
    }
}
