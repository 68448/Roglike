using Mirror;
using UnityEngine;
using Project.Items;

namespace Project.Player
{
    public sealed class PlayerEquipment : NetworkBehaviour
    {
        [SyncVar] public int WeaponItemId;
        [SyncVar] public int HelmetItemId;
        [SyncVar] public int ChestItemId;
        [SyncVar] public int BootsItemId;
        [SyncVar] public int RingItemId;
        [SyncVar] public int AmuletItemId;

        [Server]
        public int ServerGetEquipped(EquipmentSlotType slot)
        {
            return slot switch
            {
                EquipmentSlotType.Weapon => WeaponItemId,
                EquipmentSlotType.Helmet => HelmetItemId,
                EquipmentSlotType.Chest => ChestItemId,
                EquipmentSlotType.Boots => BootsItemId,
                EquipmentSlotType.Ring => RingItemId,
                EquipmentSlotType.Amulet => AmuletItemId,
                _ => 0
            };
        }

        [Server]
        public void ServerSetEquipped(EquipmentSlotType slot, int itemId)
        {
            switch (slot)
            {
                case EquipmentSlotType.Weapon: WeaponItemId = itemId; break;
                case EquipmentSlotType.Helmet: HelmetItemId = itemId; break;
                case EquipmentSlotType.Chest: ChestItemId = itemId; break;
                case EquipmentSlotType.Boots: BootsItemId = itemId; break;
                case EquipmentSlotType.Ring: RingItemId = itemId; break;
                case EquipmentSlotType.Amulet: AmuletItemId = itemId; break;
            }
        }
    }
}