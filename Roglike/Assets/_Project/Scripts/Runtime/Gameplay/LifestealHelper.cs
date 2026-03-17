using UnityEngine;

namespace Project.Gameplay
{
    public static class LifestealHelper
    {
        // Вызывай на СЕРВЕРЕ после нанесения урона врагу
        public static void TryApplyLifesteal(GameObject attacker, int dealtDamage)
        {
            if (attacker == null || dealtDamage <= 0) return;

            var stats = attacker.GetComponent<Project.Player.PlayerStats>();
            var hp = attacker.GetComponent<PlayerHealth>();

            if (stats == null || hp == null) return;

            int pct = stats.LifestealPct;
            if (pct <= 0) return;

            int heal = Mathf.FloorToInt(dealtDamage * (pct / 100f));
            if (heal <= 0) heal = 1;

            hp.ServerHeal(heal);
        }
    }
}