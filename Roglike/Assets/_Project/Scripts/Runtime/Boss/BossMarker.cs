using Mirror;
using UnityEngine;

namespace Project.Boss
{
    // Просто метка: этот враг считается боссом сегмента
    public sealed class BossMarker : NetworkBehaviour
    {
        [SyncVar] public int SegmentIndex;
        [SyncVar] public int RoomId;

        [Server]
        public void Init(int segmentIndex, int roomId)
        {
            SegmentIndex = segmentIndex;
            RoomId = roomId;
        }
    }
}