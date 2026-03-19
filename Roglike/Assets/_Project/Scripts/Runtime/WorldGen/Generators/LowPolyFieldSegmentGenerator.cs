using System.Collections.Generic;
using UnityEngine;

namespace Project.WorldGen.Generators
{
    public sealed class LowPolyFieldSegmentGenerator : SegmentGeneratorBase
    {
        private struct AreaNode
        {
            public int RoomId;
            public Vector2 Center;
            public Vector2 Size;
            public bool IsStart;
            public bool IsExit;
            public bool IsSide;
            public int AnchorRoomId;
        }

        private struct PathLink
        {
            public int A;
            public int B;
        }

        [Header("Terrain")]
        [SerializeField] private float terrainWidth = 180f;
        [SerializeField] private float terrainDepth = 140f;
        [SerializeField, Range(20, 120)] private int terrainResolutionX = 54;
        [SerializeField, Range(20, 120)] private int terrainResolutionZ = 42;
        [SerializeField] private float terrainHeight = 7f;
        [SerializeField] private float terrainNoiseScale = 0.04f;
        [SerializeField] private float secondaryNoiseScale = 0.09f;
        [SerializeField] private float secondaryNoiseWeight = 0.35f;
        [SerializeField] private float broadHillScale = 0.015f;
        [SerializeField] private float broadHillWeight = 0.55f;
        [SerializeField] private float terrainYOffset = 0f;

        [Header("Route")]
        [SerializeField] private int mainAreaMin = 4;
        [SerializeField] private int mainAreaMax = 6;
        [SerializeField] private int sideAreaMin = 1;
        [SerializeField] private int sideAreaMax = 2;
        [SerializeField] private float mainStepMin = 24f;
        [SerializeField] private float mainStepMax = 34f;
        [SerializeField] private float sideOffsetMin = 16f;
        [SerializeField] private float sideOffsetMax = 28f;
        [SerializeField] private float routeTurnMaxDeg = 22f;
        [SerializeField] private Vector2 areaSizeMin = new Vector2(18f, 16f);
        [SerializeField] private Vector2 areaSizeMax = new Vector2(28f, 24f);

        [Header("Safe Zone")]
        [SerializeField] private float safeZoneEdgeInset = 20f;
        [SerializeField] private float firstCombatGapMin = 34f;
        [SerializeField] private float firstCombatGapMax = 44f;
        [SerializeField] private Vector2 safeAreaBonusSize = new Vector2(8f, 6f);
        [SerializeField] private bool buildSafeZoneShell = true;
        [SerializeField] private float safeShellHeight = 6f;
        [SerializeField] private float safeShellThickness = 4f;

        [Header("Flattening")]
        [SerializeField] private float pathFlattenRadius = 8f;
        [SerializeField] private float pathFlattenStrength = 0.92f;
        [SerializeField] private float areaFlattenPadding = 6f;
        [SerializeField] private float areaFlattenStrength = 0.97f;
        [SerializeField] private float pathVisualWidth = 7f;
        [SerializeField] private float pathVisualHeightOffset = 0.08f;

        [Header("Visuals")]
        [SerializeField] private Material terrainMaterial;
        [SerializeField] private Material pathMaterial;
        [SerializeField] private Material structureMaterial;
        [SerializeField] private Material foliageMaterial;
        [SerializeField] private Color terrainTint = new Color(0.47f, 0.68f, 0.39f, 1f);
        [SerializeField] private Color pathTint = new Color(0.72f, 0.64f, 0.48f, 1f);
        [SerializeField] private Color structureTint = new Color(0.73f, 0.64f, 0.53f, 1f);
        [SerializeField] private Color foliageTint = new Color(0.33f, 0.58f, 0.26f, 1f);

        [Header("Boundary Shell")]
        [SerializeField] private bool buildBoundaryShell = true;
        [SerializeField] private float boundaryInset = 8f;
        [SerializeField] private float boundaryHeightMin = 7f;
        [SerializeField] private float boundaryHeightMax = 13f;
        [SerializeField] private float boundaryWidthMin = 8f;
        [SerializeField] private float boundaryWidthMax = 14f;
        [SerializeField] private int boundaryPiecesPerSide = 8;
        [SerializeField] private float boundaryPieceOverlap = 1.45f;
        [SerializeField] private int boundaryRows = 2;
        [SerializeField] private float boundaryRowDepthStep = 6f;

        [Header("Structures And Props")]
        [SerializeField] private GameObject[] structurePrefabs;
        [SerializeField] private GameObject[] landmarkPrefabs;
        [SerializeField] private GameObject[] ambientPropPrefabs;
        [SerializeField] private int ambientPropCountMin = 16;
        [SerializeField] private int ambientPropCountMax = 28;
        [SerializeField] private float structureChancePerArea = 0.55f;
        [SerializeField] private float landmarkChancePerSegment = 0.6f;
        [SerializeField] private bool generateFallbackStructures = true;
        [SerializeField] private bool generateFallbackTrees = true;

        [Header("Enemy Camps")]
        [SerializeField] private bool buildEnemyCampShells = true;
        [SerializeField] private float campShellHeight = 2.4f;
        [SerializeField] private float campShellThickness = 0.8f;
        [SerializeField] private float campShellCoverage = 0.65f;
        [SerializeField] private float campShellInset = 1.6f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private float[,] _heightMap;
        private float _minX;
        private float _maxX;
        private float _minZ;
        private float _maxZ;
        private Material _runtimeTerrainFallback;
        private Material _runtimePathFallback;
        private Material _runtimeStructureFallback;
        private Material _runtimeFoliageFallback;

        public override float GetRecommendedSegmentOffset() => terrainWidth + 50f;

        public override void GenerateInto(int seed, int segmentIndex, Transform segmentRoot, Project.WorldGen.BiomeConfig biomeConfig)
        {
            var rng = new System.Random(seed);

            InitializeTerrainBounds();

            var markersGo = new GameObject("SegmentMarkers");
            markersGo.transform.SetParent(segmentRoot, false);
            var markers = markersGo.AddComponent<Project.WorldGen.SegmentMarkers>();

            var terrainRoot = new GameObject("Terrain");
            terrainRoot.transform.SetParent(segmentRoot, false);

            var pathRoot = new GameObject("Paths");
            pathRoot.transform.SetParent(segmentRoot, false);

            var propsRoot = new GameObject("Props");
            propsRoot.transform.SetParent(segmentRoot, false);

            var triggerRoot = new GameObject("RoomTriggers");
            triggerRoot.transform.SetParent(segmentRoot, false);

            var layout = segmentRoot.GetComponent<Project.WorldGen.SegmentLayout>();
            if (layout == null)
                layout = segmentRoot.gameObject.AddComponent<Project.WorldGen.SegmentLayout>();

            layout.GeneratedSeed = seed;
            layout.SegmentIndex = segmentIndex;
            layout.BiomeType = biomeConfig != null ? biomeConfig.biomeType : Project.WorldGen.BiomeService.GetBiomeForSegment(segmentIndex);
            layout.BiomeDisplayName = biomeConfig != null ? biomeConfig.GetResolvedDisplayName() : Project.WorldGen.BiomeService.GetDisplayName(layout.BiomeType);
            layout.Rooms.Clear();

            var mainAreas = BuildMainAreas(rng);
            var sideAreas = BuildSideAreas(rng, mainAreas);
            var allAreas = new List<AreaNode>(mainAreas.Count + sideAreas.Count);
            allAreas.AddRange(mainAreas);
            allAreas.AddRange(sideAreas);

            var links = BuildLinks(mainAreas, sideAreas);
            _heightMap = BuildHeightMap(seed, allAreas, links);

            BuildTerrainMesh(terrainRoot.transform, biomeConfig);
            BuildPathMeshes(pathRoot.transform, links, allAreas, biomeConfig);
            BuildBoundaryShell(propsRoot.transform, rng, biomeConfig);
            BuildSafeZoneShell(propsRoot.transform, mainAreas[0], biomeConfig);
            BuildEnemyCampShells(propsRoot.transform, allAreas, rng, biomeConfig);
            BuildRoomsAndMarkers(segmentIndex, segmentRoot, triggerRoot.transform, markers, layout, allAreas);
            PopulateStructuresAndProps(propsRoot.transform, rng, allAreas, biomeConfig);

            layout.SafeRoomId = 0;
            layout.ExitRoomId = Mathf.Max(0, mainAreas.Count - 1);
            markers.SafeRoomId = layout.SafeRoomId;
            markers.ExitRoomId = layout.ExitRoomId;

            Debug.Log($"[LowPolyFieldSegmentGenerator] Generated seg={segmentIndex} areas={allAreas.Count} main={mainAreas.Count} side={sideAreas.Count}");
        }

        private void InitializeTerrainBounds()
        {
            _minX = -terrainWidth * 0.5f;
            _maxX = terrainWidth * 0.5f;
            _minZ = -terrainDepth * 0.5f;
            _maxZ = terrainDepth * 0.5f;
        }

        private List<AreaNode> BuildMainAreas(System.Random rng)
        {
            int count = rng.Next(mainAreaMin, mainAreaMax + 1);
            var nodes = new List<AreaNode>(count);

            Vector2 pos = new Vector2(_minX + safeZoneEdgeInset, RandRange(rng, -terrainDepth * 0.10f, terrainDepth * 0.10f));
            Vector2 dir = new Vector2(1f, RandRange(rng, -0.15f, 0.15f)).normalized;

            for (int i = 0; i < count; i++)
            {
                Vector2 size = RandVec2(rng, areaSizeMin, areaSizeMax);
                if (i == 0)
                    size += safeAreaBonusSize;

                nodes.Add(new AreaNode
                {
                    RoomId = i,
                    Center = ClampInsideTerrain(pos),
                    Size = size,
                    IsStart = i == 0,
                    IsExit = i == count - 1,
                    IsSide = false,
                    AnchorRoomId = -1
                });

                if (i == count - 1)
                    break;

                float step = i == 0
                    ? RandRange(rng, firstCombatGapMin, firstCombatGapMax)
                    : RandRange(rng, mainStepMin, mainStepMax);
                dir = Rotate(dir, RandRange(rng, -routeTurnMaxDeg, routeTurnMaxDeg));
                dir.x = Mathf.Abs(dir.x);
                dir = dir.normalized;
                pos += dir * step;
            }

            return nodes;
        }

        private List<AreaNode> BuildSideAreas(System.Random rng, List<AreaNode> mainAreas)
        {
            int count = rng.Next(sideAreaMin, sideAreaMax + 1);
            var nodes = new List<AreaNode>(count);
            int roomId = mainAreas.Count;

            for (int i = 0; i < count; i++)
            {
                int anchorIndex = rng.Next(1, Mathf.Max(2, mainAreas.Count - 1));
                anchorIndex = Mathf.Clamp(anchorIndex, 1, mainAreas.Count - 2);
                var anchor = mainAreas[anchorIndex];
                Vector2 along = anchorIndex < mainAreas.Count - 1
                    ? (mainAreas[anchorIndex + 1].Center - anchor.Center).normalized
                    : Vector2.right;
                if (along.sqrMagnitude < 0.01f)
                    along = Vector2.right;

                Vector2 perp = new Vector2(-along.y, along.x);
                float offset = RandRange(rng, sideOffsetMin, sideOffsetMax) * (rng.Next(0, 2) == 0 ? -1f : 1f);
                Vector2 center = anchor.Center + perp * offset + new Vector2(RandRange(rng, -4f, 4f), RandRange(rng, -4f, 4f));

                nodes.Add(new AreaNode
                {
                    RoomId = roomId++,
                    Center = ClampInsideTerrain(center),
                    Size = RandVec2(rng, areaSizeMin * 0.72f, areaSizeMax * 0.78f),
                    IsStart = false,
                    IsExit = false,
                    IsSide = true,
                    AnchorRoomId = anchor.RoomId
                });
            }

            return nodes;
        }

        private List<PathLink> BuildLinks(List<AreaNode> mainAreas, List<AreaNode> sideAreas)
        {
            var links = new List<PathLink>(mainAreas.Count + sideAreas.Count);
            for (int i = 0; i < mainAreas.Count - 1; i++)
                links.Add(new PathLink { A = mainAreas[i].RoomId, B = mainAreas[i + 1].RoomId });

            for (int i = 0; i < sideAreas.Count; i++)
                links.Add(new PathLink { A = sideAreas[i].AnchorRoomId, B = sideAreas[i].RoomId });

            return links;
        }

        private float[,] BuildHeightMap(int seed, List<AreaNode> areas, List<PathLink> links)
        {
            int vx = terrainResolutionX + 1;
            int vz = terrainResolutionZ + 1;
            var map = new float[vx, vz];

            float seedOffsetX = (seed & 1023) * 0.071f;
            float seedOffsetZ = ((seed >> 10) & 1023) * 0.053f;

            for (int z = 0; z < vz; z++)
            {
                float tz = z / (float)terrainResolutionZ;
                float worldZ = Mathf.Lerp(_minZ, _maxZ, tz);

                for (int x = 0; x < vx; x++)
                {
                    float tx = x / (float)terrainResolutionX;
                    float worldX = Mathf.Lerp(_minX, _maxX, tx);

                    float h = SampleBaseNoise(worldX, worldZ, seedOffsetX, seedOffsetZ);
                    h = FlattenByAreas(worldX, worldZ, h, areas);
                    h = FlattenByPaths(worldX, worldZ, h, areas, links);
                    map[x, z] = h + terrainYOffset;
                }
            }

            return map;
        }

        private float SampleBaseNoise(float x, float z, float seedOffsetX, float seedOffsetZ)
        {
            float n1 = Mathf.PerlinNoise((x + seedOffsetX) * terrainNoiseScale, (z + seedOffsetZ) * terrainNoiseScale);
            float n2 = Mathf.PerlinNoise((x + seedOffsetX + 37.13f) * secondaryNoiseScale, (z + seedOffsetZ + 19.27f) * secondaryNoiseScale);
            float n3 = Mathf.PerlinNoise((x + seedOffsetX + 71.49f) * broadHillScale, (z + seedOffsetZ + 11.74f) * broadHillScale);

            float noise = n1;
            noise += (n2 - 0.5f) * secondaryNoiseWeight;
            noise += (n3 - 0.5f) * broadHillWeight;
            noise = Mathf.Clamp01(noise);

            return (noise - 0.45f) * terrainHeight;
        }

        private float FlattenByAreas(float worldX, float worldZ, float height, List<AreaNode> areas)
        {
            Vector2 p = new Vector2(worldX, worldZ);
            for (int i = 0; i < areas.Count; i++)
            {
                float radius = Mathf.Max(areas[i].Size.x, areas[i].Size.y) * 0.5f + areaFlattenPadding;
                float dist = Vector2.Distance(p, areas[i].Center);
                if (dist > radius)
                    continue;

                float t = 1f - Mathf.Clamp01(dist / radius);
                float localTarget = Mathf.Lerp(height, 0f, areaFlattenStrength);
                height = Mathf.Lerp(height, localTarget, t);
            }

            return height;
        }

        private float FlattenByPaths(float worldX, float worldZ, float height, List<AreaNode> areas, List<PathLink> links)
        {
            Vector2 p = new Vector2(worldX, worldZ);
            float bestDist = float.MaxValue;

            for (int i = 0; i < links.Count; i++)
            {
                Vector2 a = FindAreaCenter(areas, links[i].A);
                Vector2 b = FindAreaCenter(areas, links[i].B);
                float dist = DistanceToSegment(p, a, b);
                if (dist < bestDist)
                    bestDist = dist;
            }

            if (bestDist < pathFlattenRadius)
            {
                float t = 1f - Mathf.Clamp01(bestDist / pathFlattenRadius);
                float localTarget = Mathf.Lerp(height, -0.1f, pathFlattenStrength);
                height = Mathf.Lerp(height, localTarget, t);
            }

            return height;
        }

        private void BuildTerrainMesh(Transform parent, Project.WorldGen.BiomeConfig biomeConfig)
        {
            int vx = terrainResolutionX + 1;
            int vz = terrainResolutionZ + 1;

            var vertices = new Vector3[vx * vz];
            var uvs = new Vector2[vx * vz];
            var triangles = new int[terrainResolutionX * terrainResolutionZ * 6];

            int vi = 0;
            for (int z = 0; z < vz; z++)
            {
                float tz = z / (float)terrainResolutionZ;
                float worldZ = Mathf.Lerp(_minZ, _maxZ, tz);

                for (int x = 0; x < vx; x++)
                {
                    float tx = x / (float)terrainResolutionX;
                    float worldX = Mathf.Lerp(_minX, _maxX, tx);
                    vertices[vi] = new Vector3(worldX, _heightMap[x, z], worldZ);
                    uvs[vi] = new Vector2(tx, tz);
                    vi++;
                }
            }

            int ti = 0;
            for (int z = 0; z < terrainResolutionZ; z++)
            {
                for (int x = 0; x < terrainResolutionX; x++)
                {
                    int i0 = z * vx + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + vx;
                    int i3 = i2 + 1;

                    triangles[ti++] = i0;
                    triangles[ti++] = i2;
                    triangles[ti++] = i1;
                    triangles[ti++] = i1;
                    triangles[ti++] = i2;
                    triangles[ti++] = i3;
                }
            }

            var mesh = new Mesh { name = "LowPolyFieldTerrain" };
            mesh.indexFormat = vertices.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uvs;
            mesh.RecalculateBounds();
            mesh = CreateFlatShaded(mesh);

            var terrainGo = new GameObject("TerrainMesh");
            terrainGo.transform.SetParent(parent, false);

            var filter = terrainGo.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = terrainGo.AddComponent<MeshRenderer>();
            ApplySurfaceStyle(renderer, biomeConfig != null && biomeConfig.groundMaterial != null ? biomeConfig.groundMaterial : terrainMaterial, biomeConfig != null ? biomeConfig.groundTint : terrainTint);

            var collider = terrainGo.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
        }

        private void BuildPathMeshes(Transform parent, List<PathLink> links, List<AreaNode> areas, Project.WorldGen.BiomeConfig biomeConfig)
        {
            for (int i = 0; i < links.Count; i++)
            {
                Vector2 a = FindAreaCenter(areas, links[i].A);
                Vector2 b = FindAreaCenter(areas, links[i].B);
                BuildPathSegment(parent, a, b, $"Path_{links[i].A}_{links[i].B}", biomeConfig);
            }
        }

        private void BuildPathSegment(Transform parent, Vector2 a, Vector2 b, string name, Project.WorldGen.BiomeConfig biomeConfig)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.01f)
                return;

            Vector2 dir = delta / length;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            int slices = Mathf.Max(4, Mathf.CeilToInt(length / 6f));

            var verts = new List<Vector3>(slices * 2);
            var tris = new List<int>((slices - 1) * 6);
            var uvs = new List<Vector2>(slices * 2);

            for (int i = 0; i <= slices; i++)
            {
                float t = i / (float)slices;
                Vector2 p = Vector2.Lerp(a, b, t);
                float widthNoise = Mathf.Sin(t * Mathf.PI) * 0.55f + Mathf.Cos(t * Mathf.PI * 2f) * 0.2f;
                float width = pathVisualWidth + widthNoise;
                Vector2 left = p - perp * (width * 0.5f);
                Vector2 right = p + perp * (width * 0.5f);

                verts.Add(new Vector3(left.x, SampleHeight(left.x, left.y) + pathVisualHeightOffset, left.y));
                verts.Add(new Vector3(right.x, SampleHeight(right.x, right.y) + pathVisualHeightOffset, right.y));
                uvs.Add(new Vector2(0f, t));
                uvs.Add(new Vector2(1f, t));

                if (i == slices)
                    continue;

                int baseIndex = i * 2;
                tris.Add(baseIndex);
                tris.Add(baseIndex + 2);
                tris.Add(baseIndex + 1);
                tris.Add(baseIndex + 1);
                tris.Add(baseIndex + 2);
                tris.Add(baseIndex + 3);
            }

            var mesh = new Mesh { name = $"{name}_Mesh" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
            mesh = CreateFlatShaded(mesh);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            ApplySurfaceStyle(renderer, biomeConfig != null && biomeConfig.trailMaterial != null ? biomeConfig.trailMaterial : pathMaterial, biomeConfig != null ? biomeConfig.trailTint : pathTint);
        }

        private void BuildBoundaryShell(Transform parent, System.Random rng, Project.WorldGen.BiomeConfig biomeConfig)
        {
            if (!buildBoundaryShell)
                return;

            var shellRoot = new GameObject("BoundaryShell");
            shellRoot.transform.SetParent(parent, false);

            BuildBoundarySide(shellRoot.transform, rng, biomeConfig, true, 1f);
            BuildBoundarySide(shellRoot.transform, rng, biomeConfig, true, -1f);
            BuildBoundarySide(shellRoot.transform, rng, biomeConfig, false, 1f);
            BuildBoundarySide(shellRoot.transform, rng, biomeConfig, false, -1f);
        }

        private void BuildBoundarySide(Transform parent, System.Random rng, Project.WorldGen.BiomeConfig biomeConfig, bool alongX, float sideSign)
        {
            int pieces = Mathf.Max(3, boundaryPiecesPerSide);
            float span = alongX ? terrainWidth : terrainDepth;
            float min = alongX ? _minX : _minZ;
            float max = alongX ? _maxX : _maxZ;
            float baseCoord = alongX
                ? (sideSign > 0f ? _maxZ - boundaryInset : _minZ + boundaryInset)
                : (sideSign > 0f ? _maxX - boundaryInset : _minX + boundaryInset);

            for (int row = 0; row < Mathf.Max(1, boundaryRows); row++)
            {
                float rowOffset = row * boundaryRowDepthStep;
                float fixedCoord = baseCoord - sideSign * rowOffset;

                for (int i = 0; i < pieces; i++)
                {
                    float t = (i + 0.5f) / pieces;
                    float primary = Mathf.Lerp(min, max, t);
                    if (row == 0)
                        primary += RandRange(rng, -span * 0.02f, span * 0.02f);

                    float width = RandRange(rng, boundaryWidthMin, boundaryWidthMax) + row * 1.5f;
                    float height = RandRange(rng, boundaryHeightMin, boundaryHeightMax) + row * 1.8f;
                    float length = (span / pieces) * boundaryPieceOverlap;

                    Vector3 localPos;
                    Vector3 scale;
                    Vector3 rot;
                    if (alongX)
                    {
                        float y = SampleHeight(primary, fixedCoord);
                        localPos = new Vector3(primary, y + height * 0.45f, fixedCoord);
                        scale = new Vector3(length, height, width);
                        rot = new Vector3(RandRange(rng, -4f, 4f), RandRange(rng, -8f, 8f), RandRange(rng, -4f, 4f));
                    }
                    else
                    {
                        float y = SampleHeight(fixedCoord, primary);
                        localPos = new Vector3(fixedCoord, y + height * 0.45f, primary);
                        scale = new Vector3(width, height, length);
                        rot = new Vector3(RandRange(rng, -4f, 4f), RandRange(rng, -8f, 8f), RandRange(rng, -4f, 4f));
                    }

                    var ridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    ridge.name = "BoundaryRidge";
                    ridge.transform.SetParent(parent, false);
                    ridge.transform.localPosition = localPos;
                    ridge.transform.localRotation = Quaternion.Euler(rot);
                    ridge.transform.localScale = scale;
                    ApplySurfaceStyle(ridge.GetComponent<Renderer>(), structureMaterial, biomeConfig != null ? biomeConfig.decorTint : structureTint);
                }
            }
        }

        private void BuildSafeZoneShell(Transform parent, AreaNode startArea, Project.WorldGen.BiomeConfig biomeConfig)
        {
            if (!buildSafeZoneShell)
                return;

            float groundY = SampleHeight(startArea.Center.x, startArea.Center.y);
            float halfX = startArea.Size.x * 0.5f;
            float halfZ = startArea.Size.y * 0.5f;
            float leftX = startArea.Center.x - halfX - safeShellThickness * 0.5f - 1.5f;
            float backX = startArea.Center.x - halfX * 0.45f;
            float leftZ = startArea.Center.y - halfZ - safeShellThickness * 0.5f;
            float rightZ = startArea.Center.y + halfZ + safeShellThickness * 0.5f;

            var root = new GameObject("SafeZoneShell");
            root.transform.SetParent(parent, false);

            CreateShellPiece(root.transform, new Vector3(leftX, groundY + safeShellHeight * 0.45f, startArea.Center.y), new Vector3(safeShellThickness, safeShellHeight, startArea.Size.y + 8f), biomeConfig);
            CreateShellPiece(root.transform, new Vector3(backX, groundY + safeShellHeight * 0.5f, leftZ), new Vector3(startArea.Size.x * 0.9f, safeShellHeight + 1f, safeShellThickness), biomeConfig);
            CreateShellPiece(root.transform, new Vector3(backX, groundY + safeShellHeight * 0.5f, rightZ), new Vector3(startArea.Size.x * 0.9f, safeShellHeight + 1f, safeShellThickness), biomeConfig);
        }

        private void CreateShellPiece(Transform parent, Vector3 localPosition, Vector3 localScale, Project.WorldGen.BiomeConfig biomeConfig)
        {
            var piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            piece.name = "SafeZoneBarrier";
            piece.transform.SetParent(parent, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localScale = localScale;
            ApplySurfaceStyle(piece.GetComponent<Renderer>(), structureMaterial, biomeConfig != null ? biomeConfig.decorTint : structureTint);
        }

        private void BuildEnemyCampShells(Transform parent, List<AreaNode> areas, System.Random rng, Project.WorldGen.BiomeConfig biomeConfig)
        {
            if (!buildEnemyCampShells)
                return;

            var root = new GameObject("EnemyCampShells");
            root.transform.SetParent(parent, false);

            for (int i = 0; i < areas.Count; i++)
            {
                var area = areas[i];
                if (area.IsStart)
                    continue;

                BuildEnemyCampShell(root.transform, area, rng, biomeConfig);
            }
        }

        private void BuildEnemyCampShell(Transform parent, AreaNode area, System.Random rng, Project.WorldGen.BiomeConfig biomeConfig)
        {
            float halfX = area.Size.x * 0.5f - campShellInset;
            float halfZ = area.Size.y * 0.5f - campShellInset;
            float coverageX = Mathf.Max(3f, area.Size.x * campShellCoverage * 0.5f);
            float coverageZ = Mathf.Max(3f, area.Size.y * campShellCoverage * 0.5f);
            float y = SampleHeight(area.Center.x, area.Center.y);

            var camp = new GameObject($"EnemyCampShell_{area.RoomId}");
            camp.transform.SetParent(parent, false);

            CreateCampWall(camp.transform, new Vector3(area.Center.x - halfX, y + campShellHeight * 0.5f, area.Center.y), new Vector3(campShellThickness, campShellHeight, coverageZ * 2f), biomeConfig, rng);
            CreateCampWall(camp.transform, new Vector3(area.Center.x, y + campShellHeight * 0.5f, area.Center.y - halfZ), new Vector3(coverageX * 2f, campShellHeight, campShellThickness), biomeConfig, rng);

            if (rng.NextDouble() < 0.7)
                CreateCampWall(camp.transform, new Vector3(area.Center.x, y + campShellHeight * 0.5f, area.Center.y + halfZ), new Vector3(coverageX * 1.6f, campShellHeight, campShellThickness), biomeConfig, rng);
            else
                CreateCampWall(camp.transform, new Vector3(area.Center.x + halfX, y + campShellHeight * 0.5f, area.Center.y), new Vector3(campShellThickness, campShellHeight, coverageZ * 1.6f), biomeConfig, rng);
        }

        private void CreateCampWall(Transform parent, Vector3 localPosition, Vector3 localScale, Project.WorldGen.BiomeConfig biomeConfig, System.Random rng)
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "EnemyCampWall";
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = localPosition;
            wall.transform.localRotation = Quaternion.Euler(0f, RandRange(rng, -8f, 8f), 0f);
            wall.transform.localScale = localScale;
            ApplySurfaceStyle(wall.GetComponent<Renderer>(), structureMaterial, biomeConfig != null ? biomeConfig.decorTint : structureTint);
        }

        private void BuildRoomsAndMarkers(int segmentIndex, Transform segmentRoot, Transform triggerRoot, Project.WorldGen.SegmentMarkers markers, Project.WorldGen.SegmentLayout layout, List<AreaNode> areas)
        {
            for (int i = 0; i < areas.Count; i++)
            {
                var area = areas[i];
                float roomY = SampleHeight(area.Center.x, area.Center.y);
                Vector3 centerLocal = new Vector3(area.Center.x, roomY, area.Center.y);
                Vector3 centerWorld = segmentRoot.position + centerLocal;
                Vector3 size = new Vector3(area.Size.x, 0f, area.Size.y);
                Bounds bounds = new Bounds(centerWorld + Vector3.up, new Vector3(area.Size.x, 3f, area.Size.y));

                layout.Rooms.Add(new Project.WorldGen.RoomData
                {
                    RoomId = area.RoomId,
                    Center = centerWorld,
                    Size = size,
                    Bounds = bounds,
                    DoorWorld = centerWorld
                });

                var triggerGo = new GameObject($"RoomTrigger_{segmentIndex}_{area.RoomId}");
                triggerGo.transform.SetParent(triggerRoot, false);
                triggerGo.transform.position = centerWorld + Vector3.up;

                var trigger = triggerGo.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.size = new Vector3(Mathf.Max(8f, area.Size.x * 0.8f), 4f, Mathf.Max(8f, area.Size.y * 0.8f));

                var roomTrigger = triggerGo.AddComponent<Project.WorldGen.RoomTrigger>();
                roomTrigger.Init(segmentIndex, area.RoomId);

                if (area.IsStart)
                {
                    var safeGo = new GameObject("SafeSpawn");
                    safeGo.transform.SetParent(markers.transform, false);
                    safeGo.transform.localPosition = centerLocal + Vector3.up;
                    markers.SafeSpawn = safeGo.transform;
                }

                if (area.IsExit)
                {
                    var exitGo = new GameObject("ExitPoint");
                    exitGo.transform.SetParent(markers.transform, false);
                    exitGo.transform.localPosition = centerLocal + Vector3.up;
                    markers.ExitPoint = exitGo.transform;
                }
            }
        }

        private void PopulateStructuresAndProps(Transform parent, System.Random rng, List<AreaNode> areas, Project.WorldGen.BiomeConfig biomeConfig)
        {
            var structureRoot = new GameObject("Structures");
            structureRoot.transform.SetParent(parent, false);

            var propsRoot = new GameObject("AmbientProps");
            propsRoot.transform.SetParent(parent, false);

            for (int i = 0; i < areas.Count; i++)
            {
                var area = areas[i];
                if (area.IsStart || area.IsExit)
                    continue;

                if (RandRange(rng, 0f, 1f) > structureChancePerArea)
                    continue;

                Vector2 offset = RandomInsideUnitCircle(rng) * Mathf.Min(area.Size.x, area.Size.y) * 0.22f;
                Vector2 position = area.Center + offset;
                SpawnStructure(structureRoot.transform, position, rng, biomeConfig);
            }

            if (RandRange(rng, 0f, 1f) <= landmarkChancePerSegment && areas.Count > 2)
            {
                var landmarkArea = areas[rng.Next(1, areas.Count)];
                Vector2 lp = landmarkArea.Center + RandomInsideUnitCircle(rng) * Mathf.Min(landmarkArea.Size.x, landmarkArea.Size.y) * 0.18f;
                SpawnLandmark(structureRoot.transform, lp, rng, biomeConfig);
            }

            int ambientCount = rng.Next(ambientPropCountMin, ambientPropCountMax + 1);
            int attempts = ambientCount * 6;
            int spawned = 0;
            while (spawned < ambientCount && attempts-- > 0)
            {
                Vector2 p = new Vector2(RandRange(rng, _minX + 8f, _maxX - 8f), RandRange(rng, _minZ + 8f, _maxZ - 8f));
                if (IsNearArea(p, areas, 7f))
                    continue;

                SpawnAmbientProp(propsRoot.transform, p, rng, biomeConfig);
                spawned++;
            }
        }

        private void SpawnStructure(Transform parent, Vector2 position, System.Random rng, Project.WorldGen.BiomeConfig biomeConfig)
        {
            if (TrySpawnPrefab(parent, position, structurePrefabs, rng))
                return;

            if (!generateFallbackStructures)
                return;

            float y = SampleHeight(position.x, position.y);
            var root = new GameObject("FieldHouse");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(position.x, y, position.y);
            root.transform.localRotation = Quaternion.Euler(0f, RandRange(rng, 0f, 360f), 0f);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            body.transform.localScale = new Vector3(4f, 2.6f, 3.2f);
            ApplySurfaceStyle(body.GetComponent<Renderer>(), structureMaterial, biomeConfig != null ? biomeConfig.decorTint : structureTint);

            var roof = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            roof.name = "Roof";
            roof.transform.SetParent(root.transform, false);
            roof.transform.localPosition = new Vector3(0f, 3.0f, 0f);
            roof.transform.localRotation = Quaternion.Euler(90f, 45f, 0f);
            roof.transform.localScale = new Vector3(1.8f, 2.9f, 1.8f);
            ApplySurfaceStyle(roof.GetComponent<Renderer>(), structureMaterial, (biomeConfig != null ? biomeConfig.decorTint : structureTint) * 0.9f);
        }

        private void SpawnLandmark(Transform parent, Vector2 position, System.Random rng, Project.WorldGen.BiomeConfig biomeConfig)
        {
            if (TrySpawnPrefab(parent, position, landmarkPrefabs, rng))
                return;

            float y = SampleHeight(position.x, position.y);
            var root = new GameObject("StoneCircle");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(position.x, y, position.y);

            for (int i = 0; i < 6; i++)
            {
                float angle = i / 6f * Mathf.PI * 2f;
                var stone = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                stone.transform.SetParent(root.transform, false);
                stone.transform.localPosition = new Vector3(Mathf.Cos(angle) * 3.5f, 1.2f, Mathf.Sin(angle) * 3.5f);
                stone.transform.localScale = new Vector3(1.1f, RandRange(rng, 1.1f, 1.8f), 1.1f);
                ApplySurfaceStyle(stone.GetComponent<Renderer>(), structureMaterial, biomeConfig != null ? biomeConfig.decorTint : structureTint);
            }
        }

        private void SpawnAmbientProp(Transform parent, Vector2 position, System.Random rng, Project.WorldGen.BiomeConfig biomeConfig)
        {
            if (TrySpawnPrefab(parent, position, ambientPropPrefabs, rng))
                return;

            if (!generateFallbackTrees)
                return;

            float y = SampleHeight(position.x, position.y);
            var root = new GameObject("LowPolyTree");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(position.x, y, position.y);
            root.transform.localRotation = Quaternion.Euler(0f, RandRange(rng, 0f, 360f), 0f);

            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            trunk.transform.localScale = new Vector3(0.35f, 0.9f, 0.35f);
            ApplySurfaceStyle(trunk.GetComponent<Renderer>(), structureMaterial, new Color(0.46f, 0.32f, 0.22f, 1f));

            var canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            canopy.transform.SetParent(root.transform, false);
            canopy.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            canopy.transform.localScale = new Vector3(2.2f, 1.8f, 2.2f);
            ApplySurfaceStyle(canopy.GetComponent<Renderer>(), foliageMaterial, biomeConfig != null ? biomeConfig.groundTint : foliageTint);
        }

        private bool TrySpawnPrefab(Transform parent, Vector2 position, GameObject[] prefabs, System.Random rng)
        {
            if (prefabs == null || prefabs.Length == 0)
                return false;

            var prefab = prefabs[rng.Next(0, prefabs.Length)];
            if (prefab == null)
                return false;

            var instance = Object.Instantiate(prefab, parent);
            instance.name = prefab.name;
            instance.transform.localPosition = new Vector3(position.x, SampleHeight(position.x, position.y), position.y);
            instance.transform.localRotation = Quaternion.Euler(0f, RandRange(rng, 0f, 360f), 0f);
            return true;
        }

        private float SampleHeight(float x, float z)
        {
            if (_heightMap == null)
                return terrainYOffset;

            float tx = Mathf.InverseLerp(_minX, _maxX, x) * terrainResolutionX;
            float tz = Mathf.InverseLerp(_minZ, _maxZ, z) * terrainResolutionZ;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(tx), 0, terrainResolutionX);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(tz), 0, terrainResolutionZ);
            int x1 = Mathf.Clamp(x0 + 1, 0, terrainResolutionX);
            int z1 = Mathf.Clamp(z0 + 1, 0, terrainResolutionZ);

            float fx = tx - x0;
            float fz = tz - z0;

            float h00 = _heightMap[x0, z0];
            float h10 = _heightMap[x1, z0];
            float h01 = _heightMap[x0, z1];
            float h11 = _heightMap[x1, z1];

            float hx0 = Mathf.Lerp(h00, h10, fx);
            float hx1 = Mathf.Lerp(h01, h11, fx);
            return Mathf.Lerp(hx0, hx1, fz);
        }

        private Mesh CreateFlatShaded(Mesh source)
        {
            var srcVerts = source.vertices;
            var srcUvs = source.uv;
            var srcTris = source.triangles;

            var verts = new Vector3[srcTris.Length];
            var uvs = new Vector2[srcTris.Length];
            var tris = new int[srcTris.Length];

            for (int i = 0; i < srcTris.Length; i++)
            {
                int srcIndex = srcTris[i];
                verts[i] = srcVerts[srcIndex];
                if (srcUvs != null && srcUvs.Length > srcIndex)
                    uvs[i] = srcUvs[srcIndex];
                tris[i] = i;
            }

            var flat = new Mesh { name = $"{source.name}_Flat" };
            flat.indexFormat = verts.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            flat.vertices = verts;
            flat.triangles = tris;
            flat.uv = uvs;
            flat.RecalculateNormals();
            flat.RecalculateBounds();
            return flat;
        }

        private void ApplySurfaceStyle(Renderer renderer, Material materialOverride, Color tint)
        {
            if (renderer == null)
                return;

            renderer.sharedMaterial = ResolveMaterial(materialOverride, tint);

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tint);
            block.SetColor(ColorId, tint);
            renderer.SetPropertyBlock(block);
        }

        private Material ResolveMaterial(Material materialOverride, Color tint)
        {
            if (materialOverride != null)
                return materialOverride;

            if (ApproximatelyColor(tint, terrainTint))
                return _runtimeTerrainFallback ??= CreateFallbackMaterial("RuntimeTerrainFallback", terrainTint);

            if (ApproximatelyColor(tint, pathTint))
                return _runtimePathFallback ??= CreateFallbackMaterial("RuntimePathFallback", pathTint);

            if (ApproximatelyColor(tint, foliageTint))
                return _runtimeFoliageFallback ??= CreateFallbackMaterial("RuntimeFoliageFallback", foliageTint);

            return _runtimeStructureFallback ??= CreateFallbackMaterial("RuntimeStructureFallback", structureTint);
        }

        private Material CreateFallbackMaterial(string name, Color tint)
        {
            Shader shader =
                Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Color");

            var material = new Material(shader) { name = name };
            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, tint);
            if (material.HasProperty(ColorId))
                material.SetColor(ColorId, tint);
            return material;
        }

        private static bool ApproximatelyColor(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.02f &&
                   Mathf.Abs(a.g - b.g) < 0.02f &&
                   Mathf.Abs(a.b - b.b) < 0.02f;
        }

        private Vector2 ClampInsideTerrain(Vector2 p)
        {
            return new Vector2(
                Mathf.Clamp(p.x, _minX + 12f, _maxX - 12f),
                Mathf.Clamp(p.y, _minZ + 12f, _maxZ - 12f));
        }

        private bool IsNearArea(Vector2 point, List<AreaNode> areas, float extraRadius)
        {
            for (int i = 0; i < areas.Count; i++)
            {
                float radius = Mathf.Max(areas[i].Size.x, areas[i].Size.y) * 0.5f + extraRadius;
                if (Vector2.Distance(point, areas[i].Center) <= radius)
                    return true;
            }

            return false;
        }

        private static Vector2 RandVec2(System.Random rng, Vector2 min, Vector2 max)
        {
            return new Vector2(RandRange(rng, min.x, max.x), RandRange(rng, min.y, max.y));
        }

        private static float RandRange(System.Random rng, float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }

        private static Vector2 Rotate(Vector2 dir, float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            float cs = Mathf.Cos(rad);
            float sn = Mathf.Sin(rad);
            return new Vector2(dir.x * cs - dir.y * sn, dir.x * sn + dir.y * cs);
        }

        private static Vector2 FindAreaCenter(List<AreaNode> areas, int roomId)
        {
            for (int i = 0; i < areas.Count; i++)
            {
                if (areas[i].RoomId == roomId)
                    return areas[i].Center;
            }

            return Vector2.zero;
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len = ab.sqrMagnitude;
            if (len < 0.0001f)
                return Vector2.Distance(p, a);

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
            return Vector2.Distance(p, a + ab * t);
        }

        private static Vector2 RandomInsideUnitCircle(System.Random rng)
        {
            float angle = RandRange(rng, 0f, Mathf.PI * 2f);
            float radius = Mathf.Sqrt(RandRange(rng, 0f, 1f));
            return new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        }
    }
}
