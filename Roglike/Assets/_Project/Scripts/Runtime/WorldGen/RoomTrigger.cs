using Mirror;
using UnityEngine;

namespace Project.WorldGen
{
    public sealed class RoomTrigger : MonoBehaviour
    {
        public int SegmentIndex;
        public int RoomId;

        public void Init(int segmentIndex, int roomId)
        {
            SegmentIndex = segmentIndex;
            RoomId = roomId;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!NetworkServer.active)
                return;

            // ВАЖНО: коллайдер может быть на дочернем объекте игрока,
            // а NetworkIdentity почти всегда на корне Player.
            var ni = other.GetComponentInParent<NetworkIdentity>();
            if (ni == null)
                return;

            // Можно дополнительно ограничить только игроками
            if (!ni.CompareTag("Player"))
                return;

            var controller = FindFirstObjectByType<ChunkDungeonController>();
            if (controller == null)
            {
                Debug.LogError("[RoomTrigger] ChunkDungeonController not found!");
                return;
            }

            Debug.Log($"[RoomTrigger] Server detected player enter: seg={SegmentIndex} room={RoomId} playerNetId={ni.netId}");
            controller.ServerActivateRoom(SegmentIndex, RoomId);
        }
    }
}
