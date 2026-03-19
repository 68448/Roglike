using Mirror;
using UnityEngine;

namespace Project.Gameplay
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemySupportAI : NetworkBehaviour
    {
        private static readonly Color CastIndicatorColor = new(0.35f, 1f, 0.45f, 1f);

        [Header("Defaults")]
        [SerializeField] private int defaultDamage = 5;
        [SerializeField] private float defaultMoveSpeed = 2.8f;

        [Header("Support")]
        [SerializeField] private float buffRadius = 7f;
        [SerializeField] private float buffCooldown = 4f;
        [SerializeField] private float buffWindup = 0.8f;
        [SerializeField] private float buffRecovery = 0.35f;
        [SerializeField] private float buffDuration = 3.5f;
        [SerializeField] private float damageBuffMultiplier = 1.25f;
        [SerializeField] private float moveSpeedBuffMultiplier = 1.15f;
        [SerializeField] private float preferredDistanceFromPlayer = 5.5f;
        [SerializeField] private float followAllyDistance = 3.5f;
        [SerializeField] private float emergencyRetreatDistance = 2.5f;
        [SerializeField] private float contactAttackRange = 1.3f;
        [SerializeField] private float contactAttackCooldown = 1.4f;

        [SyncVar] public bool IsAwake;
        [SyncVar] private bool _isCastingBuff;
        [SyncVar] private int _damage;
        [SyncVar] private float _moveSpeed;

        private Rigidbody _rb;
        private float _buffTimer;
        private float _buffWindupTimer;
        private float _buffRecoveryTimer;
        private float _contactAttackTimer;
        private GameObject _castIndicator;
        private MaterialPropertyBlock _castIndicatorBlock;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _castIndicatorBlock = new MaterialPropertyBlock();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_damage <= 0) _damage = defaultDamage;
            if (_moveSpeed <= 0.01f) _moveSpeed = defaultMoveSpeed;
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

            var targetPlayer = FindClosestAlivePlayer();
            var allyAnchor = FindClosestBuffTarget();

            _buffTimer -= Time.fixedDeltaTime;
            _contactAttackTimer -= Time.fixedDeltaTime;

            if (_buffRecoveryTimer > 0f)
            {
                _buffRecoveryTimer -= Time.fixedDeltaTime;
                if (targetPlayer != null)
                    LookAt(targetPlayer.position);
                return;
            }

            if (_isCastingBuff)
            {
                _buffWindupTimer -= Time.fixedDeltaTime;
                if (targetPlayer != null)
                    LookAt(targetPlayer.position);

                if (_buffWindupTimer <= 0f)
                {
                    int buffed = ApplyBuffToNearbyAllies();
                    _isCastingBuff = false;
                    _buffRecoveryTimer = buffRecovery;
                    if (buffed > 0)
                        _buffTimer = buffCooldown;
                }

                return;
            }

            if (targetPlayer != null)
            {
                float playerDist = FlatDistance(_rb.position, targetPlayer.position);

                if (playerDist < emergencyRetreatDistance)
                    MoveAway(targetPlayer.position);
                else if (allyAnchor != null && FlatDistance(_rb.position, allyAnchor.position) > followAllyDistance)
                    MoveTowards(allyAnchor.position);
                else if (playerDist > preferredDistanceFromPlayer)
                    MoveTowards(targetPlayer.position);
                else
                    LookAt(targetPlayer.position);

                if (playerDist <= contactAttackRange)
                    TryContactAttack(targetPlayer);
            }

            if (_buffTimer <= 0f)
            {
                if (HasBuffableAlliesInRange())
                    StartBuffCast();
            }
        }

        [Server]
        private void StartBuffCast()
        {
            _isCastingBuff = true;
            _buffWindupTimer = Mathf.Max(0.1f, buffWindup);
        }

        private Transform FindClosestAlivePlayer()
        {
            var players = FindObjectsByType<Project.Gameplay.PlayerHealth>(FindObjectsSortMode.None);

            Transform closest = null;
            float best = float.MaxValue;

            foreach (var p in players)
            {
                if (p == null || p.CurrentHP <= 0) continue;

                float d = FlatDistance(transform.position, p.transform.position);
                if (d < best)
                {
                    best = d;
                    closest = p.transform;
                }
            }

            return closest;
        }

        private Transform FindClosestBuffTarget()
        {
            Transform closest = null;
            float best = float.MaxValue;

            var meleeAllies = FindObjectsByType<Project.Gameplay.EnemyAI>(FindObjectsSortMode.None);
            for (int i = 0; i < meleeAllies.Length; i++)
            {
                var ally = meleeAllies[i];
                if (ally == null || ally.gameObject == gameObject || !ally.IsAwake) continue;

                float d = FlatDistance(transform.position, ally.transform.position);
                if (d < best)
                {
                    best = d;
                    closest = ally.transform;
                }
            }

            var rangedAllies = FindObjectsByType<Project.Gameplay.EnemyRangedAI>(FindObjectsSortMode.None);
            for (int i = 0; i < rangedAllies.Length; i++)
            {
                var ally = rangedAllies[i];
                if (ally == null || ally.gameObject == gameObject || !ally.IsAwake) continue;

                float d = FlatDistance(transform.position, ally.transform.position);
                if (d < best)
                {
                    best = d;
                    closest = ally.transform;
                }
            }

            return closest;
        }

        [Server]
        private int ApplyBuffToNearbyAllies()
        {
            int buffed = 0;

            var meleeAllies = FindObjectsByType<Project.Gameplay.EnemyAI>(FindObjectsSortMode.None);
            for (int i = 0; i < meleeAllies.Length; i++)
            {
                var ally = meleeAllies[i];
                if (ally == null || ally.gameObject == gameObject || !ally.IsAwake) continue;
                if (FlatDistance(transform.position, ally.transform.position) > buffRadius) continue;

                ally.ServerApplySupportBuff(damageBuffMultiplier, moveSpeedBuffMultiplier, buffDuration);
                buffed++;
            }

            var rangedAllies = FindObjectsByType<Project.Gameplay.EnemyRangedAI>(FindObjectsSortMode.None);
            for (int i = 0; i < rangedAllies.Length; i++)
            {
                var ally = rangedAllies[i];
                if (ally == null || ally.gameObject == gameObject || !ally.IsAwake) continue;
                if (FlatDistance(transform.position, ally.transform.position) > buffRadius) continue;

                ally.ServerApplySupportBuff(damageBuffMultiplier, moveSpeedBuffMultiplier, buffDuration);
                buffed++;
            }

            return buffed;
        }

        [Server]
        private bool HasBuffableAlliesInRange()
        {
            var meleeAllies = FindObjectsByType<Project.Gameplay.EnemyAI>(FindObjectsSortMode.None);
            for (int i = 0; i < meleeAllies.Length; i++)
            {
                var ally = meleeAllies[i];
                if (ally == null || ally.gameObject == gameObject || !ally.IsAwake) continue;
                if (FlatDistance(transform.position, ally.transform.position) <= buffRadius) return true;
            }

            var rangedAllies = FindObjectsByType<Project.Gameplay.EnemyRangedAI>(FindObjectsSortMode.None);
            for (int i = 0; i < rangedAllies.Length; i++)
            {
                var ally = rangedAllies[i];
                if (ally == null || ally.gameObject == gameObject || !ally.IsAwake) continue;
                if (FlatDistance(transform.position, ally.transform.position) <= buffRadius) return true;
            }

            return false;
        }

        [Server]
        private void TryContactAttack(Transform target)
        {
            if (_contactAttackTimer > 0f)
                return;

            var hp = target.GetComponent<Project.Gameplay.PlayerHealth>();
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
            if (!_isCastingBuff)
            {
                if (_castIndicator != null)
                    _castIndicator.SetActive(false);
                return;
            }

            EnsureCastIndicator();
            if (_castIndicator == null)
                return;

            _castIndicator.SetActive(true);
            _castIndicator.transform.position = transform.position + Vector3.up * 1.8f;

            float scale = 0.65f + Mathf.PingPong(Time.time * 3f, 0.2f);
            _castIndicator.transform.localScale = new Vector3(scale, scale, scale);
        }

        private void EnsureCastIndicator()
        {
            if (_castIndicator != null)
                return;

            _castIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _castIndicator.name = "SupportCastIndicator_Runtime";
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
