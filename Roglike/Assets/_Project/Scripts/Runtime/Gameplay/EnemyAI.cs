using Mirror;
using UnityEngine;

namespace Project.Gameplay
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemyAI : NetworkBehaviour
    {
        [Header("Defaults (used if scaling not applied)")]
        [SerializeField] private int defaultDamage = 10;
        [SerializeField] private float defaultMoveSpeed = 3f;

        [Header("Movement")]
        [SerializeField] private float stopDistance = 1.2f;   // ВАЖНО: меньше attackRange
        [SerializeField] private float attackRange = 1.8f;

        [Header("Attack")]
        [SerializeField] private float attackCooldown = 1.2f;

        // scaling values (server-driven)
        [SyncVar] private int _damage;
        [SyncVar] private float _moveSpeed;
        
        [SyncVar(hook = nameof(OnEliteChanged))]
        public bool IsElite;

        private MaterialPropertyBlock _mpb;
        private float _supportDamageMultiplier = 1f;
        private float _supportMoveSpeedMultiplier = 1f;
        private float _supportBuffTimer;
        private GameObject _supportBuffMarker;

        // room activation
        [SyncVar] public bool IsAwake;

        private Rigidbody _rb;
        private float _attackTimer;

        [SerializeField] private GameObject eliteMarker;

        private GameObject _eliteMarkerInstance;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();

            _mpb = new MaterialPropertyBlock(); // ✅ создаём здесь
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyEliteVisual(IsElite); // на случай если значение уже есть
        }


        public override void OnStartServer()
        {
            base.OnStartServer();

            // ✅ страховка: если scaling ещё не применили, задаём дефолты
            if (_damage <= 0) _damage = defaultDamage;
            if (_moveSpeed <= 0.01f) _moveSpeed = defaultMoveSpeed;
        }

        [Server]
        public void ServerApplyScaling(int damage, float moveSpeed, bool isElite)
        {
            _damage = Mathf.Max(1, damage);
            _moveSpeed = Mathf.Max(0.5f, moveSpeed);
            IsElite = isElite;
        }

        [Server]
        public void ServerWakeUp()
        {
            // ✅ страховка на случай, если scaling не успел примениться
            if (_damage <= 0) _damage = defaultDamage;
            if (_moveSpeed <= 0.01f) _moveSpeed = defaultMoveSpeed;

            IsAwake = true;
        }

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            if (!IsAwake) return;

            TickSupportBuffTimer();

            var target = FindClosestAlivePlayer();
            if (target == null) return;

            // ✅ считаем расстояние в плоскости XZ (без Y)
            Vector3 a = _rb.position; a.y = 0;
            Vector3 b = target.position; b.y = 0;
            float dist = Vector3.Distance(a, b);

            if (dist > stopDistance)
                MoveTowards(target.position);

            if (dist <= attackRange)
                TryAttack(target);
        }

        private Transform FindClosestAlivePlayer()
        {
            var players = FindObjectsByType<Project.Gameplay.PlayerHealth>(FindObjectsSortMode.None);

            Transform closest = null;
            float best = float.MaxValue;

            foreach (var p in players)
            {
                if (p.CurrentHP <= 0) continue;

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
            Vector3 dir = (targetPos - _rb.position);
            dir.y = 0f;

            if (dir.sqrMagnitude < 0.001f)
                return;

            dir.Normalize();

            float speed = ((_moveSpeed > 0.01f) ? _moveSpeed : defaultMoveSpeed) * _supportMoveSpeedMultiplier;

            Vector3 next = _rb.position + dir * speed * Time.fixedDeltaTime;
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

            int dmg = Mathf.Max(1, Mathf.RoundToInt(((_damage > 0) ? _damage : defaultDamage) * _supportDamageMultiplier));

            var hp = target.GetComponent<Project.Gameplay.PlayerHealth>();
            if (hp != null)
            {
                hp.TakeDamage(dmg, gameObject);
                _attackTimer = attackCooldown;
            }
        }

        [Server]
        public void ServerApplySupportBuff(float damageMultiplier, float moveSpeedMultiplier, float duration)
        {
            _supportDamageMultiplier = Mathf.Max(_supportDamageMultiplier, Mathf.Max(1f, damageMultiplier));
            _supportMoveSpeedMultiplier = Mathf.Max(_supportMoveSpeedMultiplier, Mathf.Max(1f, moveSpeedMultiplier));
            _supportBuffTimer = Mathf.Max(_supportBuffTimer, Mathf.Max(0f, duration));
            RpcSetSupportBuffVisual(true);
        }

        [Server]
        private void TickSupportBuffTimer()
        {
            if (_supportBuffTimer <= 0f)
                return;

            _supportBuffTimer -= Time.fixedDeltaTime;
            if (_supportBuffTimer > 0f)
                return;

            _supportBuffTimer = 0f;
            _supportDamageMultiplier = 1f;
            _supportMoveSpeedMultiplier = 1f;
            RpcSetSupportBuffVisual(false);
        }

        private void OnEliteChanged(bool oldValue, bool newValue)
        {
            ApplyEliteVisual(newValue);
        }

        

        private void ApplyEliteVisual(bool elite)
        {
            // 1) 100% заметный маркер (не зависит от шейдера)
            if (elite)
            {
                if (_eliteMarkerInstance == null)
                {
                    _eliteMarkerInstance = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    _eliteMarkerInstance.name = "EliteMarker_Runtime";
                    Destroy(_eliteMarkerInstance.GetComponent<Collider>());

                    _eliteMarkerInstance.transform.SetParent(transform, false);
                    _eliteMarkerInstance.transform.localPosition = new Vector3(0f, 1.8f, 0f);
                    _eliteMarkerInstance.transform.localScale = Vector3.one * 0.35f;
                }
                _eliteMarkerInstance.SetActive(true);

                transform.localScale = Vector3.one * 1.15f;
            }
            else
            {
                if (_eliteMarkerInstance != null)
                    _eliteMarkerInstance.SetActive(false);

                transform.localScale = Vector3.one;
            }

            // 2) Дополнительно красим (если получится) — учитываем URP и Standard
            var r = GetComponentInChildren<Renderer>();
            if (r == null) return;

            r.GetPropertyBlock(_mpb);

            if (elite)
            {
                // Standard
                _mpb.SetColor("_Color", Color.magenta);
                // URP/Lit
                _mpb.SetColor("_BaseColor", Color.magenta);
                // (опционально) эмиссия, если материал её поддерживает
                _mpb.SetColor("_EmissionColor", Color.magenta * 1.2f);
            }
            else
            {
                _mpb.SetColor("_Color", Color.white);
                _mpb.SetColor("_BaseColor", Color.white);
                _mpb.SetColor("_EmissionColor", Color.black);
            }

            r.SetPropertyBlock(_mpb);
        }

        [ClientRpc]
        private void RpcSetSupportBuffVisual(bool active)
        {
            if (active)
            {
                if (_supportBuffMarker == null)
                {
                    _supportBuffMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    _supportBuffMarker.name = "SupportBuffMarker_Runtime";
                    Destroy(_supportBuffMarker.GetComponent<Collider>());
                    _supportBuffMarker.transform.SetParent(transform, false);
                    _supportBuffMarker.transform.localPosition = new Vector3(0f, 2.2f, 0f);
                    _supportBuffMarker.transform.localScale = Vector3.one * 0.22f;

                    var renderer = _supportBuffMarker.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.GetPropertyBlock(_mpb);
                        _mpb.SetColor("_Color", Color.green);
                        _mpb.SetColor("_BaseColor", Color.green);
                        renderer.SetPropertyBlock(_mpb);
                    }
                }

                _supportBuffMarker.SetActive(true);
            }
            else if (_supportBuffMarker != null)
            {
                _supportBuffMarker.SetActive(false);
            }
        }


    }
}
