using UnityEngine;

namespace Project.WorldGen
{
    // Просто контейнер для маркеров сегмента
    public sealed class SegmentMarkers : MonoBehaviour
    {
        public Transform SafeSpawn;
        public Transform ExitPoint;

        public int SafeRoomId = 0;
        public int ExitRoomId = -1;
    }
}