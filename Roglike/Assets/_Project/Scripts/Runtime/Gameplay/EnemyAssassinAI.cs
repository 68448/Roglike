using Mirror;
using UnityEngine;

namespace Project.Gameplay
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemyAssassinAI : NetworkBehaviour
    {
        [Header("Defaults")]
        [SerializeField] private int defaultDamage = 9;
        [SerializeField] private float defaultMoveSpeed = 3.4f;

        [Header("Movement")]
        [SerializeField] private float stalkDistance = 6f;
        [SerializeField] private float disengageDistance = 2.4f;
        [SerializeField] private float contactAttackRange = 1.35f;
        [SerializeField] private float contactAttackCooldown = 1.2f;
        [SerializeField] private float contactDamageMultiplier = 0.7f;

        [Header("Dash Burst")]
        [SerializeField] private float dashTriggerRange = 7.5f;
        [SerializeField] private float dashWindup = 0.65f;
        [SerializeField] private float dashCooldown = 4.2f;
        [SerializeField] private float dashSpeedMultiplier = 3.25f;
        [SerializeField] private float dashDuration = 0.35f;
        [SerializeField] private float dashHitRange = 1.5f;
        [SerializeField] private float dashDamageMultiplier = 1.7f;
        [SerializeField] private float recoveryDuration = 0.75f;

        [SyncVar] public bool IsAwake;
        [SyncVar] private int _damage;
        [SyncVar] private float _moveSpeed;
        [SyncVar] private bool _isPreparingDash;
        [SyncVar] private bool _isDashing;

        private Rigidbody _rb;
        private float _contactAttackTimer;
        private float _dashCooldownTimer;
        private float _windupTimer;
        private float _dashTimer;
        private float _recoveryTimer;
        private Vector3 _dashTargetPosition;
        private Vector3 _dashDirection;
        private NetworkIdentity _dashTargetIdentity;
        private bool _dashHitApplied;

        private GameObject _dashMarker;
        private MaterialPropertyBlock _dashMarkerBlock;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _dashMarkerBlock = new MaterialPropertyBlock();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (_damage <= 0) _damage = defaultDamage;
            if (_moveSpeed <= 0.01f) _moveSpeed = defaultMoveSpeed;
            _dashCooldownTimer = dashCooldown * 0.5f;
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
            UpdateDashMarker();
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            if (!IsAwake) return;

            TickTimers();

            Transform target = FindClosestAlivePlayer();
            if (target == null)
                return;

            float dist = FlatDistance(_rb.position, target.position);

            if (_isPreparingDash)
            {
                LookAt(_dashTargetPosition);
                if (_windupTimer <= 0f)
                    BeginDash();
                return;
            }

            if (_isDashing)
            {
                ContinueDash();
                return;
            }

            if (_recoveryTimer > 0f)
            {
                LookAt(target.position);
                return;
            }

            if (dist < disengageDistance)
                MoveAway(target.position);
            else if (dist > stalkDistance)
                MoveTowards(target.position);
            else
                LookAt(target.position);

            if (dist <= contactAttackRange)
                TryContactAttack(target);

            if (_dashCooldownTimer <= 0f && dist <= dashTriggerRange)
                StartDash(target);
        }

        [Server]
        private void TickTimers()
        {
            _contactAttackTimer -= Time.fixedDeltaTime;
            _dashCooldownTimer -= Time.fixedDeltaTime;

            if (_isPreparingDash)
                _windupTimer -= Time.fixedDeltaTime;

            if (_isDashing)
                _dashTimer -= Time.fixedDeltaTime;

            if (_recoveryTimer > 0f)
                _recoveryTimer -= Time.fixedDeltaTime;
        }

        [Server]
        private void StartDash(Transform target)
        {
            _dashTargetPosition = target.position;
            _dashTargetIdentity = target.GetComponent<NetworkIdentity>();
            _windupTimer = Mathf.Max(0.1f, dashWindup);
            _isPreparingDash = true;
            _dashHitApplied = false;
        }

        [Server]
        private void BeginDash()
        {
            _isPreparingDash = false;
            _isDashing = true;
            _dashTimer = Mathf.Max(0.05f, dashDuration);

            Vector3 currentTargetPosition = _dashTargetPosition;
            if (_dashTargetIdentity != null)
                currentTargetPosition = _dashTargetIdentity.transform.position;

            _dashDirection = currentTargetPosition - _rb.position;
            _dashDirection.y = 0f;
            if (_dashDirection.sqrMagnitude < 0.001f)
                _dashDirection = transform.forward;

            _dashDirection.Normalize();
        }

        [Server]
        private void ContinueDash()
        {
            float speed = Mathf.Max(0.5f, _moveSpeed) * dashSpeedMultiplier;
            Vector3 next = _rb.position + _dashDirection * speed * Time.fixedDeltaTime;
            _rb.MovePosition(next);
            _rb.MoveRotation(Quaternion.LookRotation(_dashDirection, Vector3.up));

            TryApplyDashDamage();

            if (_dashTimer <= 0f)
            {
                _isDashing = false;
                _recoveryTimer = recoveryDuration;
                _dashCooldownTimer = dashCooldown;
            }
        }

        [Server]
        private void TryApplyDashDamage()
        {
            if (_dashHitApplied)
                return;

            var players = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt((_damage > 0 ? _damage : defaultDamage) * dashDamageMultiplier));

            for (int i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null || player.CurrentHP <= 0)
                    continue;

                if (FlatDistance(_rb.position, player.transform.position) > dashHitRange)
                    continue;

                player.TakeDamage(finalDamage, gameObject);
                _dashHitApplied = true;
                return;
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

            int dmg = Mathf.Max(1, Mathf.RoundToInt((_damage > 0 ? _damage : defaultDamage) * contactDamageMultiplier));
            hp.TakeDamage(dmg, gameObject);
            _contactAttackTimer = contactAttackCooldown;
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

        private void MoveTowards(Vector3 targetPos)
        {
            Vector3 dir = targetPos - _rb.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f)
                return;

            dir.Normalize();
            float speed = (_moveSpeed > 0.01f) ? _moveSpeed : defaultMoveSpeed;
            _rb.MovePosition(_rb.position + dir * speed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
        }

        private void MoveAway(Vector3 targetPos)
        {
            Vector3 dir = _rb.position - targetPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f)
                return;

            dir.Normalize();
            float speed = (_moveSpeed > 0.01f) ? _moveSpeed : defaultMoveSpeed;
            _rb.MovePosition(_rb.position + dir * speed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.LookRotation(-dir, Vector3.up));
        }

        private void LookAt(Vector3 targetPos)
        {
            Vector3 dir = targetPos - _rb.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f)
                return;

            dir.Normalize();
            _rb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
        }

        private void UpdateDashMarker()
        {
            bool shouldShow = _isPreparingDash || _isDashing;
            if (!shouldShow)
            {
                if (_dashMarker != null)
                    _dashMarker.SetActive(false);
                return;
            }

            EnsureDashMarker();
            if (_dashMarker == null)
                return;

            _dashMarker.SetActive(true);
            _dashMarker.transform.position = transform.position + Vector3.up * 1.7f;

            float scale = _isPreparingDash
                ? 0.55f + Mathf.PingPong(Time.time * 4f, 0.25f)
                : 0.9f + Mathf.PingPong(Time.time * 10f, 0.15f);
            _dashMarker.transform.localScale = new Vector3(scale, scale, scale);
        }

        private void EnsureDashMarker()
        {
            if (_dashMarker != null)
                return;

            _dashMarker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _dashMarker.name = "AssassinDashMarker_Runtime";
            Destroy(_dashMarker.GetComponent<Collider>());

            var renderer = _dashMarker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.GetPropertyBlock(_dashMarkerBlock);
                _dashMarkerBlock.SetColor(ColorId, new Color(1f, 0.15f, 0.6f, 1f));
                _dashMarkerBlock.SetColor(BaseColorId, new Color(1f, 0.15f, 0.6f, 1f));
                renderer.SetPropertyBlock(_dashMarkerBlock);
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
