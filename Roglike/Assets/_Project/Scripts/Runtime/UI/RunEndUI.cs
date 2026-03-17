using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    public sealed class RunEndUI : MonoBehaviour
    {
        public static RunEndUI Instance { get; private set; }

        [Header("Root")]
        [SerializeField] private GameObject root;

        [Header("Texts")]
        [SerializeField] private Text titleText;
        [SerializeField] private Text segmentReachedText;
        [SerializeField] private Text essenceEarnedText;
        [SerializeField] private Text bestRunText;

        [Header("Buttons")]
        [SerializeField] private Button returnToMenuButton;
        [SerializeField] private Button restartRunButton;

        [Header("Scenes")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";
        [SerializeField] private string lobbySceneName = "Lobby";

        private bool _isShown;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (returnToMenuButton != null)
            {
                returnToMenuButton.onClick.RemoveAllListeners();
                returnToMenuButton.onClick.AddListener(OnReturnToMenuClicked);
            }

            if (restartRunButton != null)
            {
                restartRunButton.onClick.RemoveAllListeners();
                restartRunButton.onClick.AddListener(OnRestartRunClicked);
            }

            HideImmediate();
        }

        public void ShowForLocalDeath(int segmentReached, int essenceEarnedThisRun, int bestSegment)
        {
            if (_isShown)
                return;

            _isShown = true;

            if (titleText != null)
                titleText.text = "Run End";

            if (segmentReachedText != null)
                segmentReachedText.text = $"Segment reached: {segmentReached}";

            if (essenceEarnedText != null)
                essenceEarnedText.text = $"Essence earned: {essenceEarnedThisRun}";

            if (bestRunText != null)
                bestRunText.text = $"Best run: {bestSegment}";

            if (root != null)
                root.SetActive(true);
            else
                gameObject.SetActive(true);

            UIStateManager.Instance?.Push("RunEndUI");
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void HideImmediate()
        {
            _isShown = false;
            if (root != null)
                root.SetActive(false);
        }

        private void OnReturnToMenuClicked()
        {
            ShutdownNetworkAndLoad(mainMenuSceneName);
        }

        private void OnRestartRunClicked()
        {
            ShutdownNetworkAndLoad(lobbySceneName);
        }

        private void ShutdownNetworkAndLoad(string sceneName)
        {
            var nm = NetworkManager.singleton;
            if (nm != null)
            {
                if (NetworkServer.active && NetworkClient.isConnected)
                    nm.StopHost();
                else if (NetworkClient.isConnected)
                    nm.StopClient();
                else if (NetworkServer.active)
                    nm.StopServer();
            }

            UIStateManager.Instance?.ClearAll();
            SceneManager.LoadScene(sceneName);
        }
    }
}
