using System.Collections.Generic;
using UnityEngine;
using Project.WorldGen.Generators;

namespace Project.WorldGen.Generators
{
    // Генератор сегмента как "полу-открытая карта":
    // большие локации (узлы) + широкие тропы + декор.
    public sealed class RouteSegmentGeneratorWide : SegmentGeneratorBase
    {
        [Header("Layout")]
        [SerializeField] private int mainPathMin = 5;
        [SerializeField] private int mainPathMax = 7;
        [SerializeField] private float stepDistanceMin = 22f;
        [SerializeField] private float stepDistanceMax = 32f;
        [SerializeField] private float turnAngleMaxDeg = 35f; // извилистость дороги

        [Header("Locations (big clearings)")]
        [SerializeField] private Vector2 locationSizeMin = new Vector2(18f, 18f);
        [SerializeField] private Vector2 locationSizeMax = new Vector2(28f, 28f);
        [SerializeField] private float groundY = 0f;

        [Header("Trail (wide road)")]
        [SerializeField] private float trailWidth = 8f;
        [SerializeField] private float trailTileLength = 6f;   // длина одного "кусочка" дороги
        [SerializeField] private float trailThickness = 0.2f;  // толщина коллайдера/плитки
        [SerializeField] private float trailJitter = 1.2f;     // лёгкая неровность

        [Header("Decor (non-empty trails)")]
        [SerializeField] private int decorPerTrailTileMin = 0;
        [SerializeField] private int decorPerTrailTileMax = 2;
        [SerializeField] private float decorOffsetFromCenterMin = 3.5f;
        [SerializeField] private float decorOffsetFromCenterMax = 6.0f;

        [Header("Materials (optional)")]
        [SerializeField] private Material groundMaterial;
        [SerializeField] private Material trailMaterial;
        [SerializeField] private Material decorMaterial;

        // ВАЖНО: Сигнатура как у твоего текущего генератора, чтобы ChunkDungeonController не менять
        public override void GenerateInto(int seed, int segmentIndex, Transform segmentRoot)
        {
            var rng = new System.Random(seed);

            // контейнеры
            var geoRoot = new GameObject("Geometry");
            geoRoot.transform.SetParent(segmentRoot, false);

            var triggersRoot = new GameObject("RoomTriggers");
            triggersRoot.transform.SetParent(segmentRoot, false);

            // маркеры
            var markersGO = new GameObject("SegmentMarkers");
            markersGO.transform.SetParent(segmentRoot, false);
            var markers = markersGO.AddComponent<Project.WorldGen.SegmentMarkers>();

            // === SegmentLayout (совместимость с ChunkDungeonController) ===
            var layout = segmentRoot.GetComponent<Project.WorldGen.SegmentLayout>();
            if (layout == null) layout = segmentRoot.gameObject.AddComponent<Project.WorldGen.SegmentLayout>();

            layout.SegmentIndex = segmentIndex;
            layout.GeneratedSeed = seed;
            layout.SafeRoomId = 0;          // у нас safe — всегда первый узел
            layout.ExitRoomId = -1;         // выставим позже
            layout.Rooms.Clear();  

            // ---- 1) строим main path (узлы) ----
            int mainCount = rng.Next(mainPathMin, mainPathMax + 1); // включая start и exit в этой цепочке
            var nodes = new List<Node>(mainCount);

            // стартовая точка около (0,0) в локале сегмента
            Vector2 dir = RandomDir(rng);
            Vector2 pos = Vector2.zero;

            for (int i = 0; i < mainCount; i++)
            {
                Vector2 size = RandVec2(rng, locationSizeMin, locationSizeMax);

                // создаём узел
                var n = new Node
                {
                    index = i,
                    center = pos,
                    size = size,
                    isStart = (i == 0),
                    isExit = (i == mainCount - 1)
                };
                nodes.Add(n);

                // следующий шаг (кроме последнего)
                if (i < mainCount - 1)
                {
                    float step = RandRange(rng, stepDistanceMin, stepDistanceMax);
                    dir = TurnDir(rng, dir, turnAngleMaxDeg);
                    pos += dir * step;
                }
            }

            // ---- 2) редко добавляем ответвление (1 штука) ----
            // ответвление короткое: от узла k к side и обратно в k+1 (визуально "карман")
            bool makeBranch = rng.NextDouble() < 0.35 && mainCount >= 5;
            Branch branch = default;

            if (makeBranch)
            {
                int k = rng.Next(1, mainCount - 2); // не start и не exit
                var from = nodes[k];
                var to = nodes[k + 1];

                // точка side около середины между from и to, но уводим в сторону
                Vector2 mid = (from.center + to.center) * 0.5f;

                Vector2 along = (to.center - from.center).normalized;
                Vector2 perp = new Vector2(-along.y, along.x);
                float sideDist = RandRange(rng, 16f, 22f);
                Vector2 sidePos = mid + perp * (rng.Next(0, 2) == 0 ? -sideDist : sideDist);

                branch = new Branch
                {
                    fromIndex = k,
                    toIndex = k + 1,
                    sideIndex = mainCount // будет новый индекс
                };

                var sideNode = new Node
                {
                    index = branch.sideIndex,
                    center = sidePos,
                    size = RandVec2(rng, locationSizeMin * 0.9f, locationSizeMax * 0.9f),
                    isStart = false,
                    isExit = false,
                    isSide = true
                };
                nodes.Add(sideNode);
            }

            // ---- 3) строим геометрию локаций ----
            for (int i = 0; i < nodes.Count; i++)
            {
                BuildLocation(nodes[i], segmentIndex, segmentRoot, geoRoot.transform, triggersRoot.transform, layout);
            }

            // ---- 4) строим тропы main path ----
            for (int i = 0; i < mainCount - 1; i++)
            {
                BuildTrail(seed, segmentRoot, geoRoot.transform, rng, nodes[i].center, nodes[i + 1].center);
            }

            // ---- 5) строим тропы ответвления ----
            if (makeBranch)
            {
                var from = nodes[branch.fromIndex];
                var side = nodes[branch.sideIndex];
                var to = nodes[branch.toIndex];

                BuildTrail(seed ^ 0x12345, segmentRoot, geoRoot.transform, rng, from.center, side.center);
                BuildTrail(seed ^ 0x77777, segmentRoot, geoRoot.transform, rng, side.center, to.center);
            }

            // ---- 6) маркеры safe/exit ----
            // SafeSpawn = центр стартовой локации, ExitPoint = центр exit локации
            var safe = new GameObject("SafeSpawn");
            safe.transform.SetParent(markersGO.transform, false);
            safe.transform.localPosition = new Vector3(nodes[0].center.x, 1f, nodes[0].center.y);
            markers.SafeSpawn = safe.transform;

            var exit = new GameObject("ExitPoint");
            exit.transform.SetParent(markersGO.transform, false);
            exit.transform.localPosition = new Vector3(nodes[mainCount - 1].center.x, 1f, nodes[mainCount - 1].center.y);
            markers.ExitPoint = exit.transform;

            // индексы "комнат" (для твоей логики)
            markers.SafeRoomId = 0;
            markers.ExitRoomId = mainCount - 1;

            layout.SafeRoomId = markers.SafeRoomId;
            layout.ExitRoomId = markers.ExitRoomId;

            // === DoorWorld: направляем "дверь" каждой комнаты в сторону следующей по main path ===
            for (int i = 0; i < layout.Rooms.Count; i++)
            {
                var rd = layout.Rooms[i];

                // найдём соответствующий node по RoomId
                int id = rd.RoomId;

                // вычислим "куда смотреть"
                Vector2 from = GetNodeCenter(nodes, id);

                Vector2 toward;

                // main path door: к следующей комнате, иначе к exit point
                if (id >= 0 && id < mainCount - 1)
                    toward = GetNodeCenter(nodes, id + 1);
                else if (id == mainCount - 1)
                    toward = GetNodeCenter(nodes, id); // exit room: дверь условно в центре
                else
                {
                    // side room: дверь к ближайшей main-комнате (from/to)
                    // (простая эвристика: находим ближайший main узел)
                    toward = FindNearestMain(nodes, mainCount, from);
                }

                Vector2 doordir = (toward - from);
                if (doordir.sqrMagnitude < 0.0001f) doordir = Vector2.up;
                doordir.Normalize();

                // ставим точку двери на границе bounds комнаты
                float halfX = rd.Size.x * 0.5f;
                float halfZ = rd.Size.z * 0.5f;

                Vector2 door = from;

                if (Mathf.Abs(doordir.x) > Mathf.Abs(doordir.y))
                {
                    door.x += Mathf.Sign(doordir.x) * halfX;
                    door.y += Mathf.Clamp(doordir.y * halfZ, -halfZ + 1f, halfZ - 1f);
                }
                else
                {
                    door.y += Mathf.Sign(doordir.y) * halfZ;
                    door.x += Mathf.Clamp(doordir.x * halfX, -halfX + 1f, halfX - 1f);
                }

                rd.DoorWorld = new Vector3(door.x, 1f, door.y);
                layout.Rooms[i] = rd;
            }

            // --- local helpers inside class RouteSegmentGeneratorWide ---
            static Vector2 GetNodeCenter(List<Node> nodesList, int roomId)
            {
                for (int i = 0; i < nodesList.Count; i++)
                    if (nodesList[i].index == roomId)
                        return nodesList[i].center;
                return Vector2.zero;
            }

            static Vector2 FindNearestMain(List<Node> nodesList, int mainCount, Vector2 from)
            {
                float best = float.MaxValue;
                Vector2 bestPos = Vector2.zero;
                for (int i = 0; i < mainCount; i++)
                {
                    Vector2 p = nodesList[i].center;
                    float d = (p - from).sqrMagnitude;
                    if (d < best) { best = d; bestPos = p; }
                }
                return bestPos;
            }

            Debug.Log($"[RouteSegmentGeneratorWide] Generated seg={segmentIndex} nodes={nodes.Count} main={mainCount} branch={makeBranch}");
        }

        // ------------------ building blocks ------------------

        private void BuildLocation(
            Node n,
            int segmentIndex,
            Transform segmentRoot,
            Transform geoRoot,
            Transform triggersRoot,
            Project.WorldGen.SegmentLayout layout)
        {
            // Площадка (как большой низкий куб)
            var locGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            locGO.name = n.isStart ? $"Location_Start_{n.index}" :
                        n.isExit ? $"Location_Exit_{n.index}" :
                        n.isSide ? $"Location_Side_{n.index}" :
                        $"Location_{n.index}";

            locGO.transform.SetParent(geoRoot, false);
            locGO.transform.localPosition = new Vector3(n.center.x, groundY, n.center.y);
            locGO.transform.localScale = new Vector3(n.size.x, 1f, n.size.y);

            // === записываем RoomData в SegmentLayout ===
            var centerWorld = locGO.transform.position;

            // Size хранится по XZ, Y неважно
            var sizeWorld = new Vector3(n.size.x, 0f, n.size.y);

            // Bounds в мировых координатах
            var bounds = new Bounds(centerWorld, new Vector3(n.size.x, 2f, n.size.y));

            // DoorWorld: пока поставим "вперёд" (будем уточнять позже в конце генерации)
            var doorWorld = centerWorld; // временно

            layout.Rooms.Add(new Project.WorldGen.RoomData
            {
                RoomId = n.index,
                Center = centerWorld,
                Size = sizeWorld,
                Bounds = bounds,
                DoorWorld = doorWorld
            });

            // делаем тонким “полом”
            locGO.transform.localScale = new Vector3(n.size.x, 1f, n.size.y);
            var col = locGO.GetComponent<BoxCollider>();
            col.size = new Vector3(1f, trailThickness, 1f);
            col.center = new Vector3(0f, trailThickness * 0.5f, 0f);

            var r = locGO.GetComponent<Renderer>();
            if (r != null && groundMaterial != null) r.sharedMaterial = groundMaterial;

            // RoomTrigger (активация врагов "при входе в локацию")
            var triggerGO = new GameObject($"RoomTrigger_{segmentIndex}_{n.index}");
            triggerGO.transform.SetParent(triggersRoot, false);
            triggerGO.transform.localPosition = new Vector3(n.center.x, 1f, n.center.y);

            var trig = triggerGO.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.size = new Vector3(n.size.x * 0.9f, 3f, n.size.y * 0.9f);

            var rt = triggerGO.AddComponent<Project.WorldGen.RoomTrigger>();
            rt.Init(segmentIndex, n.index); // <- это важно: метод Init, который ты добавлял/проверял

            // Визуальные “границы” (чтобы игрок не уходил далеко): камни/пеньки по углам
            // Это не стены-коридоры — просто мягкая рамка вокруг поляны.
            BuildSoftBorder(geoRoot, n);
        }

        private void BuildSoftBorder(Transform geoRoot, Node n)
        {
            // 4 "камня" по углам, чтобы локация визуально читалась
            float hx = n.size.x * 0.5f;
            float hz = n.size.y * 0.5f;

            Vector3[] corners =
            {
                new Vector3(n.center.x - hx, 0.6f, n.center.y - hz),
                new Vector3(n.center.x - hx, 0.6f, n.center.y + hz),
                new Vector3(n.center.x + hx, 0.6f, n.center.y - hz),
                new Vector3(n.center.x + hx, 0.6f, n.center.y + hz),
            };

            for (int i = 0; i < corners.Length; i++)
            {
                var rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rock.name = "Rock";
                rock.transform.SetParent(geoRoot, false);
                rock.transform.localPosition = corners[i];
                rock.transform.localScale = new Vector3(1.6f, 1.2f, 1.6f);

                var rr = rock.GetComponent<Renderer>();
                if (rr != null && decorMaterial != null) rr.sharedMaterial = decorMaterial;
            }
        }

        private void BuildTrail(int seed, Transform segmentRoot, Transform geoRoot, System.Random rng, Vector2 a, Vector2 b)
        {
            // делаем дорогу из нескольких перекрывающихся "плиток" (кубы-полосы)
            Vector2 dir = (b - a);
            float dist = dir.magnitude;
            if (dist < 0.001f) return;
            dir /= dist;

            int tiles = Mathf.CeilToInt(dist / trailTileLength);

            // перпендикуляр для декора по краям
            Vector2 perp = new Vector2(-dir.y, dir.x);

            for (int i = 0; i <= tiles; i++)
            {
                float t = (tiles == 0) ? 0f : (i / (float)tiles);
                Vector2 p = Vector2.Lerp(a, b, t);

                // лёгкий jitter, чтобы не была идеальная прямая
                float j = RandRange(rng, -trailJitter, trailJitter);
                Vector2 pj = p + perp * j;

                // плитка дороги
                var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.name = "TrailTile";
                tile.transform.SetParent(geoRoot, false);
                tile.transform.localPosition = new Vector3(pj.x, groundY + 0.05f, pj.y);

                // ориентируем по направлению
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
                tile.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

                // ширина дороги + небольшой запас, чтобы не было дыр
                float length = trailTileLength * 1.15f;
                tile.transform.localScale = new Vector3(trailWidth, 1f, length);

                var col = tile.GetComponent<BoxCollider>();
                col.size = new Vector3(1f, trailThickness, 1f);
                col.center = new Vector3(0f, trailThickness * 0.5f + 0.05f, 0f);

                var r = tile.GetComponent<Renderer>();
                if (r != null && trailMaterial != null) r.sharedMaterial = trailMaterial;

                // декор — НЕ по центру дороги, а по бокам
                int decorCount = rng.Next(decorPerTrailTileMin, decorPerTrailTileMax + 1);
                for (int d = 0; d < decorCount; d++)
                {
                    float side = (rng.Next(0, 2) == 0) ? -1f : 1f;
                    float off = RandRange(rng, decorOffsetFromCenterMin, decorOffsetFromCenterMax);
                    Vector2 dp = pj + perp * side * off;

                    var deco = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    deco.name = "TrailDecor";
                    deco.transform.SetParent(geoRoot, false);
                    deco.transform.localPosition = new Vector3(dp.x, 0.6f, dp.y);
                    deco.transform.localScale = new Vector3(0.7f, 0.6f, 0.7f);

                    var dr = deco.GetComponent<Renderer>();
                    if (dr != null && decorMaterial != null) dr.sharedMaterial = decorMaterial;
                }
            }
        }

        // ------------------ helpers ------------------

        private static float RandRange(System.Random rng, float min, float max)
        {
            double t = rng.NextDouble();
            return (float)(min + (max - min) * t);
        }

        private static Vector2 RandVec2(System.Random rng, Vector2 min, Vector2 max)
        {
            return new Vector2(
                RandRange(rng, min.x, max.x),
                RandRange(rng, min.y, max.y)
            );
        }

        private static Vector2 RandomDir(System.Random rng)
        {
            // стартовое направление
            float a = RandRange(rng, 0f, 360f) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        private static Vector2 TurnDir(System.Random rng, Vector2 dir, float maxAngleDeg)
        {
            float angle = RandRange(rng, -maxAngleDeg, maxAngleDeg) * Mathf.Deg2Rad;
            float c = Mathf.Cos(angle);
            float s = Mathf.Sin(angle);
            return new Vector2(dir.x * c - dir.y * s, dir.x * s + dir.y * c).normalized;
        }

        private struct Node
        {
            public int index;
            public Vector2 center;
            public Vector2 size;
            public bool isStart;
            public bool isExit;
            public bool isSide;
        }

        private struct Branch
        {
            public int fromIndex;
            public int toIndex;
            public int sideIndex;
        }

        public override float GetRecommendedSegmentOffset()
        {
            // Грубая оценка "длины" маршрута: max шаг * max количество переходов + запас
            float pathLen = (mainPathMax - 1) * stepDistanceMax;
            return pathLen + 80f;
        }
    }
}