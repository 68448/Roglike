using System;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;
using Project.WorldGen.Generators;

namespace Project.WorldGen
{
    public sealed class DungeonGeneratorStub : SegmentGeneratorBase
    {
        [Header("Visuals")]
        [SerializeField] private Material roomMaterial;
        [SerializeField] private Material wallMaterial;
        [SerializeField] private Material corridorMaterial;

        [Header("Generation")]
        [SerializeField] private int desiredRooms = 10;
        [SerializeField] private Vector2 roomSizeRange = new Vector2(8, 16);
        [SerializeField] private int maxPlacementAttempts = 200;
        [SerializeField] private float roomPadding = 2f;

        [Header("Geometry")]
        [SerializeField] private float floorHeight = 1f;
        [SerializeField] private float wallHeight = 3f;
        [SerializeField] private float wallThickness = 0.5f;
        [SerializeField] private float corridorWidth = 3f;
        [SerializeField] private float doorWidth = 3f;

        [Header("Corridor Fix")]
        [SerializeField] private float corridorYBias = 0.02f;  // чтобы не мерцало с полом комнаты
        [SerializeField] private float corridorOverlap = 0.25f; // нахлёст, чтобы не было дыр
        [SerializeField] private float corridorSnapStep = 0.5f; // шаг сетки для привязки

        [SerializeField] private float generationAreaHalf = 55f;

        private Transform _dungeonRoot;

        private Vector2 Snap2(Vector2 p)
        {
            if (corridorSnapStep <= 0f) return p;

            float x = Mathf.Round(p.x / corridorSnapStep) * corridorSnapStep;
            float y = Mathf.Round(p.y / corridorSnapStep) * corridorSnapStep;
            return new Vector2(x, y);
        }

        private void Awake()
        {
            if (_dungeonRoot == null)
                _dungeonRoot = transform;

            doorWidth = corridorWidth;
        }

        // Старый метод оставляем
        public void Generate(int seed)
        {
            GenerateInto(seed, segmentIndex: 1, parent: _dungeonRoot);
        }

        // Новый основной метод
        public override void GenerateInto(int seed, int segmentIndex, Transform parent)
        {

            // === create markers container ===
            var markersGO = new GameObject("SegmentMarkers");
            markersGO.transform.SetParent(parent, false);
            var markers = markersGO.AddComponent<Project.WorldGen.SegmentMarkers>();

            _dungeonRoot = parent;

            ClearChildren(_dungeonRoot);

            // Создаём/обновляем layout-компонент
            var layout = _dungeonRoot.GetComponent<SegmentLayout>();
            if (layout == null) layout = _dungeonRoot.gameObject.AddComponent<SegmentLayout>();
            layout.GeneratedSeed = seed;
            layout.SegmentIndex = segmentIndex;
            layout.Rooms.Clear();

            var rng = new Random(seed);

            // 1) Размещаем комнаты (не пересекаются)
            var rooms = PlaceRooms(rng);

            // 2) Соединяем комнаты (MST + немного петель)
            var edges = BuildConnections(rng, rooms);

            // 3) Для каждой комнаты вычислим “дверь” (упрощённо: 1 дверь в сторону ближайшей связи)
            ComputeDoorsForEdges(rooms, edges, out var edgeDoorA, out var edgeDoorB);

            int exitRoomId = ComputeExitRoomId(rooms, edges, startRoomId: 0);
            layout.SafeRoomId = 0;
            layout.ExitRoomId = exitRoomId;

            // 4) Строим геометрию
            BuildRoomsAndWalls(rooms);
            BuildCorridors(edges, rooms, edgeDoorA, edgeDoorB);

            var safeRoom = rooms[0];
            var exitRoom = rooms[rooms.Count - 1];

            markers.SafeRoomId = 0;
            markers.ExitRoomId = rooms.Count - 1;

            // === SAFE marker ===
            var safeGO = new GameObject("SafeSpawn");
            safeGO.transform.SetParent(markersGO.transform, false);
            safeGO.transform.localPosition = new Vector3(safeRoom.Center.x, 1f, safeRoom.Center.y);
            markers.SafeSpawn = safeGO.transform;
            
            // === EXIT marker ===
            var exitGO = new GameObject("ExitPoint");
            exitGO.transform.SetParent(markersGO.transform, false);
            exitGO.transform.localPosition = new Vector3(exitRoom.Center.x, 1f, exitRoom.Center.y);
            markers.ExitPoint = exitGO.transform;
            

            // 5) Заполняем layout.Rooms (в мировых координатах)
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];

                // Переводим в мир: root.position + локальное
                Vector3 worldCenter = _dungeonRoot.position + new Vector3(r.Center.x, 0, r.Center.y);
                Vector3 worldSize = new Vector3(r.Size.x, 0, r.Size.y);

                var b = new Bounds(worldCenter + Vector3.up * 1f, new Vector3(r.Size.x, 4f, r.Size.y));

                layout.Rooms.Add(new RoomData
                {
                    RoomId = i,
                    Center = worldCenter,
                    Size = worldSize,
                    Bounds = b,
                    DoorWorld = _dungeonRoot.position + new Vector3(
                        (r.Doors != null && r.Doors.Count > 0) ? r.Doors[0].x : r.Center.x,
                        1f,
                        (r.Doors != null && r.Doors.Count > 0) ? r.Doors[0].y : r.Center.y
                    )
                });

                // Создаём триггер комнаты (для Day 2 части)
                CreateRoomTrigger(layout.SegmentIndex, i, worldCenter, r.Size);
            }

            Debug.Log($"[DungeonGeneratorStub] Generated segment={segmentIndex} rooms={rooms.Count} seed={seed}");
        }

        // ======= ВНУТРЕННИЕ СТРУКТУРЫ =======

        private struct RoomLocal
        {
            public Vector2 Center;
            public Vector2 Size;
            public Rect Rect;
            public List<Vector2> Doors; // двери в локальных координатах
        }

        private struct Edge
        {
            public int A;
            public int B;
        }

        // ======= РАЗМЕЩЕНИЕ КОМНАТ =======

        private List<RoomLocal> PlaceRooms(Random rng)
        {

            // Область генерации в локальных координатах
            // Чем больше — тем “лабиринтнее” и менее линейно
            float areaHalf = generationAreaHalf;

            var rooms = new List<RoomLocal>();

            float safeSx = Mathf.Max(roomSizeRange.y, 16f); // делаем побольше
            float safeSz = Mathf.Max(roomSizeRange.y, 16f);

            float safecx = 0f;
            float safecz = 0f;

            var saferect = new Rect(safecx - safeSx * 0.5f, safecz - safeSz * 0.5f, safeSx, safeSz);

            rooms.Add(new RoomLocal
            {
                Center = new Vector2(safecx, safecz),
                Size = new Vector2(safeSx, safeSz),
                Rect = saferect,
                Doors = new System.Collections.Generic.List<Vector2>()
            });


            int attempts = 0;
            while (rooms.Count < desiredRooms && attempts < maxPlacementAttempts)
            {
                attempts++;

                float sx = Lerp(roomSizeRange.x, roomSizeRange.y, (float)rng.NextDouble());
                float sz = Lerp(roomSizeRange.x, roomSizeRange.y, (float)rng.NextDouble());

                float cx = Lerp(-areaHalf, areaHalf, (float)rng.NextDouble());
                float cz = Lerp(-areaHalf, areaHalf, (float)rng.NextDouble());

                var rect = new Rect(
                    cx - sx * 0.5f,
                    cz - sz * 0.5f,
                    sx,
                    sz
                );

                // Проверка пересечений с padding
                bool overlaps = false;
                for (int i = 0; i < rooms.Count; i++)
                {
                    Rect other = rooms[i].Rect;
                    other.xMin -= roomPadding;
                    other.xMax += roomPadding;
                    other.yMin -= roomPadding;
                    other.yMax += roomPadding;

                    if (other.Overlaps(rect))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (overlaps)
                    continue;

                rooms.Add(new RoomLocal
                {
                    Center = new Vector2(cx, cz),
                    Size = new Vector2(sx, sz),
                    Rect = rect,
                    Doors = new List<Vector2>()
                });
            }

            return rooms;
        }

        // ======= СОЕДИНЕНИЕ КОМНАТ (MST + петли) =======

        private List<Edge> BuildConnections(Random rng, List<RoomLocal> rooms)
        {
            var edges = new List<Edge>();
            if (rooms.Count <= 1) return edges;

            // MST (Prim)
            var inTree = new bool[rooms.Count];
            inTree[0] = true;
            int connected = 1;

            while (connected < rooms.Count)
            {
                float bestDist = float.MaxValue;
                int bestA = -1, bestB = -1;

                for (int a = 0; a < rooms.Count; a++)
                {
                    if (!inTree[a]) continue;

                    for (int b = 0; b < rooms.Count; b++)
                    {
                        if (inTree[b]) continue;

                        float d = Vector2.Distance(rooms[a].Center, rooms[b].Center);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestA = a;
                            bestB = b;
                        }
                    }
                }

                edges.Add(new Edge { A = bestA, B = bestB });
                inTree[bestB] = true;
                connected++;
            }

            // Добавим несколько “лишних” связей (петли), чтобы не было строго дерева
            int extra = Mathf.Clamp(rooms.Count / 4, 1, 4);
            for (int i = 0; i < extra; i++)
            {
                int a = rng.Next(0, rooms.Count);
                int b = rng.Next(0, rooms.Count);
                if (a == b) continue;

                // если уже есть связь — пропускаем
                if (HasEdge(edges, a, b)) continue;

                edges.Add(new Edge { A = a, B = b });
            }

            return edges;
        }

        private bool HasEdge(List<Edge> edges, int a, int b)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                if ((edges[i].A == a && edges[i].B == b) || (edges[i].A == b && edges[i].B == a))
                    return true;
            }
            return false;
        }

        // ======= ДВЕРИ =======
        private void ComputeDoorsForEdges(List<RoomLocal> rooms, List<Edge> edges, out Vector2[] edgeDoorA, out Vector2[] edgeDoorB)
        {
            edgeDoorA = new Vector2[edges.Count];
            edgeDoorB = new Vector2[edges.Count];

            for (int i = 0; i < edges.Count; i++)
            {
                int a = edges[i].A;
                int b = edges[i].B;

                var ra = rooms[a];
                var rb = rooms[b];

                Vector2 doorA = ComputeDoorToward(ra, rb.Center);
                Vector2 doorB = ComputeDoorToward(rb, ra.Center);

                if (ra.Doors == null) ra.Doors = new List<Vector2>();
                if (rb.Doors == null) rb.Doors = new List<Vector2>();

                ra.Doors.Add(doorA);
                rb.Doors.Add(doorB);

                rooms[a] = ra;
                rooms[b] = rb;

                edgeDoorA[i] = doorA;
                edgeDoorB[i] = doorB;
            }
        }


        private Vector2 ComputeDoorToward(RoomLocal room, Vector2 toward)
        {
            Vector2 dir = (toward - room.Center).normalized;

            float halfX = room.Size.x * 0.5f;
            float halfZ = room.Size.y * 0.5f;

            Vector2 door = room.Center;

            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
            {
                door.x += Mathf.Sign(dir.x) * halfX;
                door.y += Mathf.Clamp(dir.y * halfZ, -halfZ + 1f, halfZ - 1f);
            }
            else
            {
                door.y += Mathf.Sign(dir.y) * halfZ;
                door.x += Mathf.Clamp(dir.x * halfX, -halfX + 1f, halfX - 1f);
            }

            return door;
        }


        // private RoomLocal SetDoorToward(RoomLocal room, Vector2 toward)
        // {
        //     Vector2 dir = (toward - room.Center).normalized;

        //     // Выбираем сторону комнаты по направлению
        //     float halfX = room.Size.x * 0.5f;
        //     float halfZ = room.Size.y * 0.5f;

        //     Vector2 door = room.Center;

        //     if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
        //     {
        //         // дверь на левой/правой стене
        //         door.x += Mathf.Sign(dir.x) * halfX;
        //         door.y += Mathf.Clamp(dir.y * halfZ, -halfZ + 1f, halfZ - 1f);
        //     }
        //     else
        //     {
        //         // дверь на верх/низ стене
        //         door.y += Mathf.Sign(dir.y) * halfZ;
        //         door.x += Mathf.Clamp(dir.x * halfX, -halfX + 1f, halfX - 1f);
        //     }

        //     room.Door = door;
        //     return room;
        // }

        // ======= СТРОИТЕЛЬСТВО ГЕОМЕТРИИ =======

        private void BuildRoomsAndWalls(List<RoomLocal> rooms)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                var r = rooms[i];

                // Пол комнаты
                CreateBox(
                    name: $"RoomFloor_{i}",
                    parent: _dungeonRoot,
                    localPos: new Vector3(r.Center.x, floorHeight * 0.5f, r.Center.y),
                    localScale: new Vector3(r.Size.x, floorHeight, r.Size.y),
                    mat: roomMaterial
                );

                // Стены (4 стороны), с проёмом около двери
                BuildRoomWalls(i, r);
            }
        }

        private void BuildRoomWalls(int roomId, RoomLocal r)
        {
            float halfX = r.Size.x * 0.5f;
            float halfZ = r.Size.y * 0.5f;

            float eps = 0.6f; // допуск “дверь на этой стене”

            var rightOffsets = new List<float>(); // по Z
            var leftOffsets = new List<float>();
            var topOffsets = new List<float>();   // по X
            var bottomOffsets = new List<float>();

            for (int i = 0; i < r.Doors.Count; i++)
            {
                Vector2 d = r.Doors[i];

                if (Mathf.Abs(d.x - (r.Center.x + halfX)) < eps) rightOffsets.Add(d.y - r.Center.y);
                else if (Mathf.Abs(d.x - (r.Center.x - halfX)) < eps) leftOffsets.Add(d.y - r.Center.y);
                else if (Mathf.Abs(d.y - (r.Center.y + halfZ)) < eps) topOffsets.Add(d.x - r.Center.x);
                else if (Mathf.Abs(d.y - (r.Center.y - halfZ)) < eps) bottomOffsets.Add(d.x - r.Center.x);
            }

            // +X wall (вдоль Z)
            BuildWallWithDoors(
                _dungeonRoot, $"Wall_R_{roomId}",
                new Vector3(r.Center.x + halfX, wallHeight * 0.5f, r.Center.y),
                r.Size.y, alongZ: true, doorOffsets: rightOffsets
            );

            // -X wall
            BuildWallWithDoors(
                _dungeonRoot, $"Wall_L_{roomId}",
                new Vector3(r.Center.x - halfX, wallHeight * 0.5f, r.Center.y),
                r.Size.y, alongZ: true, doorOffsets: leftOffsets
            );

            // +Z wall (вдоль X)
            BuildWallWithDoors(
                _dungeonRoot, $"Wall_T_{roomId}",
                new Vector3(r.Center.x, wallHeight * 0.5f, r.Center.y + halfZ),
                r.Size.x, alongZ: false, doorOffsets: topOffsets
            );

            // -Z wall
            BuildWallWithDoors(
                _dungeonRoot, $"Wall_B_{roomId}",
                new Vector3(r.Center.x, wallHeight * 0.5f, r.Center.y - halfZ),
                r.Size.x, alongZ: false, doorOffsets: bottomOffsets
            );
        }


        private void BuildWallWithOptionalDoor(Transform parent, string name, Vector3 wallCenter, float wallLength, bool alongZ, bool doorOnThisWall, float doorOffsetOnWall)
        {
            // Если двери нет — строим цельную стену
            if (!doorOnThisWall)
            {
                CreateWallSegment(parent, name, wallCenter, wallLength, alongZ);
                return;
            }

            // Если дверь есть — делим стену на 2 сегмента, оставляя проём doorWidth
            float half = wallLength * 0.5f;

            float doorHalf = doorWidth * 0.5f;
            float doorCenter = Mathf.Clamp(doorOffsetOnWall, -half + doorHalf, half - doorHalf);

            float leftLen = (doorCenter - doorHalf) - (-half);
            float rightLen = (half) - (doorCenter + doorHalf);

            // Левый сегмент
            if (leftLen > 0.5f)
            {
                Vector3 c = wallCenter + (alongZ ? new Vector3(0, 0, -half + leftLen * 0.5f) : new Vector3(-half + leftLen * 0.5f, 0, 0));
                CreateWallSegment(parent, name + "_A", c, leftLen, alongZ);
            }

            // Правый сегмент
            if (rightLen > 0.5f)
            {
                Vector3 c = wallCenter + (alongZ ? new Vector3(0, 0, half - rightLen * 0.5f) : new Vector3(half - rightLen * 0.5f, 0, 0));
                CreateWallSegment(parent, name + "_B", c, rightLen, alongZ);
            }
        }

        private void CreateWallSegment(Transform parent, string name, Vector3 wallCenter, float wallLength, bool alongZ)
        {
            Vector3 scale = alongZ
                ? new Vector3(wallThickness, wallHeight, wallLength)
                : new Vector3(wallLength, wallHeight, wallThickness);

            CreateBox(name, parent, wallCenter, scale, wallMaterial);
        }

        private void BuildCorridors(List<Edge> edges, List<RoomLocal> rooms, Vector2[] edgeDoorA, Vector2[] edgeDoorB)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                int a = edges[i].A;
                int b = edges[i].B;

                Vector2 doorA = edgeDoorA[i];
                Vector2 doorB = edgeDoorB[i];

                // Точки выхода из двери за стену
                Vector2 from = DoorExitPoint(rooms[a], doorA);
                Vector2 to   = DoorExitPoint(rooms[b], doorB);

                // ✅ соединяем DoorPad с коридором
                BuildCorridorSegment(doorA, from);
                BuildCorridorSegment(doorB, to);

                // Сначала сделаем L-образный коридор.
                // Чтобы уменьшить промахи, выбираем порядок поворота в зависимости от осей
                Vector2 mid1 = new Vector2(to.x, from.y);
                Vector2 mid2 = new Vector2(from.x, to.y);

                // Выбираем "более короткий" вариант
                float len1 = (mid1 - from).magnitude + (to - mid1).magnitude;
                float len2 = (mid2 - from).magnitude + (to - mid2).magnitude;

                if (len1 <= len2)
                {
                    Vector2 mid = Snap2(mid1);
                    BuildCorridorSegment(from, mid);
                    BuildCorridorSegment(mid, to);
                    BuildCornerPad(mid);
                }
                else
                {
                    Vector2 mid = Snap2(mid2);
                    BuildCorridorSegment(from, mid);
                    BuildCorridorSegment(mid, to);
                    BuildCornerPad(mid);
                }

                // (Опционально) Маленькие "стыковочные площадки" у двери:
                // они визуально гарантируют попадание коридора в проём.
                BuildDoorPad(doorA);
                BuildDoorPad(doorB);
            }
        }


        // private Vector2 FindClosestDoor(RoomLocal room, Vector2 towardCenter)
        // {
        //     if (room.Doors == null || room.Doors.Count == 0)
        //         return room.Center; // fallback

        //     Vector2 best = room.Doors[0];
        //     float bestDist = float.MaxValue;

        //     for (int i = 0; i < room.Doors.Count; i++)
        //     {
        //         float d = Vector2.Distance(room.Doors[i], towardCenter);
        //         if (d < bestDist)
        //         {
        //             bestDist = d;
        //             best = room.Doors[i];
        //         }
        //     }

        //     return best;
        // }


        private void BuildCorridorSegment(Vector2 from, Vector2 to)
        {
            // 1) Снэпим точки, чтобы убрать дробные стыки
            from = Snap2(from);
            to   = Snap2(to);

            Vector2 delta = to - from;
            float dx = delta.x;
            float dz = delta.y;

            // почти нулевой сегмент
            if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dz) < 0.01f)
                return;

            // 2) Делаем строго осевой сегмент (X или Z)
            bool alongX = Mathf.Abs(dx) >= Mathf.Abs(dz);

            if (alongX)
            {
                float len = Mathf.Abs(dx);
                float centerX = (from.x + to.x) * 0.5f;
                float z = from.y; // Z фиксирован

                // 3) Нахлёст: чуть увеличиваем длину
                float sizeX = len + corridorOverlap;

                CreateBox(
                    name: "Corridor",
                    parent: _dungeonRoot,
                    localPos: new Vector3(centerX, floorHeight * 0.5f + corridorYBias, z),
                    localScale: new Vector3(sizeX, floorHeight, corridorWidth),
                    mat: corridorMaterial != null ? corridorMaterial : roomMaterial
                );
            }
            else
            {
                float len = Mathf.Abs(dz);
                float centerZ = (from.y + to.y) * 0.5f;
                float x = from.x; // X фиксирован

                float sizeZ = len + corridorOverlap;

                CreateBox(
                    name: "Corridor",
                    parent: _dungeonRoot,
                    localPos: new Vector3(x, floorHeight * 0.5f + corridorYBias, centerZ),
                    localScale: new Vector3(corridorWidth, floorHeight, sizeZ),
                    mat: corridorMaterial != null ? corridorMaterial : roomMaterial
                );
            }
        }


        // ======= TRIGGERS =======

        private void CreateRoomTrigger(int segmentIndex, int roomId, Vector3 worldCenter, Vector2 roomSize)
        {
            var go = new GameObject($"RoomTrigger_{roomId}");
            go.transform.SetParent(_dungeonRoot, true);
            go.transform.position = worldCenter + Vector3.up * 1f;

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(roomSize.x - 1f, 3f, roomSize.y - 1f);

            // Нужен Rigidbody (kinematic) для стабильных триггеров
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            var rt = go.AddComponent<RoomTrigger>();
            rt.SegmentIndex = segmentIndex;
            rt.RoomId = roomId;
        }

        // ======= UTILS =======

        private void ClearChildren(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
                Destroy(t.GetChild(i).gameObject);
        }

        private void CreateBox(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = localScale;

            var mr = g.GetComponent<MeshRenderer>();
            if (mr != null && mat != null)
                mr.sharedMaterial = mat;
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private void BuildWallWithDoors(Transform parent, string name, Vector3 wallCenter, float wallLength, bool alongZ, List<float> doorOffsets)
        {
            // doorOffsets — список смещений вдоль стены относительно центра (в локальных координатах)
            // Вырежем интервалы дверей и построим остатки сегментами.

            float half = wallLength * 0.5f;
            float doorHalf = doorWidth * 0.5f;

            // Нормализуем и сортируем
            doorOffsets.Sort();

            float cursor = -half;

            for (int i = 0; i < doorOffsets.Count; i++)
            {
                float doorCenter = Mathf.Clamp(doorOffsets[i], -half + doorHalf, half - doorHalf);
                float doorStart = doorCenter - doorHalf;
                float doorEnd = doorCenter + doorHalf;

                float segLen = doorStart - cursor;
                if (segLen > 0.5f)
                {
                    Vector3 c = wallCenter + (alongZ ? new Vector3(0, 0, cursor + segLen * 0.5f) : new Vector3(cursor + segLen * 0.5f, 0, 0));
                    CreateWallSegment(parent, name + $"_seg{i}", c, segLen, alongZ);
                }

                cursor = doorEnd;
            }

            // Последний сегмент до конца стены
            float lastLen = half - cursor;
            if (lastLen > 0.5f)
            {
                Vector3 c = wallCenter + (alongZ ? new Vector3(0, 0, cursor + lastLen * 0.5f) : new Vector3(cursor + lastLen * 0.5f, 0, 0));
                CreateWallSegment(parent, name + "_last", c, lastLen, alongZ);
            }
        }

        private int ComputeExitRoomId(List<RoomLocal> rooms, List<Edge> edges, int startRoomId)
        {
            // adjacency
            var adj = new List<int>[rooms.Count];
            for (int i = 0; i < rooms.Count; i++) adj[i] = new List<int>();

            for (int i = 0; i < edges.Count; i++)
            {
                adj[edges[i].A].Add(edges[i].B);
                adj[edges[i].B].Add(edges[i].A);
            }

            var dist = new int[rooms.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;

            var q = new Queue<int>();
            dist[startRoomId] = 0;
            q.Enqueue(startRoomId);

            while (q.Count > 0)
            {
                int v = q.Dequeue();
                for (int i = 0; i < adj[v].Count; i++)
                {
                    int to = adj[v][i];
                    if (dist[to] != -1) continue;
                    dist[to] = dist[v] + 1;
                    q.Enqueue(to);
                }
            }

            int bestId = startRoomId;
            int bestD = 0;
            for (int i = 0; i < dist.Length; i++)
            {
                if (dist[i] > bestD)
                {
                    bestD = dist[i];
                    bestId = i;
                }
            }

            return bestId;
        }

        public override float GetRecommendedSegmentOffset()
        {
            // максимальный радиус в локальных координатах
            float maxRoomHalf = roomSizeRange.y * 0.5f;
            float margin = 12f; // запас под стены/коридоры/ошибки
            float radius = generationAreaHalf + maxRoomHalf + corridorWidth + margin;
            return radius * 2f;
        }

        private Vector2 DoorNormal(RoomLocal room, Vector2 door)
        {
            // Определяем, на какой стене дверь, и возвращаем нормаль наружу комнаты
            float halfX = room.Size.x * 0.5f;
            float halfZ = room.Size.y * 0.5f;
            float eps = 0.75f;

            // дверь на правой стене (+X)
            if (Mathf.Abs(door.x - (room.Center.x + halfX)) < eps) return Vector2.right;
            // левая (-X)
            if (Mathf.Abs(door.x - (room.Center.x - halfX)) < eps) return Vector2.left;
            // верхняя (+Z) => у нас door.y это Z
            if (Mathf.Abs(door.y - (room.Center.y + halfZ)) < eps) return Vector2.up;
            // нижняя (-Z)
            if (Mathf.Abs(door.y - (room.Center.y - halfZ)) < eps) return Vector2.down;

            // fallback — если не распознали, “примерно наружу” от центра
            Vector2 dir = (door - room.Center);
            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y))
                return dir.x >= 0 ? Vector2.right : Vector2.left;
            return dir.y >= 0 ? Vector2.up : Vector2.down;
        }

        private Vector2 DoorExitPoint(RoomLocal room, Vector2 door)
        {
            // Смещаемся от двери наружу комнаты, чтобы коридор начинался "за стеной"
            Vector2 n = DoorNormal(room, door);

            float push = wallThickness + corridorWidth * 0.5f + 0.15f; // запас
            return door + n * push;
        }

        private void BuildDoorPad(Vector2 door)
        {
            // маленький квадрат пола на месте двери — маскирует любые визуальные зазоры
            Vector3 scale = new Vector3(corridorWidth + corridorOverlap, floorHeight, corridorWidth + corridorOverlap);
            CreateBox(
                name: "DoorPad",
                parent: _dungeonRoot,
                localPos: new Vector3(door.x, floorHeight * 0.5f + corridorYBias, door.y),
                localScale: scale,
                mat: corridorMaterial != null ? corridorMaterial : roomMaterial
            );
        }

        private void BuildCornerPad(Vector2 corner)
        {
            corner = Snap2(corner);

            Vector3 scale = new Vector3(corridorWidth + corridorOverlap, floorHeight, corridorWidth + corridorOverlap);

            CreateBox(
                name: "CornerPad",
                parent: _dungeonRoot,
                localPos: new Vector3(corner.x, floorHeight * 0.5f + corridorYBias, corner.y),
                localScale: scale,
                mat: corridorMaterial != null ? corridorMaterial : roomMaterial
            );
        }

    }
}
