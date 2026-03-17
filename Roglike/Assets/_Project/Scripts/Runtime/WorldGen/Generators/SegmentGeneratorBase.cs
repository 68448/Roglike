using UnityEngine;

namespace Project.WorldGen.Generators
{
    // Общая база для любых генераторов сегмента.
    // ChunkDungeonController будет хранить ссылку на неё.
    public abstract class SegmentGeneratorBase : MonoBehaviour
    {
        // Генерация сегмента в segmentRoot (в ЛОКАЛЬНЫХ координатах segmentRoot).
        public abstract void GenerateInto(int seed, int segmentIndex, Transform segmentRoot);

        // Рекомендуемое смещение между сегментами по X (чтобы сегменты не пересекались).
        // По умолчанию можно вернуть 0, а контроллер возьмёт max(segmentOffset, recommended).
        public virtual float GetRecommendedSegmentOffset() => 0f;
    }
}