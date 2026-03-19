using Mirror;
using UnityEngine;

namespace Project.Gameplay
{
    [RequireComponent(typeof(NetworkIdentity))]
    public sealed class EnemyHazardZone : NetworkBehaviour
    {
        private const float GroundProbeHeight = 200f;
        private const float GroundProbeDistance = 500f;
        private const float VisualHeight = 0.08f;
        private const float VisualLift = 0.06f;
        private static readonly Color WarningColor = new(1f, 0.78f, 0.2f, 1f);
        private static readonly Color ActiveColor = new(1f, 0.2f, 0.15f, 1f);

        [SyncVar] private int _damagePerTick;
        [SyncVar] private float _tickInterval;
        [SyncVar] private float _radius;
        [SyncVar] private double _armAt;
        [SyncVar] private double _expireAt;

        private float _tickTimer;
        private GameObject _visualMarker;
        private MaterialPropertyBlock _block;

        [Server]
        public void ServerInit(int damagePerTick, float tickInterval, float radius, float warningDuration, float lifetime)
        {
            _damagePerTick = Mathf.Max(1, damagePerTick);
            _tickInterval = Mathf.Max(0.1f, tickInterval);
            _radius = Mathf.Max(0.5f, radius);
            _armAt = NetworkTime.time + Mathf.Max(0.1f, warningDuration);
            _expireAt = _armAt + Mathf.Max(0.5f, lifetime);
            _tickTimer = 0f;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            EnsureVisualMarker();
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            SnapToGround();

            if (isClient)
                UpdateVisualMarker();

            if (!NetworkServer.active)
                return;

            if (NetworkTime.time >= _expireAt)
            {
                NetworkServer.Destroy(gameObject);
                return;
            }

            if (NetworkTime.time < _armAt)
                return;

            _tickTimer -= Time.deltaTime;
            if (_tickTimer > 0f)
                return;

            _tickTimer = _tickInterval;

            var hits = Physics.OverlapSphere(transform.position, _radius);
            for (int i = 0; i < hits.Length; i++)
            {
                var player = hits[i].GetComponentInParent<PlayerHealth>();
                if (player == null || player.CurrentHP <= 0)
                    continue;

                player.TakeDamage(_damagePerTick, gameObject);
            }
        }

        private void EnsureVisualMarker()
        {
            if (_visualMarker != null)
                return;

            _visualMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _visualMarker.name = "HazardZoneMarker_Runtime";
            Destroy(_visualMarker.GetComponent<Collider>());
            _visualMarker.transform.SetParent(transform, false);
            _visualMarker.transform.localPosition = new Vector3(0f, VisualLift, 0f);

            var renderer = _visualMarker.GetComponent<Renderer>();
            if (renderer != null)
            {
                ApplyMarkerColor(renderer, WarningColor);
            }
        }

        private void UpdateVisualMarker()
        {
            EnsureVisualMarker();
            if (_visualMarker == null)
                return;

            float diameter = Mathf.Max(1f, _radius * 2f);
            _visualMarker.transform.localScale = new Vector3(diameter, VisualHeight, diameter);

            var renderer = _visualMarker.GetComponent<Renderer>();
            if (renderer != null)
            {
                bool isArmed = NetworkTime.time >= _armAt;
                ApplyMarkerColor(renderer, isArmed ? ActiveColor : WarningColor);
            }
        }

        private void SnapToGround()
        {
            Vector3 origin = transform.position + Vector3.up * GroundProbeHeight;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, GroundProbeDistance, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
                return;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (!IsValidGroundHit(hit))
                    continue;

                transform.position = BuildSurfacePosition(hit);
                return;
            }
        }

        private static bool IsValidGroundHit(RaycastHit hit)
        {
            if (hit.collider == null)
                return false;

            if (hit.normal.y < 0.35f)
                return false;

            if (hit.collider.attachedRigidbody != null)
                return false;

            if (hit.collider.GetComponentInParent<PlayerHealth>() != null)
                return false;

            if (hit.collider.GetComponentInParent<EnemyHealth>() != null)
                return false;

            if (hit.collider.GetComponentInParent<EnemyHazardZone>() != null)
                return false;

            return true;
        }

        private Vector3 BuildSurfacePosition(RaycastHit hit)
        {
            float y = hit.point.y;

            var renderer = hit.collider.GetComponent<Renderer>();
            if (renderer == null)
                renderer = hit.collider.GetComponentInParent<Renderer>();

            if (renderer != null)
                y = renderer.bounds.max.y;
            else
                y = Mathf.Max(y, hit.collider.bounds.max.y);

            return new Vector3(transform.position.x, y + 0.02f, transform.position.z);
        }

        private void ApplyMarkerColor(Renderer renderer, Color color)
        {
            renderer.GetPropertyBlock(_block);
            _block.SetColor("_Color", color);
            _block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(_block);
        }
    }
}
