using Mirror;
using Project.Networking;
using UnityEngine;

namespace Project.Player
{
    /// <summary>
    /// Отвечает за взаимодействия (E).
    /// Клиент нажимает E -> отправляем Command на сервер.
    /// Сервер решает, можно ли перейти в следующий сегмент.
    /// </summary>
    public sealed class PlayerInteractor : NetworkBehaviour
    {
        [SerializeField] private float interactRadius = 2.5f;
        [SerializeField] private LayerMask interactMask; // будем использовать слой Portal
        [SerializeField] private LayerMask itemMask;

        private void Update()
        {
            if (!isLocalPlayer)
                return;

            if (Input.GetKeyDown(KeyCode.E))
            {
                CmdTryUsePortal(transform.position);
            }
            // Для UI подсказки
            bool canInteract = Physics.CheckSphere(transform.position, interactRadius, interactMask | itemMask);

            var hud = FindFirstObjectByType<Project.UI.PlayerHUDController>();
            if (hud != null)
            {
                // можно расширить HUD позже, пока MVP
            }
        }

        [Command]
        private void CmdTryUsePortal(Vector3 playerPosition)
        {
            // 1) Сначала пробуем подобрать предмет
            var itemHits = Physics.OverlapSphere(playerPosition, interactRadius, itemMask);
            if (itemHits != null && itemHits.Length > 0)
            {
                var wi = itemHits[0].GetComponentInParent<Project.Items.WorldItem>();
                if (wi != null)
                {
                    wi.Pickup(connectionToClient.identity);
                    return;
                }
            }
            // Сервер проверяет, есть ли портал рядом
            Collider[] hits = Physics.OverlapSphere(playerPosition, interactRadius, interactMask);
            if (hits == null || hits.Length == 0)
                return;

            var portalState = hits[0].GetComponentInParent<Project.WorldGen.PortalState>();
            if (portalState == null)
                return;

            if (!portalState.IsOpen)
                return;

            // Портал открыт -> переходим
            var session = FindFirstObjectByType<RunSessionNetworkState>();
            if (session == null)
            {
                Debug.LogError("[PlayerInteractor] RunSessionNetworkState not found on server!");
                return;
            }

            session.NextSegment();
            var controller = FindFirstObjectByType<Project.WorldGen.ChunkDungeonController>();
            if (controller != null)
                controller.ServerForcePrepareAndTeleport(session.SegmentIndex);
        }
    }
}
