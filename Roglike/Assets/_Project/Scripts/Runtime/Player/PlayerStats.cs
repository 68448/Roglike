using Mirror;
using UnityEngine;

namespace Project.Player
{
    public enum RewardKind : int
    {
        MaxHpFlat = 1,      // +X к maxHP
        DamagePct = 2,      // +X% урон
        MoveSpeedPct = 3,    // +X% скорость

        LifestealPct = 4,
        ThornsFlat = 5,
        RegenPer10s = 6,
        BossDamagePct = 7
    }

    public sealed class PlayerStats : NetworkBehaviour
    {
        [Header("Synced Stats")]
        [SyncVar] public int BonusMaxHp;        // например +10, +20
        [SyncVar] public int BonusDamagePct;    // например +10 => +10%
        [SyncVar] public int BonusMoveSpeedPct; // например +10 => +10%
        [SyncVar] public int LifestealPct;      // 0..100 (например 5 = 5%)
        [SyncVar] public int ThornsFlat;        // фикс отражение урона
        [SyncVar] public int RegenPer10s;       // сколько HP за 10 секунд
        [SyncVar] public int BossDamagePct;     // +% урона по боссам
        [SyncVar] public int EquipmentMoveSpeedPctBonus;

        // Удобные геттеры (для других систем)
        public float DamageMultiplier => 1f + (BonusDamagePct / 100f);
        public float MoveSpeedMultiplier => 1f + ((BonusMoveSpeedPct + EquipmentMoveSpeedPctBonus) / 100f);

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

            Debug.Log($"[PlayerStats] Applied reward {kind} value={value} to player netId={netId}. " +
                      $"Now: +HP={BonusMaxHp}, +DMG%={BonusDamagePct}, +SPD%={BonusMoveSpeedPct}");
        }

        // Применяем +HP сразу к текущему здоровью (чтобы игрок видел эффект)
        [Server]
        private void ServerTryApplyToHealth()
        {
            // Если у тебя есть PlayerHealth — подстроимся.
            // Название класса может отличаться. Ниже делаем максимально безопасно:
            // ищем компонент с методом ServerAddMaxHp(int) или полями/методами, если у тебя они уже есть.
            var health = GetComponent<Project.Gameplay.PlayerHealth>();
            if (health != null)
            {
                // Предположим, что в PlayerHealth есть такие методы.
                // Если их нет — скажи, я подгоню под твой PlayerHealth.
                health.ServerRecalculateMaxHpFromStats(fillToFull: false);
            }
        }
    }
}