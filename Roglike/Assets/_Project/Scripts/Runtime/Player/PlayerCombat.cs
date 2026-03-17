using Mirror;
using Project.Gameplay;
using UnityEngine;

namespace Project.Player
{
    /// <summary>
    /// MVP-атака: ЛКМ -> серверный OverlapSphere -> урон по врагу.
    /// </summary>
    public sealed class PlayerCombat : NetworkBehaviour
    {
        [SerializeField] private float attackRadius = 2.2f;
        [SerializeField] private int damage = 10;
        [SerializeField] private LayerMask enemyMask;

        private void Update()
        {
            if (!isLocalPlayer)
                return;

            if (Input.GetMouseButtonDown(0))
            {
                CmdAttack(transform.position);
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
        private void CmdAttack(Vector3 playerPos)
        {
            Collider[] hits = Physics.OverlapSphere(playerPos, attackRadius, enemyMask);
            if (hits == null || hits.Length == 0)
                return;

            var enemyHealth = hits[0].GetComponentInParent<EnemyHealth>();
            if (enemyHealth == null)
                return;

            int weaponBonus = 0;
            var equip = GetComponent<Project.Player.PlayerEquipment>();
            if (equip != null && equip.WeaponItemId > 0)
            {
                var all = Resources.LoadAll<Project.Items.ItemData>("Items");
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].Id == equip.WeaponItemId)
                    {
                        weaponBonus = all[i].DamageBonus;
                        break;
                    }
                }
            }

            int raw = Mathf.Max(1, damage + weaponBonus);

            var stats = GetComponent<Project.Player.PlayerStats>();
            float dmgMul = (stats != null) ? stats.DamageMultiplier : 1f;

            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(raw * dmgMul));

            enemyHealth.TakeDamage(finalDamage);
            Project.Gameplay.LifestealHelper.TryApplyLifesteal(gameObject, finalDamage);

            Debug.Log($"[PlayerCombat] raw={raw} dmgMul={dmgMul:0.00} final={finalDamage}");
        }

    }
}
