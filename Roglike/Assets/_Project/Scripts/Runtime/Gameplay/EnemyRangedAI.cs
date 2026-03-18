using Mirror;
using UnityEngine;

namespace Project.Gameplay
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemyRangedAI : NetworkBehaviour
    {
        [Header("Defaults (if scaling not applied)")]
        [SerializeField] private int defaultDamage = 8;
        [SerializeField] private float defaultMoveSpeed = 2.6f;

        [Header("Ranged")]
        [SerializeField] private float shootRange = 10f;
        [SerializeField] private float keepDistance = 6f;
        [SerializeField] private float shootCooldown = 1.6f;

        [Header("Projectile")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private float projectileSpeed = 10f;
        [SerializeField] private Transform muzzle; // точка вылета (можно пустышку)

        [SyncVar] public bool IsAwake;

        [SyncVar] private int _damage;
        [SyncVar] private float _moveSpeed;

        private Rigidbody _rb;
        private float _shootTimer;
        private float _supportDamageMultiplier = 1f;
        private float _supportMoveSpeedMultiplier = 1f;
        private float _supportBuffTimer;
        private MaterialPropertyBlock _buffBlock;
        private GameObject _supportBuffMarker;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _buffBlock = new MaterialPropertyBlock();
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

        private void FixedUpdate()
        {
            if (!NetworkServer.active) return;
            if (!IsAwake) return;

            TickSupportBuffTimer();

            var target = FindClosestAlivePlayer();
            if (target == null) return;

            Vector3 a = _rb.position; a.y = 0;
            Vector3 b = target.position; b.y = 0;
            float dist = Vector3.Distance(a, b);

            // Движение: держим дистанцию
            if (dist < keepDistance)
            {
                // отходим назад
                MoveAway(target.position);
            }
            else if (dist > shootRange)
            {
                // подходим ближе, если слишком далеко
                MoveTowards(target.position);
            }
            else
            {
                // в пределах комфортной дистанции — просто поворачиваемся к цели
                LookAt(target.position);
            }

            // Стрельба если в радиусе
            _shootTimer -= Time.fixedDeltaTime;
            if (dist <= shootRange && _shootTimer <= 0f)
            {
                Shoot(target.position);
                _shootTimer = shootCooldown;
            }
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
            if (dir.sqrMagnitude < 0.001f) return;

            dir.Normalize();
            float speed = ((_moveSpeed > 0.01f) ? _moveSpeed : defaultMoveSpeed) * _supportMoveSpeedMultiplier;

            _rb.MovePosition(_rb.position + dir * speed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
        }

        private void MoveAway(Vector3 targetPos)
        {
            Vector3 dir = (_rb.position - targetPos);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;

            dir.Normalize();
            float speed = ((_moveSpeed > 0.01f) ? _moveSpeed : defaultMoveSpeed) * _supportMoveSpeedMultiplier;

            _rb.MovePosition(_rb.position + dir * speed * Time.fixedDeltaTime);
            _rb.MoveRotation(Quaternion.LookRotation(-dir, Vector3.up)); // смотрит на цель
        }

        private void LookAt(Vector3 targetPos)
        {
            Vector3 dir = (targetPos - _rb.position);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) return;

            dir.Normalize();
            _rb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
        }

        [Server]
        private void Shoot(Vector3 targetPos)
        {
            if (projectilePrefab == null) return;

            Vector3 origin = (muzzle != null) ? muzzle.position : (transform.position + Vector3.up * 1.2f);
            Vector3 dir = (targetPos - origin);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) dir = transform.forward;
            dir.Normalize();

            var go = Instantiate(projectilePrefab, origin, Quaternion.LookRotation(dir, Vector3.up));

            var p = go.GetComponent<Project.Projectiles.EnemyProjectile>();
            if (p != null)
                p.Init(Mathf.Max(1, Mathf.RoundToInt(_damage * _supportDamageMultiplier)), projectileSpeed);

            NetworkServer.Spawn(go);
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
                        renderer.GetPropertyBlock(_buffBlock);
                        _buffBlock.SetColor("_Color", Color.green);
                        _buffBlock.SetColor("_BaseColor", Color.green);
                        renderer.SetPropertyBlock(_buffBlock);
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
