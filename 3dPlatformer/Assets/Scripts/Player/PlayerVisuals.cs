using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Observes PlayerCharacterController events and drives all visual side-effects.
    ///
    /// OBSERVER PATTERN:
    ///   - Subscribes to controller events in OnEnable, unsubscribes in OnDisable.
    ///   - Never polls state every frame — reacts only when something changes.
    ///   - Never modifies controller state — read-only relationship.
    ///
    /// ADDING NEW VISUALS:
    ///   1. Add a [Header] and public field for your VFX/component reference.
    ///   2. Subscribe a handler method in SubscribeToEvents().
    ///   3. Unsubscribe it in UnsubscribeFromEvents().
    ///   4. Write the handler — keep it focused on one responsibility.
    /// </summary>
    public class PlayerVisuals : MonoBehaviour
    {
        [Header("References")]
        public PlayerCharacterController Controller;

        [Header("Speed Lines VFX")]
        public GameObject SpeedLinesVFX;

        [Header("Gliding Model")]
        public GameObject GlidingModel; // Gliding visual — enabled while gliding, disabled otherwise

        [Header("Sprint FOV")]
        public Camera PlayerCamera;
        public float BaseFOV = 60f;   // Normal field of view
        public float SprintFOV = 75f;   // FOV when sprinting
        public float GlideFOV = 80f;   // FOV when gliding (wider for sense of speed)
        public float FOVChangeSpeed = 8f;    // How fast FOV lerps in/out

        // ─── Private State ────────────────────────────────────────────────────

        private float _targetFOV;

        // ─── Unity Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            if (SpeedLinesVFX != null)
                SpeedLinesVFX.SetActive(false);

            if (GlidingModel != null)
                GlidingModel.SetActive(false);

            _targetFOV = BaseFOV;

            if (PlayerCamera != null)
                PlayerCamera.fieldOfView = BaseFOV;
        }

        private void OnEnable()
        {
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

        // ─── Subscription Management ─────────────────────────────────────────

        private void SubscribeToEvents()
        {
            if (Controller == null) return;

            Controller.OnGlideChanged += HandleGlideChanged;
            Controller.OnSprintChanged += HandleSprintChanged;
            Controller.OnLandedEvent += HandleLanded;
            Controller.OnLeftGroundEvent += HandleLeftGround;
            Controller.OnStateChanged += HandleStateChanged;
            Controller.OnJumpedEvent += HandleJumped;
            Controller.OnFloatChanged += HandleFloatChanged;
            Controller.OnManaChanged += HandleManaChanged;
        }

        private void UnsubscribeFromEvents()
        {
            if (Controller == null) return;

            Controller.OnGlideChanged -= HandleGlideChanged;
            Controller.OnSprintChanged -= HandleSprintChanged;
            Controller.OnLandedEvent -= HandleLanded;
            Controller.OnLeftGroundEvent -= HandleLeftGround;
            Controller.OnStateChanged -= HandleStateChanged;
            Controller.OnJumpedEvent -= HandleJumped;
            Controller.OnFloatChanged -= HandleFloatChanged;
            Controller.OnManaChanged -= HandleManaChanged;
        }

        // ─── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Enables or disables the speed lines VFX.
        /// Public so any future feature (sliding, dash, boost pad, etc.) can trigger it:
        ///   playerVisuals.SetSpeedLines(true);
        /// </summary>
        public void SetSpeedLines(bool active)
        {
            if (SpeedLinesVFX != null)
                SpeedLinesVFX.SetActive(active);
        }

        // ─── Event Handlers ──────────────────────────────────────────────────

        private void HandleGlideChanged(bool isGliding)
        {
            SetSpeedLines(isGliding);

            if (GlidingModel != null)
                GlidingModel.SetActive(isGliding);

            // Glide FOV takes priority over sprint FOV — revert to base (or sprint) when glide ends
            if (isGliding)
                _targetFOV = GlideFOV;
            else
                _targetFOV = Controller.IsSprinting ? SprintFOV : BaseFOV;
        }

        private void HandleSprintChanged(bool isSprinting)
        {
            // Set the FOV target — UpdateFOV() smoothly lerps toward it every frame
            _targetFOV = isSprinting ? SprintFOV : BaseFOV;
        }

        private void HandleLanded()
        {
            SoundManager.Instance.Play("land");
        }

        private void HandleLeftGround()
        {
            // e.g. trigger jump anticipation animation
        }

        private void HandleStateChanged(CharacterState newState, CharacterState previousState)
        {
            // e.g. swap animator layers when entering Carrying state
        }

        private void HandleJumped() => SoundManager.Instance.Play("jump");

        private void HandleFloatChanged(bool isFloating)
        {
            // e.g. toggle float particles, play float sound
            // SoundManager.Instance.Play(isFloating ? "float_start" : "float_end");
        }

        private void HandleManaChanged(float normalizedMana)
        {
            // normalizedMana is 0-1 — drive your mana bar UI here
            // e.g. ManaBarUI.SetFill(normalizedMana);
        }

        // ─── Private Updaters ────────────────────────────────────────────────

        /// <summary>
        /// Smoothly lerps camera FOV toward the target set by event handlers.
        /// Runs every frame but only does meaningful work during transitions.
        /// </summary>
        private void UpdateFOV()
        {
            if (PlayerCamera == null) return;
            if (Mathf.Approximately(PlayerCamera.fieldOfView, _targetFOV)) return;

            PlayerCamera.fieldOfView = Mathf.Lerp(
                PlayerCamera.fieldOfView,
                _targetFOV,
                1f - Mathf.Exp(-FOVChangeSpeed * Time.deltaTime)
            );
        }
    }
}