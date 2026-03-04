using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Active when the player is on stable ground.
    /// Handles: walking, sprinting, crouching, jump initiation.
    ///
    /// TRANSITIONS OUT:
    ///   → Air      : OnLeftGround (walked off ledge or jumped)
    ///   → Floating : F held + mana available
    /// </summary>
    public class GroundedState : PlayerStateBase
    {
        private bool _isSprinting;
        public override bool IsSprinting => _isSprinting;

        // ── Lifecycle ────────────────────────────────────────────────────────

        public override void Enter(PlayerCharacterController controller)
        {
            base.Enter(controller);
            C.JumpConsumed = false;
            C.DoubleJumpConsumed = false;
        }

        public override void Exit() => SetSprinting(false);

        public override void UpdateRotation(ref Quaternion r, float dt)
        {
            SmoothRotateTowards(ref r, C.LookInputVector, C.Movement.OrientationSharpness, dt);
            ApplyBonusOrientation(ref r, dt);
        }

        public override void UpdateVelocity(ref Vector3 v, float dt)
        {
            bool grounded = IsGrounded();

            if (grounded)
            {
                UpdateSprintState();
                ApplyGroundMovement(ref v, dt);
            }
            else
            {
                // Frame gap between ForceUnground() and PostGroundingUpdate:
                // fall back to air movement so jump velocity isn't zeroed.
                ApplyAirMovement(ref v, dt);
            }

            HandleJump(ref v, dt, treatAsGrounded: grounded);
            ApplyAdditiveVelocity(ref v);
        }

        public override void AfterUpdate(float dt)
        {
            bool grounded = IsGrounded();
            UpdateJumpTimers(dt, grounded);
            TryUncrouch();
            UpdateGlideTilt(dt, isGliding: false);

            // Jumped this frame — don't wait for PostGroundingUpdate
            if (!grounded && C.JumpedThisFrame)
            {
                C.TransitionToState(CharacterState.Air);
                return;
            }

            if (grounded && C.Floating.Enabled && C.FloatInputHeld && C.SharedMana > 0f)
                C.TransitionToState(CharacterState.Floating);
        }

        public override void OnLeftGround() => C.TransitionToState(CharacterState.Air);

        // ── Ground Movement ───────────────────────────────────────────────────

        private void ApplyGroundMovement(ref Vector3 v, float dt)
        {
            float speed = v.magnitude;
            Vector3 normal = Motor.GroundingStatus.GroundNormal;

            v = Motor.GetDirectionTangentToSurface(v, normal) * speed;

            Vector3 inputRight = Vector3.Cross(C.MoveInputVector, Motor.CharacterUp);
            Vector3 reoriented = Vector3.Cross(normal, inputRight).normalized * C.MoveInputVector.magnitude;
            float targetSpeed = _isSprinting
                ? C.Movement.MaxSpeed * C.Sprint.SpeedMultiplier
                : C.Movement.MaxSpeed;

            v = Vector3.Lerp(v, reoriented * targetSpeed,
                1f - Mathf.Exp(-C.Movement.Sharpness * dt));
        }

        // ── Sprinting ─────────────────────────────────────────────────────────

        private void SetSprinting(bool value)
        {
            if (value == _isSprinting) return;
            _isSprinting = value;
            C.FireSprintChanged(_isSprinting);
        }

        private void UpdateSprintState()
        {
            SetSprinting(C.Sprint.Enabled
                && C.SprintInputHeld
                && C.MoveInputVector.sqrMagnitude > 0f
                && !C.IsCrouching);
        }

        // ── Crouching ─────────────────────────────────────────────────────────

        public override void HandleInput(ref PlayerCharacterInputs inputs, Vector3 cameraPlanarDir)
        {
            if (C.CrouchDown && !C.IsCrouching)
            {
                C.ShouldBeCrouching = true;
                C.IsCrouching = true;
                Motor.SetCapsuleDimensions(
                    C.Crouch.StandingCapsuleRadius,
                    C.Crouch.CapsuleHeight,
                    C.Crouch.CapsuleHeight * 0.5f);
                C.MeshRoot.localScale = C.Crouch.MeshScale;
            }
            else if (C.CrouchUp)
            {
                C.ShouldBeCrouching = false;
            }
        }

        private void TryUncrouch()
        {
            if (!C.IsCrouching || C.ShouldBeCrouching) return;

            Motor.SetCapsuleDimensions(
                C.Crouch.StandingCapsuleRadius,
                C.Crouch.StandingCapsuleHeight,
                C.Crouch.StandingCapsuleHeight * 0.5f);

            bool blocked = Motor.CharacterOverlap(
                Motor.TransientPosition, Motor.TransientRotation,
                C.ProbedColliders, Motor.CollidableLayers,
                QueryTriggerInteraction.Ignore) > 0;

            if (blocked)
                Motor.SetCapsuleDimensions(
                    C.Crouch.StandingCapsuleRadius,
                    C.Crouch.CapsuleHeight,
                    C.Crouch.CapsuleHeight * 0.5f);
            else
            {
                C.MeshRoot.localScale = Vector3.one;
                C.IsCrouching = false;
            }
        }
    }
}