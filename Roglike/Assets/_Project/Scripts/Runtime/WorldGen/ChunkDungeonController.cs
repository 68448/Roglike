using Mirror;
using Project.Networking;
using UnityEngine;
using System.Collections.Generic;
using Project.WorldGen.Generators;
using Project.Rewards;

namespace Project.WorldGen
{
    public sealed class ChunkDungeonController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SegmentGeneratorBase  generator;
        [SerializeField] private BiomeCatalog biomeCatalog;

        [Header("Chunk Settings")]
        [SerializeField] private int keepSegmentsInMemory = 2; // текущий + следующий
        [SerializeField] private float segmentOffset = 80f;    // расстояние между сегментами по X

        [Header("Enemies")]
        [SerializeField] private GameObject enemyPrefab;
        [SerializeField] private int enemiesPerSegmentMin = 3;
        [SerializeField] private int enemiesPerSegmentMax = 6;

        [SerializeField] private GameObject portalPrefab;

        [Header("Loot")]
        [SerializeField] private GameObject worldItemPrefab;
        public GameObject WorldItemPrefab => worldItemPrefab;

        [Header("Difficulty")]
        [SerializeField] private Project.Difficulty.RunDifficultySettings difficultySettings;

        private RunSessionNetworkState _session;
        private int _lastKnownSegmentIndex = -1;

        private readonly Dictionary<int, int> _aliveEnemies = new();
        private readonly Dictionary<int, Project.WorldGen.PortalState> _portals = new();

        private Project.Difficulty.RunDifficultySettings _difficulty;

        private readonly System.Collections.Generic.HashSet<int> _enemiesSpawnedForSegment = new();

        private struct RoomKey
        {
            public int Segment;
            public int Room;

            public RoomKey(int segment, int room) { Segment = segment; Room = room; }

            public override int GetHashCode() => (Segment * 397) ^ Room;
            public override bool Equals(object obj) => obj is RoomKey other && other.Segment == Segment && other.Room == Room;
        }

        private readonly Dictionary<RoomKey, List<Project.Gameplay.EnemyAI>> _roomEnemies = new();
        private readonly HashSet<RoomKey> _activatedRooms = new();      

        // === Boss gating ===
        private readonly System.Collections.Generic.HashSet<int> _bossAliveSegments = new();
        private readonly System.Collections.Generic.Dictionary<int, Project.WorldGen.PortalTrigger> _exitPortalBySegment = new();  

        [Header("Boss")]
        [SerializeField] private GameObject bossPrefab;
        [SerializeField] private float bossHpMult = 6f;
        [SerializeField] private float bossDamageMult = 2f;
        [SerializeField] private float bossSpeedMult = 0.9f;

        private readonly System.Collections.Generic.Dictionary<int, Vector3> _safeSpawnBySegment = new();
        private readonly System.Collections.Generic.Dictionary<int, int> _exitRoomIdBySegment = new();

        private readonly System.Collections.Generic.HashSet<int> _generatedSegments = new();
        private BiomeType _lastLoggedBiome = (BiomeType)(-1);

        private readonly System.Collections.Generic.HashSet<int> _bossSpawnedSegments = new();

        [Header("Rewards")]
        [SerializeField] private GameObject rewardOrbPrefab;

        private void Awake()
        {
            if (generator == null)
                generator = FindFirstObjectByType<DungeonGeneratorStub>();

            if (biomeCatalog == null)
                biomeCatalog = GetComponent<BiomeCatalog>();

            if (enemyPrefab == null)
                Debug.LogWarning("[ChunkDungeonController] enemyPrefab is not assigned!");
        }

        private void Start()
        {
            StartCoroutine(InitWhenReady());
        }

        // private void Start()
        // {
        //     _session = FindFirstObjectByType<RunSessionNetworkState>();
        //     _lastKnownSegmentIndex = _session.SegmentIndex;
        //     if (_session == null)
        //     {
        //         Debug.LogError("[ChunkDungeonController] RunSessionNetworkState not found!");
        //         return;
        //     }

        //     // На старте: генерируем текущий сегмент и следующий (preload)
        //     GenerateSegment(_session.SegmentIndex);
        //     GenerateSegment(_session.SegmentIndex + 1);
        //     CleanupOldSegments();
        // }

        private void Update()
        {
            if (_session == null)
                return;

            if (_session.SegmentIndex != _lastKnownSegmentIndex)
            {
                _lastKnownSegmentIndex = _session.SegmentIndex;

                EnsureSegmentGenerated(_session.SegmentIndex);
                EnsureSegmentGenerated(_session.SegmentIndex + 1);
                CleanupOldSegments();
                ApplyCurrentSegmentBiomeVisuals();
                LogBiomeTransitionIfNeeded(_session.SegmentIndex);

                Debug.Log($"[ChunkDungeonController] Detected SegmentIndex change -> {_session.SegmentIndex}");
            }
        }

        private int GetSegmentSeed(int runSeed, int segmentIndex)
        {
            // Простой детерминированный хэш (стабилен на всех машинах)
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + runSeed;
                hash = hash * 31 + segmentIndex;
                return hash;
            }
        }

        private void GenerateSegmentInternal(int segmentIndex, int segmentSeed)
        {
            BiomeConfig biomeConfig = GetBiomeConfigForSegment(segmentIndex);
            var segmentRoot = new GameObject($"Segment_{segmentIndex}");
            float recommended = (generator != null) ? generator.GetRecommendedSegmentOffset() : segmentOffset;
            float offset = Mathf.Max(segmentOffset, recommended);

            segmentRoot.transform.position = new Vector3((segmentIndex - 1) * offset, 0, 0);
            var biomeState = segmentRoot.AddComponent<SegmentBiomeState>();
            biomeState.Init(segmentIndex, biomeConfig);

            // Важно: генератор сейчас создаёт комнаты под свой dungeonRoot.
            // Мы временно создадим генерацию в segmentRoot:
            generator.GenerateInto(segmentSeed, segmentIndex, segmentRoot.transform, biomeConfig);
            // === Read markers from generator ===
            var markers = segmentRoot.GetComponentInChildren<Project.WorldGen.SegmentMarkers>();
            if (markers == null)
            {
                Debug.LogError($"[ChunkDungeonController] SegmentMarkers not found for Segment={segmentIndex}. Teleport/portal will break.");
            }
            else
            {
                if (markers.SafeSpawn != null)
                {
                    _safeSpawnBySegment[segmentIndex] = markers.SafeSpawn.position;
                    Debug.Log($"[ChunkDungeonController] SafeSpawn recorded seg={segmentIndex} pos={markers.SafeSpawn.position}");
                }
                else
                {
                    Debug.LogError($"[ChunkDungeonController] SafeSpawn marker missing seg={segmentIndex}");
                }

                _exitRoomIdBySegment[segmentIndex] = markers.ExitRoomId;
                Debug.Log($"[ChunkDungeonController] ExitRoomId recorded seg={segmentIndex} exitRoomId={markers.ExitRoomId}");
            }
            if (NetworkServer.active){
                ServerSpawnPortalForSegment(segmentIndex,segmentRoot.transform);
                ServerSpawnEnemiesForSegment(segmentIndex, segmentRoot.transform);
            }

            Debug.Log($"[ChunkDungeonController] Generated Segment={segmentIndex} Seed={segmentSeed} Biome={biomeState.DisplayName}");
        }

        private void EnsureSegmentGenerated(int segmentIndex)
        {
            int seedForSegment  = GetSegmentSeed(_session.Seed, segmentIndex);

            var existing = GameObject.Find($"Segment_{segmentIndex}");
            if (existing != null)
            {
                var layout = existing.GetComponent<Project.WorldGen.SegmentLayout>();
                if (layout != null && layout.GeneratedSeed == seedForSegment)
                {
                    // уже сгенерирован корректно
                    return;
                }
                _enemiesSpawnedForSegment.Remove(segmentIndex);
                // есть сегмент, но он неправильный (сгенерен при seed=0 или другом seed)
                Destroy(existing);
            }

            GenerateSegmentInternal(segmentIndex, seedForSegment);
            _generatedSegments.Add(segmentIndex);
        }


        private void CleanupOldSegments()
        {
            // Удаляем сегменты, которые сильно позади
            int minSegmentToKeep = _session.SegmentIndex - (keepSegmentsInMemory - 1);

            var allSegments = GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in allSegments)
            {
                if (t.name.StartsWith("Segment_"))
                {
                    if (int.TryParse(t.name.Replace("Segment_", ""), out int idx))
                    {
                        if (idx < minSegmentToKeep)
                        {
                            Destroy(t.gameObject);
                            Debug.Log($"[ChunkDungeonController] Destroyed old Segment_{idx}");
                        }
                    }
                }
            }
        }

        
        public void AdvanceToNextSegment()
        {
            Debug.Log("[ChunkDungeonController] AdvanceToNextSegment() called");
            var session = FindFirstObjectByType<Project.Networking.RunSessionNetworkState>();
            if (session == null)
            {
                Debug.LogError("[ChunkDungeonController] No RunSessionNetworkState found!");
                return;
            }

            Debug.Log($"[ChunkDungeonController] AdvanceToNextSegment called. Before: SegmentIndex={session.SegmentIndex} serverActive={Mirror.NetworkServer.active}");

            // Только сервер (хост) увеличивает SegmentIndex
            if (Mirror.NetworkServer.active)
                session.NextSegment();

            Debug.Log($"[ChunkDungeonController] After increment: SegmentIndex={session.SegmentIndex}");

            int current = session.SegmentIndex;

            EnsureSegmentGenerated(current);
            EnsureSegmentGenerated(current + 1);
            Debug.Log($"[ChunkDungeonController] SegmentIndex now = {session.SegmentIndex}");
            CleanupOldSegments();
        }

        
        private void RpcOnSegmentAdvanced(int newSegmentIndex)
        {
            // На всех клиентах + хосте:
            EnsureSegmentGenerated(newSegmentIndex);
            EnsureSegmentGenerated(newSegmentIndex + 1);
            CleanupOldSegments();
        }

        private void ServerSpawnEnemiesForSegment(int segmentIndex, Transform segmentRoot)
        {
            if (_enemiesSpawnedForSegment.Contains(segmentIndex))
            {
                Debug.Log($"[ChunkDungeonController] Enemies already spawned for Segment={segmentIndex}, skipping.");
                return;
            }

            _enemiesSpawnedForSegment.Add(segmentIndex);

            int baseSeed = GetSegmentSeed(_session.Seed, segmentIndex);
            var rng = new System.Random(baseSeed ^ 0x51EDBEEF);

            _difficulty = difficultySettings;
            if (_difficulty == null)
                Debug.LogError("[ChunkDungeonController] DifficultySettings is NULL. Assign it in inspector.");
            else
                Debug.Log($"[ChunkDungeonController] Difficulty loaded: {_difficulty.name}, eliteChance={_difficulty.eliteChance}");
                

            if (!NetworkServer.active)
                return;

            if (enemyPrefab == null)
            {
                Debug.LogError("[ChunkDungeonController] enemyPrefab is NULL on server!");
                return;
            }

            var layout = segmentRoot.GetComponent<Project.WorldGen.SegmentLayout>();
            if (layout == null)
            {
                Debug.LogError("[ChunkDungeonController] SegmentLayout not found on segmentRoot!");
                return;
            }

            int total = 0;
            int eliteTotal = 0;
            var spawnedAIs = new System.Collections.Generic.List<Project.Gameplay.EnemyAI>(128);
            for (int r = 0; r < layout.Rooms.Count; r++)
            {
                var (minCount, maxCount) = Project.Difficulty.DifficultyService.GetEnemiesPerRoom(segmentIndex, _difficulty);
                int count = rng.Next(minCount, maxCount + 1);
                total += count;

                var room = layout.Rooms[r];

                // ✅ SAFE ROOM — пропускаем
                if (room.RoomId == layout.SafeRoomId)
                    continue;

                var key = new RoomKey(segmentIndex, room.RoomId);

                if (!_roomEnemies.ContainsKey(key))
                    _roomEnemies[key] = new List<Project.Gameplay.EnemyAI>();


                for (int i = 0; i < count; i++)
                {
                    Vector3 pos = room.Center + new Vector3(
                        UnityEngine.Random.Range(-room.Size.x * 0.35f, room.Size.x * 0.35f),
                        1f,
                        UnityEngine.Random.Range(-room.Size.z * 0.35f, room.Size.z * 0.35f)
                    );


                    // 2) Scaling

                    var pickedPrefab = ResolveEnemyPrefabForSegment(segmentIndex, rng, out var entry);

                    if (pickedPrefab == null)
                    {
                        // fallback — если вдруг таблица пустая, используем старый enemyPrefab
                        pickedPrefab = enemyPrefab;
                    }

                    var enemyObj = Instantiate(pickedPrefab, pos, Quaternion.identity);

                    float eliteRoll = (float)rng.NextDouble();

                    float typeHp = (entry != null) ? entry.hpMult : 1f;
                    float typeDmg = (entry != null) ? entry.damageMult : 1f;
                    float typeSpd = (entry != null) ? entry.speedMult : 1f;

                    var stats = Project.Difficulty.DifficultyService.GetEnemyStats(
                        segmentIndex,
                        _difficulty,
                        eliteRoll,
                        typeHp,
                        typeDmg,
                        typeSpd
                    );

                    if (stats.isElite)
                        eliteTotal++;
                        Debug.Log($"[Spawn] ELITE enemy spawned in seg={segmentIndex} room={room.RoomId}");



                    // Помечаем сегмент/комнату
                    var eh = enemyObj.GetComponent<Project.Gameplay.EnemyHealth>();
                    if (eh != null)
                        eh.InitRoom(segmentIndex, room.RoomId);
                        int alivePlayers = GetAlivePlayersCount();
                        float coopMult = CoopHpMultiplier(alivePlayers);

                        int finalHp = Mathf.Max(1, Mathf.RoundToInt(stats.hp * coopMult));
                        eh.ServerSetBaseHP(stats.hp);
                        eh.ServerSetMaxHP(finalHp);
                        

                    // Выключаем AI до активации комнаты
                    var ai = enemyObj.GetComponent<Project.Gameplay.EnemyAI>();
                    if (ai != null)
                    {
                        ai.ServerApplyScaling(stats.damage, stats.moveSpeed, stats.isElite);
                        ai.IsAwake = false; // важно: до Spawn
                        _roomEnemies[key].Add(ai);
                        spawnedAIs.Add(ai);
                    }


                    NetworkServer.Spawn(enemyObj);
                }

            }
 
            // ===== Elite guarantee (1 per segment) =====
            if (eliteTotal == 0 && spawnedAIs.Count > 0)
            {
                int idx = rng.Next(0, spawnedAIs.Count);
                var ai = spawnedAIs[idx];

                if (ai != null)
                {
                    // Рассчитываем "элитные" множители для текущего сегмента
                    var s = _difficulty;
                    int depth = Mathf.Max(1, segmentIndex);

                    float hpMult  = (1f + s.hpGrowthPerSegment * (depth - 1)) * s.eliteHpMult;
                    float dmgMult = (1f + s.damageGrowthPerSegment * (depth - 1)) * s.eliteDamageMult;
                    float spdMult = (1f + s.speedGrowthPerSegment * (depth - 1)) * s.eliteSpeedMult;

                    int eliteHp = Mathf.Max(1, Mathf.RoundToInt(s.baseEnemyHP * hpMult));
                    int eliteDmg = Mathf.Max(1, Mathf.RoundToInt(s.baseEnemyDamage * dmgMult));
                    float eliteSpd = Mathf.Max(0.5f, s.baseEnemyMoveSpeed * spdMult);

                    // Применяем как элиту
                    ai.ServerApplyScaling(eliteDmg, eliteSpd, true);

                    var eh = ai.GetComponent<Project.Gameplay.EnemyHealth>();
                    if (eh != null)
                    {
                        // учтём кооп-множитель и тут, чтобы было честно
                        int alivePlayers = GetAlivePlayersCount();
                        float coopMult = CoopHpMultiplier(alivePlayers);
                        int finalHp = Mathf.Max(1, Mathf.RoundToInt(eliteHp * coopMult));
                        eh.ServerSetMaxHP(finalHp);
                    }

                    Debug.Log($"[ChunkDungeonController] Elite guarantee applied in Segment={segmentIndex}");
                }
            }

            Debug.Log($"[ChunkDungeonController] Elite spawned total={eliteTotal} for Segment={segmentIndex}");

            _aliveEnemies[segmentIndex] = total;
            Debug.Log($"[ChunkDungeonController] Spawned enemies total={total} for Segment={segmentIndex} (per room)");
        }


        public void ServerNotifyEnemyDied(int segmentIndex, int roomId)
        {
            if (!NetworkServer.active)
                return;

            // Надёжный пересчёт: сколько живых врагов осталось в этом сегменте
            int alive = 0;
            var allEnemies = FindObjectsByType<Project.Gameplay.EnemyHealth>(FindObjectsSortMode.None);
            for (int i = 0; i < allEnemies.Length; i++)
            {
                if (allEnemies[i] == null) continue;
                if (allEnemies[i].SegmentIndex != segmentIndex) continue;
                if (allEnemies[i].CurrentHP > 0) alive++;
            }

            Debug.Log($"[ChunkDungeonController] Segment {segmentIndex} enemies alive now = {alive}");

            // Обновим кэш (не обязательно, но удобно)
            _aliveEnemies[segmentIndex] = alive;

            if (alive == 0)
            {
                if (_portals.TryGetValue(segmentIndex, out var portal) && portal != null)
                {
                    portal.Open();
                    Debug.Log($"[ChunkDungeonController] Portal opened for Segment={segmentIndex}");
                }
                else
                {
                    Debug.LogError($"[ChunkDungeonController] PortalState not found for Segment={segmentIndex}");
                }
            }
        }



        private void ServerSpawnPortalForSegment(int segmentIndex, Transform segmentRoot)
        {
            if (!NetworkServer.active)
                return;

            if (portalPrefab == null)
            {
                Debug.LogError("[ChunkDungeonController] portalPrefab is NULL on server!");
                return;
            }

            var layout = segmentRoot.GetComponent<SegmentLayout>();
            if (layout == null || layout.Rooms.Count == 0)
            {
                Debug.LogError("[ChunkDungeonController] SegmentLayout missing for portal spawn!");
                return;
            }

            int exitId = layout.ExitRoomId;
            exitId = Mathf.Clamp(exitId, 0, layout.Rooms.Count - 1);

            var markers = segmentRoot.GetComponentInChildren<Project.WorldGen.SegmentMarkers>();
            if (markers == null || markers.ExitPoint == null)
            {
                Debug.LogError($"[ChunkDungeonController] ExitPoint marker missing seg={segmentIndex}. Portal not spawned.");
                return;
            }

            Vector3 pos = markers.ExitPoint.position;

            // Vector3 pos = layout.Rooms[exitId].Center + Vector3.up * 1.0f;

            var portalObj = Instantiate(portalPrefab, pos, Quaternion.identity);
            NetworkServer.Spawn(portalObj);

            int exitRoomId = _exitRoomIdBySegment.TryGetValue(segmentIndex, out var rid) ? rid : -1;
            ServerSpawnBossForExitRoom(segmentIndex, exitRoomId, pos);

            // ServerSpawnBossForExitRoom(segmentIndex,exitId, portalObj.transform.position);

            var pt = portalObj.GetComponent<Project.WorldGen.PortalTrigger>();
            if (pt != null)
            {
                pt.ServerInit(fromSegment: segmentIndex, toSegment: segmentIndex + 1, locked: true);
                _exitPortalBySegment[segmentIndex] = pt;

                // Запоминаем exit portal для сегмента
                _exitPortalBySegment[segmentIndex] = pt;

                // По умолчанию запираем (пока босс жив)
                pt.ServerSetLocked(true);
            }

            var portalState = portalObj.GetComponent<Project.WorldGen.PortalState>();
            if (portalState != null)
                portalState.Init(segmentIndex);

            _portals[segmentIndex] = portalState;

            Debug.Log($"[ChunkDungeonController] Spawned portal for Segment={segmentIndex}");
        }

        public void ServerActivateRoom(int segmentIndex, int roomId)
        {
            if (!NetworkServer.active)
                return;

            var key = new RoomKey(segmentIndex, roomId);
            if (_activatedRooms.Contains(key))
                return;

            _activatedRooms.Add(key);

            // ✅ Надёжно: будим всех врагов, у которых совпадают SegmentIndex и RoomId
            var enemies = FindObjectsByType<Project.Gameplay.EnemyHealth>(FindObjectsSortMode.None);

            int woke = 0;
            for (int i = 0; i < enemies.Length; i++)
            {
                var eh = enemies[i];
                if (eh == null) continue;

                if (eh.SegmentIndex != segmentIndex) continue;
                if (eh.RoomId != roomId) continue;

                var ai = eh.GetComponent<Project.Gameplay.EnemyAI>();
                if (ai != null)
                {
                    int alivePlayers = GetAlivePlayersCount();
                    float coopMult = CoopHpMultiplier(alivePlayers);

                    int baseHp = (eh.BaseHP > 0) ? eh.BaseHP : eh.MaxHP; // fallback
                    int finalHp = Mathf.Max(1, Mathf.RoundToInt(baseHp * coopMult));

                    // если враг ещё не был ранен (спит) — просто заполняем до фулла
                    eh.ServerSetMaxHP(finalHp, fillToFull: true);
                    ai.ServerWakeUp();
                    woke++;
                }

                var ranged = eh.GetComponent<Project.Gameplay.EnemyRangedAI>();
                if (ranged != null)
                    ranged.ServerWakeUp();  
            }

            ServerWakeBossIfInThisRoom(segmentIndex, roomId);

            Debug.Log($"[ChunkDungeonController] Room activated seg={segmentIndex} room={roomId}. WokeEnemies={woke}");
        }


        [Server]
        private void ServerTeleportPlayersToSafeRoom(int segmentIndex)
        {
            var seg = GameObject.Find($"Segment_{segmentIndex}");
            if (seg == null)
            {
                Debug.LogError($"[ChunkDungeonController] Segment_{segmentIndex} not found for teleport!");
                return;
            }

            var layout = seg.GetComponent<SegmentLayout>();
            if (layout == null || layout.Rooms.Count == 0)
            {
                Debug.LogError("[ChunkDungeonController] SegmentLayout missing or empty!");
                return;
            }

            int safeId = layout.SafeRoomId;
            safeId = Mathf.Clamp(safeId, 0, layout.Rooms.Count - 1);

            Vector3 safeCenter = layout.Rooms[safeId].Center;

            // Разводим игроков чуть-чуть, чтобы не стояли друг в друге
            var players = FindObjectsByType<Project.Gameplay.PlayerHealth>(FindObjectsSortMode.None);

            for (int i = 0; i < players.Length; i++)
            {
                var pc = players[i].GetComponent<Project.Player.PlayerController>();
                if (pc == null) continue;

                Vector3 offset = new Vector3((i % 2) * 1.5f, 0f, (i / 2) * 1.5f);
                pc.ServerTeleport(safeCenter + offset + Vector3.up * 1.2f);
            }

            Debug.Log($"[ChunkDungeonController] Teleported players to SafeRoom in Segment_{segmentIndex}");
        }

        [Server]
        public void ServerForcePrepareAndTeleport(int segmentIndex)
        {
            // генерируем сегмент (если ещё не создан)
            EnsureSegmentGenerated(segmentIndex);
            EnsureSegmentGenerated(segmentIndex + 1);
            CleanupOldSegments();

            // телепортируем игроков в safe room
            ServerTeleportPlayersToSafeRoom(segmentIndex);
        }

        private System.Collections.IEnumerator InitWhenReady()
        {
            _session = FindFirstObjectByType<Project.Networking.RunSessionNetworkState>();
            while (_session == null)
            {
                yield return null;
                _session = FindFirstObjectByType<Project.Networking.RunSessionNetworkState>();
            }

            // ВАЖНО: ждём пока seed реально доедет на клиент
            float timeout = 5f;
            while (_session.Seed == 0 && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (_session.Seed == 0)
            {
                Debug.LogError("[ChunkDungeonController] Seed is 0 after timeout. Generation cancelled.");
                yield break;
            }

            _difficulty = Project.Difficulty.DifficultyService.LoadSettings();
            if (_difficulty == null)
                Debug.LogError("[ChunkDungeonController] Difficulty settings not found at Resources/Difficulty/RunDifficulty_Default");


            // Теперь можно безопасно генерировать
            EnsureSegmentGenerated(_session.SegmentIndex);
            EnsureSegmentGenerated(_session.SegmentIndex + 1);
            CleanupOldSegments();
            ApplyCurrentSegmentBiomeVisuals();
            LogBiomeTransitionIfNeeded(_session.SegmentIndex);

            _lastKnownSegmentIndex = _session.SegmentIndex;

            Debug.Log($"[ChunkDungeonController] Init ready. Seed={_session.Seed} SegmentIndex={_session.SegmentIndex}");
        }

        private BiomeConfig GetBiomeConfigForSegment(int segmentIndex)
        {
            BiomeType biomeType = BiomeService.GetBiomeForSegment(segmentIndex);

            if (biomeCatalog != null)
                return biomeCatalog.GetConfig(biomeType);

            return BiomeCatalog.CreateFallbackConfig(biomeType);
        }

        private void ApplyCurrentSegmentBiomeVisuals()
        {
            if (_session == null)
                return;

            var currentSegment = GameObject.Find($"Segment_{_session.SegmentIndex}");
            if (currentSegment == null)
                return;

            var biomeState = currentSegment.GetComponent<SegmentBiomeState>();
            biomeState?.ApplySceneVisuals();
        }

        private void LogBiomeTransitionIfNeeded(int segmentIndex)
        {
            BiomeType biomeType = BiomeService.GetBiomeForSegment(segmentIndex);
            if (_lastLoggedBiome == biomeType)
                return;

            _lastLoggedBiome = biomeType;
            var biomeConfig = GetBiomeConfigForSegment(segmentIndex);
            string biomeName = biomeConfig != null ? biomeConfig.GetResolvedDisplayName() : BiomeService.GetDisplayName(biomeType);
            int startSegment = BiomeService.GetBiomeStartSegment(biomeType);
            int endSegment = BiomeService.GetBiomeEndSegment(biomeType);
            Debug.Log($"[Biome] Segment {segmentIndex} -> {biomeName} ({startSegment}-{endSegment})");
        }

        // ===== Coop scaling helpers =====

        private int GetAlivePlayersCount()
        {
            var players = FindObjectsByType<Project.Gameplay.PlayerHealth>(FindObjectsSortMode.None);

            int alive = 0;
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null && players[i].CurrentHP > 0)
                    alive++;
            }

            return Mathf.Max(1, alive);
        }

        private float CoopHpMultiplier(int alivePlayers)
        {
            // Можно менять цифры, это стартовые.
            return alivePlayers switch
            {
                1 => 1.0f,
                2 => 1.35f,
                3 => 1.65f,
                _ => 1.90f
            };
        }

        [Server]
        private void TryUnlockExitPortal(int segmentIndex)
        {
            // 1) если босс ещё жив — нельзя
            if (_bossAliveSegments.Contains(segmentIndex))
                return;

            // 2) если у тебя есть логика "сегмент очищен" — учитываем её
            // Если у тебя уже есть метод/условие — вставь сюда.
            // MVP: будем считать, что босса достаточно.
            // Если хочешь строго: добавим условие remainingEnemies == 0.

            if (_exitPortalBySegment.TryGetValue(segmentIndex, out var portal) && portal != null)
            {
                portal.ServerSetLocked(false);
                Debug.Log($"[ChunkDungeonController] Exit portal unlocked for Segment={segmentIndex}");
            }
        }

        [Server]
        private void ServerSpawnBossForExitRoom(int segmentIndex, int exitRoomId, Vector3 portalPos)
        {
            GameObject resolvedBossPrefab = ResolveBossPrefabForSegment(segmentIndex);
            if (resolvedBossPrefab == null)
            {
                Debug.LogWarning("[ChunkDungeonController] bossPrefab is null, boss skipped.");
                return;
            }

            // Ставим босса чуть в стороне от портала
            Vector3 spawnPos = portalPos + new Vector3(0f, 0f, -4f);

            var bossObj = Instantiate(resolvedBossPrefab, spawnPos, Quaternion.identity);

            // Помечаем как босса
            var marker = bossObj.GetComponent<Project.Boss.BossMarker>();
            if (marker != null)
                marker.Init(segmentIndex, exitRoomId);

            // Применяем scaling (как обычному врагу, но с множителями босса)
            if (_difficulty == null)
                _difficulty = Project.Difficulty.DifficultyService.LoadSettings();

            // Используем системный rng для сегмента, чтобы было стабильно
            int baseSeed = GetSegmentSeed(_session.Seed, segmentIndex);
            var rng = new System.Random(baseSeed ^ 0x51EDBEEF);

            float eliteRoll = 0f; // босс не элита, он сам босс
            var stats = Project.Difficulty.DifficultyService.GetEnemyStats(
                segmentIndex,
                _difficulty,
                eliteRoll,
                typeHpMult: bossHpMult,
                typeDmgMult: bossDamageMult,
                typeSpeedMult: bossSpeedMult
            );

            // HP
            var eh = bossObj.GetComponent<Project.Gameplay.EnemyHealth>();
            if (eh != null)
            {
                eh.ServerSetBaseHP(stats.hp); // чтобы кооп-пересчёт при входе работал
                // Ставим базово сейчас (кооп будет пересчитан при входе в комнату)
                eh.ServerSetMaxHP(stats.hp, fillToFull: true);
            }

            // AI — пусть будет милишный EnemyAI (или свой BossAI позже)
            var ai = bossObj.GetComponent<Project.Gameplay.EnemyAI>();
            if (ai != null)
            {
                ai.ServerApplyScaling(stats.damage, stats.moveSpeed, isElite: false);
                ai.IsAwake = false; // проснётся при входе в комнату
            }

            // Важно: на сервере
            NetworkServer.Spawn(bossObj);

            // Запоминаем: босс жив
            _bossAliveSegments.Add(segmentIndex);

            Debug.Log($"[ChunkDungeonController] Boss spawned for Segment={segmentIndex} hp={stats.hp}");
        }

        private GameObject ResolveBossPrefabForSegment(int segmentIndex)
        {
            var biomeConfig = GetBiomeConfigForSegment(segmentIndex);
            if (biomeConfig != null && biomeConfig.bossPrefab != null)
                return biomeConfig.bossPrefab;

            return bossPrefab;
        }

        private GameObject ResolveEnemyPrefabForSegment(int segmentIndex, System.Random rng, out Project.Difficulty.EnemySpawnEntry entry)
        {
            entry = null;

            var biomeConfig = GetBiomeConfigForSegment(segmentIndex);
            if (biomeConfig != null && biomeConfig.enemyPrefabs != null && biomeConfig.enemyPrefabs.Length > 0)
            {
                var validPrefabs = new List<GameObject>();
                for (int i = 0; i < biomeConfig.enemyPrefabs.Length; i++)
                {
                    var prefab = biomeConfig.enemyPrefabs[i];
                    if (prefab != null)
                        validPrefabs.Add(prefab);
                }

                if (validPrefabs.Count > 0)
                {
                    var pickedPrefab = validPrefabs[rng.Next(0, validPrefabs.Count)];
                    Project.Difficulty.DifficultyService.TryGetSpawnEntryForPrefab(segmentIndex, _difficulty, pickedPrefab, out entry);
                    return pickedPrefab;
                }
            }

            return Project.Difficulty.DifficultyService.PickEnemyPrefab(segmentIndex, _difficulty, rng, out entry);
        }

        [Server]
        public void ServerNotifyBossDied(int segmentIndex)
        {
            if (_bossAliveSegments.Contains(segmentIndex))
                _bossAliveSegments.Remove(segmentIndex);

            Debug.Log($"[ChunkDungeonController] Boss died for Segment={segmentIndex}");

            TryUnlockExitPortal(segmentIndex);
        }

        [Server]
        private void ServerWakeBossIfInThisRoom(int segmentIndex, int roomId)
        {
            var bosses = FindObjectsByType<Project.Boss.BossMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < bosses.Length; i++)
            {
                var bm = bosses[i];
                if (bm == null) continue;
                if (bm.SegmentIndex != segmentIndex) continue;
                if (bm.RoomId != roomId) continue; // ✅ будим только если это нужная комната

                // Пересчёт HP по коопу перед боем
                var eh = bm.GetComponent<Project.Gameplay.EnemyHealth>();
                if (eh != null)
                {
                    int alive = GetAlivePlayersCount();
                    float coop = CoopHpMultiplier(alive);

                    int baseHp = (eh.BaseHP > 0) ? eh.BaseHP : eh.MaxHP;
                    int finalHp = Mathf.Max(1, Mathf.RoundToInt(baseHp * coop));

                    eh.ServerSetMaxHP(finalHp, fillToFull: true);
                }

                // Будим AI
                var ai = bm.GetComponent<Project.Gameplay.EnemyAI>();
                if (ai != null)
                    ai.ServerWakeUp();

                Debug.Log($"[BossGate] Boss awakened seg={segmentIndex} room={roomId}");
                return;
            }
        }

        [Server]
        public void ServerAdvanceToNextSegmentAndTeleportParty(int targetSegment)
        {
            if (_session == null)
                _session = FindFirstObjectByType<Project.Networking.RunSessionNetworkState>();

            if (_session != null && _session.SegmentIndex != targetSegment)
            {
                _session.SegmentIndex = targetSegment;
                Debug.Log($"[ChunkDungeonController] Session SegmentIndex updated to {targetSegment}");
            }

            // 1) гарантируем генерацию
            EnsureSegmentGenerated(targetSegment); // если у тебя другой метод — замени
            EnsureSegmentGenerated(targetSegment + 1);
            CleanupOldSegments();

            // 2) safe точка должна быть записана из SegmentMarkers
            if (!_safeSpawnBySegment.TryGetValue(targetSegment, out var safePos))
            {
                Debug.LogError($"[ChunkDungeonController] No SafeSpawn for seg={targetSegment}. Teleport cancelled.");
                return;
            }

            // 3) телепорт всех подключённых игроков
            int moved = 0;
            int idx = 0;

            foreach (var kv in NetworkServer.connections)
            {
                var conn = kv.Value;
                if (conn == null || conn.identity == null) continue;

                var t = conn.identity.transform;

                Vector3 offset = new Vector3((idx % 2) * 1.2f, 0f, (idx / 2) * 1.2f);
                idx++;

                var rb = conn.identity.GetComponent<Rigidbody>();
                if (rb != null) rb.linearVelocity = Vector3.zero;

                t.position = safePos + offset;
                // Принудительно сдвигаем клиента (чтобы не перезаписал позицию обратно)
                var tele = conn.identity.GetComponent<Project.Networking.PlayerTeleportReceiver>();
                if (tele != null)
                {
                    tele.TargetForceTeleport(conn, safePos + offset);
                }
                else
                {
                    Debug.LogWarning("[ChunkDungeonController] PlayerTeleportReceiver missing on player prefab!");
                }
                moved++;
            }

            Debug.Log($"[ChunkDungeonController] Party teleported to seg={targetSegment}. moved={moved}");
        }

        [Server]
        public void ServerSpawnRewardOrb(int segmentIndex, Vector3 worldPos)
        {
            if (rewardOrbPrefab == null)
            {
                Debug.LogWarning("[ChunkDungeonController] rewardOrbPrefab is null.");
                return;
            }

            var go = Instantiate(rewardOrbPrefab, worldPos, Quaternion.identity);

            // seed для наград (детерминированно)
            int baseSeed = GetSegmentSeed(_session.Seed, segmentIndex);
            int rewardSeed = baseSeed ^ 0x0BADC0DE;

            var orb = go.GetComponent<RewardOrb>();
            if (orb != null)
                orb.ServerInit(segmentIndex, rewardSeed);

            NetworkServer.Spawn(go);
            Debug.Log($"[ChunkDungeonController] RewardOrb spawned seg={segmentIndex} pos={worldPos}");
        }

    }
}
