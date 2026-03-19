using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace Project.Gameplay
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemySummonerAI : NetworkBehaviour
    {
        private static readonly Color CastIndicatorColor = new(0.85f, 0.25f, 1f, 1f);

        [Header("Defaults")]
        [SerializeField] private int defaultDamage = 6;
        [SerializeField] private float defaultMoveSpeed = 2.7f;

        [Header("Movement")]
        [SerializeField] private float preferredDistanceFromPlayer = 6f;
        [SerializeField] private float retreatDistance = 2.8f;
        [SerializeField] private float contactAttackRange = 1.3f;
        [SerializeField] private float contactAttackCooldown = 1.5f;

        [Header("Summoning")]
        [SerializeField] private GameObject summonPrefab;
        [SerializeField] private int summonCountPerCast = 1;
        [SerializeField] private int maxAliveSummons = 3;
        [SerializeField] private float summonCooldown = 5f;
        [SerializeField] private float summonWindup = 1f;
        [SerializeField] private float summonRecovery = 0.45f;
        [SerializeField] private float summonRadius = 2.5f;
        [SerializeField] private float summonHpMultiplier = 0.65f;
        [SerializeField] private float summonDamageMultiplier = 0.75f;
        [SerializeField] private float summonSpeedMultiplier = 1.05f;

        [SyncVar] public bool IsAwake;
        [SyncVar] private bool _isSummoning;
        [SyncVar] private int _damage;
        [SyncVar] private float _moveSpeed;

        private Rigidbody _rb;
        private EnemyHealth _health;
        private float _contactAttackTimer;
        private float _summonTimer;
        private float _summonWindupTimer;
        private float _summonRecoveryTimer;
        private readonly List<NetworkIdentity> _activeSummons = new List<NetworkIdentity>(8);
        private GameObject _castIndicator;
        private MaterialPropertyBlock _castIndicatorBlock;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _health = GetComponent<EnemyHealth>();
            _castIndicatorBlock = new MaterialPropertyBlock();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_damage <= 0) _damage = defaultDamage;
            if (_moveSpeed <= 0.01f) _moveSpeed = defaultMoveSpeed;
            _summonTimer = summonCooldown;
        }

        [Server]
        public void ServerApplyScaling(int damage, float moveSpeed)
        {
            _damage = Mathf.Max(1, damage);
            _moveSpeed = Mathf.Max(0.5f, moveSpeed);
        }

        [Server]
        public void ServerWakeUp()
        {
            if (_damage <= 0) _damage = defaultDamage;
            if (_moveSpeed <= 0.01f) _moveSpeed = defaultMoveSpeed;
            IsAwake = true;
        }

        private void Update()
        {
            UpdateCastIndicator();
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            if (!IsAwake) return;

            CleanupDeadSummons();

            var target = FindClosestAlivePlayer();
            if (target == null) return;

            _contactAttackTimer -= Time.fixedDeltaTime;
            _summonTimer -= Time.fixedDeltaTime;

            if (_summonRecoveryTimer > 0f)
            {
                _summonRecoveryTimer -= Time.fixedDeltaTime;
                LookAt(target.position);
                return;
            }

            if (_isSummoning)
            {
                _summonWindupTimer -= Time.fixedDeltaTime;
                LookAt(target.position);

                if (_summonWindupTimer <= 0f)
                {
                    int summoned = TrySummonMinions();
                    _isSummoning = false;
                    _summonRecoveryTimer = summonRecovery;
                    if (summoned > 0)
                        _summonTimer = summonCooldown;
                }

                return;
            }

            float dist = FlatDistance(_rb.position, target.position);

            if (dist < retreatDistance)
                MoveAway(target.position);
            else if (dist > preferredDistanceFromPlayer)
                MoveTowards(target.position);
            else
                LookAt(target.position);

            if (dist <= contactAttackRange)
                TryContactAttack(target);

            if (_summonTimer <= 0f && _activeSummons.Count < maxAliveSummons)
            {
                StartSummonCast();
            }
        }

        [Server]
        private void StartSummonCast()
        {
            if (summonPrefab == null)
                return;

            _isSummoning = true;
            _summonWindupTimer = Mathf.Max(0.1f, summonWindup);
        }

        [Server]
        private int TrySummonMinions()
        {
            if (summonPrefab == null)
                return 0;

            if (summonPrefab.GetComponent<EnemySummonerAI>() != null)
            {
                Debug.LogWarning("[EnemySummonerAI] summonPrefab should not be another EnemySummonerAI.");
                return 0;
            }

            int canSpawn = Mathf.Max(0, maxAliveSummons - _activeSummons.Count);
            int toSpawn = Mathf.Min(summonCountPerCast, canSpawn);
            int spawned = 0;

            for (int i = 0; i < toSpawn; i++)
            {
                Vector2 circle = Random.insideUnitCircle * summonRadius;
                Vector3 spawnPos = transform.position + new Vector3(circle.x, 0.5f, circle.y);

                var go = Instantiate(summonPrefab, spawnPos, Quaternion.identity);
                SetupSummonedEnemy(go);
                NetworkServer.Spawn(go);

                var identity = go.GetComponent<NetworkIdentity>();
                if (identity != null)
                    _activeSummons.Add(identity);

                spawned++;
            }

            return spawned;
        }

        [Server]
        private void SetupSummonedEnemy(GameObject summoned)
        {
            if (summoned == null)
                return;

            int segmentIndex = _health != null ? Mathf.Max(1, _health.SegmentIndex) : 1;
            int roomId = _health != null ? _health.RoomId : 0;
            int baseHp = _health != null
                ? (_health.BaseHP > 0 ? _health.BaseHP : Mathf.Max(1, _health.MaxHP))
                : 30;

            int summonHp = Mathf.Max(1, Mathf.RoundToInt(baseHp * summonHpMultiplier));
            int summonDamage = Mathf.Max(1, Mathf.RoundToInt(_damage * summonDamageMultiplier));
            float summonSpeed = Mathf.Max(0.5f, _moveSpeed * summonSpeedMultiplier);

            var enemyHealth = summoned.GetComponent<EnemyHealth>();
            if (enemyHealth != null)
            {
                enemyHealth.InitRoom(segmentIndex, roomId);
                enemyHealth.ServerSetBaseHP(summonHp);
                enemyHealth.ServerSetMaxHP(summonHp, fillToFull: true);
            }

            var enemyAi = summoned.GetComponent<EnemyAI>();
            if (enemyAi != null)
            {
                enemyAi.ServerApplyScaling(summonDamage, summonSpeed, false);
                enemyAi.ServerWakeUp();
            }

            var rangedAi = summoned.GetComponent<EnemyRangedAI>();
            if (rangedAi != null)
            {
                rangedAi.ServerApplyScaling(summonDamage, summonSpeed);
                rangedAi.ServerWakeUp();
            }

            var supportAi = summoned.GetComponent<EnemySupportAI>();
            if (supportAi != null)
            {
                supportAi.ServerApplyScaling(summonDamage, summonSpeed);
                supportAi.ServerWakeUp();
            }

            var tankAi = summoned.GetComponent<EnemyTankAI>();
            if (tankAi != null)
            {
                tankAi.ServerApplyScaling(summonDamage, summonSpeed);
                tankAi.ServerWakeUp();
            }

            var assassinAi = summoned.GetComponent<EnemyAssassinAI>();
            if (assassinAi != null)
            {
                assassinAi.ServerApplyScaling(summonDamage, summonSpeed);
                assassinAi.ServerWakeUp();
            }
        }

        private Transform FindClosestAlivePlayer()
        {
            var players = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);

            Transform closest = null;
            float best = float.MaxValue;

            for (int i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null || player.CurrentHP <= 0)
                    continue;

                float d = FlatDistance(transform.position, player.transform.position);
                if (d < best)
                {
                    best = d;
                    closest = player.transform;
                }
            }

            return closest;
        }

        [Server]
        private void CleanupDeadSummons()
        {
            for (int i = _activeSummons.Count - 1; i >= 0; i--)
            {
                var identity = _activeSummons[i];
                if (identity == null)
                {
                    _activeSummons.RemoveAt(i);
                    continue;
                }

                var hp = identity.GetComponent<EnemyHealth>();
                if (hp != null && hp.CurrentHP > 0)
                    continue;

                _activeSummons.RemoveAt(i);
            }
        }

        [Server]
        private void TryContactAttack(Transform target)
        {
            if (_contactAttackTimer > 0f)
                return;

            var hp = target.GetComponent<PlayerHealth>();
            if (hp == null)
                return;

            hp.TakeDamage(Mathf.Max(1, _damage), gameObject);
            _contactAttackTimer = contactAttackCooldown;
        }

        private void MoveTowards(Vector3 targetPos)
        {
            Vector3 dir = targetPos - _rb.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;

            dir.Normalize();
            float speed = (_moveSpeed > 0.01f) ? _moveSpeed : defaultMoveSpeed;
            _rb.MovePosition(_rb.position + dir * speed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
        }

        private void MoveAway(Vector3 targetPos)
        {
            Vector3 dir = _rb.position - targetPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;

            dir.Normalize();
            float speed = (_moveSpeed > 0.01f) ? _moveSpeed : defaultMoveSpeed;
            _rb.MovePosition(_rb.position + dir * speed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.LookRotation(-dir, Vector3.up));
        }

        private void LookAt(Vector3 targetPos)
        {
            Vector3 dir = targetPos - _rb.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;

            dir.Normalize();
            _rb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
        }

        private void UpdateCastIndicator()
        {
            if (!_isSummoning)
            {
                if (_castIndicator != null)
                    _castIndicator.SetActive(false);
                return;
            }

            EnsureCastIndicator();
            if (_castIndicator == null)
                return;

            _castIndicator.SetActive(true);
            _castIndicator.transform.position = transform.position + Vector3.up * 1.9f;

            float scale = 0.7f + Mathf.PingPong(Time.time * 3.5f, 0.2f);
            _castIndicator.transform.localScale = new Vector3(scale, scale, scale);
        }

        private void EnsureCastIndicator()
        {
            if (_castIndicator != null)
                return;

            _castIndicator = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _castIndicator.name = "SummonerCastIndicator_Runtime";
            Destroy(_castIndicator.GetComponent<Collider>());

            var renderer = _castIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.GetPropertyBlock(_castIndicatorBlock);
                _castIndicatorBlock.SetColor(ColorId, CastIndicatorColor);
                _castIndicatorBlock.SetColor(BaseColorId, CastIndicatorColor);
                renderer.SetPropertyBlock(_castIndicatorBlock);
            }
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
