using Mirror;
using UnityEngine;

namespace Project.Projectiles
{
    public sealed class EnemyProjectile : NetworkBehaviour
    {
        [SyncVar] public int Damage;
        [SyncVar] public float Speed;

        // Данные для детерминированного полёта на клиентах
        [SyncVar] private Vector3 _startPos;
        [SyncVar] private Vector3 _dir;
        [SyncVar] private double _startTime;

        [SerializeField] private float lifeTime = 6f;
        [SerializeField] private float radius = 0.15f;

        [Server]
        public void Init(int damage, float speed)
        {
            Damage = Mathf.Max(1, damage);
            Speed = Mathf.Max(0.5f, speed);

            _startPos = transform.position;
            _dir = transform.forward.normalized;
            _startTime = NetworkTime.time;
        }

        private void Update()
        {
            // 1) Двигаем снаряд ПЛАВНО у всех (сервер и клиенты)
            double t = NetworkTime.time - _startTime;
            if (t < 0) t = 0;

            transform.position = _startPos + _dir * Speed * (float)t;

            // 2) Время жизни / попадания — ТОЛЬКО сервер
            if (!NetworkServer.active)
                return;

            if (t > lifeTime)
            {
                NetworkServer.Destroy(gameObject);
                return;
            }

            // Простейшая проверка попаданий
            var hits = Physics.OverlapSphere(transform.position, radius);
            for (int i = 0; i < hits.Length; i++)
            {
                var ph = hits[i].GetComponentInParent<Project.Gameplay.PlayerHealth>();
                if (ph != null && ph.CurrentHP > 0)
                {
                    ph.TakeDamage(Damage);
                    NetworkServer.Destroy(gameObject);
                    return;
                }
            }
        }
    }
}