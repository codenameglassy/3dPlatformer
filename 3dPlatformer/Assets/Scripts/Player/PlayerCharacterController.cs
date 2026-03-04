using System;
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
    /// Fires events when movement states change — subscribe in PlayerVisuals or any observer.
    /// Never references VFX, audio, or animation directly.
    /// </summary>
    public class PlayerCharacterController : MonoBehaviour, ICharacterController
    {
        [Header("References")]
        public KinematicCharacterMotor Motor;
        public Transform MeshRoot;
        public Transform CameraFollowPoint;

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
        public float DoubleJumpUpSpeed = 8f;

        [Header("Gliding")]
        public bool AllowGliding = true;
        public float GlideGravityScale = 0.15f;
        public float GlideMaxFallSpeed = 2f;
        public float GlideHorizontalSpeed = 12f;
        public float GlideEntryMinAirTime = 0.1f;

        [Header("Glide Tilt")]
        public float MaxBankAngle = 30f;
        public float MaxPitchAngle = 20f;
        [Range(0f, 1f)] public float TiltSmoothing = 0.8f;
        [Range(0f, 2f)] public float TiltRecoverySpeed = 0.2f;

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

        // ─── Events (Observer Pattern) ───────────────────────────────────────

        /// <summary>Fired when glide starts (true) or stops (false).</summary>
        public event Action<bool> OnGlideChanged;

        /// <summary>Fired when the character lands on stable ground.</summary>
        public event Action OnLandedEvent;

        /// <summary>Fired when the character leaves stable ground.</summary>
        public event Action OnLeftGroundEvent;

        /// <summary>Fired when CharacterState transitions. Args: (newState, previousState).</summary>
        public event Action<CharacterState, CharacterState> OnStateChanged;

        // ─── Constants ───────────────────────────────────────────────────────

        private const float TiltSmoothingScale = 15f;
        private const float TiltRecoveryScale = 5f;

        // ─── Public State ────────────────────────────────────────────────────

        public CharacterState CurrentCharacterState { get; private set; }
        public bool IsGliding => _isGliding;

        // ─── Private Fields ──────────────────────────────────────────────────

        private readonly Collider[] _probedColliders = new Collider[8];
        private HashSet<Collider> _ignoredCollidersSet;

        private Vector3 _moveInputVector;
        private Vector3 _lookInputVector;
        private Vector3 _internalVelocityAdd = Vector3.zero;

        private bool _jumpRequested = false;
        private bool _jumpConsumed = false;
        private bool _jumpedThisFrame = false;
        private float _timeSinceJumpRequested = Mathf.Infinity;
        private float _timeSinceLastAbleToJump = 0f;
        private bool _doubleJumpConsumed = false;

        private bool _isGliding = false;
        private bool _glideInputHeld = false;

        private float _currentBank = 0f;
        private float _currentPitch = 0f;

        private bool _shouldBeCrouching = false;
        private bool _isCrouching = false;

        // ─── Unity Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            Motor.CharacterController = this;
            _ignoredCollidersSet = new HashSet<Collider>(IgnoredColliders);
            TransitionToState(CharacterState.Default);
        }

        // ─── State Machine ───────────────────────────────────────────────────

        /// <summary>Transition to a new CharacterState, firing exit/enter callbacks and OnStateChanged event.</summary>
        public void TransitionToState(CharacterState newState)
        {
            CharacterState previousState = CurrentCharacterState;
            OnStateExit(previousState, newState);
            CurrentCharacterState = newState;
            OnStateEnter(newState, previousState);
            OnStateChanged?.Invoke(newState, previousState);
        }

        private void OnStateEnter(CharacterState state, CharacterState fromState)
        {
            switch (state)
            {
                case CharacterState.Default:
                    break;
                case CharacterState.Carrying:
                    break;
                case CharacterState.Stunned:
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
                    break;
            }
        }

        // ─── Input ───────────────────────────────────────────────────────────

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
                    break;
            }
        }

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
                        bool isGrounded = IsGrounded();

                        if (isGrounded)
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

                        HandleJump(ref currentVelocity, deltaTime, isGrounded);
                        ApplyAdditiveVelocity(ref currentVelocity);
                        break;
                    }

                case CharacterState.Stunned:
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
                        bool isGrounded = IsGrounded();
                        UpdateJumpState(deltaTime, isGrounded);
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
            return !_ignoredCollidersSet.Contains(coll);
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

        // ─── Private Helpers ─────────────────────────────────────────────────

        private bool IsGrounded()
        {
            return AllowJumpingWhenSliding
                ? Motor.GroundingStatus.FoundAnyGround
                : Motor.GroundingStatus.IsStableOnGround;
        }

        // ─── Private Movement ────────────────────────────────────────────────

        private void ApplyGroundMovement(ref Vector3 currentVelocity, float deltaTime)
        {
            float currentSpeed = currentVelocity.magnitude;
            Vector3 groundNormal = Motor.GroundingStatus.GroundNormal;

            currentVelocity = Motor.GetDirectionTangentToSurface(currentVelocity, groundNormal) * currentSpeed;

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
                    addedVelocity = Vector3.ProjectOnPlane(addedVelocity, velocityOnPlane.normalized);
                }

                if (Motor.GroundingStatus.FoundAnyGround)
                {
                    Vector3 obstructionNormal = Vector3.Cross(
                        Vector3.Cross(Motor.CharacterUp, Motor.GroundingStatus.GroundNormal),
                        Motor.CharacterUp
                    ).normalized;

                    if (Vector3.Dot(currentVelocity + addedVelocity, addedVelocity) > 0f)
                        addedVelocity = Vector3.ProjectOnPlane(addedVelocity, obstructionNormal);
                }

                currentVelocity += addedVelocity;
            }

            currentVelocity += Gravity * deltaTime;
            currentVelocity *= 1f / (1f + Drag * deltaTime);
        }

        // ─── Gliding ─────────────────────────────────────────────────────────

        /// <summary>
        /// Single point of truth for toggling glide state.
        /// Fires OnGlideChanged event so observers (PlayerVisuals, etc.) react automatically.
        /// </summary>
        private void SetGliding(bool gliding)
        {
            if (gliding == _isGliding) return;
            _isGliding = gliding;
            OnGlideChanged?.Invoke(_isGliding);
        }

        private void UpdateGlideState()
        {
            bool isAirborne = !Motor.GroundingStatus.IsStableOnGround;
            bool pastEntryDelay = _timeSinceLastAbleToJump >= GlideEntryMinAirTime;
            bool wantsToGlide = AllowGliding && _glideInputHeld && isAirborne && pastEntryDelay;

            SetGliding(wantsToGlide);
        }

        private void ApplyGlideMovement(ref Vector3 currentVelocity, float deltaTime)
        {
            if (_moveInputVector.sqrMagnitude > 0f)
            {
                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(currentVelocity, Motor.CharacterUp);
                Vector3 targetHorizontal = _moveInputVector * GlideHorizontalSpeed;
                Vector3 smoothedHorizontal = Vector3.Lerp(horizontalVelocity, targetHorizontal,
                    1f - Mathf.Exp(-StableMovementSharpness * deltaTime));

                currentVelocity = smoothedHorizontal + Vector3.Project(currentVelocity, Motor.CharacterUp);
            }

            currentVelocity += Gravity * GlideGravityScale * deltaTime;

            float verticalSpeed = Vector3.Dot(currentVelocity, Motor.CharacterUp);
            if (verticalSpeed < -GlideMaxFallSpeed)
                currentVelocity -= Motor.CharacterUp * (verticalSpeed + GlideMaxFallSpeed);

            currentVelocity *= 1f / (1f + Drag * deltaTime);
        }

        private void HandleJump(ref Vector3 currentVelocity, float deltaTime, bool isGrounded)
        {
            _jumpedThisFrame = false;
            _timeSinceJumpRequested += deltaTime;

            if (!_jumpRequested) return;

            bool withinGracePeriod = _timeSinceLastAbleToJump <= JumpPostGroundingGraceTime;

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

            bool canDoubleJump = AllowDoubleJump && !_doubleJumpConsumed && !isGrounded;
            if (canDoubleJump)
            {
                Motor.ForceUnground();
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

        private void UpdateJumpState(float deltaTime, bool isGrounded)
        {
            if (_jumpRequested && _timeSinceJumpRequested > JumpPreGroundingGraceTime)
                _jumpRequested = false;

            if (isGrounded)
            {
                if (!_jumpedThisFrame)
                {
                    _jumpConsumed = false;
                    _doubleJumpConsumed = false;
                    SetGliding(false);
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

            Motor.SetCapsuleDimensions(StandingCapsuleRadius, StandingCapsuleHeight, StandingCapsuleHeight * 0.5f);

            bool obstructed = Motor.CharacterOverlap(
                Motor.TransientPosition,
                Motor.TransientRotation,
                _probedColliders,
                Motor.CollidableLayers,
                QueryTriggerInteraction.Ignore) > 0;

            if (obstructed)
                Motor.SetCapsuleDimensions(StandingCapsuleRadius, CrouchedCapsuleHeight, CrouchedCapsuleHeight * 0.5f);
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
                default:
                    {
                        Vector3 smoothedDir = Vector3.Slerp(currentUp, Vector3.up,
                            1f - Mathf.Exp(-BonusOrientationSharpness * deltaTime));
                        currentRotation = Quaternion.FromToRotation(currentUp, smoothedDir) * currentRotation;
                        break;
                    }
            }
        }

        // ─── Glide Tilt ──────────────────────────────────────────────────────

        private void UpdateGlideTilt(float deltaTime)
        {
            bool atNeutral = !_isGliding && Mathf.Abs(_currentBank) < 0.01f && Mathf.Abs(_currentPitch) < 0.01f;
            if (atNeutral)
            {
                if (_currentBank != 0f || _currentPitch != 0f)
                {
                    _currentBank = 0f;
                    _currentPitch = 0f;
                    MeshRoot.localRotation = Quaternion.identity;
                }
                return;
            }

            float targetBank = 0f;
            float targetPitch = 0f;

            if (_isGliding)
            {
                Vector3 localMove = Motor.Transform.InverseTransformDirection(_moveInputVector);
                targetBank = -localMove.x * MaxBankAngle;

                Vector3 horizontalVelocity = Vector3.ProjectOnPlane(Motor.Velocity, Motor.CharacterUp);
                float speedRatio = Mathf.Clamp01(horizontalVelocity.magnitude / GlideHorizontalSpeed);
                targetPitch = speedRatio * MaxPitchAngle;
            }

            float activeSpeed = _isGliding ? TiltSmoothing * TiltSmoothingScale : TiltRecoverySpeed * TiltRecoveryScale;
            float smoothFactor = 1f - Mathf.Exp(-activeSpeed * deltaTime);
            _currentBank = Mathf.Lerp(_currentBank, targetBank, smoothFactor);
            _currentPitch = Mathf.Lerp(_currentPitch, targetPitch, smoothFactor);

            MeshRoot.localRotation = Quaternion.Euler(_currentPitch, 0f, _currentBank);
        }

        // ─── Ground Event Callbacks ──────────────────────────────────────────

        protected virtual void OnLanded()
        {
            OnLandedEvent?.Invoke();
        }

        protected virtual void OnLeaveStableGround()
        {
            OnLeftGroundEvent?.Invoke();
        }
    }
}