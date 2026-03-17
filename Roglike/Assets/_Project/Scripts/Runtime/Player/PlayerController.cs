using Mirror;
using UnityEngine;

namespace Project.Player
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float gravity = -20f;

        [Header("Mouse Look")]
        [SerializeField] private float mouseSensitivity = 2f;
        [SerializeField] private float minPitch = -35f;
        [SerializeField] private float maxPitch = 70f;

        [Header("References")]
        [SerializeField] private Transform cameraPivot;  
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener audioListener;

        private CharacterController _cc;
        private float _verticalVelocity;
        private float _pitch;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();

            // Автопоиск ссылок
            if (cameraPivot == null)
            {
                var pivot = transform.Find("CameraPivot");
                if (pivot != null) cameraPivot = pivot;
            }

            if (playerCamera == null && cameraPivot != null)
                playerCamera = cameraPivot.GetComponentInChildren<Camera>(true);

            if (audioListener == null && playerCamera != null)
                audioListener = playerCamera.GetComponent<AudioListener>();

            // По умолчанию ВЫКЛЮЧАЕМ камеру и AudioListener у всех.
            // Включим только у локального игрока в OnStartLocalPlayer().
            SetLocalViewEnabled(false);
        }

        public override void OnStartLocalPlayer()
        {
            SetLocalViewEnabled(true);

            // В лобби курсор НЕ лочим, чтобы можно было нажимать UI
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool shouldLock = scene != "Lobby" && scene != "MainMenu";

            Cursor.lockState = shouldLock ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !shouldLock;

            Debug.Log($"[PlayerController] OnStartLocalPlayer scene={scene} lock={shouldLock} pos={transform.position}");
        }

        public override void OnStopLocalPlayer()
        {
            SetLocalViewEnabled(false);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void SetLocalViewEnabled(bool enabled)
        {
            if (playerCamera != null)
                playerCamera.enabled = enabled;

            if (audioListener != null)
                audioListener.enabled = enabled;
        }

        private void Update()
        {
            if (!isLocalPlayer)
                return;

            Look();
            Move();
        }

        private void Look()
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            transform.Rotate(0f, mouseX, 0f);

            _pitch -= mouseY;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);

            if (cameraPivot != null)
                cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void Move()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            Vector3 move = (transform.right * h + transform.forward * v).normalized;
            Vector3 velocity = move * moveSpeed;

            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            _verticalVelocity += gravity * Time.deltaTime;
            velocity.y = _verticalVelocity;

            _cc.Move(velocity * Time.deltaTime);
        }

        [Server]
        public void ServerTeleport(Vector3 worldPos)
        {
            TeleportLocal(worldPos);
            RpcTeleport(worldPos);
        }

        [ClientRpc]
        private void RpcTeleport(Vector3 worldPos)
        {
            TeleportLocal(worldPos);
        }

        private void TeleportLocal(Vector3 worldPos)
        {
            // Сброс “падения” и корректная установка позиции с CharacterController
            _verticalVelocity = 0f;

            if (_cc != null)
                _cc.enabled = false;

            transform.position = worldPos;

            if (_cc != null)
                _cc.enabled = true;
        }

    }
}
