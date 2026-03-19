using System.Collections.Generic;
using UnityEngine;

namespace Project.WorldGen
{
    [DisallowMultipleComponent]
    public sealed class BiomeCatalog : MonoBehaviour
    {
        [SerializeField] private List<BiomeConfig> biomes = new();
        [SerializeField] private bool autoPopulateDefaults = true;

        public BiomeConfig GetConfig(BiomeType biomeType)
        {
            for (int i = 0; i < biomes.Count; i++)
            {
                if (biomes[i] != null && biomes[i].biomeType == biomeType)
                    return biomes[i];
            }

            return CreateFallbackConfig(biomeType);
        }

        private void Reset()
        {
            PopulateDefaultsIfNeeded(force: true);
        }

        private void OnValidate()
        {
            PopulateDefaultsIfNeeded(force: false);

            for (int i = 0; i < biomes.Count; i++)
            {
                var config = biomes[i];
                if (config == null)
                    continue;

                if (string.IsNullOrWhiteSpace(config.displayName))
                    config.displayName = BiomeService.GetDisplayName(config.biomeType);

                config.fogDensity = Mathf.Clamp(config.fogDensity, 0f, 0.1f);
            }
        }

        private void PopulateDefaultsIfNeeded(bool force)
        {
            if (!autoPopulateDefaults && !force)
                return;

            if (biomes.Count > 0 && !force)
                return;

            biomes.Clear();
            foreach (BiomeType biomeType in System.Enum.GetValues(typeof(BiomeType)))
                biomes.Add(CreateFallbackConfig(biomeType));
        }

        public static BiomeConfig CreateFallbackConfig(BiomeType biomeType)
        {
            var config = new BiomeConfig
            {
                biomeType = biomeType,
                displayName = BiomeService.GetDisplayName(biomeType),
                layoutStyle = GetDefaultLayoutStyle(biomeType)
            };

            switch (biomeType)
            {
                case BiomeType.Shire:
                    config.groundTint = new Color(0.38f, 0.63f, 0.31f);
                    config.trailTint = new Color(0.63f, 0.52f, 0.32f);
                    config.decorTint = new Color(0.30f, 0.40f, 0.22f);
                    config.ambientLight = new Color(0.70f, 0.74f, 0.60f);
                    config.fogColor = new Color(0.72f, 0.84f, 0.62f);
                    config.fogDensity = 0.006f;
                    break;

                case BiomeType.Rivendell:
                    config.groundTint = new Color(0.56f, 0.71f, 0.58f);
                    config.trailTint = new Color(0.78f, 0.82f, 0.73f);
                    config.decorTint = new Color(0.52f, 0.65f, 0.70f);
                    config.ambientLight = new Color(0.72f, 0.76f, 0.82f);
                    config.fogColor = new Color(0.71f, 0.85f, 0.88f);
                    config.fogDensity = 0.008f;
                    break;

                case BiomeType.MistyMountains:
                    config.groundTint = new Color(0.36f, 0.38f, 0.42f);
                    config.trailTint = new Color(0.46f, 0.47f, 0.50f);
                    config.decorTint = new Color(0.24f, 0.25f, 0.29f);
                    config.ambientLight = new Color(0.52f, 0.55f, 0.60f);
                    config.fogColor = new Color(0.52f, 0.55f, 0.60f);
                    config.fogDensity = 0.012f;
                    break;

                case BiomeType.Mirkwood:
                    config.groundTint = new Color(0.18f, 0.31f, 0.18f);
                    config.trailTint = new Color(0.31f, 0.28f, 0.18f);
                    config.decorTint = new Color(0.12f, 0.18f, 0.10f);
                    config.ambientLight = new Color(0.38f, 0.45f, 0.34f);
                    config.fogColor = new Color(0.22f, 0.34f, 0.20f);
                    config.fogDensity = 0.018f;
                    break;

                case BiomeType.GreyHavens:
                    config.groundTint = new Color(0.52f, 0.61f, 0.68f);
                    config.trailTint = new Color(0.80f, 0.84f, 0.88f);
                    config.decorTint = new Color(0.31f, 0.45f, 0.56f);
                    config.ambientLight = new Color(0.69f, 0.74f, 0.80f);
                    config.fogColor = new Color(0.73f, 0.80f, 0.86f);
                    config.fogDensity = 0.010f;
                    break;

                case BiomeType.LonelyMountain:
                    config.groundTint = new Color(0.44f, 0.32f, 0.22f);
                    config.trailTint = new Color(0.58f, 0.42f, 0.24f);
                    config.decorTint = new Color(0.28f, 0.18f, 0.10f);
                    config.ambientLight = new Color(0.58f, 0.48f, 0.36f);
                    config.fogColor = new Color(0.45f, 0.34f, 0.24f);
                    config.fogDensity = 0.014f;
                    break;
            }

            return config;
        }

        private static BiomeLayoutStyle GetDefaultLayoutStyle(BiomeType biomeType)
        {
            return biomeType switch
            {
                BiomeType.Shire => BiomeLayoutStyle.OpenFields,
                BiomeType.Rivendell => BiomeLayoutStyle.ForestPaths,
                BiomeType.MistyMountains => BiomeLayoutStyle.MountainPass,
                BiomeType.Mirkwood => BiomeLayoutStyle.ForestPaths,
                BiomeType.GreyHavens => BiomeLayoutStyle.CoastalRoute,
                BiomeType.LonelyMountain => BiomeLayoutStyle.MountainPass,
                _ => BiomeLayoutStyle.OpenFields
            };
        }
    }
}
