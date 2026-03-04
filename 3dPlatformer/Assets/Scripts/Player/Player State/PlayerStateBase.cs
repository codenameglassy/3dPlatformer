using UnityEngine;
using KinematicCharacterController;

namespace JourneyGator.Player
{
    /// <summary>
    /// Abstract base for all player states.
    ///
    /// LIFECYCLE ORDER PER FRAME (mirrors KCC):
    ///   1. BeforeUpdate(dt)          — runs once per frame before sub-steps
    ///   2. HandleInput(inputs, dir)  — cache any state-specific input
    ///   3. UpdateRotation(ref r, dt) — orientation (may run per sub-step)
    ///   4. UpdateVelocity(ref v, dt) — velocity    (may run per sub-step)
    ///   5. AfterUpdate(dt)           — state transition decisions
    ///   6. OnLanded() / OnLeftGround()
    ///
    /// RULES:
    ///   - Read settings from C (controller). Never cache them locally.
    ///   - Call TransitionToState() only from AfterUpdate, OnLanded, or OnLeftGround.
    ///   - Fire events via C.FireXxx() — never touch C's events directly.
    ///   - Keep UpdateVelocity side-effect free — KCC may call it multiple times.
    ///   - JumpEventFired and mana are managed by the controller — don't touch them.
    /// </summary>
    public abstract class PlayerStateBase
    {
        protected PlayerCharacterController C { get; private set; }
        protected KinematicCharacterMotor Motor => C.Motor;

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

        /// <summary>
        /// Single source of truth for grounded check.
        /// Respects AllowJumpingWhenSliding setting.
        /// </summary>
        protected bool IsGrounded() => C.Jump.AllowWhenSliding
            ? Motor.GroundingStatus.FoundAnyGround
            : Motor.GroundingStatus.IsStableOnGround;

        /// <summary>Standard smooth orientation toward a target direction.</summary>
        protected void SmoothRotateTowards(ref Quaternion r, Vector3 targetDir, float sharpness, float dt)
        {
            if (targetDir.sqrMagnitude == 0f || sharpness <= 0f) return;
            Vector3 smoothed = Vector3.Slerp(Motor.CharacterForward, targetDir,
                1f - Mathf.Exp(-sharpness * dt)).normalized;
            r = Quaternion.LookRotation(smoothed, Motor.CharacterUp);
        }

        /// <summary>Applies bonus orientation (gravity/slope alignment).</summary>
        protected void ApplyBonusOrientation(ref Quaternion r, float dt)
        {
            Vector3 up = r * Vector3.up;

            switch (C.Misc.BonusOrientation)
            {
                case BonusOrientationMethod.TowardsGravity:
                    {
                        Vector3 d = Vector3.Slerp(up, -C.Misc.Gravity.normalized,
                            1f - Mathf.Exp(-C.Misc.BonusOrientationSharpness * dt));
                        r = Quaternion.FromToRotation(up, d) * r;
                        break;
                    }
                case BonusOrientationMethod.TowardsGroundSlopeAndGravity:
                    {
                        if (Motor.GroundingStatus.IsStableOnGround)
                        {
                            Vector3 center = Motor.TransientPosition + up * Motor.Capsule.radius;
                            Vector3 normal = Vector3.Slerp(Motor.CharacterUp, Motor.GroundingStatus.GroundNormal,
                                1f - Mathf.Exp(-C.Misc.BonusOrientationSharpness * dt));
                            r = Quaternion.FromToRotation(up, normal) * r;
                            Motor.SetTransientPosition(center + r * Vector3.down * Motor.Capsule.radius);
                        }
                        else
                        {
                            Vector3 d = Vector3.Slerp(up, -C.Misc.Gravity.normalized,
                                1f - Mathf.Exp(-C.Misc.BonusOrientationSharpness * dt));
                            r = Quaternion.FromToRotation(up, d) * r;
                        }
                        break;
                    }
                default:
                    {
                        Vector3 d = Vector3.Slerp(up, Vector3.up,
                            1f - Mathf.Exp(-C.Misc.BonusOrientationSharpness * dt));
                        r = Quaternion.FromToRotation(up, d) * r;
                        break;
                    }
            }
        }

        /// <summary>Flushes any pending AddVelocity impulses.</summary>
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
                Vector3 added = C.MoveInputVector * C.Air.Acceleration * dt;
                Vector3 flat = Vector3.ProjectOnPlane(v, Motor.CharacterUp);

                if (flat.magnitude < C.Air.MaxSpeed)
                    added = Vector3.ClampMagnitude(flat + added, C.Air.MaxSpeed) - flat;
                else if (Vector3.Dot(flat, added) > 0f)
                    added = Vector3.ProjectOnPlane(added, flat.normalized);

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

            v += C.Misc.Gravity * dt;
            v *= 1f / (1f + C.Air.Drag * dt);
        }

        /// <summary>
        /// Handles first jump, coyote time, and double jump.
        /// All jump state lives on C so it persists across state transitions.
        /// </summary>
        protected void HandleJump(ref Vector3 v, float dt, bool treatAsGrounded)
        {
            C.JumpedThisFrame = false;
            C.TimeSinceJumpRequested += dt;

            if (!C.JumpRequested) return;

            bool withinGrace = C.TimeSinceLastAbleToJump <= C.Jump.PostGroundingGraceTime;
            bool canFirst = !C.JumpConsumed && (treatAsGrounded || withinGrace);

            if (canFirst)
            {
                Vector3 jumpDir = Motor.CharacterUp;
                if (Motor.GroundingStatus.FoundAnyGround && !Motor.GroundingStatus.IsStableOnGround)
                    jumpDir = Motor.GroundingStatus.GroundNormal;

                Motor.ForceUnground();
                v += (jumpDir * C.Jump.UpSpeed) - Vector3.Project(v, Motor.CharacterUp);
                v += C.MoveInputVector * C.Jump.ScalableForwardSpeed;

                C.JumpRequested = false;
                C.JumpConsumed = true;
                C.JumpedThisFrame = true;

                if (!C.JumpEventFired) { C.JumpEventFired = true; C.FireJumped(); }
                return;
            }

            bool canDouble = C.DoubleJump.Enabled && !C.DoubleJumpConsumed && !treatAsGrounded;
            if (canDouble)
            {
                Motor.ForceUnground();
                v -= Vector3.Project(v, Motor.CharacterUp);
                v += Motor.CharacterUp * C.DoubleJump.UpSpeed;
                v += C.MoveInputVector * C.Jump.ScalableForwardSpeed;

                C.JumpRequested = false;
                C.DoubleJumpConsumed = true;
                C.JumpedThisFrame = true;

                if (!C.JumpEventFired) { C.JumpEventFired = true; C.FireJumped(); }
            }
        }

        /// <summary>Ticks coyote timer and resets jump flags on landing.</summary>
        protected void UpdateJumpTimers(float dt, bool isGrounded)
        {
            if (C.JumpRequested && C.TimeSinceJumpRequested > C.Jump.PreGroundingGraceTime)
                C.JumpRequested = false;

            if (isGrounded)
            {
                if (!C.JumpedThisFrame)
                {
                    C.JumpConsumed = false;
                    C.DoubleJumpConsumed = false;
                }
                C.TimeSinceLastAbleToJump = 0f;
            }
            else
            {
                C.TimeSinceLastAbleToJump += dt;
            }
        }

        /// <summary>
        /// Drives glide mesh tilt. Tilt fields live on C so any state
        /// can call recovery without coupling to GlidingState.
        /// Pass isGliding=false from Air/Grounded states for smooth recovery.
        /// </summary>
        protected void UpdateGlideTilt(float dt, bool isGliding)
        {
            bool atNeutral = !isGliding
                && Mathf.Abs(C.TiltBank) < 0.01f
                && Mathf.Abs(C.TiltPitch) < 0.01f;

            if (atNeutral)
            {
                C.TiltBank = C.TiltPitch = 0f;
                C.MeshRoot.localRotation = Quaternion.identity;
                return;
            }

            float targetBank = 0f, targetPitch = 0f;

            if (isGliding)
            {
                Vector3 localMove = Motor.Transform.InverseTransformDirection(C.MoveInputVector);
                targetBank = -localMove.x * C.GlideTilt.MaxBankAngle;

                float speedRatio = Mathf.Clamp01(
                    Vector3.ProjectOnPlane(Motor.Velocity, Motor.CharacterUp).magnitude
                    / C.Gliding.HorizontalSpeed);
                targetPitch = speedRatio * C.GlideTilt.MaxPitchAngle;
            }

            float speed = isGliding
                ? C.GlideTilt.Smoothing * PlayerCharacterController.TiltSmoothingScale
                : C.GlideTilt.RecoverySpeed * PlayerCharacterController.TiltRecoveryScale;
            float factor = 1f - Mathf.Exp(-speed * dt);

            C.TiltBank = Mathf.Lerp(C.TiltBank, targetBank, factor);
            C.TiltPitch = Mathf.Lerp(C.TiltPitch, targetPitch, factor);
            C.MeshRoot.localRotation = Quaternion.Euler(C.TiltPitch, 0f, C.TiltBank);
        }
    }
}