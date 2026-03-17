using Mirror;
using UnityEngine;

namespace Project.Items
{
    [RequireComponent(typeof(WorldItem))]
    public sealed class WorldItemPickupTrigger : NetworkBehaviour
    {
        private WorldItem _item;
        private NetworkIdentity _localPlayer;
        private bool _inRange;

        private void Awake()
        {
            _item = GetComponent<WorldItem>();
        }

        private void Update()
        {
            if (!_inRange) return;
            if (_localPlayer == null) return;

            if (Input.GetKeyDown(KeyCode.E))
            {
                CmdPickup(_localPlayer);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            var ni = other.GetComponent<NetworkIdentity>();
            if (ni != null && ni.isLocalPlayer)
            {
                _localPlayer = ni;
                _inRange = true;
                Debug.Log("[WorldItemPickup] Local in range");
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            var ni = other.GetComponent<NetworkIdentity>();
            if (ni != null && ni.isLocalPlayer)
            {
                _inRange = false;
                _localPlayer = null;
                Debug.Log("[WorldItemPickup] Local out of range");
            }
        }

        [Command(requiresAuthority = false)]
        private void CmdPickup(NetworkIdentity picker, NetworkConnectionToClient sender = null)
        {
            if (!NetworkServer.active) return;
            if (_item == null) return;

            // защита: picker должен быть тем, кто отправил команду
            if (sender != null && sender.identity != null && picker != sender.identity)
                picker = sender.identity;

            _item.Pickup(picker);
        }
    }
}