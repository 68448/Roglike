using UnityEngine;

namespace Project.WorldGen
{
    public static class BiomeService
    {
        public const int SegmentsPerBiome = 10;

        private static readonly BiomeType[] BiomeOrder =
        {
            BiomeType.Shire,
            BiomeType.Rivendell,
            BiomeType.MistyMountains,
            BiomeType.Mirkwood,
            BiomeType.GreyHavens,
            BiomeType.LonelyMountain
        };

        public static BiomeType GetBiomeForSegment(int segmentIndex)
        {
            int safeSegment = Mathf.Max(1, segmentIndex);
            int biomeIndex = Mathf.Clamp((safeSegment - 1) / SegmentsPerBiome, 0, BiomeOrder.Length - 1);
            return BiomeOrder[biomeIndex];
        }

        public static string GetDisplayName(BiomeType biomeType)
        {
            return biomeType switch
            {
                BiomeType.Shire => "Shire",
                BiomeType.Rivendell => "Rivendell",
                BiomeType.MistyMountains => "Misty Mountains",
                BiomeType.Mirkwood => "Mirkwood",
                BiomeType.GreyHavens => "Grey Havens",
                BiomeType.LonelyMountain => "Lonely Mountain",
                _ => biomeType.ToString()
            };
        }

        public static int GetBiomeStartSegment(BiomeType biomeType)
        {
            for (int i = 0; i < BiomeOrder.Length; i++)
            {
                if (BiomeOrder[i] == biomeType)
                    return i * SegmentsPerBiome + 1;
            }

            return 1;
        }

        public static int GetBiomeEndSegment(BiomeType biomeType)
        {
            return GetBiomeStartSegment(biomeType) + SegmentsPerBiome - 1;
        }
    }
}
