using Mirror;
using UnityEngine;

namespace Project.UI
{
    public sealed class DungeonUI : MonoBehaviour
    {
        [SerializeField] private string lobbySceneName = "Lobby";

        public void OnBackToLobbyClicked()
        {
            if (!NetworkServer.active)
                return;

            var nm = NetworkManager.singleton;
            if (nm != null)
                nm.ServerChangeScene(lobbySceneName);
        }
    }
}
