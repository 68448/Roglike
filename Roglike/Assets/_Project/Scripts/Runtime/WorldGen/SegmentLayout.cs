using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.WorldGen
{
    [Serializable]
    public struct RoomData
    {
        public int RoomId;
        public Vector3 Center;      // мировая позиция
        public Vector3 Size;        // по XZ, Y неважно
        public Bounds Bounds;       // мировые bounds для удобства
        public Vector3 DoorWorld;   // где “дверь” в сторону коридора (упрощённо 1 дверь)
    }

    /// <summary>
    /// Хранит layout сегмента (комнаты/размеры) на стороне клиента и сервера.
    /// НЕ сетевой объект. Создаётся локально при генерации сегмента.
    /// </summary>
    public sealed class SegmentLayout : MonoBehaviour
    {
        public int SafeRoomId = 0;
        public int ExitRoomId = -1;
        public int SegmentIndex;
        public int GeneratedSeed;
        public List<RoomData> Rooms = new List<RoomData>();
    }
}
