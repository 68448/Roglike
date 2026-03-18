using Mirror;
using UnityEngine;

namespace Project.Gameplay
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemyTankAI : NetworkBehaviour
    {
        [Header("Defaults")]
        [SerializeField] private int defaultDamage = 14;
        [SerializeField] private float defaultMoveSpeed = 2.2f;

        [Header("Movement")]
        [SerializeField] private float stopDistance = 1.4f;
        [SerializeField] private float attackRange = 1.9f;

        [Header("Attack")]
        [SerializeField] private float attackCooldown = 1.8f;

        [Header("Guard")]
        [SerializeField] private float guardCooldown = 5f;
        [SerializeField] private float guardDuration = 2.2f;
        [SerializeField] private float guardedDamageMultiplier = 0.4f;
        [SerializeField] private float guardedMoveSpeedMultiplier = 0.55f;

        [SyncVar] public bool IsAwake;
        [SyncVar] private int _damage;
        [SyncVar] private float _moveSpeed;
        [SyncVar] private bool _isGuarding;

        private Rigidbody _rb;
        private float _attackTimer;
        private float _guardCooldownTimer;
        private float _guardTimer;
        private MaterialPropertyBlock _block;
        private GameObject _guardMarker;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _block = new MaterialPropertyBlock();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_damage <= 0) _damage = defaultDamage;
            if (_moveSpeed <= 0.01f) _moveSpeed = defaultMoveSpeed;
            _guardCooldownTimer = guardCooldown;
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

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            if (!IsAwake) return;

            TickGuardState();

            var target = FindClosestAlivePlayer();
            if (target == null) return;

            Vector3 a = _rb.position; a.y = 0f;
            Vector3 b = target.position; b.y = 0f;
            float dist = Vector3.Distance(a, b);

            if (dist > stopDistance)
                MoveTowards(target.position);

            if (dist <= attackRange)
                TryAttack(target);
        }

        [Server]
        public int ModifyIncomingDamage(int amount)
        {
            if (!_isGuarding)
                return amount;

            return Mathf.Max(1, Mathf.RoundToInt(amount * guardedDamageMultiplier));
        }

        [Server]
        private void TickGuardState()
        {
            if (_isGuarding)
            {
                _guardTimer -= Time.fixedDeltaTime;
                if (_guardTimer <= 0f)
                {
                    _isGuarding = false;
                    _guardTimer = 0f;
                    _guardCooldownTimer = guardCooldown;
                    RpcSetGuardVisual(false);
                }
                return;
            }

            _guardCooldownTimer -= Time.fixedDeltaTime;
            if (_guardCooldownTimer <= 0f)
            {
                _isGuarding = true;
                _guardTimer = guardDuration;
                _guardCooldownTimer = guardCooldown;
                RpcSetGuardVisual(true);
            }
        }

        private Transform FindClosestAlivePlayer()
        {
            var players = FindObjectsByType<Project.Gameplay.PlayerHealth>(FindObjectsSortMode.None);

            Transform closest = null;
            float best = float.MaxValue;

            foreach (var p in players)
            {
                if (p == null || p.CurrentHP <= 0) continue;

                float d = Vector3.Distance(transform.position, p.transform.position);
                if (d < best)
                {
                    best = d;
                    closest = p.transform;
                }
            }

            return closest;
        }

        private void MoveTowards(Vector3 targetPos)
        {
            Vector3 dir = targetPos - _rb.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f)
                return;

            dir.Normalize();

            float baseSpeed = (_moveSpeed > 0.01f) ? _moveSpeed : defaultMoveSpeed;
            float finalSpeed = _isGuarding ? baseSpeed * guardedMoveSpeedMultiplier : baseSpeed;

            Vector3 next = _rb.position + dir * finalSpeed * Time.fixedDeltaTime;
            _rb.MovePosition(next);
            _rb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
        }

        private void TryAttack(Transform target)
        {
            if (_attackTimer > 0f)
            {
                _attackTimer -= Time.fixedDeltaTime;
                return;
            }

            int dmg = (_damage > 0) ? _damage : defaultDamage;
            if (_isGuarding)
                dmg = Mathf.Max(1, Mathf.RoundToInt(dmg * 0.75f));

            var hp = target.GetComponent<Project.Gameplay.PlayerHealth>();
            if (hp == null)
                return;

            hp.TakeDamage(dmg, gameObject);
            _attackTimer = attackCooldown;
        }

        [ClientRpc]
        private void RpcSetGuardVisual(bool active)
        {
            if (active)
            {
                if (_guardMarker == null)
                {
                    _guardMarker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    _guardMarker.name = "TankGuardMarker_Runtime";
                    Destroy(_guardMarker.GetComponent<Collider>());
                    _guardMarker.transform.SetParent(transform, false);
                    _guardMarker.transform.localPosition = new Vector3(0f, 2.1f, 0f);
                    _guardMarker.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);

                    var renderer = _guardMarker.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.GetPropertyBlock(_block);
                        _block.SetColor(ColorId, Color.cyan);
                        _block.SetColor(BaseColorId, Color.cyan);
                        renderer.SetPropertyBlock(_block);
                    }
                }

                _guardMarker.SetActive(true);
            }
            else if (_guardMarker != null)
            {
                _guardMarker.SetActive(false);
            }
        }
    }
}
