using Mirror;
using UnityEngine;
using Project.Items;
using Project.Gameplay;

namespace Project.Player
{
    [RequireComponent(typeof(PlayerInventoryItem))]
    [RequireComponent(typeof(PlayerEquipment))]
    [RequireComponent(typeof(PlayerHealth))]
    [RequireComponent(typeof(PlayerStats))]
    public sealed class PlayerEquipmentController : NetworkBehaviour
    {
        private PlayerInventoryItem _inventory;
        private PlayerEquipment _equipment;
        private PlayerHealth _health;
        private PlayerStats _stats;

        private void Awake()
        {
            _inventory = GetComponent<PlayerInventoryItem>();
            _equipment = GetComponent<PlayerEquipment>();
            _health = GetComponent<PlayerHealth>();
            _stats = GetComponent<PlayerStats>();
        }

        [Command]
        public void CmdEquipItem(int itemId)
        {
            ServerEquipItem(itemId);
        }

        [Command]
        public void CmdUnequipSlot(Project.Items.EquipmentSlotType slot)
        {
            Debug.Log($"[PlayerEquipmentController] CmdUnequipSlot slot={slot} netId={netId}");
            ServerUnequipSlot(slot);
        }

        [Server]
        public void ServerEquipItem(int itemId)
        {
            if (_inventory == null || _equipment == null)
                return;

            if (!_inventory.ServerContains(itemId))
            {
                Debug.LogWarning($"[PlayerEquipmentController] Item {itemId} not found in inventory.");
                return;
            }

            ItemData def = FindItemData(itemId);
            if (def == null)
            {
                Debug.LogWarning($"[PlayerEquipmentController] ItemData not found for itemId={itemId}");
                return;
            }

            if (def.EquipSlot == EquipmentSlotType.None)
            {
                Debug.LogWarning($"[PlayerEquipmentController] Item {itemId} is not equippable.");
                return;
            }

            int oldItemId = _equipment.ServerGetEquipped(def.EquipSlot);

            // снять новый предмет из инвентаря
            if (!_inventory.ServerRemoveFirstItem(itemId))
                return;

            // старый предмет вернуть в инвентарь
            if (oldItemId > 0)
                _inventory.ServerAddItem(oldItemId);

            // надеть новый
            _equipment.ServerSetEquipped(def.EquipSlot, itemId);

            // пересчитать итоговые бонусы
            ServerRecalculateDerivedStats();

            Debug.Log($"[PlayerEquipmentController] Equipped itemId={itemId} into slot={def.EquipSlot}");
        }

        [Server]
        public void ServerUnequipSlot(EquipmentSlotType slot)
        {
            Debug.Log($"[PlayerEquipmentController] ServerUnequipSlot slot={slot}");

            if (_inventory == null || _equipment == null)
            {
                Debug.LogWarning("[PlayerEquipmentController] Unequip failed: inventory or equipment is null");
                return;
            }

            int oldItemId = _equipment.ServerGetEquipped(slot);
            Debug.Log($"[PlayerEquipmentController] Slot {slot} currently has itemId={oldItemId}");

            if (oldItemId <= 0)
            {
                Debug.LogWarning($"[PlayerEquipmentController] Unequip skipped: slot {slot} is empty");
                return;
            }

            _equipment.ServerSetEquipped(slot, 0);
            _inventory.ServerAddItem(oldItemId);

            ServerRecalculateDerivedStats();

            Debug.Log($"[PlayerEquipmentController] Unequipped slot={slot} itemId={oldItemId}");
        }

        [Server]
        public void ServerRecalculateDerivedStats()
        {
            int hpBonus = 0;
            int speedBonusPct = 0;

            AddBonusesFromItem(_equipment.WeaponItemId, ref hpBonus, ref speedBonusPct);
            AddBonusesFromItem(_equipment.HelmetItemId, ref hpBonus, ref speedBonusPct);
            AddBonusesFromItem(_equipment.ChestItemId, ref hpBonus, ref speedBonusPct);
            AddBonusesFromItem(_equipment.BootsItemId, ref hpBonus, ref speedBonusPct);
            AddBonusesFromItem(_equipment.RingItemId, ref hpBonus, ref speedBonusPct);
            AddBonusesFromItem(_equipment.AmuletItemId, ref hpBonus, ref speedBonusPct);

            if (_health != null)
                _health.ServerSetEquipmentHpBonus(hpBonus);

            if (_stats != null)
                _stats.ServerSetEquipmentMoveSpeedPctBonus(speedBonusPct);
        }

        private void AddBonusesFromItem(int itemId, ref int hpBonus, ref int speedBonusPct)
        {
            if (itemId <= 0)
                return;

            ItemData def = FindItemData(itemId);
            if (def == null)
                return;

            hpBonus += def.MaxHpBonus;
            speedBonusPct += def.MoveSpeedPctBonus;
        }

        private ItemData FindItemData(int itemId)
        {
            ItemData[] all = Resources.LoadAll<ItemData>("Items");
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].Id == itemId)
                    return all[i];
            }
            return null;
        }
    }
}