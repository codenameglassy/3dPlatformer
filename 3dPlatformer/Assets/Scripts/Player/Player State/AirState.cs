using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Active when the player is airborne (jumped or walked off a ledge).
    /// Handles: air movement, double jump, coyote time, jump buffering.
    ///
    /// TRANSITIONS OUT:
    ///   → Grounded : OnLanded
    ///   → Gliding  : RMB held + entry delay met (AfterUpdate)
    ///   → Floating : F held + mana available (AfterUpdate)
    /// </summary>
    public class AirState : PlayerStateBase
    {
        public override void Enter(PlayerCharacterController controller)
        {
            base.Enter(controller);
        }

        public override void BeforeUpdate(float dt)
        {
            C.JumpEventFired = false;
        }

        public override void UpdateRotation(ref Quaternion r, float dt)
        {
            SmoothRotateTowards(ref r, C.LookInputVector, C.OrientationSharpness, dt);
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

            // Drive tilt back to neutral after leaving glide
            C.GlidingStateInstance.UpdateGlideTilt(dt, isGliding: false);

            // → Gliding: RMB held + minimum airtime
            if (C.AllowGliding
                && C.GlideInputHeld
                && C.TimeSinceLastAbleToJump >= C.GlideEntryMinAirTime)
            {
                C.TransitionToState(CharacterState.Gliding);
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

        public override void HandleInput(ref PlayerCharacterInputs inputs, Vector3 cameraPlanarDir)
        {
            C.LookInputVector = C.OrientationMethod == OrientationMethod.TowardsCamera
                ? cameraPlanarDir
                : C.MoveInputVector.normalized;
        }
    }
}