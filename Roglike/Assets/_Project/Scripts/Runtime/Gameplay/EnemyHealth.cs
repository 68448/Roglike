using Mirror;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Project.WorldGen;

namespace Project.Gameplay
{
    /// <summary>
    /// Простая HP-система врага. HP синхронизируется (SyncVar).
    /// Урон применяет только сервер.
    /// </summary>
    public sealed class EnemyHealth : NetworkBehaviour
    {
        [SyncVar] public int CurrentHP;
        [SyncVar] public int SegmentIndex;
        [SyncVar] public int RoomId;
        [SyncVar] public int MaxHP;
        [SerializeField] private int maxHP = 30;
        [SyncVar] public int BaseHP;
        private bool _isDead;

        [Header("Combat Feedback")]
        [SerializeField] private float hitFlashDuration = 0.08f;
        [SerializeField] private Color hitFlashColor = new Color(1f, 0.35f, 0.35f, 1f);
        [SerializeField] private Vector3 floatingDamageOffset = new Vector3(0f, 1.6f, 0f);
        [SerializeField] private Color floatingDamageColor = new Color(1f, 0.95f, 0.65f, 1f);
        [SerializeField] private Color floatingCritDamageColor = new Color(1f, 0.45f, 0.2f, 1f);

        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private Coroutine _flashRoutine;
        private readonly Dictionary<Renderer, Color> _savedBaseColors = new Dictionary<Renderer, Color>();
        private readonly Dictionary<Renderer, Color> _savedColorColors = new Dictionary<Renderer, Color>();

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            // ✅ НЕ перетираем, если уже задано (например scaling-ом)
            if (MaxHP <= 0)
            {
                MaxHP = Mathf.Max(1, maxHP);
            }

            if (CurrentHP <= 0)
            {
                CurrentHP = MaxHP;
            }

            _isDead = false;
        }

        [Server]
        public void TakeDamage(int amount)
        {
            TakeDamage(amount, false);
        }

        [Server]
        public void TakeDamage(int amount, bool isCritical)
        {
            if (_isDead || CurrentHP <= 0)
                return;

            if (amount <= 0) return;

            CurrentHP -= amount;
            if (CurrentHP < 0)
                CurrentHP = 0;

            RpcOnHitFeedback(amount, transform.position + floatingDamageOffset, isCritical);

            if (CurrentHP <= 0)
            {
                _isDead = true;
                var controller = FindFirstObjectByType<Project.WorldGen.ChunkDungeonController>();
                if (controller != null)
                    controller.ServerNotifyEnemyDied(SegmentIndex, RoomId);
                // 2) дропаем предмет
                DropItem();
                var boss = GetComponent<Project.Boss.BossMarker>();
                if (boss != null)
                {
                    var dungeonCtrl = FindFirstObjectByType<Project.WorldGen.ChunkDungeonController>();
                    if (dungeonCtrl != null)
                    {
                        dungeonCtrl.ServerNotifyBossDied(boss.SegmentIndex);
                        dungeonCtrl.ServerSpawnRewardOrb(boss.SegmentIndex, transform.position);
                    }
                }
                NetworkServer.Destroy(gameObject);
            }
        }

        [ClientRpc]
        private void RpcOnHitFeedback(int damage, Vector3 worldPos, bool isCritical)
        {
            if (damage > 0)
            {
                var color = isCritical ? floatingCritDamageColor : floatingDamageColor;
                Project.UI.FloatingDamageText.Spawn(worldPos, damage, color, isCritical);
            }

            if (!isActiveAndEnabled)
                return;

            if (_flashRoutine != null)
                StopCoroutine(_flashRoutine);

            _flashRoutine = StartCoroutine(HitFlashRoutine());
        }

        private IEnumerator HitFlashRoutine()
        {
            if (_renderers == null || _renderers.Length == 0)
                yield break;

            _savedBaseColors.Clear();
            _savedColorColors.Clear();

            for (int i = 0; i < _renderers.Length; i++)
            {
                var r = _renderers[i];
                if (r == null)
                    continue;

                Color baseCol = ReadCurrentColor(r, BaseColorId, "_BaseColor");
                Color stdCol = ReadCurrentColor(r, ColorId, "_Color");

                _savedBaseColors[r] = baseCol;
                _savedColorColors[r] = stdCol;

                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, Color.Lerp(baseCol, hitFlashColor, 0.8f));
                _block.SetColor(ColorId, Color.Lerp(stdCol, hitFlashColor, 0.8f));
                r.SetPropertyBlock(_block);
            }

            yield return new WaitForSeconds(hitFlashDuration);

            foreach (var kv in _savedBaseColors)
            {
                if (kv.Key == null)
                    continue;

                kv.Key.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, kv.Value);
                kv.Key.SetPropertyBlock(_block);
            }

            foreach (var kv in _savedColorColors)
            {
                if (kv.Key == null)
                    continue;

                kv.Key.GetPropertyBlock(_block);
                _block.SetColor(ColorId, kv.Value);
                kv.Key.SetPropertyBlock(_block);
            }

            _flashRoutine = null;
        }

        private Color ReadCurrentColor(Renderer renderer, int colorId, string shaderPropName)
        {
            if (renderer == null)
                return Color.white;

            renderer.GetPropertyBlock(_block);
            Color fromBlock = _block.GetColor(colorId);

            bool blockHasValue =
                !Mathf.Approximately(fromBlock.a, 0f) ||
                !Mathf.Approximately(fromBlock.r, 0f) ||
                !Mathf.Approximately(fromBlock.g, 0f) ||
                !Mathf.Approximately(fromBlock.b, 0f);

            if (blockHasValue)
                return fromBlock;

            var mat = renderer.sharedMaterial;
            if (mat != null && mat.HasProperty(shaderPropName))
                return mat.GetColor(shaderPropName);

            return Color.white;
        }

        [Server]
        public void InitSegment(int segmentIndex)
        {
            SegmentIndex = segmentIndex;
        }

        [Server]
        public void InitRoom(int segmentIndex, int roomId)
        {
            SegmentIndex = segmentIndex;
            RoomId = roomId;
        }

        [Server]
        private void DropItem()
        {

            var ai = GetComponent<Project.Gameplay.EnemyAI>();
            bool isElite = (ai != null && ai.IsElite);

            int seg = SegmentIndex <= 0 ? 1 : SegmentIndex;
            float baseChance = 0.25f;
            float bonusPerSeg = 0.01f;   // +1% за сегмент
            float maxChance = 0.60f;

            float dropChance = Mathf.Clamp(baseChance + bonusPerSeg * (seg - 1), 0f, maxChance);

            if (isElite) dropChance = 1f;

            if (Random.value > dropChance)
                return;

            var controller = FindFirstObjectByType<Project.WorldGen.ChunkDungeonController>();
            if (controller == null) return;

            var prefab = controller.WorldItemPrefab;
            if (prefab == null) return;

            var obj = Instantiate(prefab, transform.position + Vector3.up * 0.5f, Quaternion.identity);

            var wi = obj.GetComponent<Project.Items.WorldItem>();
            if (wi != null)
            {
                var all = Resources.LoadAll<Project.Items.ItemData>("Items");

                if (all != null && all.Length > 0)
                {
                    int segmentIndex = seg;

                    var rolledRarity = RollItemRarity(segmentIndex);

                    var pool = new System.Collections.Generic.List<Project.Items.ItemData>();

                    // 1) сначала пытаемся взять предметы нужной редкости,
                    // которые уже разрешены по сегменту
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] == null) continue;
                        if (all[i].Id <= 0) continue;
                        if (all[i].MinDropSegment > segmentIndex) continue;
                        if (all[i].Rarity != rolledRarity) continue;

                        pool.Add(all[i]);
                    }

                    // 2) fallback: если такой редкости нет, берём любой доступный предмет
                    if (pool.Count == 0)
                    {
                        for (int i = 0; i < all.Length; i++)
                        {
                            if (all[i] == null) continue;
                            if (all[i].Id <= 0) continue;
                            if (all[i].MinDropSegment > segmentIndex) continue;

                            pool.Add(all[i]);
                        }
                    }

                    if (pool.Count > 0)
                    {
                        var picked = PickWeightedItem(pool);
                        if (picked != null)
                        {
                            wi.Init(picked.Id);
                            Debug.Log($"[EnemyHealth] Dropped itemId={picked.Id} name={picked.ItemName} rarity={picked.Rarity} seg={segmentIndex}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[EnemyHealth] No eligible items found for segment={segmentIndex}");
                    }
                }
                else
                {
                    Debug.LogWarning("[EnemyHealth] Resources/Items is empty");
                }
            }

            NetworkServer.Spawn(obj);
        }

        [Server]
        public void ServerSetMaxHP(int maxHp, bool fillToFull = true)
        {
            MaxHP = Mathf.Max(1, maxHp);
            if (fillToFull)
                CurrentHP = MaxHP;
            else
                CurrentHP = Mathf.Clamp(CurrentHP, 0, MaxHP);
        }

        [Server]
        public void ServerSetBaseHP(int baseHp)
        {
            BaseHP = Mathf.Max(1, baseHp);
        }

        private Project.Items.ItemRarity RollItemRarity(int segmentIndex)
        {
            float commonChance = 100f;
            float rareChance = 0f;
            float epicChance = 0f;
            float legendaryChance = 0f;

            if (segmentIndex >= 5)
            {
                commonChance = 80f;
                rareChance = 20f;
            }

            if (segmentIndex >= 12)
            {
                commonChance = 65f;
                rareChance = 25f;
                epicChance = 10f;
            }

            if (segmentIndex >= 25)
            {
                commonChance = 50f;
                rareChance = 30f;
                epicChance = 15f;
                legendaryChance = 5f;
            }

            float roll = UnityEngine.Random.Range(0f, 100f);

            if (roll < legendaryChance)
                return Project.Items.ItemRarity.Legendary;

            roll -= legendaryChance;
            if (roll < epicChance)
                return Project.Items.ItemRarity.Epic;

            roll -= epicChance;
            if (roll < rareChance)
                return Project.Items.ItemRarity.Rare;

            return Project.Items.ItemRarity.Common;
        }

        private Project.Items.ItemData PickWeightedItem(
            System.Collections.Generic.List<Project.Items.ItemData> pool)
        {
            if (pool == null || pool.Count == 0)
                return null;

            int totalWeight = 0;
            for (int i = 0; i < pool.Count; i++)
            {
                totalWeight += Mathf.Max(1, pool[i].DropWeight);
            }

            int roll = UnityEngine.Random.Range(0, totalWeight);

            for (int i = 0; i < pool.Count; i++)
            {
                int w = Mathf.Max(1, pool[i].DropWeight);
                if (roll < w)
                    return pool[i];

                roll -= w;
            }

            return pool[0];
        }
    }
}
