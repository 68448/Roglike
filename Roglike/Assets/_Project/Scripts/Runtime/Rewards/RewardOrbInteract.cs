using Mirror;
using UnityEngine;

namespace Project.Rewards
{
    [RequireComponent(typeof(RewardOrb))]
    public sealed class RewardOrbInteract : MonoBehaviour
    {
        private RewardOrb _orb;
        private bool _localInRange;
        private NetworkIdentity _localPlayerNi;

        private void Awake()
        {
            _orb = GetComponent<RewardOrb>();
        }

        private void Update()
        {
            if (!_localInRange) return;
            if (_orb == null) return;
            if (_orb.isServer && !_orb.isClient) return; // чистый сервер без клиента — UI не нужен

            if (Input.GetKeyDown(KeyCode.E))
            {
                Debug.Log("[RewardOrbInteract] E pressed, trying open RewardUI...");

                if (_localPlayerNi == null)
                {
                    Debug.LogWarning("[RewardOrbInteract] Local player NI is null.");
                    return;
                }

                if (RewardUI.Instance == null)
                {
                    Debug.LogWarning("[RewardOrbInteract] RewardUI.Instance is NULL. UI not in scene?");
                    return;
                }

                RewardUI.Instance.ShowFor(_orb, _localPlayerNi);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            var ni = other.GetComponentInParent<NetworkIdentity>();
            if (ni != null && ni.isLocalPlayer)
            {
                _localInRange = true;
                _localPlayerNi = ni;
                Debug.Log("[RewardOrbInteract] Local in range.");
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player")) return;

            var ni = other.GetComponentInParent<NetworkIdentity>();
            if (ni != null && ni.isLocalPlayer)
            {
                _localInRange = false;
                _localPlayerNi = null;
                Debug.Log("[RewardOrbInteract] Local left range.");
            }
        }
    }
}