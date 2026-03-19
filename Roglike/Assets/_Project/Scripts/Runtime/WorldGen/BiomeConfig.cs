using System;
using UnityEngine;

namespace Project.WorldGen
{
    [Serializable]
    public sealed class BiomeConfig
    {
        public BiomeType biomeType;
        public string displayName;
        public BiomeLayoutStyle layoutStyle;

        [Header("Segment Visuals")]
        public Material groundMaterial;
        public Material trailMaterial;
        public Material decorMaterial;
        public Color groundTint = Color.white;
        public Color trailTint = Color.white;
        public Color decorTint = Color.white;

        [Header("Scene Mood")]
        public Color ambientLight = Color.gray;
        public Color fogColor = Color.gray;
        [Range(0f, 0.1f)] public float fogDensity = 0.01f;

        [Header("Biome Pools")]
        public GameObject[] enemyPrefabs;
        public GameObject bossPrefab;

        public string GetResolvedDisplayName()
        {
            return string.IsNullOrWhiteSpace(displayName)
                ? BiomeService.GetDisplayName(biomeType)
                : displayName;
        }
    }
}
