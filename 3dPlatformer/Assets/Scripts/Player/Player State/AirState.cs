using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Active when the player is airborne.
    /// Handles: air movement, double jump, coyote time, jump buffering.
    ///
    /// TRANSITIONS OUT:
    ///   → Grounded : OnLanded
    ///   → Gliding  : RMB held + entry delay met
    ///   → Floating : F held + mana available
    /// </summary>
    public class AirState : PlayerStateBase
    {
        public override void UpdateRotation(ref Quaternion r, float dt)
        {
            SmoothRotateTowards(ref r, C.LookInputVector, C.Movement.OrientationSharpness, dt);
            ApplyBonusOrientation(ref r, dt);
        }

        public override void UpdateVelocity(ref Vector3 v, float dt)
        {
            ApplyAirMovement(ref v, dt);
            HandleJump(ref v, dt, treatAsGrounded: false);
            ApplyAdditiveVelocity(ref v);
        }

        public override void AfterUpdate(float dt)
        {
            UpdateJumpTimers(dt, isGrounded: false);
            UpdateGlideTilt(dt, isGliding: false);

            if (C.Gliding.Enabled
                && C.GlideInputHeld
                && C.TimeSinceLastAbleToJump >= C.Gliding.EntryMinAirTime)
            {
                C.TransitionToState(CharacterState.Gliding);
                return;
            }

            if (C.Floating.Enabled && C.FloatInputHeld && C.SharedMana > 0f)
            {
                C.TransitionToState(CharacterState.Floating);
                return;
            }
        }

        public override void OnLanded() => C.TransitionToState(CharacterState.Grounded);
    }
}