using UnityEngine;

namespace Project.UI
{
    /// <summary>
    /// В лобби нам нужен свободный курсор для UI.
    /// </summary>
    public sealed class LobbyCursorFix : MonoBehaviour
    {
        private void Start()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
