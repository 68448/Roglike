using Mirror;
using UnityEngine;

namespace Project.Gameplay
{
    public sealed class PlayerHealth : NetworkBehaviour
    {
        [SyncVar] public int CurrentHP;
        [SyncVar] public int MaxHP;
        [SyncVar] public int EquipmentHpBonus;

        [SerializeField] private int baseMaxHP = 100;
        private bool _localDeathHandled;

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            _localDeathHandled = false;
        }

        public override void OnStartServer()
        {
            MaxHP = Mathf.Max(1, baseMaxHP);
            CurrentHP = MaxHP;
        }

        [Server]
        public void TakeDamage(int amount)
        {
            Debug.Log($"[PlayerHealth] TakeDamage netId={netId} amount={amount} hpBefore={CurrentHP}");

            if (amount <= 0)
                return;

            CurrentHP -= amount;
            Debug.Log($"[PlayerHealth] hpAfter={CurrentHP}");

            if (CurrentHP <= 0)
            {
                CurrentHP = 0;
                RpcOnDeath();
            }
        }

        [Server]
        public void TakeDamage(int amount, GameObject attacker)
        {
            TakeDamage(amount);

            if (attacker == null)
                return;

            var stats = GetComponent<Project.Player.PlayerStats>();
            if (stats == null)
                return;

            int thorns = stats.ThornsFlat;
            if (thorns <= 0)
                return;

            var enemyHp = attacker.GetComponent<Project.Gameplay.EnemyHealth>();
            if (enemyHp != null)
            {
                enemyHp.TakeDamage(thorns);
                Debug.Log($"[PlayerHealth] Thorns reflected {thorns} to enemy.");
            }
        }

        [ClientRpc]
        private void RpcOnDeath()
        {
            var controller = GetComponent<Project.Player.PlayerController>();
            if (controller != null)
                controller.enabled = false;

            if (isLocalPlayer && !_localDeathHandled)
            {
                _localDeathHandled = true;

                int segmentReached = 1;
                var session = FindFirstObjectByType<Project.Networking.RunSessionNetworkState>();
                if (session != null)
                    segmentReached = Mathf.Max(1, session.SegmentIndex);

                int essenceEarned = 0;
                var stats = GetComponent<Project.Player.PlayerStats>();
                if (stats != null)
                    essenceEarned = Mathf.Max(0, stats.RunEssenceEarned);

                // Bank run currency once, right at run end.
                if (essenceEarned > 0)
                    Project.Progression.MetaProgressionService.AddCurrency(essenceEarned);

                Project.Progression.RunProgressTracker.FinalizeRun(segmentReached);
                int bestSegment = Project.Progression.RunProgressTracker.GetBestSegment();

                Project.UI.RunEndUI.Instance?.ShowForLocalDeath(segmentReached, essenceEarned, bestSegment);
            }

            Debug.Log($"[PlayerHealth] Player died: {netId}");
        }

        [Server]
        public void ServerRecalculateMaxHpFromStats(bool fillToFull = false)
        {
            var stats = GetComponent<Project.Player.PlayerStats>();

            int runBonus = (stats != null) ? stats.BonusMaxHp : 0;
            int metaBonus = (stats != null) ? stats.MetaMaxHpBonus : 0;

            int oldMax = MaxHP > 0 ? MaxHP : Mathf.Max(1, baseMaxHP);
            int newMax = Mathf.Max(1, baseMaxHP + runBonus + metaBonus + EquipmentHpBonus);

            MaxHP = newMax;

            if (fillToFull)
            {
                CurrentHP = MaxHP;
            }
            else
            {
                int delta = newMax - oldMax;
                if (delta > 0)
                    CurrentHP = Mathf.Min(CurrentHP + delta, MaxHP);
                else
                    CurrentHP = Mathf.Min(CurrentHP, MaxHP);
            }

            Debug.Log($"[PlayerHealth] Recalc maxHP old={oldMax} new={newMax} cur={CurrentHP} netId={netId}");
        }

        [Server]
        public void ServerHeal(int amount)
        {
            if (amount <= 0 || CurrentHP <= 0)
                return;

            CurrentHP = Mathf.Min(CurrentHP + amount, MaxHP);
        }

        [Server]
        public void ServerSetEquipmentHpBonus(int value)
        {
            EquipmentHpBonus = Mathf.Max(0, value);
            ServerRecalculateMaxHpFromStats(fillToFull: false);
        }
    }
}
