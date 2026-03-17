using Mirror;
using TMPro;
using UnityEngine;

namespace Project.UI
{
    public sealed class LobbyStatusUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text statusText;

        private void Awake()
        {
            if (statusText == null)
                statusText = GetComponent<TMP_Text>();
        }

        private void Update()
        {
            bool server = NetworkServer.active;
            bool client = NetworkClient.active;
            bool connected = NetworkClient.isConnected;

            int serverConnections = server ? NetworkServer.connections.Count : 0;

            statusText.text =
                $"Mode: {(Project.Core.LaunchParams.IsHost ? "HOST" : "CLIENT")}\n" +
                $"Server active: {server}\n" +
                $"Client active: {client}\n" +
                $"Client connected: {connected}\n" +
                $"Server connections: {serverConnections}\n" +
                $"Address: {(FindAddress() ?? "-")}";
        }

        private string FindAddress()
        {
            var nm = FindFirstObjectByType<NetworkManager>();
            return nm != null ? nm.networkAddress : null;
        }
    }
}
