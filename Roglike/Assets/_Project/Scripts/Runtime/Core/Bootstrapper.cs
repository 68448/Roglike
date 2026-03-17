using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.Core
{
    /// <summary>
    /// Точка входа игры. Должна находиться в сцене Boot.
    /// Делает объект постоянным (DontDestroyOnLoad) и грузит главное меню.
    /// </summary>
    public sealed class Bootstrapper : MonoBehaviour
    {
        private static Bootstrapper _instance;

        [Header("Startup")]
        [Tooltip("Название сцены главного меню (должна быть в Build Settings).")]
        [SerializeField] private string mainMenuSceneName = "MainMenu";

        private void Awake()
        {
            // Защита от дублей (если случайно появится второй Bootstrapper)
            if (_instance != null)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            // Если мы уже не в Boot (например, запустили Play из другой сцены),
            // то всё равно корректно переходим в главное меню.
            if (SceneManager.GetActiveScene().name != mainMenuSceneName)
            {
                SceneManager.LoadScene(mainMenuSceneName);
            }
        }
    }
}
