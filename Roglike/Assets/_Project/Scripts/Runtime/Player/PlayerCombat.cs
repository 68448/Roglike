using Mirror;
using Project.Gameplay;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Player
{
    /// <summary>
    /// MVP-атака: ЛКМ -> серверный OverlapSphere -> урон по врагу.
    /// </summary>
    public sealed class PlayerCombat : NetworkBehaviour
    {
        [SerializeField] private float attackRadius = 2.2f;
        [SerializeField] private float attackCooldown = 0.25f;
        [SerializeField] private int damage = 10;
        [SerializeField] private LayerMask enemyMask;
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        private double _nextAttackAt;

        private static Dictionary<int, int> _weaponDamageByItemId;
        private static bool _weaponCacheReady;

        private void Update()
        {
            if (!isLocalPlayer)
                return;

            if (Input.GetMouseButtonDown(0))
            {
                CmdAttack();
            }
        }

        // [Command]
        // private void CmdAttack(Vector3 playerPos)
        // {
        //     Collider[] hits = Physics.OverlapSphere(playerPos, attackRadius, enemyMask);
        //     if (hits == null || hits.Length == 0)
        //         return;

        //     // Бьём первого попавшегося
        //     var enemyHealth = hits[0].GetComponentInParent<EnemyHealth>();
        //     if (enemyHealth != null)
        //     {
        //         var inv = GetComponent<Project.Player.PlayerInventory>();
        //         int finalDamage = damage + (inv != null ? inv.TotalDamageBonus : 0);

        //         enemyHealth.TakeDamage(finalDamage);
        //         Project.Gameplay.LifestealHelper.TryApplyLifesteal(gameObject, finalDamage);
        //     }
        // }

        [Command]
        private void CmdAttack()
        {
            // Server-side rate limiting: protects from client spam/macros.
            if (NetworkTime.time < _nextAttackAt)
                return;
            _nextAttackAt = NetworkTime.time + attackCooldown;

            Vector3 serverPos = transform.position;
            Collider[] hits = Physics.OverlapSphere(serverPos, attackRadius, enemyMask, triggerInteraction);
            if (hits == null || hits.Length == 0)
                return;

            EnemyHealth enemyHealth = FindClosestAliveEnemy(hits, serverPos);
            if (enemyHealth == null)
                return;

            int weaponBonus = 0;
            var equip = GetComponent<Project.Player.PlayerEquipment>();
            if (equip != null && equip.WeaponItemId > 0)
            {
                weaponBonus = GetWeaponDamageBonus(equip.WeaponItemId);
            }

            int raw = Mathf.Max(1, damage + weaponBonus);

            var stats = GetComponent<Project.Player.PlayerStats>();
            float dmgMul = (stats != null) ? stats.DamageMultiplier : 1f;

            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(raw * dmgMul));

            enemyHealth.TakeDamage(finalDamage);
            Project.Gameplay.LifestealHelper.TryApplyLifesteal(gameObject, finalDamage);

            Debug.Log($"[PlayerCombat] raw={raw} dmgMul={dmgMul:0.00} final={finalDamage}");
        }

        private static int GetWeaponDamageBonus(int itemId)
        {
            if (itemId <= 0)
                return 0;

            EnsureWeaponDamageCache();

            if (_weaponDamageByItemId != null && _weaponDamageByItemId.TryGetValue(itemId, out int bonus))
                return bonus;

            return 0;
        }

        private static void EnsureWeaponDamageCache()
        {
            if (_weaponCacheReady && _weaponDamageByItemId != null)
                return;

            var all = Resources.LoadAll<Project.Items.ItemData>("Items");
            _weaponDamageByItemId = new Dictionary<int, int>(all.Length);

            for (int i = 0; i < all.Length; i++)
            {
                var item = all[i];
                if (item == null || item.Id <= 0)
                    continue;

                _weaponDamageByItemId[item.Id] = item.DamageBonus;
            }

            _weaponCacheReady = true;
        }

        private static EnemyHealth FindClosestAliveEnemy(Collider[] hits, Vector3 fromPos)
        {
            EnemyHealth best = null;
            float bestSqrDist = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null)
                    continue;

                var hp = hits[i].GetComponentInParent<EnemyHealth>();
                if (hp == null || hp.CurrentHP <= 0)
                    continue;

                float sqrDist = (hp.transform.position - fromPos).sqrMagnitude;
                if (sqrDist < bestSqrDist)
                {
                    bestSqrDist = sqrDist;
                    best = hp;
                }
            }

            return best;
        }

    }
}
