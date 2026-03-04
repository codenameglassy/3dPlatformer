using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Active while the player is gliding (RMB held in air).
    /// Handles: glide movement, reduced gravity, mesh tilt.
    ///
    /// TRANSITIONS OUT:
    ///   → Air      : RMB released (AfterUpdate)
    ///   → Grounded : OnLanded
    ///   → Floating : F held + mana available (AfterUpdate)
    /// </summary>
    public class GlidingState : PlayerStateBase
    {
        private float _currentBank;
        private float _currentPitch;

        // ── Lifecycle ────────────────────────────────────────────────────────

        public override void Enter(PlayerCharacterController controller)
        {
            base.Enter(controller);
            C.FireGlideChanged(true);
        }

        public override void Exit()
        {
            C.FireGlideChanged(false);
            // Tilt will recover naturally via UpdateGlideTilt continuing to run from AfterUpdate
        }

        public override void BeforeUpdate(float dt)
        {
            C.JumpEventFired = false;
        }

        public override void HandleInput(ref PlayerCharacterInputs inputs, Vector3 cameraPlanarDir)
        {
            C.LookInputVector = C.OrientationMethod == OrientationMethod.TowardsCamera
                ? cameraPlanarDir
                : C.MoveInputVector.normalized;
        }

        public override void UpdateRotation(ref Quaternion r, float dt)
        {
            SmoothRotateTowards(ref r, C.LookInputVector, C.OrientationSharpness, dt);
            ApplyBonusOrientation(ref r, dt);
        }

        public override void UpdateVelocity(ref Vector3 v, float dt)
        {
            ApplyGlideMovement(ref v, dt);
            HandleJump(ref v, dt, treatAsGrounded: false);
            ApplyAdditiveVelocity(ref v);
        }

        public override void AfterUpdate(float dt)
        {
            UpdateJumpTimers(dt, isGrounded: false);
            UpdateGlideTilt(dt, isGliding: true);

            // → Air: RMB released
            if (!C.GlideInputHeld)
            {
                C.TransitionToState(CharacterState.Air);
                return;
            }

            // → Floating: F held + mana
            if (C.AllowFloating && C.FloatInputHeld && C.SharedMana > 0f)
            {
                C.TransitionToState(CharacterState.Floating);
                return;
            }
        }

        public override void OnLanded()
        {
            C.TransitionToState(CharacterState.Grounded);
        }

        // ── Glide Movement ────────────────────────────────────────────────────

        private void ApplyGlideMovement(ref Vector3 v, float dt)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(v, Motor.CharacterUp);
            Vector3 vertical   = Vector3.Project(v, Motor.CharacterUp);

            horizontal = C.MoveInputVector.sqrMagnitude > 0f
                ? Vector3.Lerp(horizontal, C.MoveInputVector * C.GlideHorizontalSpeed,
                    1f - Mathf.Exp(-C.GlideAcceleration * dt))
                : Vector3.Lerp(horizontal, Vector3.zero,
                    1f - Mathf.Exp(-C.GlideDeceleration * dt));

            v = horizontal + vertical;
            v += C.Gravity * C.GlideGravityScale * dt;

            float vSpeed = Vector3.Dot(v, Motor.CharacterUp);
            if (vSpeed < -C.GlideMaxFallSpeed)
                v -= Motor.CharacterUp * (vSpeed + C.GlideMaxFallSpeed);

            v *= 1f / (1f + C.Drag * dt);
        }

        // ── Glide Tilt ────────────────────────────────────────────────────────

        /// <summary>
        /// Called from AfterUpdate with isGliding=true.
        /// Also called from AirState/GroundedState AfterUpdate with isGliding=false
        /// to allow tilt to recover smoothly after glide ends.
        /// </summary>
        public void UpdateGlideTilt(float dt, bool isGliding)
        {
            bool atNeutral = !isGliding
                && Mathf.Abs(_currentBank)  < 0.01f
                && Mathf.Abs(_currentPitch) < 0.01f;

            if (atNeutral)
            {
                if (_currentBank != 0f || _currentPitch != 0f)
                {
                    _currentBank = _currentPitch = 0f;
                    C.MeshRoot.localRotation = Quaternion.identity;
                }
                return;
            }

            float targetBank = 0f, targetPitch = 0f;

            if (isGliding)
            {
                Vector3 localMove = Motor.Transform.InverseTransformDirection(C.MoveInputVector);
                targetBank = -localMove.x * C.MaxBankAngle;

                float speedRatio = Mathf.Clamp01(
                    Vector3.ProjectOnPlane(Motor.Velocity, Motor.CharacterUp).magnitude / C.GlideHorizontalSpeed);
                targetPitch = speedRatio * C.MaxPitchAngle;
            }

            float speed  = isGliding
                ? C.TiltSmoothing     * PlayerCharacterController.TiltSmoothingScale
                : C.TiltRecoverySpeed * PlayerCharacterController.TiltRecoveryScale;
            float factor = 1f - Mathf.Exp(-speed * dt);

            _currentBank  = Mathf.Lerp(_currentBank,  targetBank,  factor);
            _currentPitch = Mathf.Lerp(_currentPitch, targetPitch, factor);
            C.MeshRoot.localRotation = Quaternion.Euler(_currentPitch, 0f, _currentBank);
        }
    }
}
