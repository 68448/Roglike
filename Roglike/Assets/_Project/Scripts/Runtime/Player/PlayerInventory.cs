using Mirror;
using UnityEngine;

namespace Project.Player
{
    public sealed class PlayerInventory : NetworkBehaviour
    {
        // Суммарный бонус урона от всех предметов
        [SyncVar] public int TotalDamageBonus;

        [Server]
        public void ServerAddItem(int itemId)
        {
            // MVP: ищем ItemData в Resources
            var all = Resources.LoadAll<Project.Items.ItemData>("Items");
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Id == itemId)
                {
                    TotalDamageBonus += all[i].DamageBonus;
                    Debug.Log($"[PlayerInventory] Added itemId={itemId} bonus={all[i].DamageBonus} total={TotalDamageBonus} netId={netId}");
                    return;
                }
            }

            Debug.LogWarning($"[PlayerInventory] ItemData id={itemId} not found in Resources/Items");
        }
    }
}
