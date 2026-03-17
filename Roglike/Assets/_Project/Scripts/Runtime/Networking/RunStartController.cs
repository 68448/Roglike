using Mirror;
using UnityEngine;

namespace Project.Networking
{
    public sealed class RunStartController : MonoBehaviour
    {
        [Server]
        public void PrepareNewRunIfNeeded()
        {
            // Не полагаемся на ссылки из инспектора — ищем актуальный объект
            var sessionState = FindFirstObjectByType<RunSessionNetworkState>();

            if (sessionState == null)
            {
                Debug.LogError("[RunStartController] RunSessionNetworkState not found on server!");
                return;
            }

            Debug.Log($"[RunStartController] Before: Seed={sessionState.Seed} Depth={sessionState.Depth}");

            if (sessionState.Seed == 0)
            {
                int seed = Random.Range(int.MinValue, int.MaxValue);
                sessionState.SetSeed(seed);
                sessionState.SetDepth(1);
                Debug.Log($"[RunStartController] New run seed={seed}");
            }
            else
            {
                Debug.Log("[RunStartController] Seed already set, reusing existing seed.");
            }

            Debug.Log($"[RunStartController] After: Seed={sessionState.Seed} Depth={sessionState.Depth}");
        }
    }
}
