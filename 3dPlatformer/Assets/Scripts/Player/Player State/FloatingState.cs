using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Active while the player is floating upward with mana.
    /// Mana drain is handled by the controller's TickMana() — not here.
    ///
    /// TRANSITIONS OUT:
    ///   → Air      : F released or mana empty
    ///   → Grounded : OnLanded
    /// </summary>
    public class FloatingState : PlayerStateBase
    {
        public override void Enter(PlayerCharacterController controller)
        {
            base.Enter(controller);
            Motor.ForceUnground();
            C.FireFloatChanged(true);
        }

        public override void Exit() => C.FireFloatChanged(false);

        public override void UpdateRotation(ref Quaternion r, float dt)
        {
            SmoothRotateTowards(ref r, C.LookInputVector, C.Movement.OrientationSharpness, dt);
            ApplyBonusOrientation(ref r, dt);
        }

        public override void UpdateVelocity(ref Vector3 v, float dt)
        {
            ApplyAirMovement(ref v, dt);
            ApplyFloatLift(ref v, dt);
            HandleJump(ref v, dt, treatAsGrounded: false);
            ApplyAdditiveVelocity(ref v);
        }

        public override void AfterUpdate(float dt)
        {
            UpdateJumpTimers(dt, isGrounded: false);

            if (!C.FloatInputHeld || C.SharedMana <= 0f)
            {
                C.TransitionToState(CharacterState.Air);
                return;
            }
        }

        public override void OnLanded() => C.TransitionToState(CharacterState.Grounded);

        // ── Float Lift ────────────────────────────────────────────────────────

        private void ApplyFloatLift(ref Vector3 v, float dt)
        {
            float vSpeed = Vector3.Dot(v, Motor.CharacterUp);
            if (vSpeed < C.Floating.LiftSpeed)
            {
                float newV = Mathf.MoveTowards(vSpeed, C.Floating.LiftSpeed, C.Floating.UpAcceleration * dt);
                v += Motor.CharacterUp * (newV - vSpeed);
            }
        }
    }
}