using UnityEngine;

namespace Project.Items
{
    [CreateAssetMenu(menuName = "Roguelike/Item Data")]
    public sealed class ItemData : ScriptableObject
    {
        
        [Header("Identity")]
        public int Id = 1;
        public string ItemName = "Item";

        [TextArea]
        public string Description;

        [Header("Visual")]
        public Color ItemColor = Color.white;
        public Sprite Icon;

        [Header("Equipment")]
        public EquipmentSlotType EquipSlot = EquipmentSlotType.None;

        [Header("Bonuses")]
        public int DamageBonus = 0;
        public int MaxHpBonus = 0;
        public int MoveSpeedPctBonus = 0;
        public int CritChancePct = 0;
        public int CritDamageBonusPct = 0;

        [Header("Drop")]
        public ItemRarity Rarity = ItemRarity.Common;
        public int MinDropSegment = 1;
        public int DropWeight = 10;
    }

    public enum EquipmentSlotType
    {
        None = 0,
        Weapon = 1,
        Helmet = 2,
        Chest = 3,
        Boots = 4,
        Ring = 5,
        Amulet = 6
    }

    public enum ItemRarity
    {
        Common = 0,
        Rare = 1,
        Epic = 2,
        Legendary = 3
    }

    
}
