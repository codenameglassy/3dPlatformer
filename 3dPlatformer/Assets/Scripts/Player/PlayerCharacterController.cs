using System.Collections.Generic;
using UnityEngine;
using KinematicCharacterController;

namespace JourneyGator.Player
{
    // ─── Enums & Structs ────────────────────────────────────────────────────

    public enum CharacterState
    {
        Default,
        Carrying,       // Holding a litter item
        Throwing,       // Locked into throw animation
        Stunned,        // Temporarily unable to move
        Interacting,    // Using a bin / station
    }

    public enum OrientationMethod
    {
        TowardsCamera,
        TowardsMovement,
    }

    public enum BonusOrientationMethod
    {
        None,
        TowardsGravity,
        TowardsGroundSlopeAndGravity,
    }

    public struct PlayerCharacterInputs
    {
        public float MoveAxisForward;
        public float MoveAxisRight;
        public Quaternion CameraRotation;
        public bool JumpDown;
        public bool GlideHeld;  // Hold right-click to glide
        public bool CrouchDown;
        public bool CrouchUp;
    }

    public struct AICharacterInputs
    {
        public Vector3 MoveVector;
        public Vector3 LookVector;
    }

    // ─── Main Controller ────────────────────────────────────────────────────

    /// <summary>
    /// Drives character movement via the KinematicCharacterMotor.
    /// Handles grounded movement, air movement, jumping, and crouching.
    /// Game-specific states (Carrying, Throwing, etc.) extend the switch blocks below.
    /// </summary>
    public class PlayerCharacterController : MonoBehaviour, ICharacterController
    {
        [Header("References")]
        public KinematicCharacterMotor Motor;
        public Transform MeshRoot;
        public Transform CameraFollowPoint;
        public GameObject SpeedLinesVFX; // Assign the speed lines child on the camera

        [Header("Stable Movement")]
        public float MaxStableMoveSpeed = 10f;
        public float StableMovementSharpness = 15f;
        public float OrientationSharpness = 10f;
        public OrientationMethod OrientationMethod = OrientationMethod.TowardsCamera;

        [Header("Air Movement")]
        public float MaxAirMoveSpeed = 15f;
        public float AirAccelerationSpeed = 15f;
        public float Drag = 0.1f;

        [Header("Jumping")]
        public bool AllowJumpingWhenSliding = false;
        public float JumpUpSpeed = 10f;
        public float JumpScalableForwardSpeed = 10f;
        public float JumpPreGroundingGraceTime = 0f;
        public float JumpPostGroundingGraceTime = 0f;

        [Header("Double Jump")]
        public bool AllowDoubleJump = true;
        public float DoubleJumpUpSpeed = 8f;   // Slightly weaker than first jump by default


        [Header("Gliding")]
        public bool AllowGliding = true;
        public float GlideGravityScale = 0.15f;  // Fraction of normal gravity applied while gliding
        public float GlideMaxFallSpeed = 2f;     // Terminal velocity while gliding (m/s downward)
        public float GlideHorizontalSpeed = 12f;    // Forward speed boost while gliding
        public float GlideEntryMinAirTime = 0.1f;   // Seconds airborne before glide can activate


        [Header("Glide Tilt")]
        public float MaxBankAngle = 30f;   // Max left/right roll when turning (degrees)
        public float MaxPitchAngle = 20f;   // Max forward pitch at full speed (degrees)
        [Range(0f, 1f)] public float TiltSmoothing = 0.8f;  // How snappy the tilt lerps IN while gliding
        [Range(0f, 2f)] public float TiltRecoverySpeed = 0.2f;  // How fast tilt returns to neutral after glide ends
        [Header("Crouching")]
        public float CrouchedCapsuleHeight = 1f;
        public float StandingCapsuleHeight = 2f;
        public float StandingCapsuleRadius = 0.5f;
        public Vector3 CrouchMeshScale = new Vector3(1f, 0.5f, 1f);

        [Header("Misc")]
        public List<Collider> IgnoredColliders = new List<Collider>();
        public BonusOrientationMethod BonusOrientationMethod = BonusOrientationMethod.None;
        public float BonusOrientationSharpness = 10f;
        public Vector3 Gravity = new Vector3(0f, -30f, 0f);

        // ─── Public State ────────────────────────────────────────────────────

        public CharacterState CurrentCharacterState { get; private set; }

        // ─── Private Fields ──────────────────────────────────────────────────

        // Preallocated buffers — avoids per-frame GC allocations
        private readonly Collider[] _probedColliders = new Collider[8];

        private Vector3 _moveInputVector;
        private Vector3 _lookInputVector;
        private Vector3 _internalVelocityAdd = Vector3.zero;

        private bool _jumpRequested = false;
        private bool _jumpConsumed = false;
        private bool _jumpedThisFrame = false;
        private float _timeSinceJumpRequested = Mathf.Infinity;
        private float _timeSinceLastAbleToJump = 0f;
        private bool _doubleJumpConsumed = false; // Tracks whether the mid-air jump has been used

        private bool _isGliding = false;
        private bool _glideInputHeld = false;

        private float _currentBank = 0f; // Smoothed bank angle, interpolated independently
        private float _currentPitch = 0f; // Smoothed pitch angle, interpolated independently

        private bool _shouldBeCrouching = false;
        private bool _isCrouching = false;

        // ─── Unity Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            Motor.CharacterController = this;
            TransitionToState(CharacterState.Default);
        }

        // ─── State Machine ───────────────────────────────────────────────────

        /// <summary>Transition to a new CharacterState, firing exit/enter callbacks.</summary>
        public void TransitionToState(CharacterState newState)
        {
            CharacterState previousState = CurrentCharacterState;
            OnStateExit(previousState, newState);
            CurrentCharacterState = newState;
            OnStateEnter(newState, previousState);
        }

        private void OnStateEnter(CharacterState state, CharacterState fromState)
        {
            switch (state)
            {
                case CharacterState.Default:
                    break;
                case CharacterState.Carrying:
                    // e.g. reduce move speed, play carry animation
                    break;
                case CharacterState.Stunned:
                    // e.g. disable input, play stun VFX
                    break;
            }
        }

        private void OnStateExit(CharacterState state, CharacterState toState)
        {
            switch (state)
            {
                case CharacterState.Default:
                    break;
                case CharacterState.Stunned:
                    // e.g. restore movement after stun ends
                    break;
            }
        }

        // ─── Input ───────────────────────────────────────────────────────────

        /// <summary>Called each frame by PlayerInputHandler to supply player input.</summary>
        public void SetInputs(ref PlayerCharacterInputs inputs)
        {
            Vector3 moveInputVector = Vector3.ClampMagnitude(
                new Vector3(inputs.MoveAxisRight, 0f, inputs.MoveAxisForward), 1f);

            Vector3 cameraPlanarDirection = Vector3.ProjectOnPlane(
                inputs.CameraRotation * Vector3.forward, Motor.CharacterUp).normalized;

            if (cameraPlanarDirection.sqrMagnitude == 0f)
            {
                cameraPlanarDirection = Vector3.ProjectOnPlane(
                    inputs.CameraRotation * Vector3.up, Motor.CharacterUp).normalized;
            }

            Quaternion cameraPlanarRotation = Quaternion.LookRotation(cameraPlanarDirection, Motor.CharacterUp);

            switch (CurrentCharacterState)
            {
                case CharacterState.Default:
                case CharacterState.Carrying:
                    {
                        _moveInputVector = cameraPlanarRotation * moveInputVector;
                        _lookInputVector = OrientationMethod == OrientationMethod.TowardsCamera
                            ? cameraPlanarDirection
                            : _moveInputVector.normalized;

                        if (inputs.JumpDown)
                        {
                            _timeSinceJumpRequested = 0f;
                            _jumpRequested = true;
                        }

                        _glideInputHeld = inputs.GlideHeld;
                        HandleCrouchInput(inputs.CrouchDown, inputs.CrouchUp);
                        break;
                    }

                case CharacterState.Stunned:
                case CharacterState.Throwing:
                    // Ignore all movement input while stunned or in throw
                    break;
            }
        }

        /// <summary>Called each frame by AI scripts to supply movement input.</summary>
        public void SetInputs(ref AICharacterInputs inputs)
        {
            _moveInputVector = inputs.MoveVector;
            _lookInputVector = inputs.LookVector;
        }

        // ─── ICharacterController Implementation ─────────────────────────────

        public void BeforeCharacterUpdate(float deltaTime) { }

        public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
        {
            switch (CurrentCharacterState)
            {
                case CharacterState.Default:
                case CharacterState.Carrying:
                    {
                        if (_lookInputVector.sqrMagnitude > 0f && OrientationSharpness > 0f)
                        {
                            Vector3 smoothedLookDir = Vector3.Slerp(
                                Motor.CharacterForward,
                                _lookInputVector,
                                1f - Mathf.Exp(-OrientationSharpness * deltaTime)
                            ).normalized;

                            currentRotation = Quaternion.LookRotation(smoothedLookDir, Motor.CharacterUp);
                        }

                        ApplyBonusOrientation(ref currentRotation, deltaTime);
                        break;
                    }
            }
        }

        public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
        {
            switch (CurrentCharacterState)
            {
                case CharacterState.Default:
                case CharacterState.Carrying:
                    {
                        if (Motor.GroundingStatus.IsStableOnGround)
                        {
                            ApplyGroundMovement(ref currentVelocity, deltaTime);
                        }
                        else
                        {
                            UpdateGlideState();
                            if (_isGliding)
                                ApplyGlideMovement(ref currentVelocity, deltaTime);
                            else
                                ApplyAirMovement(ref currentVelocity, deltaTime);
                        }

                        HandleJump(ref currentVelocity, deltaTime);
                        ApplyAdditiveVelocity(ref currentVelocity);
                        break;
                    }

                case CharacterState.Stunned:
                    // Apply gravity only — no player control
                    currentVelocity += Gravity * deltaTime;
                    currentVelocity *= 1f / (1f + Drag * deltaTime);
                    break;
            }
        }

        public void AfterCharacterUpdate(float deltaTime)
        {
            switch (CurrentCharacterState)
            {
                case CharacterState.Default:
                case CharacterState.Carrying:
                    {
                        UpdateJumpState(deltaTime);
                        TryUncrouch();
                        UpdateGlideTilt(deltaTime);
                        break;
                    }
            }
        }

        public void PostGroundingUpdate(float deltaTime)
        {
            bool justLanded = Motor.GroundingStatus.IsStableOnGround && !Motor.LastGroundingStatus.IsStableOnGround;
            bool justLeftGround = !Motor.GroundingStatus.IsStableOnGround && Motor.LastGroundingStatus.IsStableOnGround;

            if (justLanded) OnLanded();
            if (justLeftGround) OnLeaveStableGround();
        }

        public bool IsColliderValidForCollisions(Collider coll)
        {
            return !IgnoredColliders.Contains(coll);
        }

        public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
            ref HitStabilityReport hitStabilityReport)
        { }

        public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
            ref HitStabilityReport hitStabilityReport)
        { }

        public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
            Vector3 atCharacterPosition, Quaternion atCharacterRotation,
            ref HitStabilityReport hitStabilityReport)
        { }

        public void OnDiscreteCollisionDetected(Collider hitCollider) { }

        // ─── Public API ──────────────────────────────────────────────────────

        /// <summary>Apply an external impulse (e.g. from an explosion or bounce pad).</summary>
        public void AddVelocity(Vector3 velocity)
        {
            _internalVelocityAdd += velocity;
        }

        // ─── Private Movement Helpers ────────────────────────────────────────

        private void ApplyGroundMovement(ref Vector3 currentVelocity, float deltaTime)
        {
            float currentSpeed = currentVelocity.magnitude;
            Vector3 groundNormal = Motor.GroundingStatus.GroundNormal;

            // Reorient current velocity along slope
            currentVelocity = Motor.GetDirectionTangentToSurface(currentVelocity, groundNormal) * currentSpeed;

            // Build target velocity along slope
            Vector3 inputRight = Vector3.Cross(_moveInputVector, Motor.CharacterUp);
            Vector3 reorientedInput = Vector3.Cross(groundNormal, inputRight).normalized * _moveInputVector.magnitude;
            Vector3 targetVelocity = reorientedInput * MaxStableMoveSpeed;

            currentVelocity = Vector3.Lerp(currentVelocity, targetVelocity,
                1f - Mathf.Exp(-StableMovementSharpness * deltaTime));
        }

        private void ApplyAirMovement(ref Vector3 currentVelocity, float deltaTime)
        {
            if (_moveInputVector.sqrMagnitude > 0f)
            {
                Vector3 addedVelocity = _moveInputVector * AirAccelerationSpeed * deltaTime;
                Vector3 velocityOnPlane = Vector3.ProjectOnPlane(currentVelocity, Motor.CharacterUp);

                if (velocityOnPlane.magnitude < MaxAirMoveSpeed)
                {
                    Vector3 newTotal = Vector3.ClampMagnitude(velocityOnPlane + addedVelocity, MaxAirMoveSpeed);
                    addedVelocity = newTotal - velocityOnPlane;
                }
                else if (Vector3.Dot(velocityOnPlane, addedVelocity) > 0f)
                {
                    // Don't accelerate further in the direction already exceeding max
                    addedVelocity = Vector3.ProjectOnPlane(addedVelocity, velocityOnPlane.normalized);
                }

                // Prevent air-climbing sloped walls
                if (Motor.GroundingStatus.FoundAnyGround)
                {
                    Vector3 obstructionNormal = Vector3.Cross(
                        Vector3.Cross(Motor.CharacterUp, Motor.GroundingStatus.GroundNormal),
                        Motor.CharacterUp
                    ).normalized;

                    if (Vector3.Dot(currentVelocity + addedVelocity, addedVelocity) > 0f)
                    {
                        addedVelocity = Vector3.ProjectOnPlane(addedVelocity, obstructionNormal);
                    }
                }

                currentVelocity += addedVelocity;
            }

            currentVelocity += Gravity * deltaTime;
            currentVelocity *= 1f / (1f + Drag * deltaTime);
        }


        // ─── Gliding ─────────────────────────────────────────────────────────

        /// <summary>
        /// Single point of truth for toggling glide state and its side-effects (VFX, etc).
        /// Always use this instead of setting _isGliding directly.
        /// </summary>
        private void SetGliding(bool gliding)
        {
            if (gliding == _isGliding) return;

            _isGliding = gliding;

            if (SpeedLinesVFX != null)
                SpeedLinesVFX.SetActive(_isGliding);
        }


        /// <summary>
        /// Determines whether the player should enter, stay in, or exit glide.
        /// Glide activates when: airborne + jump held + above minimum air time + not used double jump mid-glide.
        /// </summary>
        private void UpdateGlideState()
        {
            bool isAirborne = !Motor.GroundingStatus.IsStableOnGround;
            bool pastEntryDelay = _timeSinceLastAbleToJump >= GlideEntryMinAirTime;
            bool wantsToGlide = AllowGliding && _glideInputHeld && isAirborne && pastEntryDelay;

            SetGliding(wantsToGlide);
        }

        /// <summary>
        /// Applies reduced gravity and caps downward velocity for a smooth glide feel.
        /// Horizontal movement uses GlideHorizontalSpeed so the player has meaningful control.
        /// </summary>
        private void ApplyGlideMovement(ref Vector3 currentVelocity, float deltaTime)
        {
            // Horizontal — full directional control at glide speed
            if (_moveInputVector.sqrMagnitude > 0f)
            {
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(currentVelocity, Motor.CharacterUp);
                Vector3 targetHorizontal = _moveInputVector * GlideHorizontalSpeed;
                Vector3 smoothedHorizontal = Vector3.Lerp(horizontalVelocity, targetHorizontal,
                    1f - Mathf.Exp(-StableMovementSharpness * deltaTime));

                currentVelocity = smoothedHorizontal + Vector3.Project(currentVelocity, Motor.CharacterUp);
            }

            // Vertical — apply a fraction of gravity so descent is slow but not zero
            currentVelocity += Gravity * GlideGravityScale * deltaTime;

            // Clamp downward speed to GlideMaxFallSpeed
            float verticalSpeed = Vector3.Dot(currentVelocity, Motor.CharacterUp);
            if (verticalSpeed < -GlideMaxFallSpeed)
            {
                currentVelocity -= Motor.CharacterUp * (verticalSpeed + GlideMaxFallSpeed);
            }

            // Apply drag normally
            currentVelocity *= 1f / (1f + Drag * deltaTime);
        }

        private void HandleJump(ref Vector3 currentVelocity, float deltaTime)
        {
            _jumpedThisFrame = false;
            _timeSinceJumpRequested += deltaTime;

            if (!_jumpRequested) return;

            bool isGrounded = AllowJumpingWhenSliding
                ? Motor.GroundingStatus.FoundAnyGround
                : Motor.GroundingStatus.IsStableOnGround;

            bool withinGracePeriod = _timeSinceLastAbleToJump <= JumpPostGroundingGraceTime;

            // ── First jump (ground or grace period) ──────────────────────────
            bool canFirstJump = !_jumpConsumed && (isGrounded || withinGracePeriod);
            if (canFirstJump)
            {
                Vector3 jumpDirection = Motor.CharacterUp;
                if (Motor.GroundingStatus.FoundAnyGround && !Motor.GroundingStatus.IsStableOnGround)
                    jumpDirection = Motor.GroundingStatus.GroundNormal;

                Motor.ForceUnground();

                currentVelocity += (jumpDirection * JumpUpSpeed) - Vector3.Project(currentVelocity, Motor.CharacterUp);
                currentVelocity += _moveInputVector * JumpScalableForwardSpeed;

                _jumpRequested = false;
                _jumpConsumed = true;
                _jumpedThisFrame = true;
                return;
            }

            // ── Double jump (mid-air, one use per grounding) ─────────────────
            bool canDoubleJump = AllowDoubleJump && !_doubleJumpConsumed && !isGrounded;
            if (canDoubleJump)
            {
                Motor.ForceUnground();

                // Reset vertical velocity before applying double jump for consistent height
                currentVelocity -= Vector3.Project(currentVelocity, Motor.CharacterUp);
                currentVelocity += Motor.CharacterUp * DoubleJumpUpSpeed;
                currentVelocity += _moveInputVector * JumpScalableForwardSpeed;

                _jumpRequested = false;
                _doubleJumpConsumed = true;
                _jumpedThisFrame = true;
            }
        }

        private void ApplyAdditiveVelocity(ref Vector3 currentVelocity)
        {
            if (_internalVelocityAdd.sqrMagnitude > 0f)
            {
                currentVelocity += _internalVelocityAdd;
                _internalVelocityAdd = Vector3.zero;
            }
        }

        private void UpdateJumpState(float deltaTime)
        {
            // Expire pre-ground jump request
            if (_jumpRequested && _timeSinceJumpRequested > JumpPreGroundingGraceTime)
            {
                _jumpRequested = false;
            }

            bool isGrounded = AllowJumpingWhenSliding
                ? Motor.GroundingStatus.FoundAnyGround
                : Motor.GroundingStatus.IsStableOnGround;

            if (isGrounded)
            {
                if (!_jumpedThisFrame)
                {
                    _jumpConsumed = false;
                    _doubleJumpConsumed = false; // Restore double jump on landing
                    SetGliding(false);  // Stop gliding on land
                }
                _timeSinceLastAbleToJump = 0f;
            }
            else
            {
                _timeSinceLastAbleToJump += deltaTime;
            }
        }

        private void HandleCrouchInput(bool crouchDown, bool crouchUp)
        {
            if (crouchDown && !_isCrouching)
            {
                _shouldBeCrouching = true;
                _isCrouching = true;
                Motor.SetCapsuleDimensions(StandingCapsuleRadius, CrouchedCapsuleHeight, CrouchedCapsuleHeight * 0.5f);
                MeshRoot.localScale = CrouchMeshScale;
            }
            else if (crouchUp)
            {
                _shouldBeCrouching = false;
            }
        }

        private void TryUncrouch()
        {
            if (!_isCrouching || _shouldBeCrouching) return;

            // Test if standing height is clear before uncrouching
            Motor.SetCapsuleDimensions(StandingCapsuleRadius, StandingCapsuleHeight, StandingCapsuleHeight * 0.5f);

            bool obstructed = Motor.CharacterOverlap(
                Motor.TransientPosition,
                Motor.TransientRotation,
                _probedColliders,
                Motor.CollidableLayers,
                QueryTriggerInteraction.Ignore) > 0;

            if (obstructed)
            {
                // Revert to crouching dimensions
                Motor.SetCapsuleDimensions(StandingCapsuleRadius, CrouchedCapsuleHeight, CrouchedCapsuleHeight * 0.5f);
            }
            else
            {
                MeshRoot.localScale = Vector3.one;
                _isCrouching = false;
            }
        }

        private void ApplyBonusOrientation(ref Quaternion currentRotation, float deltaTime)
        {
            Vector3 currentUp = currentRotation * Vector3.up;

            switch (BonusOrientationMethod)
            {
                case BonusOrientationMethod.TowardsGravity:
                    {
                        Vector3 smoothedDir = Vector3.Slerp(currentUp, -Gravity.normalized,
                            1f - Mathf.Exp(-BonusOrientationSharpness * deltaTime));
                        currentRotation = Quaternion.FromToRotation(currentUp, smoothedDir) * currentRotation;
                        break;
                    }

                case BonusOrientationMethod.TowardsGroundSlopeAndGravity:
                    {
                        if (Motor.GroundingStatus.IsStableOnGround)
                        {
                            Vector3 bottomHemiCenter = Motor.TransientPosition + currentUp * Motor.Capsule.radius;
                            Vector3 smoothedNormal = Vector3.Slerp(Motor.CharacterUp, Motor.GroundingStatus.GroundNormal,
                                1f - Mathf.Exp(-BonusOrientationSharpness * deltaTime));
                            currentRotation = Quaternion.FromToRotation(currentUp, smoothedNormal) * currentRotation;
                            Motor.SetTransientPosition(bottomHemiCenter + currentRotation * Vector3.down * Motor.Capsule.radius);
                        }
                        else
                        {
                            Vector3 smoothedDir = Vector3.Slerp(currentUp, -Gravity.normalized,
                                1f - Mathf.Exp(-BonusOrientationSharpness * deltaTime));
                            currentRotation = Quaternion.FromToRotation(currentUp, smoothedDir) * currentRotation;
                        }
                        break;
                    }

                default: // BonusOrientationMethod.None
                    {
                        Vector3 smoothedDir = Vector3.Slerp(currentUp, Vector3.up,
                            1f - Mathf.Exp(-BonusOrientationSharpness * deltaTime));
                        currentRotation = Quaternion.FromToRotation(currentUp, smoothedDir) * currentRotation;
                        break;
                    }
            }
        }

        // ─── Glide Tilt ──────────────────────────────────────────────────────

        /// <summary>
        /// Rotates MeshRoot visually during glide:
        ///   Bank  — rolls left/right based on lateral input (how hard the player is turning).
        ///   Pitch — tilts forward based on horizontal speed relative to GlideHorizontalSpeed.
        /// Both tilt out when glide ends, snapping back to neutral over TiltSmoothing.
        /// This is purely cosmetic — MeshRoot local rotation only, no physics impact.
        /// </summary>
        private void UpdateGlideTilt(float deltaTime)
        {
            float targetBank = 0f;
            float targetPitch = 0f;

            if (_isGliding)
            {
                // ── Bank (roll) ───────────────────────────────────────────────
                // Lateral input in local space: -1 = turning left, +1 = turning right.
                Vector3 localMove = Motor.Transform.InverseTransformDirection(_moveInputVector);
                float lateralInput = localMove.x;
                targetBank = -lateralInput * MaxBankAngle;

                // ── Pitch ─────────────────────────────────────────────────────
                // Map horizontal speed [0 → GlideHorizontalSpeed] to [0 → MaxPitchAngle].
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(Motor.Velocity, Motor.CharacterUp);
                float speedRatio = Mathf.Clamp01(horizontalVelocity.magnitude / GlideHorizontalSpeed);
                targetPitch = speedRatio * MaxPitchAngle;
            }

            // Smooth the bank and pitch targets independently before composing the rotation.
            // This prevents the target from jumping instantly when input changes direction,
            // which is what was causing the tilt to feel abrupt despite the Slerp below.
            // Use TiltSmoothing while gliding, TiltRecoverySpeed when returning to neutral
            // Scale normalized slider values [0-1] and [0-2] to useful exp-smoothing ranges
            float activeSpeed = _isGliding ? TiltSmoothing * 15f : TiltRecoverySpeed * 5f;
            float smoothFactor = 1f - Mathf.Exp(-activeSpeed * deltaTime);
            _currentBank = Mathf.Lerp(_currentBank, targetBank, smoothFactor);
            _currentPitch = Mathf.Lerp(_currentPitch, targetPitch, smoothFactor);

            // Compose the final rotation and apply to MeshRoot — purely visual, no physics
            MeshRoot.localRotation = Quaternion.Euler(_currentPitch, 0f, _currentBank);
        }

        // ─── Ground Event Callbacks ──────────────────────────────────────────

        protected virtual void OnLanded() { }
        protected virtual void OnLeaveStableGround() { }
    }
}