using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Active when the player is on stable ground.
    /// Handles: walking, sprinting, crouching, jump initiation.
    ///
    /// TRANSITIONS OUT:
    ///   → Air      : OnLeftGround (walked off or jumped)
    ///   → Floating : F held + mana available (AfterUpdate)
    /// </summary>
    public class GroundedState : PlayerStateBase
    {
        private bool _isSprinting;
        public override bool IsSprinting => _isSprinting;

        // ── Lifecycle ────────────────────────────────────────────────────────

        public override void Enter(PlayerCharacterController controller)
        {
            base.Enter(controller);
            // Reset jump flags fresh on landing
            C.JumpConsumed = false;
            C.DoubleJumpConsumed = false;
        }

        public override void Exit()
        {
            SetSprinting(false);
        }

        public override void BeforeUpdate(float dt)
        {
            C.JumpEventFired = false;

            // Mana regenerates on stable ground
            UpdateMana(dt);
        }

        public override void UpdateRotation(ref Quaternion r, float dt)
        {
            SmoothRotateTowards(ref r, C.LookInputVector, C.OrientationSharpness, dt);
            ApplyBonusOrientation(ref r, dt);
        }

        public override void UpdateVelocity(ref Vector3 v, float dt)
        {
            bool isGrounded = C.AllowJumpingWhenSliding
                ? Motor.GroundingStatus.FoundAnyGround
                : Motor.GroundingStatus.IsStableOnGround;

            if (isGrounded)
            {
                UpdateSprintState();
                ApplyGroundMovement(ref v, dt);
            }
            else
            {
                // Not actually on ground this sub-step (e.g. just jumped, mid-ForceUnground).
                // Apply air movement so velocity isn't zeroed out while the state transition
                // is still pending (PostGroundingUpdate fires after UpdateVelocity).
                ApplyAirMovement(ref v, dt);
            }

            HandleJump(ref v, dt, treatAsGrounded: isGrounded);
            ApplyAdditiveVelocity(ref v);
        }

        public override void AfterUpdate(float dt)
        {
            bool isGrounded = C.AllowJumpingWhenSliding
                ? Motor.GroundingStatus.FoundAnyGround
                : Motor.GroundingStatus.IsStableOnGround;

            UpdateJumpTimers(dt, isGrounded);
            TryUncrouch();

            // Drive tilt back to neutral if player landed after gliding
            C.GlidingStateInstance.UpdateGlideTilt(dt, isGliding: false);

            // Safety: if PostGroundingUpdate didn't fire OnLeftGround yet but we're
            // clearly airborne (e.g. jumped this frame), transition immediately.
            if (!isGrounded && C.JumpedThisFrame)
            {
                C.TransitionToState(CharacterState.Air);
                return;
            }

            // Transition to Floating if player presses F with mana
            if (isGrounded && C.AllowFloating && C.FloatInputHeld && C.SharedMana > 0f)
                C.TransitionToState(CharacterState.Floating);
        }

        public override void OnLeftGround()
        {
            C.TransitionToState(CharacterState.Air);
        }

        // ── Movement ─────────────────────────────────────────────────────────

        private void ApplyGroundMovement(ref Vector3 v, float dt)
        {
            float speed = v.magnitude;
            Vector3 normal = Motor.GroundingStatus.GroundNormal;

            v = Motor.GetDirectionTangentToSurface(v, normal) * speed;

            Vector3 inputRight = Vector3.Cross(C.MoveInputVector, Motor.CharacterUp);
            Vector3 reoriented = Vector3.Cross(normal, inputRight).normalized * C.MoveInputVector.magnitude;
            float targetSpeed = _isSprinting ? C.MaxStableMoveSpeed * C.SprintSpeedMultiplier : C.MaxStableMoveSpeed;

            v = Vector3.Lerp(v, reoriented * targetSpeed,
                1f - Mathf.Exp(-C.StableMovementSharpness * dt));
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
            SetSprinting(C.AllowSprinting
                && C.SprintInputHeld
                && C.MoveInputVector.sqrMagnitude > 0f
                && !C.IsCrouching);
        }

        // ── Mana ─────────────────────────────────────────────────────────────

        private void UpdateMana(float dt)
        {
            float prev = C.SharedMana;
            C.SharedMana = Mathf.Min(C.MaxMana, C.SharedMana + C.ManaRegenRate * dt);
            if (!Mathf.Approximately(C.SharedMana, prev))
                C.FireManaChanged(C.SharedMana / C.MaxMana);
        }

        // ── Crouching ─────────────────────────────────────────────────────────

        public override void HandleInput(ref PlayerCharacterInputs inputs, Vector3 cameraPlanarDir)
        {
            C.LookInputVector = C.OrientationMethod == OrientationMethod.TowardsCamera
                ? cameraPlanarDir
                : C.MoveInputVector.normalized;

            if (C.CrouchDown && !C.IsCrouching)
            {
                C.ShouldBeCrouching = true;
                C.IsCrouching = true;
                Motor.SetCapsuleDimensions(C.StandingCapsuleRadius, C.CrouchedCapsuleHeight,
                    C.CrouchedCapsuleHeight * 0.5f);
                C.MeshRoot.localScale = C.CrouchMeshScale;
            }
            else if (C.CrouchUp)
            {
                C.ShouldBeCrouching = false;
            }
        }

        private void TryUncrouch()
        {
            if (!C.IsCrouching || C.ShouldBeCrouching) return;

            Motor.SetCapsuleDimensions(C.StandingCapsuleRadius, C.StandingCapsuleHeight,
                C.StandingCapsuleHeight * 0.5f);

            bool blocked = Motor.CharacterOverlap(
                Motor.TransientPosition, Motor.TransientRotation,
                C.ProbedColliders, Motor.CollidableLayers,
                QueryTriggerInteraction.Ignore) > 0;

            if (blocked)
                Motor.SetCapsuleDimensions(C.StandingCapsuleRadius, C.CrouchedCapsuleHeight,
                    C.CrouchedCapsuleHeight * 0.5f);
            else
            {
                C.MeshRoot.localScale = Vector3.one;
                C.IsCrouching = false;
            }
        }
    }
}