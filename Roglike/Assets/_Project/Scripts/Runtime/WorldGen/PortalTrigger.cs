using Mirror;
using UnityEngine;

namespace Project.WorldGen
{
    [RequireComponent(typeof(Collider))]
    public sealed class PortalTrigger : NetworkBehaviour
    {
        // Откуда/куда ведёт портал
        [SyncVar] public int FromSegment;
        [SyncVar] public int ToSegment;

        // Лочинг (гейтинг)
        [SyncVar(hook = nameof(OnLockedChanged))]
        public bool IsLocked = true;

        [Header("Optional visuals")]
        [SerializeField] private Renderer portalRenderer;

        // локально: только для "нажать E можно"
        private bool _localInRange;

        private NetworkIdentity _localPlayerNi;

        private void Reset()
        {
            // Убедимся, что коллайдер триггер
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyVisual(IsLocked);
        }

        private void OnLockedChanged(bool oldValue, bool newValue)
        {
            ApplyVisual(newValue);
        }

        private void ApplyVisual(bool locked)
        {
            if (portalRenderer != null && portalRenderer.material != null)
            {
                portalRenderer.material.color = locked
                    ? new Color(0.25f, 0.25f, 0.25f, 1f) // закрыт
                    : new Color(0.2f, 1f, 0.2f, 1f);     // открыт
            }
        }

        // ВАЖНО: триггер нужен только, чтобы локальный игрок понял "я рядом"
        private void OnTriggerEnter(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            // Это локальный игрок?
            var ni = other.GetComponentInParent<NetworkIdentity>();
            if (ni != null && ni.isLocalPlayer)
            {
                _localInRange = true;
                _localPlayerNi = ni;
                Debug.Log($"[PortalTrigger] Local in range. Locked={IsLocked}");
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.CompareTag("Player"))
                return;

            var ni = other.GetComponentInParent<NetworkIdentity>();
            if (ni != null && ni.isLocalPlayer)
            {
                _localInRange = false;
                _localPlayerNi = null;
                Debug.Log("[PortalTrigger] Local left range.");
            }
        }

        private void Update()
        {
            // Команду отправляет только локальный клиент
            if (!_localInRange) return;

            if (!Input.GetKeyDown(KeyCode.E)) return;

            if (IsLocked)
            {
                Debug.Log("[PortalTrigger] Portal is locked.");
                return;
            }

            if (_localPlayerNi == null)
            {
                Debug.LogWarning("[PortalTrigger] Local player NI is null, can't send CmdUsePortal.");
                return;
            }

            CmdUsePortal(_localPlayerNi);
        }

        // Команда без authority — потому что портал не "принадлежит" игроку
        [Command(requiresAuthority = false)]
        private void CmdUsePortal(NetworkIdentity playerNi)
        {
            if (IsLocked)
            {
                Debug.Log("[PortalTrigger] Server denied: locked.");
                return;
            }

            if (playerNi == null)
            {
                Debug.LogWarning("[PortalTrigger] Server denied: playerNi is null.");
                return;
            }

            // Защита: убеждаемся, что это реально игрок
            if (!playerNi.CompareTag("Player"))
            {
                Debug.LogWarning("[PortalTrigger] Server denied: playerNi is not Player tag.");
                return;
            }

            // Серверная проверка дистанции
            float dist = Vector3.Distance(playerNi.transform.position, transform.position);
            if (dist > 3.0f)
            {
                Debug.LogWarning($"[PortalTrigger] Server denied: too far dist={dist:0.00}");
                return;
            }

            var controller = FindFirstObjectByType<Project.WorldGen.ChunkDungeonController>();
            if (controller == null)
            {
                Debug.LogError("[PortalTrigger] No ChunkDungeonController found on server!");
                return;
            }

            Debug.Log($"[PortalTrigger] Server accepted use. From={FromSegment} To={ToSegment} by playerNetId={playerNi.netId}");

            controller.ServerAdvanceToNextSegmentAndTeleportParty(ToSegment);
        }

        [Server]
        public void ServerInit(int fromSegment, int toSegment, bool locked)
        {
            FromSegment = fromSegment;
            ToSegment = toSegment;
            IsLocked = locked;
        }

        [Server]
        public void ServerSetLocked(bool locked)
        {
            IsLocked = locked;
        }
    }
}