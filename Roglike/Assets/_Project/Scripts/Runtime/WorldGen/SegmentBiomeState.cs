using UnityEngine;

namespace Project.WorldGen
{
    public sealed class SegmentBiomeState : MonoBehaviour
    {
        public int SegmentIndex { get; private set; }
        public BiomeType BiomeType { get; private set; }
        public string DisplayName { get; private set; }
        public Color AmbientLight { get; private set; }
        public Color FogColor { get; private set; }
        public float FogDensity { get; private set; }

        public void Init(int segmentIndex, BiomeConfig biomeConfig)
        {
            SegmentIndex = segmentIndex;
            BiomeType = biomeConfig != null ? biomeConfig.biomeType : BiomeService.GetBiomeForSegment(segmentIndex);
            DisplayName = biomeConfig != null ? biomeConfig.GetResolvedDisplayName() : BiomeService.GetDisplayName(BiomeType);
            AmbientLight = biomeConfig != null ? biomeConfig.ambientLight : RenderSettings.ambientLight;
            FogColor = biomeConfig != null ? biomeConfig.fogColor : RenderSettings.fogColor;
            FogDensity = biomeConfig != null ? biomeConfig.fogDensity : RenderSettings.fogDensity;
        }

        public void ApplySceneVisuals()
        {
            RenderSettings.ambientLight = AmbientLight;
            RenderSettings.fog = true;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = FogDensity;
        }
    }
}
