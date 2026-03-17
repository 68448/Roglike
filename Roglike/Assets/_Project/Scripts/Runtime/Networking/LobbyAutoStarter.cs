using Mirror;
using UnityEngine;

namespace Project.Networking
{
    /// <summary>
    /// Когда мы попали в сцену Lobby, автоматически запускаем Host или Client
    /// в зависимости от выбора в главном меню (LaunchParams).
    /// </summary>
    public sealed class LobbyAutoStarter : MonoBehaviour
    {
        [SerializeField] private NetworkManager networkManager;

        private void Awake()
        {
            if (networkManager == null)
                networkManager = FindFirstObjectByType<NetworkManager>();
        }

        private void Start()
        {
            if (networkManager == null)
            {
                Debug.LogError("[LobbyAutoStarter] NetworkManager not found in scene!");
                return;
            }

            // Если сеть уже запущена (на всякий случай) — ничего не делаем.
            if (NetworkServer.active || NetworkClient.active)
                return;

            if (Project.Core.LaunchParams.IsHost)
            {
                networkManager.StartHost();
                Debug.Log("[LobbyAutoStarter] Started HOST");
            }
            else
            {
                // Адрес для клиента
                networkManager.networkAddress = Project.Core.LaunchParams.Address;
                networkManager.StartClient();
                Debug.Log($"[LobbyAutoStarter] Started CLIENT. Address={networkManager.networkAddress}");
            }
        }
    }
}
