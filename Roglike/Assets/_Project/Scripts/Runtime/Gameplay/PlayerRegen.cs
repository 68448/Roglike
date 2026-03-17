using Mirror;
using UnityEngine;

namespace Project.Gameplay
{
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerRegen : NetworkBehaviour
    {
        private PlayerHealth _health;
        private Project.Player.PlayerStats _stats;

        [SerializeField] private float tickInterval = 1.0f;
        private float _t;

        private void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            _stats = GetComponent<Project.Player.PlayerStats>();
        }

        [ServerCallback]
        private void Update()
        {
            if (_health == null || _stats == null) return;
            if (_health.CurrentHP <= 0) return;

            int per10 = _stats.RegenPer10s;
            if (per10 <= 0) return;

            _t += Time.deltaTime;
            if (_t < tickInterval) return;
            _t = 0f;

            // regen per second
            float perSecond = per10 / 10f;
            int heal = Mathf.FloorToInt(perSecond * tickInterval);
            if (heal <= 0) heal = 1;

            _health.ServerHeal(heal);
        }
    }
}