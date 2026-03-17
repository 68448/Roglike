using UnityEngine;

namespace Project.Items
{
    public enum ItemType
    {
        Consumable,
        Weapon,
        Armor,
        Trinket
    }

    [CreateAssetMenu(menuName = "Project/Items/ItemDefinition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        public int ItemId = 1;              // уникальный ID
        public string DisplayName = "Item";
        public ItemType Type = ItemType.Consumable;

        [TextArea] public string Description;

        // позже добавим иконку, редкость и статы
        public Sprite Icon;
    }
}