using Mirror;
using UnityEngine;

namespace Project.Gameplay
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemyAreaControlAI : NetworkBehaviour
    {
        private const float GroundProbeHeight = 200f;
        private const float GroundProbeDistance = 500f;
        private static readonly Color CastIndicatorColor = new(1f, 0.5f, 0.1f, 1f);

        [Header("Defaults")]
        [SerializeField] private int defaultDamage = 7;
        [SerializeField] private float defaultMoveSpeed = 2.5f;

        [Header("Movement")]
        [SerializeField] private float preferredDistanceFromPlayer = 7f;
        [SerializeField] private float retreatDistance = 3f;
        [SerializeField] private float contactAttackRange = 1.4f;
        [SerializeField] private float contactAttackCooldown = 1.6f;

        [Header("Hazard Zone")]
        [SerializeField] private GameObject hazardZonePrefab;
        [SerializeField] private float castRange = 9f;
        [SerializeField] private float castCooldown = 4.5f;
        [SerializeField] private float castWindup = 1.1f;
        [SerializeField] private float zoneRadius = 2.5f;
        [SerializeField] private float zoneLifetime = 4f;
        [SerializeField] private float zoneTickInterval = 0.75f;
        [SerializeField] private float zoneDamageMultiplier = 0.4f;

        [SyncVar] public bool IsAwake;
        [SyncVar] private bool _isCasting;
        [SyncVar] private int _damage;
        [SyncVar] private float _moveSpeed;

        private Rigidbody _rb;
        private float _contactAttackTimer;
        private float _castTimer;
        private float _castWindupTimer;
        private Vector3 _pendingCastTarget;
        private GameObject _castIndicator;
        private MaterialPropertyBlock _castIndicatorBlock;

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
            _castTimer = castCooldown;
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

            var target = FindClosestAlivePlayer();
            if (target == null) return;

            _contactAttackTimer -= Time.fixedDeltaTime;
            _castTimer -= Time.fixedDeltaTime;

            float dist = FlatDistance(_rb.position, target.position);

            if (_isCasting)
            {
                LookAt(_pendingCastTarget);
                _castWindupTimer -= Time.fixedDeltaTime;

                if (_castWindupTimer <= 0f)
                {
                    _isCasting = false;
                    _castTimer = castCooldown;
                }
                return;
            }

            if (dist < retreatDistance)
                MoveAway(target.position);
            else if (dist > preferredDistanceFromPlayer)
                MoveTowards(target.position);
            else
                LookAt(target.position);

            if (dist <= contactAttackRange)
                TryContactAttack(target);

            if (dist <= castRange && _castTimer <= 0f)
            {
                StartCast(target.position);
            }
        }

        private void Update()
        {
            UpdateCastIndicator();
        }

        [Server]
        private void StartCast(Vector3 targetPos)
        {
            if (hazardZonePrefab == null)
                return;

            _pendingCastTarget = ResolveGroundPosition(targetPos);
            SpawnHazardZone(_pendingCastTarget);
            _castWindupTimer = Mathf.Max(0.1f, castWindup);
            _isCasting = true;
        }

        [Server]
        private void SpawnHazardZone(Vector3 targetPos)
        {
            if (hazardZonePrefab == null)
                return;

            Vector3 spawnPos = ResolveGroundPosition(targetPos);
            var zone = Instantiate(hazardZonePrefab, spawnPos, Quaternion.identity);

            var hazard = zone.GetComponent<EnemyHazardZone>();
            if (hazard != null)
            {
                int tickDamage = Mathf.Max(1, Mathf.RoundToInt(_damage * zoneDamageMultiplier));
                hazard.ServerInit(tickDamage, zoneTickInterval, zoneRadius, castWindup, zoneLifetime);
            }

            NetworkServer.Spawn(zone);
        }

        [Server]
        private static Vector3 ResolveGroundPosition(Vector3 targetPos)
        {
            Vector3 origin = new Vector3(targetPos.x, targetPos.y + GroundProbeHeight, targetPos.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, GroundProbeDistance, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length > 0)
            {
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                for (int i = 0; i < hits.Length; i++)
                {
                    var hit = hits[i];
                    if (!IsValidGroundHit(hit))
                        continue;

                    return BuildSurfacePosition(hit, targetPos);
                }
            }

            return targetPos;
        }

        private static bool IsValidGroundHit(RaycastHit hit)
        {
            if (hit.collider == null)
                return false;

            if (hit.normal.y < 0.35f)
                return false;

            if (hit.collider.attachedRigidbody != null)
                return false;

            if (hit.collider.GetComponentInParent<PlayerHealth>() != null)
                return false;

            if (hit.collider.GetComponentInParent<EnemyHealth>() != null)
                return false;

            return true;
        }

        private static Vector3 BuildSurfacePosition(RaycastHit hit, Vector3 fallbackPosition)
        {
            float y = hit.point.y;

            var renderer = hit.collider.GetComponent<Renderer>();
            if (renderer == null)
                renderer = hit.collider.GetComponentInParent<Renderer>();

            if (renderer != null)
                y = renderer.bounds.max.y;
            else
                y = Mathf.Max(y, hit.collider.bounds.max.y);

            return new Vector3(fallbackPosition.x, y + 0.02f, fallbackPosition.z);
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
            if (!_isCasting)
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

            float t = 0.75f + Mathf.PingPong(Time.time * 2f, 0.35f);
            _castIndicator.transform.localScale = new Vector3(t, t, t);
        }

        private void EnsureCastIndicator()
        {
            if (_castIndicator != null)
                return;

            _castIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _castIndicator.name = "AreaControlCastIndicator_Runtime";
            Destroy(_castIndicator.GetComponent<Collider>());

            var renderer = _castIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.GetPropertyBlock(_castIndicatorBlock);
                _castIndicatorBlock.SetColor("_Color", CastIndicatorColor);
                _castIndicatorBlock.SetColor("_BaseColor", CastIndicatorColor);
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
