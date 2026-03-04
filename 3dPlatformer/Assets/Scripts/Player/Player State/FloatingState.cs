using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Active while the player is floating upward with mana.
    /// Drains mana each frame. Exits immediately when mana runs out or F is released.
    ///
    /// TRANSITIONS OUT:
    ///   → Air      : F released or mana empty (AfterUpdate)
    ///   → Grounded : OnLanded
    /// </summary>
    public class FloatingState : PlayerStateBase
    {
        // ── Lifecycle ────────────────────────────────────────────────────────

        public override void Enter(PlayerCharacterController controller)
        {
            base.Enter(controller);
            Motor.ForceUnground();
            C.FireFloatChanged(true);
        }

        public override void Exit()
        {
            C.FireFloatChanged(false);
        }

        public override void BeforeUpdate(float dt)
        {
            C.JumpEventFired = false;
            DrainMana(dt);
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
            // Air movement for horizontal control + float lift for vertical
            ApplyAirMovement(ref v, dt);
            ApplyFloatLift(ref v, dt);
            HandleJump(ref v, dt, treatAsGrounded: false);
            ApplyAdditiveVelocity(ref v);
        }

        public override void AfterUpdate(float dt)
        {
            UpdateJumpTimers(dt, isGrounded: false);

            // Exit float if F released or mana depleted
            if (!C.FloatInputHeld || C.SharedMana <= 0f)
            {
                C.TransitionToState(CharacterState.Air);
                return;
            }
        }

        public override void OnLanded()
        {
            C.TransitionToState(CharacterState.Grounded);
        }

        // ── Float Movement ────────────────────────────────────────────────────

        private void ApplyFloatLift(ref Vector3 v, float dt)
        {
            float vSpeed = Vector3.Dot(v, Motor.CharacterUp);
            if (vSpeed < C.FloatLiftSpeed)
            {
                float newV = Mathf.MoveTowards(vSpeed, C.FloatLiftSpeed, C.FloatUpAcceleration * dt);
                v += Motor.CharacterUp * (newV - vSpeed);
            }
        }

        // ── Mana ─────────────────────────────────────────────────────────────

        private void DrainMana(float dt)
        {
            float prev = C.SharedMana;
            C.SharedMana = Mathf.Max(0f, C.SharedMana - C.ManaDepletionRate * dt);
            if (!Mathf.Approximately(C.SharedMana, prev))
                C.FireManaChanged(C.SharedMana / C.MaxMana);
        }
    }
}
