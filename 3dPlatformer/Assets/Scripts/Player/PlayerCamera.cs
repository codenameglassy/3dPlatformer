using System.Collections.Generic;
using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Third-person orbit camera with obstruction handling, zoom, and smooth following.
    /// Attach to the camera GameObject. Set its follow target via SetFollowTransform().
    /// </summary>
    public class PlayerCamera : MonoBehaviour
    {
        [Header("Framing")]
        public Camera Camera;
        public Vector2 FollowPointFraming = new Vector2(0f, 0f);
        public float FollowingSharpness = 10000f;

        [Header("Distance")]
        public float DefaultDistance = 6f;
        public float MinDistance = 0f;
        public float MaxDistance = 10f;
        public float DistanceMovementSpeed = 5f;
        public float DistanceMovementSharpness = 10f;

        [Header("Rotation")]
        public bool InvertX = false;
        public bool InvertY = false;
        [Range(-90f, 90f)] public float DefaultVerticalAngle = 20f;
        [Range(-90f, 90f)] public float MinVerticalAngle = -90f;
        [Range(-90f, 90f)] public float MaxVerticalAngle = 90f;
        public float RotationSpeed = 1f;
        public float RotationSharpness = 10000f;
        public bool RotateWithPhysicsMover = false;

        [Header("Obstruction")]
        public float ObstructionCheckRadius = 0.2f;
        public LayerMask ObstructionLayers = -1;
        public float ObstructionSharpness = 10000f;

        /// <summary>
        /// Colliders to ignore during obstruction checks (usually the player's own colliders).
        /// Uses a List for Inspector assignment; synced to a HashSet at runtime for O(1) lookup.
        /// </summary>
        public List<Collider> IgnoredColliders = new List<Collider>();

        // ─── Public Properties ───────────────────────────────────────────────

        public Transform Transform { get; private set; }
        public Transform FollowTransform { get; private set; }
        public Vector3 PlanarDirection { get; set; }
        public float TargetDistance { get; set; }

        // ─── Private Fields ──────────────────────────────────────────────────

        private const int MaxObstructions = 32;

        // HashSet for O(1) ignored-collider lookup (vs List's O(n) per frame)
        private HashSet<Collider> _ignoredCollidersSet = new HashSet<Collider>();

        private bool _distanceIsObstructed;
        private float _currentDistance;
        private float _targetVerticalAngle;
        private int _obstructionCount;
        private RaycastHit[] _obstructions = new RaycastHit[MaxObstructions];
        private Vector3 _currentFollowPosition;

        // ─── Unity Lifecycle ─────────────────────────────────────────────────

        private void OnValidate()
        {
            DefaultDistance = Mathf.Clamp(DefaultDistance, MinDistance, MaxDistance);
            DefaultVerticalAngle = Mathf.Clamp(DefaultVerticalAngle, MinVerticalAngle, MaxVerticalAngle);
        }

        private void Awake()
        {
            Transform = this.transform;

            _currentDistance = DefaultDistance;
            TargetDistance = _currentDistance;
            _targetVerticalAngle = 0f;
            PlanarDirection = Vector3.forward;
        }

        // ─── Public API ──────────────────────────────────────────────────────

        /// <summary>Set the transform this camera orbits around. Call once at Start.</summary>
        public void SetFollowTransform(Transform target)
        {
            FollowTransform = target;
            PlanarDirection = FollowTransform.forward;
            _currentFollowPosition = FollowTransform.position;
        }

        /// <summary>
        /// Sync the runtime HashSet with the Inspector List.
        /// Call this if you add/remove ignored colliders at runtime.
        /// </summary>
        public void RebuildIgnoredCollidersSet()
        {
            _ignoredCollidersSet.Clear();
            foreach (Collider col in IgnoredColliders)
            {
                if (col != null) _ignoredCollidersSet.Add(col);
            }
        }

        /// <summary>Called each LateUpdate by PlayerInputHandler to move and rotate the camera.</summary>
        public void UpdateWithInput(float deltaTime, float zoomInput, Vector3 rotationInput)
        {
            if (FollowTransform == null) return;

            // Sync ignored colliders set if needed (cheap — only rebuilds when list changes)
            if (_ignoredCollidersSet.Count != IgnoredColliders.Count)
            {
                RebuildIgnoredCollidersSet();
            }

            ApplyRotationInput(rotationInput, deltaTime);
            ApplyZoomInput(zoomInput);

            // Smoothly follow the target position
            _currentFollowPosition = Vector3.Lerp(
                _currentFollowPosition,
                FollowTransform.position,
                1f - Mathf.Exp(-FollowingSharpness * deltaTime)
            );

            float smoothedDistance = ResolveObstructedDistance(deltaTime);

            // Compute final camera position with framing offset
            Quaternion targetRotation = Transform.rotation;
            Vector3 targetPosition = _currentFollowPosition - (targetRotation * Vector3.forward) * smoothedDistance;
            targetPosition += Transform.right * FollowPointFraming.x;
            targetPosition += Transform.up * FollowPointFraming.y;

            Transform.position = targetPosition;
        }

        // ─── Private Helpers ─────────────────────────────────────────────────

        private void ApplyRotationInput(Vector3 rotationInput, float deltaTime)
        {
            if (InvertX) rotationInput.x *= -1f;
            if (InvertY) rotationInput.y *= -1f;

            // Horizontal rotation (yaw)
            Quaternion yawRotation = Quaternion.Euler(FollowTransform.up * (rotationInput.x * RotationSpeed));
            PlanarDirection = yawRotation * PlanarDirection;
            PlanarDirection = Vector3.Cross(
                FollowTransform.up,
                Vector3.Cross(PlanarDirection, FollowTransform.up)
            );

            Quaternion planarRot = Quaternion.LookRotation(PlanarDirection, FollowTransform.up);

            // Vertical rotation (pitch)
            _targetVerticalAngle -= rotationInput.y * RotationSpeed;
            _targetVerticalAngle = Mathf.Clamp(_targetVerticalAngle, MinVerticalAngle, MaxVerticalAngle);
            Quaternion pitchRot = Quaternion.Euler(_targetVerticalAngle, 0f, 0f);

            Transform.rotation = Quaternion.Slerp(
                Transform.rotation,
                planarRot * pitchRot,
                1f - Mathf.Exp(-RotationSharpness * deltaTime)
            );
        }

        private void ApplyZoomInput(float zoomInput)
        {
            // If camera was pushed in by obstruction, reset target to current before zooming
            if (_distanceIsObstructed && Mathf.Abs(zoomInput) > 0f)
            {
                TargetDistance = _currentDistance;
            }

            TargetDistance += zoomInput * DistanceMovementSpeed;
            TargetDistance = Mathf.Clamp(TargetDistance, MinDistance, MaxDistance);
        }

        /// <summary>
        /// Sphere-casts toward the camera to find the closest unignored obstruction.
        /// Returns the smoothed distance to use for camera placement.
        /// </summary>
        private float ResolveObstructedDistance(float deltaTime)
        {
            RaycastHit closestHit = FindClosestObstruction();

            if (closestHit.distance < Mathf.Infinity)
            {
                _distanceIsObstructed = true;
                _currentDistance = Mathf.Lerp(
                    _currentDistance,
                    closestHit.distance,
                    1f - Mathf.Exp(-ObstructionSharpness * deltaTime)
                );
            }
            else
            {
                _distanceIsObstructed = false;
                _currentDistance = Mathf.Lerp(
                    _currentDistance,
                    TargetDistance,
                    1f - Mathf.Exp(-DistanceMovementSharpness * deltaTime)
                );
            }

            return _currentDistance;
        }

        private RaycastHit FindClosestObstruction()
        {
            RaycastHit closestHit = new RaycastHit { distance = Mathf.Infinity };

            _obstructionCount = Physics.SphereCastNonAlloc(
                _currentFollowPosition,
                ObstructionCheckRadius,
                -Transform.forward,
                _obstructions,
                TargetDistance,
                ObstructionLayers,
                QueryTriggerInteraction.Ignore
            );

            for (int i = 0; i < _obstructionCount; i++)
            {
                RaycastHit hit = _obstructions[i];

                // Skip ignored colliders (O(1) HashSet lookup)
                if (_ignoredCollidersSet.Contains(hit.collider)) continue;
                if (hit.distance <= 0f) continue;

                if (hit.distance < closestHit.distance)
                {
                    closestHit = hit;
                }
            }

            return closestHit;
        }
    }
}