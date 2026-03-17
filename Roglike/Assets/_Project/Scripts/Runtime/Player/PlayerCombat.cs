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
        private static Dictionary<int, int> _weaponCritChanceByItemId;
        private static Dictionary<int, int> _weaponCritDamageBonusByItemId;
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
            int weaponCritChancePct = 0;
            int weaponCritDamageBonusPct = 0;
            var equip = GetComponent<Project.Player.PlayerEquipment>();
            if (equip != null && equip.WeaponItemId > 0)
            {
                weaponBonus = GetWeaponDamageBonus(equip.WeaponItemId);
                weaponCritChancePct = GetWeaponCritChancePct(equip.WeaponItemId);
                weaponCritDamageBonusPct = GetWeaponCritDamageBonusPct(equip.WeaponItemId);
            }

            int raw = Mathf.Max(1, damage + weaponBonus);

            var stats = GetComponent<Project.Player.PlayerStats>();
            float dmgMul = (stats != null) ? stats.DamageMultiplier : 1f;

            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(raw * dmgMul));
            bool isCritical = RollCritical(weaponCritChancePct);
            if (isCritical)
            {
                float critMult = 1f + Mathf.Max(0, weaponCritDamageBonusPct) / 100f;
                finalDamage = Mathf.Max(1, Mathf.RoundToInt(finalDamage * critMult));
            }

            enemyHealth.TakeDamage(finalDamage, isCritical);
            Project.Gameplay.LifestealHelper.TryApplyLifesteal(gameObject, finalDamage);

            Debug.Log($"[PlayerCombat] raw={raw} dmgMul={dmgMul:0.00} crit={isCritical} critChance={weaponCritChancePct}% critBonus={weaponCritDamageBonusPct}% final={finalDamage}");
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

        private static int GetWeaponCritChancePct(int itemId)
        {
            if (itemId <= 0)
                return 0;

            EnsureWeaponDamageCache();

            if (_weaponCritChanceByItemId != null && _weaponCritChanceByItemId.TryGetValue(itemId, out int value))
                return value;

            return 0;
        }

        private static int GetWeaponCritDamageBonusPct(int itemId)
        {
            if (itemId <= 0)
                return 0;

            EnsureWeaponDamageCache();

            if (_weaponCritDamageBonusByItemId != null && _weaponCritDamageBonusByItemId.TryGetValue(itemId, out int value))
                return value;

            return 0;
        }

        private static void EnsureWeaponDamageCache()
        {
            if (_weaponCacheReady && _weaponDamageByItemId != null)
                return;

            var all = Resources.LoadAll<Project.Items.ItemData>("Items");
            _weaponDamageByItemId = new Dictionary<int, int>(all.Length);
            _weaponCritChanceByItemId = new Dictionary<int, int>(all.Length);
            _weaponCritDamageBonusByItemId = new Dictionary<int, int>(all.Length);

            for (int i = 0; i < all.Length; i++)
            {
                var item = all[i];
                if (item == null || item.Id <= 0)
                    continue;

                _weaponDamageByItemId[item.Id] = item.DamageBonus;
                _weaponCritChanceByItemId[item.Id] = Mathf.Max(0, item.CritChancePct);
                _weaponCritDamageBonusByItemId[item.Id] = Mathf.Max(0, item.CritDamageBonusPct);
            }

            _weaponCacheReady = true;
        }

        private static bool RollCritical(int critChancePct)
        {
            if (critChancePct <= 0)
                return false;

            int clampedChance = Mathf.Clamp(critChancePct, 0, 100);
            float roll = Random.value * 100f;
            return roll < clampedChance;
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
