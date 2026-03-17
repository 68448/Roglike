using Mirror;
using UnityEngine;

namespace Project.Player
{
    public sealed class PlayerInventoryItem : NetworkBehaviour
    {
        // Синхроним список itemId
        public readonly SyncList<int> ItemIds = new SyncList<int>();

        [Server]
        public void ServerAddItem(int itemId)
        {
            ItemIds.Add(itemId);
            Debug.Log($"[PlayerInventoryItem] Added itemId={itemId} to player netId={netId}. Count={ItemIds.Count}");
        }

        [Server]
        public bool ServerContains(int itemId)
        {
            for (int i = 0; i < ItemIds.Count; i++)
            {
                if (ItemIds[i] == itemId)
                    return true;
            }
            return false;
        }

        [Server]
        public bool ServerRemoveFirstItem(int itemId)
        {
            for (int i = 0; i < ItemIds.Count; i++)
            {
                if (ItemIds[i] == itemId)
                {
                    ItemIds.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }
    }
}