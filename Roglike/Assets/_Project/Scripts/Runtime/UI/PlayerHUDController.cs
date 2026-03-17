using Mirror;
using TMPro;
using UnityEngine;

namespace Project.UI
{
    public class PlayerHUDController : MonoBehaviour
    {
        [SerializeField] private TMP_Text hpText;
        [SerializeField] private TMP_Text damageText;

        [SerializeField] private TMP_Text playersText;
        [SerializeField] private TMP_Text coopText;

        private Project.Gameplay.PlayerHealth _health;
        private Project.Player.PlayerInventory _inventory;

        private void Update()
        {
            if (_health == null || !_health.isLocalPlayer)
            {
                var local = NetworkClient.localPlayer;
                if (local != null)
                {
                    _health = local.GetComponent<Project.Gameplay.PlayerHealth>();
                    _inventory = local.GetComponent<Project.Player.PlayerInventory>();
                }
                return;
            }

            if (_health != null)
                hpText.text = $"HP: {_health.CurrentHP}";

            if (_inventory != null)
                damageText.text = $"Bonus Damage: +{_inventory.TotalDamageBonus}";

            int alive = CountAlivePlayersLocal();
            float coop = CoopHpMultiplier(alive);

            if (playersText != null) playersText.text = $"Players alive: {alive}";
            if (coopText != null) coopText.text = $"Coop HP: x{coop:0.00}";
        }

        private int CountAlivePlayersLocal()
        {
            // Клиентская оценка: достаточно для отображения в HUD
            var players = FindObjectsByType<Project.Gameplay.PlayerHealth>(FindObjectsSortMode.None);

            int alive = 0;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null && players[i].CurrentHP > 0)
                    alive++;
            }

            return Mathf.Max(1, alive);
        }

        private float CoopHpMultiplier(int alivePlayers)
        {
            return alivePlayers switch
            {
                1 => 1.0f,
                2 => 1.35f,
                3 => 1.65f,
                _ => 1.90f
            };
        }
    }
}
