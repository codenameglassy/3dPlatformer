using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Active while the player is gliding (RMB held in air).
    /// Handles: glide movement, reduced gravity, mesh tilt.
    ///
    /// TRANSITIONS OUT:
    ///   → Air      : RMB released
    ///   → Grounded : OnLanded
    ///   → Floating : F held + mana available
    /// </summary>
    public class GlidingState : PlayerStateBase
    {
        public override void Enter(PlayerCharacterController controller)
        {
            base.Enter(controller);
            C.FireGlideChanged(true);
        }

        public override void Exit() => C.FireGlideChanged(false);

        public override void UpdateRotation(ref Quaternion r, float dt)
        {
            SmoothRotateTowards(ref r, C.LookInputVector, C.Movement.OrientationSharpness, dt);
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

            if (!C.GlideInputHeld)
            {
                C.TransitionToState(CharacterState.Air);
                return;
            }

            if (C.Floating.Enabled && C.FloatInputHeld && C.SharedMana > 0f)
            {
                C.TransitionToState(CharacterState.Floating);
                return;
            }
        }

        public override void OnLanded() => C.TransitionToState(CharacterState.Grounded);

        // ── Glide Movement ────────────────────────────────────────────────────

        private void ApplyGlideMovement(ref Vector3 v, float dt)
        {
            Vector3 horizontal = Vector3.ProjectOnPlane(v, Motor.CharacterUp);
            Vector3 vertical = Vector3.Project(v, Motor.CharacterUp);

            horizontal = C.MoveInputVector.sqrMagnitude > 0f
                ? Vector3.Lerp(horizontal, C.MoveInputVector * C.Gliding.HorizontalSpeed,
                    1f - Mathf.Exp(-C.Gliding.Acceleration * dt))
                : Vector3.Lerp(horizontal, Vector3.zero,
                    1f - Mathf.Exp(-C.Gliding.Deceleration * dt));

            v = horizontal + vertical;
            v += C.Misc.Gravity * C.Gliding.GravityScale * dt;

            float vSpeed = Vector3.Dot(v, Motor.CharacterUp);
            if (vSpeed < -C.Gliding.MaxFallSpeed)
                v -= Motor.CharacterUp * (vSpeed + C.Gliding.MaxFallSpeed);

            v *= 1f / (1f + C.Air.Drag * dt);
        }
    }
}