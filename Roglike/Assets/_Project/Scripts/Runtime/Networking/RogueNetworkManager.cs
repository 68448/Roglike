using Mirror;
using UnityEngine;

namespace Project.Networking
{
    public sealed class RogueNetworkManager : NetworkManager
    {
        [Header("Session")]
        [SerializeField] private GameObject sessionStatePrefab;

        private GameObject _sessionInstance;

        public override void OnStartServer()
        {
            base.OnStartServer();

            // Создаем SessionState один раз на сервере
            if (_sessionInstance == null)
            {
                if (sessionStatePrefab == null)
                {
                    Debug.LogError("[RogueNetworkManager] sessionStatePrefab is not assigned!");
                    return;
                }

                _sessionInstance = Instantiate(sessionStatePrefab);
                DontDestroyOnLoad(_sessionInstance);

                // Спавним как сетевой объект
                NetworkServer.Spawn(_sessionInstance);

                Debug.Log("[RogueNetworkManager] SessionState spawned and marked DontDestroyOnLoad.");
            }
        }

        public override void OnStopServer()
        {
            // При остановке сервера чистим сессию
            if (_sessionInstance != null)
            {
                NetworkServer.Destroy(_sessionInstance);
                _sessionInstance = null;
            }

            base.OnStopServer();
        }
    }
}
