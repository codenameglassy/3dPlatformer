using System;
using System.Collections.Generic;
using UnityEngine;
using KinematicCharacterController;

namespace JourneyGator.Player
{
    // ─── Enums & Structs ────────────────────────────────────────────────────

    public enum CharacterState
    {
        Grounded,   // On stable ground — walking, sprinting, crouching
        Air,        // Airborne — normal air movement, double jump, coyote time
        Gliding,    // Gliding — reduced gravity, horizontal control, tilt
        Floating,   // Mana-powered upward float
        Stunned,    // No input, gravity + drag only
    }

    public enum OrientationMethod { TowardsCamera, TowardsMovement }
    public enum BonusOrientationMethod { None, TowardsGravity, TowardsGroundSlopeAndGravity }

    public struct PlayerCharacterInputs
    {
        public float MoveAxisForward;
        public float MoveAxisRight;
        public Quaternion CameraRotation;
        public bool JumpDown;
        public bool GlideHeld;   // RMB — glide in air
        public bool SprintHeld;  // Left Shift — sprint on ground
        public bool FloatHeld;   // F — float with mana
        public bool CrouchDown;
        public bool CrouchUp;
    }

    public struct AICharacterInputs
    {
        public Vector3 MoveVector;
        public Vector3 LookVector;
    }

    // ─── Controller ─────────────────────────────────────────────────────────

    /// <summary>
    /// Thin shell. Holds inspector settings, events, and shared transient state.
    /// ALL behaviour lives in state classes. This class only delegates.
    ///
    /// HOW TO ADD A NEW STATE:
    ///   1. Add to CharacterState enum.
    ///   2. Create a class extending PlayerStateBase.
    ///   3. Register it in Awake() _states dictionary.
    ///   4. Call TransitionToState() to activate it.
    /// </summary>
    public class PlayerCharacterController : MonoBehaviour, ICharacterController
    {
        // ── Inspector ────────────────────────────────────────────────────────

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
        public float GlideAcceleration = 4f;
        public float GlideDeceleration = 3f;

        [Header("Glide Tilt")]
        public float MaxBankAngle = 30f;
        public float MaxPitchAngle = 20f;
        [Range(0f, 1f)] public float TiltSmoothing = 0.8f;
        [Range(0f, 2f)] public float TiltRecoverySpeed = 0.2f;

        [Header("Sprinting")]
        public bool AllowSprinting = true;
        public float SprintSpeedMultiplier = 1.7f;

        [Header("Floating")]
        public bool AllowFloating = true;
        public float FloatLiftSpeed = 5f;
        public float FloatUpAcceleration = 40f;
        public float MaxMana = 100f;
        public float ManaDepletionRate = 20f;
        public float ManaRegenRate = 15f;

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

        // ── Events ───────────────────────────────────────────────────────────

        /// <summary>Fired when glide starts (true) or stops (false).</summary>
        public event Action<bool> OnGlideChanged;
        /// <summary>Fired when the character lands on stable ground.</summary>
        public event Action OnLandedEvent;
        /// <summary>Fired when the character leaves stable ground.</summary>
        public event Action OnLeftGroundEvent;
        /// <summary>Fired on CharacterState transition. Args: (newState, previousState).</summary>
        public event Action<CharacterState, CharacterState> OnStateChanged;
        /// <summary>Fired when sprint starts (true) or stops (false).</summary>
        public event Action<bool> OnSprintChanged;
        /// <summary>Fired the frame the character executes a jump (first or double).</summary>
        public event Action OnJumpedEvent;
        /// <summary>Fired when float starts (true) or stops (false).</summary>
        public event Action<bool> OnFloatChanged;
        /// <summary>Fired whenever mana changes. Value is normalized 0–1 for UI.</summary>
        public event Action<float> OnManaChanged;

        // ── Constants ────────────────────────────────────────────────────────

        internal const float TiltSmoothingScale = 15f;
        internal const float TiltRecoveryScale = 5f;

        // ── Public State ─────────────────────────────────────────────────────

        public CharacterState CurrentCharacterState { get; private set; }

        public bool IsGliding => CurrentCharacterState == CharacterState.Gliding;
        public bool IsFloating => CurrentCharacterState == CharacterState.Floating;
        public bool IsSprinting => _currentState?.IsSprinting ?? false;
        public float CurrentMana => SharedMana;

        // ── Internal Shared Fields (read/written by states) ───────────────────
        // Placed here so state instances can share data across transitions
        // without coupling to each other.

        internal Vector3 MoveInputVector = Vector3.zero;
        internal Vector3 LookInputVector = Vector3.zero;
        internal Vector3 InternalVelocityAdd = Vector3.zero;
        internal readonly Collider[] ProbedColliders = new Collider[8];
        internal HashSet<Collider> IgnoredCollidersSet;

        // Input — written by SetInputs, read by states
        internal bool GlideInputHeld;
        internal bool SprintInputHeld;
        internal bool FloatInputHeld;
        internal bool CrouchDown;
        internal bool CrouchUp;

        // Jump — shared across Grounded/Air/Gliding/Floating
        internal bool JumpRequested;
        internal bool JumpConsumed;
        internal bool DoubleJumpConsumed;
        internal bool JumpedThisFrame;
        internal bool JumpEventFired;       // Guards against multi-fire across KCC sub-steps
        internal float TimeSinceJumpRequested = Mathf.Infinity;
        internal float TimeSinceLastAbleToJump = 0f;

        // Mana — persists across all states
        internal float SharedMana;

        // Crouch — persists across grounded/carrying
        internal bool IsCrouching;
        internal bool ShouldBeCrouching;

        // ── Private ───────────────────────────────────────────────────────────

        private PlayerStateBase _currentState;
        private Dictionary<CharacterState, PlayerStateBase> _states;

        /// <summary>Typed reference so Air/Grounded states can drive tilt recovery.</summary>
        internal GlidingState GlidingStateInstance;

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            Motor.CharacterController = this;
            IgnoredCollidersSet = new HashSet<Collider>(IgnoredColliders);
            SharedMana = MaxMana;

            _states = new Dictionary<CharacterState, PlayerStateBase>
            {
                [CharacterState.Grounded] = new GroundedState(),
                [CharacterState.Air] = new AirState(),
                [CharacterState.Gliding] = new GlidingState(),
                [CharacterState.Floating] = new FloatingState(),
                [CharacterState.Stunned] = new StunnedState(),
            };

            // Typed reference so other states can call tilt recovery
            GlidingStateInstance = (GlidingState)_states[CharacterState.Gliding];

            TransitionToState(CharacterState.Grounded);
        }

        // ── State Machine ─────────────────────────────────────────────────────

        public void TransitionToState(CharacterState newState)
        {
            CharacterState prev = CurrentCharacterState;
            _currentState?.Exit();
            CurrentCharacterState = newState;
            _currentState = _states[newState];
            _currentState.Enter(this);
            OnStateChanged?.Invoke(newState, prev);
        }

        // ── Input ─────────────────────────────────────────────────────────────

        public void SetInputs(ref PlayerCharacterInputs inputs)
        {
            Vector3 moveInput = Vector3.ClampMagnitude(
                new Vector3(inputs.MoveAxisRight, 0f, inputs.MoveAxisForward), 1f);

            Vector3 camDir = Vector3.ProjectOnPlane(
                inputs.CameraRotation * Vector3.forward, Motor.CharacterUp).normalized;

            if (camDir.sqrMagnitude == 0f)
                camDir = Vector3.ProjectOnPlane(
                    inputs.CameraRotation * Vector3.up, Motor.CharacterUp).normalized;

            MoveInputVector = Quaternion.LookRotation(camDir, Motor.CharacterUp) * moveInput;

            GlideInputHeld = inputs.GlideHeld;
            SprintInputHeld = inputs.SprintHeld;
            FloatInputHeld = inputs.FloatHeld;
            CrouchDown = inputs.CrouchDown;
            CrouchUp = inputs.CrouchUp;

            if (inputs.JumpDown)
            {
                TimeSinceJumpRequested = 0f;
                JumpRequested = true;
            }

            _currentState?.HandleInput(ref inputs, camDir);
        }

        public void SetInputs(ref AICharacterInputs inputs)
        {
            MoveInputVector = inputs.MoveVector;
            LookInputVector = inputs.LookVector;
        }

        // ── ICharacterController ──────────────────────────────────────────────

        public void BeforeCharacterUpdate(float dt)
            => _currentState?.BeforeUpdate(dt);

        public void UpdateRotation(ref Quaternion r, float dt)
            => _currentState?.UpdateRotation(ref r, dt);

        public void UpdateVelocity(ref Vector3 v, float dt)
            => _currentState?.UpdateVelocity(ref v, dt);

        public void AfterCharacterUpdate(float dt)
            => _currentState?.AfterUpdate(dt);

        public void PostGroundingUpdate(float dt)
        {
            bool justLanded = Motor.GroundingStatus.IsStableOnGround && !Motor.LastGroundingStatus.IsStableOnGround;
            bool justLeftGround = !Motor.GroundingStatus.IsStableOnGround && Motor.LastGroundingStatus.IsStableOnGround;

            if (justLanded)
            {
                OnLandedEvent?.Invoke();
                _currentState?.OnLanded();
            }
            if (justLeftGround)
            {
                OnLeftGroundEvent?.Invoke();
                _currentState?.OnLeftGround();
            }
        }

        public bool IsColliderValidForCollisions(Collider c) => !IgnoredCollidersSet.Contains(c);

        public void OnGroundHit(Collider c, Vector3 n, Vector3 p, ref HitStabilityReport r) { }
        public void OnMovementHit(Collider c, Vector3 n, Vector3 p, ref HitStabilityReport r) { }
        public void ProcessHitStabilityReport(Collider c, Vector3 n, Vector3 p,
            Vector3 pos, Quaternion rot, ref HitStabilityReport r)
        { }
        public void OnDiscreteCollisionDetected(Collider c) { }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Apply an external impulse (explosion, bounce pad, etc.).</summary>
        public void AddVelocity(Vector3 velocity) => InternalVelocityAdd += velocity;

        // ── Internal Event Firers ─────────────────────────────────────────────

        internal void FireGlideChanged(bool v) => OnGlideChanged?.Invoke(v);
        internal void FireSprintChanged(bool v) => OnSprintChanged?.Invoke(v);
        internal void FireFloatChanged(bool v) => OnFloatChanged?.Invoke(v);
        internal void FireManaChanged(float v) => OnManaChanged?.Invoke(v);
        internal void FireJumped() => OnJumpedEvent?.Invoke();
    }
}