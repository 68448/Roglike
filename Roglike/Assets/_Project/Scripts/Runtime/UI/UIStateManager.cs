using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.UI
{
    /// <summary>
    /// Единый менеджер UI-режимов.
    /// Любое окно (RewardUI, BuildPanel и т.д.) "берёт" UI-фокус через Push(token),
    /// и "отпускает" через Pop(token).
    ///
    /// Пока стек не пустой:
    ///  - курсор видим + unlocked
    ///  - управление локальным игроком отключено
    /// Когда стек пуст:
    ///  - курсор скрыт + locked
    ///  - управление возвращено
    /// </summary>
    public sealed class UIStateManager : MonoBehaviour
    {
        public static UIStateManager Instance { get; private set; }

        [Header("Cursor")]
        [SerializeField] private bool lockCursorWhenGameplay = true;

        // стек "кто держит UI"
        private readonly List<string> _stack = new List<string>();

        // локальный игрок
        private Project.Player.PlayerController _cachedController;
        private NetworkIdentity _cachedLocalNi;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            ApplyState();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        /// <summary>Открыть UI-режим (добавить токен).</summary>
        public void Push(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                token = "UI";

            // не добавляем дубль
            if (!_stack.Contains(token))
                _stack.Add(token);

            ApplyState();
        }

        /// <summary>Закрыть UI-режим (убрать токен).</summary>
        public void Pop(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                token = "UI";

            _stack.Remove(token);
            ApplyState();
        }

        /// <summary>На всякий случай: очистить все UI-режимы.</summary>
        public void ClearAll()
        {
            _stack.Clear();
            ApplyState();
        }

        public bool IsUIOpen => _stack.Count > 0;

        private void ApplyState()
        {
            EnsureLocalPlayerCached();

            // In menu-like scenes we always keep cursor visible and unlocked.
            string scene = SceneManager.GetActiveScene().name;
            if (scene == "MainMenu" || scene == "Lobby")
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;

                if (_cachedController != null)
                    _cachedController.enabled = false;

                return;
            }

            if (IsUIOpen)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;

                if (_cachedController != null)
                    _cachedController.enabled = false;
            }
            else
            {
                Cursor.visible = false;
                Cursor.lockState = lockCursorWhenGameplay ? CursorLockMode.Locked : CursorLockMode.None;

                if (_cachedController != null)
                    _cachedController.enabled = true;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyState();
        }

        private void EnsureLocalPlayerCached()
        {
            // если уже закешировали и объект жив
            if (_cachedLocalNi != null && _cachedController != null)
                return;

            _cachedLocalNi = null;
            _cachedController = null;

            // Находим локального игрока (на хосте тоже есть localPlayer)
            foreach (var ni in FindObjectsByType<NetworkIdentity>(FindObjectsSortMode.None))
            {
                if (ni != null && ni.isLocalPlayer)
                {
                    _cachedLocalNi = ni;
                    _cachedController = ni.GetComponent<Project.Player.PlayerController>();
                    return;
                }
            }
        }
    }
}
