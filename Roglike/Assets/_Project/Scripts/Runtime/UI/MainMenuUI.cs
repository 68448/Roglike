using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.UI
{
    public sealed class MainMenuUI : MonoBehaviour
    {
        [Header("Scenes")]
        [SerializeField] private string lobbySceneName = "Lobby";

        public void OnHostClicked()
        {
            Project.Core.LaunchParams.IsHost = true;
            SceneManager.LoadScene(lobbySceneName);
        }

        public void OnJoinClicked()
        {
            Project.Core.LaunchParams.IsHost = false;
            Project.Core.LaunchParams.Address = "localhost"; // позже сделаем поле ввода
            SceneManager.LoadScene(lobbySceneName);
        }

        public void OnQuitClicked()
        {
            Application.Quit();
        }
    }
}
