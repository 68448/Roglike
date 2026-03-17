using Mirror;
using UnityEngine;

namespace Project.Networking
{
    public sealed class PlayerTeleportReceiver : NetworkBehaviour
    {
        // Сервер вызывает этот RPC для конкретного игрока (конкретного клиента)
        [TargetRpc]
        public void TargetForceTeleport(NetworkConnectionToClient target, Vector3 worldPos)
        {
            // ВАЖНО: этот код выполняется ТОЛЬКО на нужном клиенте
            // Ставим позицию "жёстко"
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            transform.position = worldPos;

            if (cc != null) cc.enabled = true;

            // Можно добавить лог для проверки на клиенте
            Debug.Log($"[PlayerTeleportReceiver] Forced teleport to {worldPos}");
        }
    }
}