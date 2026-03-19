using System.Collections.Generic;
using UnityEngine;
using Project.WorldGen.Generators;

namespace Project.WorldGen.Generators
{
    // Генератор сегмента как "полу-открытая карта":
    // большие локации (узлы) + широкие тропы + декор.
    public sealed class RouteSegmentGeneratorWide : SegmentGeneratorBase
    {
        private enum LocationShapeKind
        {
            WideField,
            BrokenField,
            OvalField,
            ForestPocket,
            ForestSplit,
            NarrowClearing,
            MountainShelf,
            MountainSpine,
            CoastalBend,
            CoastalSpit,
            HallCluster
        }

        private struct LayoutProfile
        {
            public int MainPathMin;
            public int MainPathMax;
            public float StepDistanceMin;
            public float StepDistanceMax;
            public float TurnAngleMaxDeg;
            public Vector2 LocationSizeMin;
            public Vector2 LocationSizeMax;
            public float TrailWidth;
            public float TrailTileLength;
            public float TrailThickness;
            public float TrailJitter;
            public int DecorPerTrailTileMin;
            public int DecorPerTrailTileMax;
            public float DecorOffsetFromCenterMin;
            public float DecorOffsetFromCenterMax;
        }

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

        [Header("Organic Shapes")]
        [SerializeField, Range(5, 12)] private int locationPatchSides = 8;
        [SerializeField, Range(0f, 0.45f)] private float locationEdgeNoise = 0.18f;
        [SerializeField, Range(0f, 0.35f)] private float trailEdgeNoise = 0.12f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Project.WorldGen.BiomeConfig _activeBiomeConfig;
        private LayoutProfile _baseProfile;
        private bool _baseProfileCaptured;
        private Project.WorldGen.BiomeLayoutStyle _activeLayoutStyle;

        // ВАЖНО: Сигнатура как у твоего текущего генератора, чтобы ChunkDungeonController не менять
        public override void GenerateInto(int seed, int segmentIndex, Transform segmentRoot, Project.WorldGen.BiomeConfig biomeConfig)
        {
            CaptureBaseProfileIfNeeded();
            _activeBiomeConfig = biomeConfig;
            ApplyLayoutProfile(biomeConfig);
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
            layout.BiomeType = biomeConfig != null ? biomeConfig.biomeType : Project.WorldGen.BiomeService.GetBiomeForSegment(segmentIndex);
            layout.BiomeDisplayName = biomeConfig != null ? biomeConfig.GetResolvedDisplayName() : Project.WorldGen.BiomeService.GetDisplayName(layout.BiomeType);
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
                BuildLocation(nodes[i], segmentIndex, segmentRoot, geoRoot.transform, triggersRoot.transform, layout, rng);
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

            Debug.Log($"[RouteSegmentGeneratorWide] Generated seg={segmentIndex} biome={layout.BiomeDisplayName} style={_activeLayoutStyle} nodes={nodes.Count} main={mainCount} branch={makeBranch}");
        }

        // ------------------ building blocks ------------------

        private void BuildLocation(
            Node n,
            int segmentIndex,
            Transform segmentRoot,
            Transform geoRoot,
            Transform triggersRoot,
            Project.WorldGen.SegmentLayout layout,
            System.Random rng)
        {
            string locationName = n.isStart ? $"Location_Start_{n.index}" :
                                  n.isExit ? $"Location_Exit_{n.index}" :
                                  n.isSide ? $"Location_Side_{n.index}" :
                                  $"Location_{n.index}";

            var locRoot = new GameObject(locationName);
            locRoot.transform.SetParent(geoRoot, false);

            Bounds locationBounds = BuildLocationSurface(locRoot.transform, n, rng);
            var centerWorld = locationBounds.center;
            var sizeWorld = new Vector3(locationBounds.size.x, 0f, locationBounds.size.z);
            var bounds = new Bounds(centerWorld, new Vector3(locationBounds.size.x, 2f, locationBounds.size.z));
            var doorWorld = centerWorld;

            layout.Rooms.Add(new Project.WorldGen.RoomData
            {
                RoomId = n.index,
                Center = centerWorld,
                Size = sizeWorld,
                Bounds = bounds,
                DoorWorld = doorWorld
            });

            var triggerGO = new GameObject($"RoomTrigger_{segmentIndex}_{n.index}");
            triggerGO.transform.SetParent(triggersRoot, false);
            triggerGO.transform.position = centerWorld + Vector3.up;

            var trig = triggerGO.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.size = new Vector3(Mathf.Max(6f, bounds.size.x * 0.88f), 3f, Mathf.Max(6f, bounds.size.z * 0.88f));

            var rt = triggerGO.AddComponent<Project.WorldGen.RoomTrigger>();
            rt.Init(segmentIndex, n.index);

            BuildSoftBorder(locRoot.transform, locationBounds, rng);
        }

        private Bounds BuildLocationSurface(Transform parent, Node n, System.Random rng)
        {
            var chunkBounds = new List<Bounds>(6);
            BuildLocationShape(chunkBounds, parent, n, rng);

            Bounds combined = chunkBounds[0];
            for (int i = 1; i < chunkBounds.Count; i++)
                combined.Encapsulate(chunkBounds[i]);

            return combined;
        }

        private void BuildLocationShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            switch (PickLocationShape(rng))
            {
                case LocationShapeKind.WideField:
                    BuildWideFieldShape(chunkBounds, parent, n, rng);
                    break;
                case LocationShapeKind.BrokenField:
                    BuildBrokenFieldShape(chunkBounds, parent, n, rng);
                    break;
                case LocationShapeKind.OvalField:
                    BuildOvalFieldShape(chunkBounds, parent, n, rng);
                    break;
                case LocationShapeKind.ForestPocket:
                    BuildForestPocketShape(chunkBounds, parent, n, rng);
                    break;
                case LocationShapeKind.ForestSplit:
                    BuildForestSplitShape(chunkBounds, parent, n, rng);
                    break;
                case LocationShapeKind.NarrowClearing:
                    BuildNarrowClearingShape(chunkBounds, parent, n, rng);
                    break;
                case LocationShapeKind.MountainShelf:
                    BuildMountainShelfShape(chunkBounds, parent, n, rng);
                    break;
                case LocationShapeKind.MountainSpine:
                    BuildMountainSpineShape(chunkBounds, parent, n, rng);
                    break;
                case LocationShapeKind.CoastalBend:
                    BuildCoastalBendShape(chunkBounds, parent, n, rng);
                    break;
                case LocationShapeKind.CoastalSpit:
                    BuildCoastalSpitShape(chunkBounds, parent, n, rng);
                    break;
                default:
                    BuildHallClusterShape(chunkBounds, parent, n, rng);
                    break;
            }
        }

        private LocationShapeKind PickLocationShape(System.Random rng)
        {
            return _activeLayoutStyle switch
            {
                Project.WorldGen.BiomeLayoutStyle.OpenFields => rng.Next(0, 3) switch
                {
                    0 => LocationShapeKind.WideField,
                    1 => LocationShapeKind.BrokenField,
                    _ => LocationShapeKind.OvalField
                },
                Project.WorldGen.BiomeLayoutStyle.ForestPaths => rng.Next(0, 3) switch
                {
                    0 => LocationShapeKind.ForestPocket,
                    1 => LocationShapeKind.ForestSplit,
                    _ => LocationShapeKind.NarrowClearing
                },
                Project.WorldGen.BiomeLayoutStyle.MountainPass => rng.Next(0, 2) == 0
                    ? LocationShapeKind.MountainShelf
                    : LocationShapeKind.MountainSpine,
                Project.WorldGen.BiomeLayoutStyle.CoastalRoute => rng.Next(0, 2) == 0
                    ? LocationShapeKind.CoastalBend
                    : LocationShapeKind.CoastalSpit,
                Project.WorldGen.BiomeLayoutStyle.DungeonHalls => LocationShapeKind.HallCluster,
                _ => LocationShapeKind.WideField
            };
        }

        private void BuildWideFieldShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            AddLocationChunk(chunkBounds, parent, n.center, n.size, RandRange(rng, -12f, 12f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(n.size.x * 0.24f, RandRange(rng, -n.size.y * 0.12f, n.size.y * 0.12f)), n.size * 0.52f, RandRange(rng, -20f, 20f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(-n.size.x * 0.20f, RandRange(rng, -n.size.y * 0.10f, n.size.y * 0.10f)), n.size * 0.46f, RandRange(rng, -16f, 16f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(RandRange(rng, -n.size.x * 0.08f, n.size.x * 0.08f), n.size.y * 0.18f), new Vector2(n.size.x * 0.40f, n.size.y * 0.34f), RandRange(rng, -18f, 18f));
        }

        private void BuildBrokenFieldShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            AddLocationChunk(chunkBounds, parent, n.center, n.size * 0.76f, RandRange(rng, -10f, 10f));
            for (int i = 0; i < 4; i++)
            {
                Vector2 dir = RandomInsideUnitCircle(rng).normalized;
                Vector2 center = n.center + new Vector2(dir.x * n.size.x * 0.26f, dir.y * n.size.y * 0.22f);
                Vector2 size = new Vector2(n.size.x * RandRange(rng, 0.26f, 0.38f), n.size.y * RandRange(rng, 0.24f, 0.34f));
                AddLocationChunk(chunkBounds, parent, center, size, RandRange(rng, -28f, 28f));
            }
        }

        private void BuildOvalFieldShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            AddLocationChunk(chunkBounds, parent, n.center, new Vector2(n.size.x * 0.82f, n.size.y * 0.64f), RandRange(rng, -24f, 24f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(0f, -n.size.y * 0.14f), new Vector2(n.size.x * 0.48f, n.size.y * 0.42f), RandRange(rng, -18f, 18f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(0f, n.size.y * 0.16f), new Vector2(n.size.x * 0.44f, n.size.y * 0.36f), RandRange(rng, -18f, 18f));
        }

        private void BuildForestPocketShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            AddLocationChunk(chunkBounds, parent, n.center, n.size * 0.48f, RandRange(rng, -14f, 14f));
            for (int i = 0; i < 4; i++)
            {
                float angle = (i / 4f) * Mathf.PI * 2f + RandRange(rng, -0.35f, 0.35f);
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 center = n.center + new Vector2(dir.x * n.size.x * 0.18f, dir.y * n.size.y * 0.18f);
                Vector2 size = new Vector2(n.size.x * RandRange(rng, 0.22f, 0.30f), n.size.y * RandRange(rng, 0.22f, 0.30f));
                AddLocationChunk(chunkBounds, parent, center, size, RandRange(rng, -32f, 32f));
            }
        }

        private void BuildForestSplitShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            Vector2 axis = RandomInsideUnitCircle(rng).normalized;
            if (axis.sqrMagnitude < 0.01f)
                axis = Vector2.right;

            AddLocationChunk(chunkBounds, parent, n.center - axis * (n.size.x * 0.14f), new Vector2(n.size.x * 0.42f, n.size.y * 0.34f), RandRange(rng, -26f, 26f));
            AddLocationChunk(chunkBounds, parent, n.center + axis * (n.size.x * 0.16f), new Vector2(n.size.x * 0.44f, n.size.y * 0.30f), RandRange(rng, -26f, 26f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(-axis.y, axis.x) * (n.size.y * 0.10f), new Vector2(n.size.x * 0.26f, n.size.y * 0.24f), RandRange(rng, -26f, 26f));
        }

        private void BuildNarrowClearingShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            AddLocationChunk(chunkBounds, parent, n.center, new Vector2(n.size.x * 0.62f, n.size.y * 0.30f), RandRange(rng, -34f, 34f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(-n.size.x * 0.18f, 0f), new Vector2(n.size.x * 0.24f, n.size.y * 0.22f), RandRange(rng, -34f, 34f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(n.size.x * 0.18f, 0f), new Vector2(n.size.x * 0.24f, n.size.y * 0.22f), RandRange(rng, -34f, 34f));
        }

        private void BuildMountainShelfShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            AddLocationChunk(chunkBounds, parent, n.center, new Vector2(n.size.x * 0.68f, n.size.y * 0.28f), RandRange(rng, -12f, 12f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(0f, n.size.y * 0.16f), new Vector2(n.size.x * 0.34f, n.size.y * 0.18f), RandRange(rng, -12f, 12f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(0f, -n.size.y * 0.16f), new Vector2(n.size.x * 0.28f, n.size.y * 0.16f), RandRange(rng, -12f, 12f));
        }

        private void BuildMountainSpineShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            Vector2 spineAxis = RandomInsideUnitCircle(rng).normalized;
            if (spineAxis.sqrMagnitude < 0.01f)
                spineAxis = Vector2.right;

            AddLocationChunk(chunkBounds, parent, n.center, new Vector2(n.size.x * 0.34f, n.size.y * 0.52f), RandRange(rng, -24f, 24f));
            AddLocationChunk(chunkBounds, parent, n.center + spineAxis * (n.size.x * 0.16f), new Vector2(n.size.x * 0.20f, n.size.y * 0.24f), RandRange(rng, -24f, 24f));
            AddLocationChunk(chunkBounds, parent, n.center - spineAxis * (n.size.x * 0.18f), new Vector2(n.size.x * 0.18f, n.size.y * 0.22f), RandRange(rng, -24f, 24f));
        }

        private void BuildCoastalBendShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            AddLocationChunk(chunkBounds, parent, n.center, new Vector2(n.size.x * 0.72f, n.size.y * 0.42f), RandRange(rng, -18f, 18f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(n.size.x * 0.18f, n.size.y * 0.10f), new Vector2(n.size.x * 0.30f, n.size.y * 0.24f), RandRange(rng, -18f, 18f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(-n.size.x * 0.16f, -n.size.y * 0.14f), new Vector2(n.size.x * 0.24f, n.size.y * 0.22f), RandRange(rng, -18f, 18f));
        }

        private void BuildCoastalSpitShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            AddLocationChunk(chunkBounds, parent, n.center, new Vector2(n.size.x * 0.64f, n.size.y * 0.30f), RandRange(rng, -10f, 10f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(n.size.x * 0.26f, 0f), new Vector2(n.size.x * 0.22f, n.size.y * 0.16f), RandRange(rng, -10f, 10f));
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(-n.size.x * 0.22f, 0f), new Vector2(n.size.x * 0.18f, n.size.y * 0.14f), RandRange(rng, -10f, 10f));
        }

        private void BuildHallClusterShape(List<Bounds> chunkBounds, Transform parent, Node n, System.Random rng)
        {
            AddLocationChunk(chunkBounds, parent, n.center, n.size * 0.52f, 0f, 0.06f);
            AddLocationChunk(chunkBounds, parent, n.center + new Vector2(n.size.x * 0.18f, 0f), new Vector2(n.size.x * 0.20f, n.size.y * 0.22f), 0f, 0.06f);
            AddLocationChunk(chunkBounds, parent, n.center - new Vector2(n.size.x * 0.18f, 0f), new Vector2(n.size.x * 0.20f, n.size.y * 0.22f), 0f, 0.06f);
        }

        private void AddLocationChunk(List<Bounds> chunkBounds, Transform parent, Vector2 center, Vector2 size, float rotationDeg = 0f, float yOffset = 0f)
        {
            var chunk = CreateGroundPatchObject("LocationChunk", parent, center, size, ResolveGroundMaterial(), ResolveGroundTint());
            chunk.transform.localRotation = Quaternion.Euler(0f, rotationDeg, 0f);
            if (Mathf.Abs(yOffset) > 0.001f)
                chunk.transform.localPosition += Vector3.up * yOffset;

            chunkBounds.Add(new Bounds(chunk.transform.position, new Vector3(size.x, trailThickness, size.y)));
        }

        private void BuildSoftBorder(Transform geoRoot, Bounds locationBounds, System.Random rng)
        {
            int borderPieces = _activeLayoutStyle switch
            {
                Project.WorldGen.BiomeLayoutStyle.OpenFields => 8,
                Project.WorldGen.BiomeLayoutStyle.ForestPaths => 14,
                Project.WorldGen.BiomeLayoutStyle.MountainPass => 12,
                Project.WorldGen.BiomeLayoutStyle.CoastalRoute => 10,
                Project.WorldGen.BiomeLayoutStyle.DungeonHalls => 8,
                _ => 8
            };

            float hx = locationBounds.extents.x;
            float hz = locationBounds.extents.z;
            Vector3 center = locationBounds.center;

            for (int i = 0; i < borderPieces; i++)
            {
                float t = i / (float)borderPieces;
                float angle = t * Mathf.PI * 2f;
                Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float radiusX = hx + RandRange(rng, 0.8f, 2.2f);
                float radiusZ = hz + RandRange(rng, 0.8f, 2.2f);
                Vector3 pos = center + new Vector3(dir.x * radiusX, 0.6f, dir.y * radiusZ);

                var rock = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                rock.name = "Rock";
                rock.transform.SetParent(geoRoot, false);
                rock.transform.position = pos;
                rock.transform.localRotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg + RandRange(rng, -18f, 18f), 0f);

                Vector3 scale = _activeLayoutStyle switch
                {
                    Project.WorldGen.BiomeLayoutStyle.OpenFields => new Vector3(RandRange(rng, 1.2f, 1.8f), RandRange(rng, 0.7f, 1.1f), RandRange(rng, 1.2f, 1.8f)),
                    Project.WorldGen.BiomeLayoutStyle.ForestPaths => new Vector3(RandRange(rng, 1.4f, 2.1f), RandRange(rng, 1.2f, 2.4f), RandRange(rng, 1.4f, 2.1f)),
                    Project.WorldGen.BiomeLayoutStyle.MountainPass => new Vector3(RandRange(rng, 1.8f, 2.8f), RandRange(rng, 1.4f, 2.8f), RandRange(rng, 1.8f, 2.8f)),
                    Project.WorldGen.BiomeLayoutStyle.CoastalRoute => new Vector3(RandRange(rng, 1.3f, 2.0f), RandRange(rng, 0.9f, 1.5f), RandRange(rng, 1.3f, 2.0f)),
                    Project.WorldGen.BiomeLayoutStyle.DungeonHalls => new Vector3(RandRange(rng, 1.0f, 1.4f), RandRange(rng, 1.4f, 2.2f), RandRange(rng, 1.0f, 1.4f)),
                    _ => new Vector3(1.6f, 1.2f, 1.6f)
                };
                rock.transform.localScale = scale;

                var rr = rock.GetComponent<Renderer>();
                ApplySurfaceStyle(rr, ResolveDecorMaterial(), ResolveDecorTint());
            }
        }
        private void BuildTrail(int seed, Transform segmentRoot, Transform geoRoot, System.Random rng, Vector2 a, Vector2 b)
        {
            Vector2 dir = (b - a);
            float dist = dir.magnitude;
            if (dist < 0.001f) return;
            dir /= dist;

            int tiles = Mathf.CeilToInt(dist / trailTileLength);
            Vector2 perp = new Vector2(-dir.y, dir.x);

            for (int i = 0; i <= tiles; i++)
            {
                float t = (tiles == 0) ? 0f : (i / (float)tiles);
                Vector2 p = Vector2.Lerp(a, b, t);
                float j = RandRange(rng, -trailJitter, trailJitter);
                Vector2 pj = p + perp * j;

                float length = trailTileLength * 1.15f;
                CreateTrailPatchObject(
                    "TrailTile",
                    geoRoot,
                    pj,
                    dir,
                    trailWidth,
                    length,
                    ResolveTrailMaterial(),
                    ResolveTrailTint(),
                    rng);

                int decorCount = rng.Next(decorPerTrailTileMin, decorPerTrailTileMax + 1);
                for (int d = 0; d < decorCount; d++)
                {
                    float side = (rng.Next(0, 2) == 0) ? -1f : 1f;
                    float off = RandRange(rng, decorOffsetFromCenterMin, decorOffsetFromCenterMax);
                    Vector2 dp = pj + perp * side * off;

                    var deco = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    deco.name = "TrailDecor";
                    deco.transform.SetParent(geoRoot, false);
                    deco.transform.localPosition = new Vector3(dp.x, 0.6f, dp.y);
                    deco.transform.localScale = new Vector3(0.7f, 0.6f, 0.7f);

                    var dr = deco.GetComponent<Renderer>();
                    ApplySurfaceStyle(dr, ResolveDecorMaterial(), ResolveDecorTint());
                }
            }
        }

        private GameObject CreateGroundPatchObject(string name, Transform parent, Vector2 center, Vector2 size, Material materialOverride, Color tint)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(center.x, groundY, center.y);

            var mesh = BuildIrregularPatchMesh(size, locationEdgeNoise, locationPatchSides);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            ApplySurfaceStyle(renderer, materialOverride, tint);

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;

            return go;
        }

        private void CreateTrailPatchObject(string name, Transform parent, Vector2 center, Vector2 direction, float width, float length, Material materialOverride, Color tint, System.Random rng)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(center.x, groundY + 0.05f, center.y);

            var mesh = BuildTrailPatchMesh(direction, width, length, rng);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            ApplySurfaceStyle(renderer, materialOverride, tint);

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }

        private Mesh BuildIrregularPatchMesh(Vector2 size, float edgeNoise, int sides)
        {
            int ringCount = Mathf.Max(5, sides);
            var vertices = new Vector3[ringCount + 1];
            var triangles = new int[ringCount * 3];
            var uvs = new Vector2[vertices.Length];

            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);

            float halfX = size.x * 0.5f;
            float halfZ = size.y * 0.5f;
            for (int i = 0; i < ringCount; i++)
            {
                float t = i / (float)ringCount;
                float angle = t * Mathf.PI * 2f;
                float noise = 1f + Mathf.Sin(angle * 2f) * edgeNoise * 0.45f + Mathf.Cos(angle * 3f) * edgeNoise * 0.35f;
                float x = Mathf.Cos(angle) * halfX * noise;
                float z = Mathf.Sin(angle) * halfZ * noise;
                vertices[i + 1] = new Vector3(x, 0f, z);
                uvs[i + 1] = new Vector2((x / Mathf.Max(0.01f, size.x)) + 0.5f, (z / Mathf.Max(0.01f, size.y)) + 0.5f);
            }

            for (int i = 0; i < ringCount; i++)
            {
                int tri = i * 3;
                triangles[tri] = 0;
                triangles[tri + 1] = i + 1;
                triangles[tri + 2] = (i == ringCount - 1) ? 1 : i + 2;
            }

            var mesh = new Mesh { name = "IrregularPatch" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh BuildTrailPatchMesh(Vector2 direction, float width, float length, System.Random rng)
        {
            Vector2 dir = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.up;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            float halfWidth = width * 0.5f;
            float halfLength = length * 0.5f;
            float widthNoise = width * trailEdgeNoise;
            float aNoise = RandRange(rng, -widthNoise, widthNoise);
            float bNoise = RandRange(rng, -widthNoise, widthNoise);

            Vector3 v0 = new Vector3((-dir.x * halfLength) + (perp.x * (halfWidth + aNoise)), 0f, (-dir.y * halfLength) + (perp.y * (halfWidth + aNoise)));
            Vector3 v1 = new Vector3((-dir.x * halfLength) - (perp.x * (halfWidth - aNoise)), 0f, (-dir.y * halfLength) - (perp.y * (halfWidth - aNoise)));
            Vector3 v2 = new Vector3((dir.x * halfLength) + (perp.x * (halfWidth + bNoise)), 0f, (dir.y * halfLength) + (perp.y * (halfWidth + bNoise)));
            Vector3 v3 = new Vector3((dir.x * halfLength) - (perp.x * (halfWidth - bNoise)), 0f, (dir.y * halfLength) - (perp.y * (halfWidth - bNoise)));

            var mesh = new Mesh { name = "TrailPatch" };
            mesh.vertices = new[] { v0, v1, v2, v3 };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f)
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
        private Material ResolveGroundMaterial()
        {
            return _activeBiomeConfig != null && _activeBiomeConfig.groundMaterial != null
                ? _activeBiomeConfig.groundMaterial
                : groundMaterial;
        }

        private Material ResolveTrailMaterial()
        {
            return _activeBiomeConfig != null && _activeBiomeConfig.trailMaterial != null
                ? _activeBiomeConfig.trailMaterial
                : trailMaterial;
        }

        private Material ResolveDecorMaterial()
        {
            return _activeBiomeConfig != null && _activeBiomeConfig.decorMaterial != null
                ? _activeBiomeConfig.decorMaterial
                : decorMaterial;
        }

        private Color ResolveGroundTint()
        {
            return _activeBiomeConfig != null ? _activeBiomeConfig.groundTint : Color.white;
        }

        private Color ResolveTrailTint()
        {
            return _activeBiomeConfig != null ? _activeBiomeConfig.trailTint : Color.white;
        }

        private Color ResolveDecorTint()
        {
            return _activeBiomeConfig != null ? _activeBiomeConfig.decorTint : Color.white;
        }

        private void ApplySurfaceStyle(Renderer renderer, Material materialOverride, Color tint)
        {
            if (renderer == null)
                return;

            if (materialOverride != null)
                renderer.sharedMaterial = materialOverride;

            var propertyBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, tint);
            propertyBlock.SetColor(ColorId, tint);
            renderer.SetPropertyBlock(propertyBlock);
        }

        private void CaptureBaseProfileIfNeeded()
        {
            if (_baseProfileCaptured)
                return;

            _baseProfile = new LayoutProfile
            {
                MainPathMin = mainPathMin,
                MainPathMax = mainPathMax,
                StepDistanceMin = stepDistanceMin,
                StepDistanceMax = stepDistanceMax,
                TurnAngleMaxDeg = turnAngleMaxDeg,
                LocationSizeMin = locationSizeMin,
                LocationSizeMax = locationSizeMax,
                TrailWidth = trailWidth,
                TrailTileLength = trailTileLength,
                TrailThickness = trailThickness,
                TrailJitter = trailJitter,
                DecorPerTrailTileMin = decorPerTrailTileMin,
                DecorPerTrailTileMax = decorPerTrailTileMax,
                DecorOffsetFromCenterMin = decorOffsetFromCenterMin,
                DecorOffsetFromCenterMax = decorOffsetFromCenterMax
            };

            _baseProfileCaptured = true;
        }

        private void ApplyLayoutProfile(Project.WorldGen.BiomeConfig biomeConfig)
        {
            mainPathMin = _baseProfile.MainPathMin;
            mainPathMax = _baseProfile.MainPathMax;
            stepDistanceMin = _baseProfile.StepDistanceMin;
            stepDistanceMax = _baseProfile.StepDistanceMax;
            turnAngleMaxDeg = _baseProfile.TurnAngleMaxDeg;
            locationSizeMin = _baseProfile.LocationSizeMin;
            locationSizeMax = _baseProfile.LocationSizeMax;
            trailWidth = _baseProfile.TrailWidth;
            trailTileLength = _baseProfile.TrailTileLength;
            trailThickness = _baseProfile.TrailThickness;
            trailJitter = _baseProfile.TrailJitter;
            decorPerTrailTileMin = _baseProfile.DecorPerTrailTileMin;
            decorPerTrailTileMax = _baseProfile.DecorPerTrailTileMax;
            decorOffsetFromCenterMin = _baseProfile.DecorOffsetFromCenterMin;
            decorOffsetFromCenterMax = _baseProfile.DecorOffsetFromCenterMax;

            _activeLayoutStyle = biomeConfig != null ? biomeConfig.layoutStyle : Project.WorldGen.BiomeLayoutStyle.OpenFields;

            switch (_activeLayoutStyle)
            {
                case Project.WorldGen.BiomeLayoutStyle.OpenFields:
                    mainPathMin = 4;
                    mainPathMax = 6;
                    stepDistanceMin = 26f;
                    stepDistanceMax = 36f;
                    turnAngleMaxDeg = 20f;
                    locationSizeMin = new Vector2(24f, 22f);
                    locationSizeMax = new Vector2(36f, 30f);
                    trailWidth = 10f;
                    trailTileLength = 7.5f;
                    trailJitter = 0.45f;
                    decorPerTrailTileMin = 0;
                    decorPerTrailTileMax = 1;
                    decorOffsetFromCenterMin = 5.5f;
                    decorOffsetFromCenterMax = 8f;
                    break;

                case Project.WorldGen.BiomeLayoutStyle.ForestPaths:
                    mainPathMin = 6;
                    mainPathMax = 8;
                    stepDistanceMin = 18f;
                    stepDistanceMax = 26f;
                    turnAngleMaxDeg = 45f;
                    locationSizeMin = new Vector2(16f, 16f);
                    locationSizeMax = new Vector2(24f, 22f);
                    trailWidth = 5.5f;
                    trailTileLength = 5f;
                    trailJitter = 1.8f;
                    decorPerTrailTileMin = 2;
                    decorPerTrailTileMax = 4;
                    decorOffsetFromCenterMin = 2.6f;
                    decorOffsetFromCenterMax = 5f;
                    break;

                case Project.WorldGen.BiomeLayoutStyle.MountainPass:
                    mainPathMin = 5;
                    mainPathMax = 7;
                    stepDistanceMin = 20f;
                    stepDistanceMax = 28f;
                    turnAngleMaxDeg = 30f;
                    locationSizeMin = new Vector2(16f, 14f);
                    locationSizeMax = new Vector2(22f, 20f);
                    trailWidth = 5f;
                    trailTileLength = 5.5f;
                    trailJitter = 0.9f;
                    decorPerTrailTileMin = 1;
                    decorPerTrailTileMax = 3;
                    decorOffsetFromCenterMin = 3.5f;
                    decorOffsetFromCenterMax = 6.5f;
                    break;

                case Project.WorldGen.BiomeLayoutStyle.CoastalRoute:
                    mainPathMin = 5;
                    mainPathMax = 7;
                    stepDistanceMin = 24f;
                    stepDistanceMax = 34f;
                    turnAngleMaxDeg = 28f;
                    locationSizeMin = new Vector2(20f, 18f);
                    locationSizeMax = new Vector2(30f, 24f);
                    trailWidth = 7f;
                    trailTileLength = 6.5f;
                    trailJitter = 0.8f;
                    decorPerTrailTileMin = 1;
                    decorPerTrailTileMax = 2;
                    decorOffsetFromCenterMin = 4.5f;
                    decorOffsetFromCenterMax = 7f;
                    break;

                case Project.WorldGen.BiomeLayoutStyle.DungeonHalls:
                    mainPathMin = 5;
                    mainPathMax = 6;
                    stepDistanceMin = 16f;
                    stepDistanceMax = 22f;
                    turnAngleMaxDeg = 18f;
                    locationSizeMin = new Vector2(14f, 14f);
                    locationSizeMax = new Vector2(20f, 18f);
                    trailWidth = 4.5f;
                    trailTileLength = 4.5f;
                    trailJitter = 0.2f;
                    decorPerTrailTileMin = 0;
                    decorPerTrailTileMax = 1;
                    decorOffsetFromCenterMin = 2f;
                    decorOffsetFromCenterMax = 3.2f;
                    break;
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

        private static Vector2 RandomInsideUnitCircle(System.Random rng)
        {
            float angle = RandRange(rng, 0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(RandRange(rng, 0f, 1f));
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
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



