using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Observes PlayerCharacterController events and drives all visual side-effects.
    ///
    /// OBSERVER PATTERN:
    ///   - Subscribes to controller events in Start (after all Awake calls complete).
    ///   - Unsubscribes in OnDisable to prevent memory leaks.
    ///   - Never polls state every frame — reacts only when something changes.
    ///   - Never modifies controller state — read-only relationship.
    ///
    /// ADDING NEW VISUALS:
    ///   1. Add a [Header] + serialized field for your component/VFX reference.
    ///   2. Subscribe a handler in SubscribeToEvents().
    ///   3. Unsubscribe it in UnsubscribeFromEvents().
    ///   4. Write the handler — keep it focused on one responsibility.
    /// </summary>
    public class PlayerVisuals : MonoBehaviour
    {
        [Header("Speed Lines VFX")]
        public GameObject SpeedLinesVFX;

        [Header("Gliding Model")]
        public GameObject GlidingModel;

        [Header("FOV")]
        public Camera PlayerCamera;
        public float BaseFOV = 60f;
        public float SprintFOV = 75f;
        public float GlideFOV = 80f;
        public float FOVChangeSpeed = 8f;

        // ── Private ───────────────────────────────────────────────────────────

        private PlayerCharacterController _controller;
        private float _targetFOV;

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Awake()
        {
            // Auto-resolve controller from same GameObject — no manual Inspector wiring needed
            _controller = GetComponent<PlayerCharacterController>();
            if (_controller == null)
                Debug.LogError("[PlayerVisuals] No PlayerCharacterController found on this GameObject.");

            if (SpeedLinesVFX != null) SpeedLinesVFX.SetActive(false);
            if (GlidingModel != null) GlidingModel.SetActive(false);

            _targetFOV = BaseFOV;
            if (PlayerCamera != null) PlayerCamera.fieldOfView = BaseFOV;
        }

        private void Start()
        {
            // Subscribe in Start — guarantees controller Awake has already run
            SubscribeToEvents();
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
        }

        private void Update()
        {
            UpdateFOV();
        }

        // ── Event Subscription ────────────────────────────────────────────────

        private void SubscribeToEvents()
        {
            if (_controller == null) return;

            _controller.OnGlideChanged += HandleGlideChanged;
            _controller.OnSprintChanged += HandleSprintChanged;
            _controller.OnLandedEvent += HandleLanded;
            _controller.OnLeftGroundEvent += HandleLeftGround;
            _controller.OnStateChanged += HandleStateChanged;
            _controller.OnJumpedEvent += HandleJumped;
            _controller.OnFloatChanged += HandleFloatChanged;
            _controller.OnManaChanged += HandleManaChanged;
        }

        private void UnsubscribeFromEvents()
        {
            if (_controller == null) return;

            _controller.OnGlideChanged -= HandleGlideChanged;
            _controller.OnSprintChanged -= HandleSprintChanged;
            _controller.OnLandedEvent -= HandleLanded;
            _controller.OnLeftGroundEvent -= HandleLeftGround;
            _controller.OnStateChanged -= HandleStateChanged;
            _controller.OnJumpedEvent -= HandleJumped;
            _controller.OnFloatChanged -= HandleFloatChanged;
            _controller.OnManaChanged -= HandleManaChanged;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Enables or disables speed lines VFX.
        /// Public so external features (dash, boost pad, etc.) can call it directly.
        /// </summary>
        public void SetSpeedLines(bool active)
        {
            if (SpeedLinesVFX != null)
                SpeedLinesVFX.SetActive(active);
        }

        // ── Event Handlers ────────────────────────────────────────────────────

        private void HandleGlideChanged(bool isGliding)
        {
            SetSpeedLines(isGliding);

            if (GlidingModel != null)
                GlidingModel.SetActive(isGliding);

            _targetFOV = isGliding
                ? GlideFOV
                : (_controller.IsSprinting ? SprintFOV : BaseFOV);
        }

        private void HandleSprintChanged(bool isSprinting)
        {
            if (!_controller.IsGliding)
                _targetFOV = isSprinting ? SprintFOV : BaseFOV;
        }

        private void HandleLanded()
        {
            SoundManager.Instance.Play("land");
        }

        private void HandleLeftGround() { }

        private void HandleStateChanged(CharacterState newState, CharacterState previousState) { }

        private void HandleJumped() => SoundManager.Instance.Play("jump");

        private void HandleFloatChanged(bool isFloating)
        {
            // SoundManager.Instance.Play(isFloating ? "float_start" : "float_end");
        }

        private void HandleManaChanged(float normalizedMana)
        {
            // ManaBarUI.SetFill(normalizedMana);
        }

        // ── FOV ───────────────────────────────────────────────────────────────

        private void UpdateFOV()
        {
            if (PlayerCamera == null) return;
            if (Mathf.Approximately(PlayerCamera.fieldOfView, _targetFOV)) return;

            PlayerCamera.fieldOfView = Mathf.Lerp(
                PlayerCamera.fieldOfView,
                _targetFOV,
                1f - Mathf.Exp(-FOVChangeSpeed * Time.deltaTime));
        }
    }
}