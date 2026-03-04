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

        // Add future VFX references here, e.g.:
        // [Header("Landing VFX")]
        // public ParticleSystem LandingDustVFX;

        // ─── Unity Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            // Ensure all VFX start disabled regardless of scene setup
            if (SpeedLinesVFX != null)
                SpeedLinesVFX.SetActive(false);
        }

        private void OnEnable()
        {
            SubscribeToEvents();
        }

        private void OnDisable()
        {
            // Always unsubscribe to prevent memory leaks and ghost callbacks
            UnsubscribeFromEvents();
        }

        // ─── Subscription Management ─────────────────────────────────────────

        private void SubscribeToEvents()
        {
            if (Controller == null) return;

            Controller.OnGlideChanged += HandleGlideChanged;
            Controller.OnLandedEvent += HandleLanded;
            Controller.OnLeftGroundEvent += HandleLeftGround;
            Controller.OnStateChanged += HandleStateChanged;
        }

        private void UnsubscribeFromEvents()
        {
            if (Controller == null) return;

            Controller.OnGlideChanged -= HandleGlideChanged;
            Controller.OnLandedEvent -= HandleLanded;
            Controller.OnLeftGroundEvent -= HandleLeftGround;
            Controller.OnStateChanged -= HandleStateChanged;
        }

        // ─── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Enables or disables the speed lines VFX.
        /// Public so any future feature (sliding, dash, boost pad, etc.) can trigger it directly:
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
        }

        private void HandleLanded()
        {
            // e.g. LandingDustVFX?.Play();
        }

        private void HandleLeftGround()
        {
            // e.g. trigger jump anticipation animation
        }

        private void HandleStateChanged(CharacterState newState, CharacterState previousState)
        {
            // e.g. swap animator layers when entering Carrying state
            // e.g. enable carry IK rig
        }
    }
}