using Mirror;
using UnityEngine;

namespace Project.WorldGen
{
    public sealed class PortalState : NetworkBehaviour
    {
        [SyncVar] public int SegmentIndex;
        [SyncVar(hook = nameof(OnOpenChanged))] public bool IsOpen;

        [Header("Visuals")]
        [SerializeField] private Renderer portalRenderer;

        private void Awake()
        {
            if (portalRenderer == null)
                portalRenderer = GetComponentInChildren<Renderer>();
        }

        public override void OnStartClient()
        {
            ApplyVisual(IsOpen);
        }

        [Server]
        public void Init(int segmentIndex)
        {
            SegmentIndex = segmentIndex;
            IsOpen = false;
        }

        [Server]
        public void Open()
        {
            IsOpen = true;
        }

        private void OnOpenChanged(bool oldValue, bool newValue)
        {
            ApplyVisual(newValue);
        }

        private void ApplyVisual(bool open)
        {
            if (portalRenderer == null)
                return;

            // Самое простое: меняем цвет материала
            // Важно: используем material (создаст инстанс на объекте), это нормально для прототипа.
            portalRenderer.material.color = open ? Color.green : Color.red;
        }
    }
}
