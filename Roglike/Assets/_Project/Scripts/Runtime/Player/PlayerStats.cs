using Mirror;
using UnityEngine;

namespace Project.Player
{
    public enum RewardKind : int
    {
        MaxHpFlat = 1,
        DamagePct = 2,
        MoveSpeedPct = 3,
        LifestealPct = 4,
        ThornsFlat = 5,
        RegenPer10s = 6,
        BossDamagePct = 7
    }

    public sealed class PlayerStats : NetworkBehaviour
    {
        [Header("Run Stats (Synced)")]
        [SyncVar] public int BonusMaxHp;
        [SyncVar] public int BonusDamagePct;
        [SyncVar] public int BonusMoveSpeedPct;
        [SyncVar] public int LifestealPct;
        [SyncVar] public int ThornsFlat;
        [SyncVar] public int RegenPer10s;
        [SyncVar] public int BossDamagePct;
        [SyncVar] public int EquipmentMoveSpeedPctBonus;

        [Header("Meta Stats (Synced)")]
        [SyncVar] public int MetaMaxHpBonus;
        [SyncVar] public int MetaDamagePctBonus;
        [SyncVar] public int RunEssenceEarned;

        public float DamageMultiplier => 1f + ((BonusDamagePct + MetaDamagePctBonus) / 100f);
        public float MoveSpeedMultiplier => 1f + ((BonusMoveSpeedPct + EquipmentMoveSpeedPctBonus) / 100f);

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            Project.Progression.RunProgressTracker.EnsureRunStarted();

            int hpBonus = Project.Progression.MetaProgressionService.GetMaxHpMetaBonus();
            int dmgBonus = Project.Progression.MetaProgressionService.GetDamageMetaBonusPct();
            CmdApplyMetaProgression(hpBonus, dmgBonus);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            RunEssenceEarned = 0;
        }

        [Command]
        private void CmdApplyMetaProgression(int metaHpBonus, int metaDamagePctBonus)
        {
            int hpCap = Project.Progression.MetaProgressionService.MaxLevelPerUpgrade *
                        Project.Progression.MetaProgressionService.HpPerLevel;
            int dmgCap = Project.Progression.MetaProgressionService.MaxLevelPerUpgrade *
                         Project.Progression.MetaProgressionService.DamagePctPerLevel;

            MetaMaxHpBonus = Mathf.Clamp(metaHpBonus, 0, hpCap);
            MetaDamagePctBonus = Mathf.Clamp(metaDamagePctBonus, 0, dmgCap);

            ServerTryApplyToHealth();
            Debug.Log($"[PlayerStats] Meta applied netId={netId} metaHP={MetaMaxHpBonus} metaDMG%={MetaDamagePctBonus}");
        }

        [Server]
        public void ServerSetEquipmentMoveSpeedPctBonus(int value)
        {
            EquipmentMoveSpeedPctBonus = Mathf.Max(0, value);
        }

        [Server]
        public void ServerApplyReward(RewardKind kind, int value)
        {
            switch (kind)
            {
                case RewardKind.MaxHpFlat:
                    BonusMaxHp += Mathf.Max(1, value);
                    ServerTryApplyToHealth();
                    break;

                case RewardKind.DamagePct:
                    BonusDamagePct += Mathf.Max(1, value);
                    break;

                case RewardKind.MoveSpeedPct:
                    BonusMoveSpeedPct += Mathf.Max(1, value);
                    break;

                case RewardKind.LifestealPct:
                    LifestealPct += Mathf.Max(1, value);
                    break;

                case RewardKind.ThornsFlat:
                    ThornsFlat += Mathf.Max(1, value);
                    break;

                case RewardKind.RegenPer10s:
                    RegenPer10s += Mathf.Max(1, value);
                    break;

                case RewardKind.BossDamagePct:
                    BossDamagePct += Mathf.Max(1, value);
                    break;
            }

            Debug.Log(
                $"[PlayerStats] Applied reward {kind} value={value} netId={netId} " +
                $"Now: runHP={BonusMaxHp}, runDMG%={BonusDamagePct}, runSPD%={BonusMoveSpeedPct}");
        }

        [Server]
        private void ServerTryApplyToHealth()
        {
            var health = GetComponent<Project.Gameplay.PlayerHealth>();
            if (health != null)
                health.ServerRecalculateMaxHpFromStats(fillToFull: false);
        }

        [Server]
        public void ServerAddRunEssence(int amount)
        {
            if (amount <= 0)
                return;

            RunEssenceEarned += amount;
        }
    }
}
