using Mirror;
using UnityEngine;

namespace Project.Gameplay
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class EnemySupportAI : NetworkBehaviour
    {
        [Header("Defaults")]
        [SerializeField] private int defaultDamage = 5;
        [SerializeField] private float defaultMoveSpeed = 2.8f;

        [Header("Support")]
        [SerializeField] private float buffRadius = 7f;
        [SerializeField] private float buffCooldown = 4f;
        [SerializeField] private float buffDuration = 3.5f;
        [SerializeField] private float damageBuffMultiplier = 1.25f;
        [SerializeField] private float moveSpeedBuffMultiplier = 1.15f;
        [SerializeField] private float preferredDistanceFromPlayer = 5.5f;
        [SerializeField] private float followAllyDistance = 3.5f;
        [SerializeField] private float emergencyRetreatDistance = 2.5f;
        [SerializeField] private float contactAttackRange = 1.3f;
        [SerializeField] private float contactAttackCooldown = 1.4f;

        [SyncVar] public bool IsAwake;
        [SyncVar] private int _damage;
        [SyncVar] private float _moveSpeed;

        private Rigidbody _rb;
        private float _buffTimer;
        private float _contactAttackTimer;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
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

            var targetPlayer = FindClosestAlivePlayer();
            var allyAnchor = FindClosestBuffTarget();

            _buffTimer -= Time.fixedDeltaTime;
            _contactAttackTimer -= Time.fixedDeltaTime;

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
                int buffed = ApplyBuffToNearbyAllies();
                if (buffed > 0)
                    _buffTimer = buffCooldown;
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

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
