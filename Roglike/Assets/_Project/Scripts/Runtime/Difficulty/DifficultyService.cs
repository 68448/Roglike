using UnityEngine;

namespace Project.Difficulty
{
    public struct EnemyScaledStats
    {
        public int hp;
        public int damage;
        public float moveSpeed;
        public bool isElite;
    }

    public static class DifficultyService
    {
        public static RunDifficultySettings LoadSettings()
        {
            // MVP: один файл в Resources
            return Resources.Load<RunDifficultySettings>("Difficulty/RunDifficulty_Default");
        }

        public static EnemyScaledStats GetEnemyStats(int segmentIndex, RunDifficultySettings s, float eliteRoll01)
        {
            int depth = Mathf.Max(1, segmentIndex);

            float hpMult  = 1f + s.hpGrowthPerSegment * (depth - 1);
            float dmgMult = 1f + s.damageGrowthPerSegment * (depth - 1);
            float spdMult = 1f + s.speedGrowthPerSegment * (depth - 1);

            bool elite = eliteRoll01 < s.eliteChance;

            if (elite)
            {
                hpMult  *= s.eliteHpMult;
                dmgMult *= s.eliteDamageMult;
                spdMult *= s.eliteSpeedMult;
            }

            return new EnemyScaledStats
            {
                hp = Mathf.Max(1, Mathf.RoundToInt(s.baseEnemyHP * hpMult)),
                damage = Mathf.Max(1, Mathf.RoundToInt(s.baseEnemyDamage * dmgMult)),
                moveSpeed = Mathf.Max(0.5f, s.baseEnemyMoveSpeed * spdMult),
                isElite = elite
            };
        }

        public static (int min, int max) GetEnemiesPerRoom(int segmentIndex, RunDifficultySettings s)
        {
            int depth = Mathf.Max(1, segmentIndex);

            int extra = (depth - 1) / Mathf.Max(1, s.segmentsPerExtraEnemy);

            int min = s.enemiesPerRoomMin + extra;
            int max = s.enemiesPerRoomMax + extra;

            if (max < min) max = min;

            min = Mathf.Clamp(min, 0, s.enemiesPerRoomHardCap);
            max = Mathf.Clamp(max, min, s.enemiesPerRoomHardCap);

            return (min, max);
        }

        public static GameObject PickEnemyPrefab(int segmentIndex, RunDifficultySettings s, System.Random rng, out EnemySpawnEntry picked)
        {
            picked = null;

            if (s.enemySpawnTable == null || s.enemySpawnTable.Length == 0)
                return null;

            // 1) считаем сумму весов всех доступных типов
            int totalWeight = 0;
            for (int i = 0; i < s.enemySpawnTable.Length; i++)
            {
                var e = s.enemySpawnTable[i];
                if (e == null) continue;
                if (e.prefab == null) continue;
                if (segmentIndex < e.minSegment) continue;

                totalWeight += Mathf.Max(0, e.weight);
            }

            if (totalWeight <= 0)
                return null;

            // 2) бросаем кубик
            int roll = rng.Next(0, totalWeight);
            int acc = 0;

            for (int i = 0; i < s.enemySpawnTable.Length; i++)
            {
                var e = s.enemySpawnTable[i];
                if (e == null) continue;
                if (e.prefab == null) continue;
                if (segmentIndex < e.minSegment) continue;

                int w = Mathf.Max(0, e.weight);
                acc += w;

                if (roll < acc)
                {
                    picked = e;
                    return e.prefab;
                }
            }

            return null;
        }

        public static EnemyScaledStats GetEnemyStats(
            int segmentIndex,
            RunDifficultySettings s,
            float eliteRoll01,
            float typeHpMult,
            float typeDmgMult,
            float typeSpeedMult)
        {
            int depth = Mathf.Max(1, segmentIndex);

            float hpMult  = (1f + s.hpGrowthPerSegment * (depth - 1)) * Mathf.Max(0.01f, typeHpMult);
            float dmgMult = (1f + s.damageGrowthPerSegment * (depth - 1)) * Mathf.Max(0.01f, typeDmgMult);
            float spdMult = (1f + s.speedGrowthPerSegment * (depth - 1)) * Mathf.Max(0.01f, typeSpeedMult);

            bool elite = eliteRoll01 < s.eliteChance;

            if (elite)
            {
                hpMult  *= s.eliteHpMult;
                dmgMult *= s.eliteDamageMult;
                spdMult *= s.eliteSpeedMult;
            }

            return new EnemyScaledStats
            {
                hp = Mathf.Max(1, Mathf.RoundToInt(s.baseEnemyHP * hpMult)),
                damage = Mathf.Max(1, Mathf.RoundToInt(s.baseEnemyDamage * dmgMult)),
                moveSpeed = Mathf.Max(0.5f, s.baseEnemyMoveSpeed * spdMult),
                isElite = elite
            };
        }
        
    }
}
