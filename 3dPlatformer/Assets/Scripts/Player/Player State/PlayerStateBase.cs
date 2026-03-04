using UnityEngine;
using KinematicCharacterController;

namespace JourneyGator.Player
{
    /// <summary>
    /// Abstract base for all player states.
    ///
    /// LIFECYCLE ORDER PER FRAME (mirrors KCC):
    ///   1. BeforeUpdate(dt)          — per-frame resets (runs once, not per sub-step)
    ///   2. HandleInput(inputs, dir)  — cache input
    ///   3. UpdateRotation(ref r, dt) — orientation
    ///   4. UpdateVelocity(ref v, dt) — velocity  ← may run multiple times (KCC sub-steps)
    ///   5. AfterUpdate(dt)           — state transition decisions
    ///   6. OnLanded() / OnLeftGround()
    ///
    /// RULES:
    ///   - Read settings from C. Never cache them locally.
    ///   - Call TransitionToState() only from AfterUpdate, OnLanded, or OnLeftGround.
    ///   - Fire events via C.FireXxx() — never touch C events directly.
    ///   - Keep UpdateVelocity side-effect free (KCC calls it multiple times).
    /// </summary>
    public abstract class PlayerStateBase
    {
        protected PlayerCharacterController C     { get; private set; }
        protected KinematicCharacterMotor   Motor => C.Motor;

        public virtual bool IsSprinting => false;

        // ── Lifecycle ────────────────────────────────────────────────────────

        public virtual void Enter(PlayerCharacterController controller) => C = controller;
        public virtual void Exit() { }
        public virtual void HandleInput(ref PlayerCharacterInputs inputs, Vector3 cameraPlanarDir) { }
        public virtual void BeforeUpdate(float dt) { }
        public virtual void UpdateRotation(ref Quaternion r, float dt) { }
        public virtual void UpdateVelocity(ref Vector3 v, float dt) { }
        public virtual void AfterUpdate(float dt) { }
        public virtual void OnLanded() { }
        public virtual void OnLeftGround() { }

        // ── Shared Helpers ────────────────────────────────────────────────────

        protected void SmoothRotateTowards(ref Quaternion r, Vector3 targetDir, float sharpness, float dt)
        {
            if (targetDir.sqrMagnitude == 0f || sharpness <= 0f) return;
            Vector3 s = Vector3.Slerp(Motor.CharacterForward, targetDir,
                1f - Mathf.Exp(-sharpness * dt)).normalized;
            r = Quaternion.LookRotation(s, Motor.CharacterUp);
        }

        protected void ApplyBonusOrientation(ref Quaternion r, float dt)
        {
            Vector3 up = r * Vector3.up;
            switch (C.BonusOrientationMethod)
            {
                case BonusOrientationMethod.TowardsGravity:
                {
                    Vector3 d = Vector3.Slerp(up, -C.Gravity.normalized, 1f - Mathf.Exp(-C.BonusOrientationSharpness * dt));
                    r = Quaternion.FromToRotation(up, d) * r;
                    break;
                }
                case BonusOrientationMethod.TowardsGroundSlopeAndGravity:
                {
                    if (Motor.GroundingStatus.IsStableOnGround)
                    {
                        Vector3 center = Motor.TransientPosition + up * Motor.Capsule.radius;
                        Vector3 normal = Vector3.Slerp(Motor.CharacterUp, Motor.GroundingStatus.GroundNormal,
                            1f - Mathf.Exp(-C.BonusOrientationSharpness * dt));
                        r = Quaternion.FromToRotation(up, normal) * r;
                        Motor.SetTransientPosition(center + r * Vector3.down * Motor.Capsule.radius);
                    }
                    else
                    {
                        Vector3 d = Vector3.Slerp(up, -C.Gravity.normalized, 1f - Mathf.Exp(-C.BonusOrientationSharpness * dt));
                        r = Quaternion.FromToRotation(up, d) * r;
                    }
                    break;
                }
                default:
                {
                    Vector3 d = Vector3.Slerp(up, Vector3.up, 1f - Mathf.Exp(-C.BonusOrientationSharpness * dt));
                    r = Quaternion.FromToRotation(up, d) * r;
                    break;
                }
            }
        }

        protected void ApplyAdditiveVelocity(ref Vector3 v)
        {
            if (C.InternalVelocityAdd.sqrMagnitude > 0f)
            {
                v += C.InternalVelocityAdd;
                C.InternalVelocityAdd = Vector3.zero;
            }
        }

        /// <summary>Standard air movement: horizontal accel + gravity + drag.</summary>
        protected void ApplyAirMovement(ref Vector3 v, float dt)
        {
            if (C.MoveInputVector.sqrMagnitude > 0f)
            {
                Vector3 added = C.MoveInputVector * C.AirAccelerationSpeed * dt;
                Vector3 flat  = Vector3.ProjectOnPlane(v, Motor.CharacterUp);

                if (flat.magnitude < C.MaxAirMoveSpeed)
                {
                    added = Vector3.ClampMagnitude(flat + added, C.MaxAirMoveSpeed) - flat;
                }
                else if (Vector3.Dot(flat, added) > 0f)
                {
                    added = Vector3.ProjectOnPlane(added, flat.normalized);
                }

                if (Motor.GroundingStatus.FoundAnyGround)
                {
                    Vector3 obs = Vector3.Cross(
                        Vector3.Cross(Motor.CharacterUp, Motor.GroundingStatus.GroundNormal),
                        Motor.CharacterUp).normalized;
                    if (Vector3.Dot(v + added, added) > 0f)
                        added = Vector3.ProjectOnPlane(added, obs);
                }

                v += added;
            }

            v += C.Gravity * dt;
            v *= 1f / (1f + C.Drag * dt);
        }

        /// <summary>
        /// Handles first jump, coyote time, and double jump.
        /// Shared via C's jump fields so all states stay in sync.
        /// </summary>
        protected void HandleJump(ref Vector3 v, float dt, bool treatAsGrounded)
        {
            C.JumpedThisFrame = false;
            C.TimeSinceJumpRequested += dt;

            if (!C.JumpRequested) return;

            bool withinGrace = C.TimeSinceLastAbleToJump <= C.JumpPostGroundingGraceTime;
            bool canFirst    = !C.JumpConsumed && (treatAsGrounded || withinGrace);

            if (canFirst)
            {
                Vector3 jumpDir = Motor.CharacterUp;
                if (Motor.GroundingStatus.FoundAnyGround && !Motor.GroundingStatus.IsStableOnGround)
                    jumpDir = Motor.GroundingStatus.GroundNormal;

                Motor.ForceUnground();
                v += (jumpDir * C.JumpUpSpeed) - Vector3.Project(v, Motor.CharacterUp);
                v += C.MoveInputVector * C.JumpScalableForwardSpeed;

                C.JumpRequested   = false;
                C.JumpConsumed    = true;
                C.JumpedThisFrame = true;

                if (!C.JumpEventFired) { C.JumpEventFired = true; C.FireJumped(); }
                return;
            }

            bool canDouble = C.AllowDoubleJump && !C.DoubleJumpConsumed && !treatAsGrounded;
            if (canDouble)
            {
                Motor.ForceUnground();
                v -= Vector3.Project(v, Motor.CharacterUp);
                v += Motor.CharacterUp * C.DoubleJumpUpSpeed;
                v += C.MoveInputVector * C.JumpScalableForwardSpeed;

                C.JumpRequested      = false;
                C.DoubleJumpConsumed = true;
                C.JumpedThisFrame    = true;

                if (!C.JumpEventFired) { C.JumpEventFired = true; C.FireJumped(); }
            }
        }

        /// <summary>Ticks coyote timer and resets jump flags on landing.</summary>
        protected void UpdateJumpTimers(float dt, bool isGrounded)
        {
            if (C.JumpRequested && C.TimeSinceJumpRequested > C.JumpPreGroundingGraceTime)
                C.JumpRequested = false;

            if (isGrounded)
            {
                if (!C.JumpedThisFrame)
                {
                    C.JumpConsumed       = false;
                    C.DoubleJumpConsumed = false;
                }
                C.TimeSinceLastAbleToJump = 0f;
            }
            else
            {
                C.TimeSinceLastAbleToJump += dt;
            }
        }
    }
}
