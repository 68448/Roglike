using UnityEngine;
using System;

namespace Project.Difficulty
{
    [CreateAssetMenu(menuName = "Roguelike/Run Difficulty Settings")]
    public sealed class RunDifficultySettings : ScriptableObject
    {
        [Header("Base Enemy Stats")]
        public int baseEnemyHP = 30;
        public int baseEnemyDamage = 10;
        public float baseEnemyMoveSpeed = 3f;

        [Header("Enemy Spawn Table")]
        public EnemySpawnEntry[] enemySpawnTable;

        [Header("Scaling Per Segment")]
        [Tooltip("Каждый сегмент добавляет % к HP. 0.10 = +10% за сегмент.")]
        public float hpGrowthPerSegment = 0.12f;

        [Tooltip("Каждый сегмент добавляет % к урону врага.")]
        public float damageGrowthPerSegment = 0.08f;

        [Tooltip("Каждый сегмент добавляет % к скорости врага.")]
        public float speedGrowthPerSegment = 0.02f;

        [Header("Enemies Per Room")]
        public int enemiesPerRoomMin = 3;
        public int enemiesPerRoomMax = 5;

        [Tooltip("Каждые N сегментов увеличиваем min/max врагов в комнате на 1.")]
        public int segmentsPerExtraEnemy = 3;

        [Header("Elite Enemies")]
        [Tooltip("Шанс элитного врага в комнате (0..1).")]
        public float eliteChance = 0.10f;

        [Tooltip("Множитель элиты к HP.")]
        public float eliteHpMult = 2.0f;

        [Tooltip("Множитель элиты к урону.")]
        public float eliteDamageMult = 1.5f;

        [Tooltip("Множитель элиты к скорости.")]
        public float eliteSpeedMult = 1.1f;

        [Header("Performance Limits")]
        [Tooltip("Максимум врагов на комнату, чтобы не взорвать FPS.")]
        public int enemiesPerRoomHardCap = 10;
    }

    [Serializable]
    public sealed class EnemySpawnEntry
    {
        public string name = "Enemy";
        public GameObject prefab;

        [Tooltip("С какого сегмента может появляться этот тип врага (включительно)")]
        public int minSegment = 1;

        [Tooltip("Относительный вес (чем больше — тем чаще выпадает среди доступных типов)")]
        public int weight = 10;

        [Header("Type Multipliers (before coop)")]
        [Tooltip("Множитель HP конкретного типа врага (например Fast = 0.7)")]
        public float hpMult = 1f;

        [Tooltip("Множитель урона конкретного типа врага")]
        public float damageMult = 1f;

        [Tooltip("Множитель скорости конкретного типа врага")]
        public float speedMult = 1f;
    }
}
