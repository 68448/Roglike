using UnityEngine;

namespace Project.Progression
{
    public enum MetaUpgradeType
    {
        MaxHp = 0,
        DamagePct = 1
    }

    public static class MetaProgressionService
    {
        private const string CurrencyKey = "meta.currency";
        private const string MaxHpLevelKey = "meta.level.maxhp";
        private const string DamageLevelKey = "meta.level.damage";

        public const int MaxLevelPerUpgrade = 20;
        public const int HpPerLevel = 5;
        public const int DamagePctPerLevel = 3;

        public static int GetCurrency()
        {
            return Mathf.Max(0, PlayerPrefs.GetInt(CurrencyKey, 0));
        }

        public static int GetLevel(MetaUpgradeType type)
        {
            string key = type == MetaUpgradeType.MaxHp ? MaxHpLevelKey : DamageLevelKey;
            return Mathf.Clamp(PlayerPrefs.GetInt(key, 0), 0, MaxLevelPerUpgrade);
        }

        public static int GetMaxHpMetaBonus()
        {
            return GetLevel(MetaUpgradeType.MaxHp) * HpPerLevel;
        }

        public static int GetDamageMetaBonusPct()
        {
            return GetLevel(MetaUpgradeType.DamagePct) * DamagePctPerLevel;
        }

        public static void AddCurrency(int amount)
        {
            if (amount <= 0)
                return;

            int next = GetCurrency() + amount;
            PlayerPrefs.SetInt(CurrencyKey, next);
            PlayerPrefs.Save();
        }

        public static bool TryBuy(MetaUpgradeType type)
        {
            int level = GetLevel(type);
            if (level >= MaxLevelPerUpgrade)
                return false;

            int cost = GetUpgradeCost(type, level);
            int currency = GetCurrency();
            if (currency < cost)
                return false;

            string key = type == MetaUpgradeType.MaxHp ? MaxHpLevelKey : DamageLevelKey;
            PlayerPrefs.SetInt(CurrencyKey, currency - cost);
            PlayerPrefs.SetInt(key, level + 1);
            PlayerPrefs.Save();
            return true;
        }

        public static int GetUpgradeCost(MetaUpgradeType type, int currentLevel)
        {
            currentLevel = Mathf.Clamp(currentLevel, 0, MaxLevelPerUpgrade);
            int baseCost = type == MetaUpgradeType.MaxHp ? 2 : 3;
            return baseCost + currentLevel;
        }

        public static void ResetAll()
        {
            PlayerPrefs.DeleteKey(CurrencyKey);
            PlayerPrefs.DeleteKey(MaxHpLevelKey);
            PlayerPrefs.DeleteKey(DamageLevelKey);
            PlayerPrefs.Save();
        }
    }
}
