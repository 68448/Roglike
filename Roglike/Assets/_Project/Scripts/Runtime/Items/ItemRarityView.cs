using UnityEngine;

namespace Project.Items
{
    public static class ItemRarityView
    {
        public static Color GetColor(ItemRarity rarity)
        {
            return rarity switch
            {
                ItemRarity.Common => new Color(0.85f, 0.85f, 0.85f),
                ItemRarity.Rare => new Color(0.35f, 0.65f, 1.00f),
                ItemRarity.Epic => new Color(0.75f, 0.35f, 1.00f),
                ItemRarity.Legendary => new Color(1.00f, 0.60f, 0.15f),
                _ => Color.white
            };
        }

        public static string GetLabel(ItemRarity rarity)
        {
            return rarity switch
            {
                ItemRarity.Common => "Common",
                ItemRarity.Rare => "Rare",
                ItemRarity.Epic => "Epic",
                ItemRarity.Legendary => "Legendary",
                _ => "Unknown"
            };
        }
    }
}