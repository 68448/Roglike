using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    public sealed class LobbyRunUI : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private Button startRunButton;
        [SerializeField] private TMP_Text startRunLabel;

        [Header("Scene Names")]
        [SerializeField] private string dungeonSceneName = "Dungeon";

        [SerializeField] private Project.Networking.RunStartController runStartController;

        private void Awake()
        {
            if (startRunButton == null)
                startRunButton = GetComponent<Button>();

            if (startRunLabel == null && startRunButton != null)
                startRunLabel = startRunButton.GetComponentInChildren<TMP_Text>(true);

            if (runStartController == null)
                runStartController = FindFirstObjectByType<Project.Networking.RunStartController>();
        }

        private void Start()
        {
            // Кнопка должна быть активна только у хоста (сервер).
            Refresh();
        }

        private void Update()
        {
            // На старте прототипа обновляем раз в кадр (потом оптимизируем).
            Refresh();
        }

        private void Refresh()
        {
            bool isServer = NetworkServer.active;
            if (startRunButton != null)
                startRunButton.gameObject.SetActive(isServer);
        }

        public void OnStartRunClicked()
        {
            if (!NetworkServer.active)
                return;

            runStartController?.PrepareNewRunIfNeeded();

            // Меняем сцену для всех через сервер
            var nm = NetworkManager.singleton;
            if (nm != null)
                nm.ServerChangeScene(dungeonSceneName);
        }
    }
}
