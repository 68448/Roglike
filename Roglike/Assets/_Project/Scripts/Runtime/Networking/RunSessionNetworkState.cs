using Mirror;
using UnityEngine;

namespace Project.Networking
{
    public sealed class RunSessionNetworkState : NetworkBehaviour
    {
        [SyncVar] public int Seed;
        [SyncVar] public int Depth;
        [SyncVar] public int SegmentIndex;

        public override void OnStartServer()
        {
            DontDestroyOnLoad(gameObject);

            if (Depth <= 0)
                Depth = 1;

            if (SegmentIndex <= 0)
                SegmentIndex = 1;

            Debug.Log("[RunSessionNetworkState] OnStartServer (persistent session state created)");
        }

        public override void OnStartClient()
        {
            DontDestroyOnLoad(gameObject);
            Debug.Log("[RunSessionNetworkState] OnStartClient (persistent session state received)");
        }

        [Server]
        public void SetSeed(int seed)
        {
            Seed = seed;
        }

        [Server]
        public void SetDepth(int depth)
        {
            Depth = depth;
        }

        [Server]
        public void NextSegment()
        {
            SegmentIndex++;
        }
    }
}
