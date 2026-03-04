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
        public bool GlideHeld;
        public bool SprintHeld;
        public bool FloatHeld;
        public bool CrouchDown;
        public bool CrouchUp;
    }

    public struct AICharacterInputs
    {
        public Vector3 MoveVector;
        public Vector3 LookVector;
    }

    // ─── Settings ────────────────────────────────────────────────────────────
    // Grouped as serializable structs so the Inspector stays navigable
    // and settings can be swapped per-character via ScriptableObjects later.

    [Serializable]
    public struct MovementSettings
    {
        public float MaxSpeed;
        public float Sharpness;
        public float OrientationSharpness;
        public OrientationMethod OrientationMethod;
    }

    [Serializable]
    public struct AirSettings
    {
        public float MaxSpeed;
        public float Acceleration;
        public float Drag;
    }

    [Serializable]
    public struct JumpSettings
    {
        public bool AllowWhenSliding;
        public float UpSpeed;
        public float ScalableForwardSpeed;
        public float PreGroundingGraceTime;
        public float PostGroundingGraceTime;
    }

    [Serializable]
    public struct DoubleJumpSettings
    {
        public bool Enabled;
        public float UpSpeed;
    }

    [Serializable]
    public struct GlidingSettings
    {
        public bool Enabled;
        public float GravityScale;
        public float MaxFallSpeed;
        public float HorizontalSpeed;
        public float EntryMinAirTime;
        public float Acceleration;
        public float Deceleration;
    }

    [Serializable]
    public struct GlideTiltSettings
    {
        public float MaxBankAngle;
        public float MaxPitchAngle;
        [Range(0f, 1f)] public float Smoothing;
        [Range(0f, 2f)] public float RecoverySpeed;
    }

    [Serializable]
    public struct SprintSettings
    {
        public bool Enabled;
        public float SpeedMultiplier;
    }

    [Serializable]
    public struct FloatingSettings
    {
        public bool Enabled;
        public float LiftSpeed;
        public float UpAcceleration;
        public float MaxMana;
        public float ManaDepletionRate;
        public float ManaRegenRate;
    }

    [Serializable]
    public struct CrouchSettings
    {
        public float CapsuleHeight;
        public float StandingCapsuleHeight;
        public float StandingCapsuleRadius;
        public Vector3 MeshScale;
    }

    [Serializable]
    public struct MiscSettings
    {
        public BonusOrientationMethod BonusOrientation;
        public float BonusOrientationSharpness;
        public Vector3 Gravity;
    }

    // ─── Controller ──────────────────────────────────────────────────────────

    /// <summary>
    /// Thin shell. Owns settings, events, and shared transient state.
    /// ALL movement behaviour lives in PlayerStateBase subclasses.
    ///
    /// HOW TO ADD A NEW STATE:
    ///   1. Add value to CharacterState enum.
    ///   2. Create a class extending PlayerStateBase.
    ///   3. Register it in BuildStates().
    ///   4. Call TransitionToState() to activate it.
    /// </summary>
    public class PlayerCharacterController : MonoBehaviour, ICharacterController
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("References")]
        public KinematicCharacterMotor Motor;
        public Transform MeshRoot;
        public Transform CameraFollowPoint;

        [Header("Movement")]
        public MovementSettings Movement = new MovementSettings
        {
            MaxSpeed = 10f,
            Sharpness = 15f,
            OrientationSharpness = 10f,
            OrientationMethod = OrientationMethod.TowardsCamera,
        };

        [Header("Air")]
        public AirSettings Air = new AirSettings
        {
            MaxSpeed = 15f,
            Acceleration = 15f,
            Drag = 0.1f,
        };

        [Header("Jumping")]
        public JumpSettings Jump = new JumpSettings
        {
            AllowWhenSliding = false,
            UpSpeed = 10f,
            ScalableForwardSpeed = 10f,
            PreGroundingGraceTime = 0f,
            PostGroundingGraceTime = 0f,
        };

        [Header("Double Jump")]
        public DoubleJumpSettings DoubleJump = new DoubleJumpSettings
        {
            Enabled = true,
            UpSpeed = 8f,
        };

        [Header("Gliding")]
        public GlidingSettings Gliding = new GlidingSettings
        {
            Enabled = true,
            GravityScale = 0.15f,
            MaxFallSpeed = 2f,
            HorizontalSpeed = 12f,
            EntryMinAirTime = 0.1f,
            Acceleration = 4f,
            Deceleration = 3f,
        };

        [Header("Glide Tilt")]
        public GlideTiltSettings GlideTilt = new GlideTiltSettings
        {
            MaxBankAngle = 30f,
            MaxPitchAngle = 20f,
            Smoothing = 0.8f,
            RecoverySpeed = 0.2f,
        };

        [Header("Sprinting")]
        public SprintSettings Sprint = new SprintSettings
        {
            Enabled = true,
            SpeedMultiplier = 1.7f,
        };

        [Header("Floating")]
        public FloatingSettings Floating = new FloatingSettings
        {
            Enabled = true,
            LiftSpeed = 5f,
            UpAcceleration = 40f,
            MaxMana = 100f,
            ManaDepletionRate = 20f,
            ManaRegenRate = 15f,
        };

        [Header("Crouching")]
        public CrouchSettings Crouch = new CrouchSettings
        {
            CapsuleHeight = 1f,
            StandingCapsuleHeight = 2f,
            StandingCapsuleRadius = 0.5f,
            MeshScale = new Vector3(1f, 0.5f, 1f),
        };

        [Header("Misc")]
        public MiscSettings Misc = new MiscSettings
        {
            BonusOrientation = BonusOrientationMethod.None,
            BonusOrientationSharpness = 10f,
            Gravity = new Vector3(0f, -30f, 0f),
        };

        [Header("Ignored Colliders")]
        public List<Collider> IgnoredColliders = new List<Collider>();

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Fired when glide starts (true) or stops (false).</summary>
        public event Action<bool> OnGlideChanged;
        /// <summary>Fired when the character lands on stable ground.</summary>
        public event Action OnLandedEvent;
        /// <summary>Fired when the character leaves stable ground.</summary>
        public event Action OnLeftGroundEvent;
        /// <summary>Fired on CharacterState transition. (newState, previousState)</summary>
        public event Action<CharacterState, CharacterState> OnStateChanged;
        /// <summary>Fired when sprint starts (true) or stops (false).</summary>
        public event Action<bool> OnSprintChanged;
        /// <summary>Fired the frame the character executes a jump (first or double).</summary>
        public event Action OnJumpedEvent;
        /// <summary>Fired when float starts (true) or stops (false).</summary>
        public event Action<bool> OnFloatChanged;
        /// <summary>Fired whenever mana changes. Value is normalized 0–1 for UI.</summary>
        public event Action<float> OnManaChanged;

        // ── Constants ─────────────────────────────────────────────────────────

        internal const float TiltSmoothingScale = 15f;
        internal const float TiltRecoveryScale = 5f;

        // ── Public State ──────────────────────────────────────────────────────

        public CharacterState CurrentCharacterState { get; private set; }

        public bool IsGliding => CurrentCharacterState == CharacterState.Gliding;
        public bool IsFloating => CurrentCharacterState == CharacterState.Floating;
        public bool IsSprinting => _currentState?.IsSprinting ?? false;
        public float CurrentMana => SharedMana;

        // ── Internal Shared Fields ────────────────────────────────────────────
        // Written by the controller, read/written by states.
        // Grouped by concern for readability.

        // Movement
        internal Vector3 MoveInputVector;
        internal Vector3 LookInputVector;
        internal Vector3 InternalVelocityAdd;

        // Input flags
        internal bool GlideInputHeld;
        internal bool SprintInputHeld;
        internal bool FloatInputHeld;
        internal bool CrouchDown;
        internal bool CrouchUp;

        // Jump — shared across all airborne-capable states
        internal bool JumpRequested;
        internal bool JumpConsumed;
        internal bool DoubleJumpConsumed;
        internal bool JumpedThisFrame;
        internal bool JumpEventFired;
        internal float TimeSinceJumpRequested = Mathf.Infinity;
        internal float TimeSinceLastAbleToJump = 0f;

        // Mana — persists across all state transitions
        internal float SharedMana;

        // Crouch — persists across grounded movement
        internal bool IsCrouching;
        internal bool ShouldBeCrouching;

        // Tilt — owned here so any state can drive recovery without coupling to GlidingState
        internal float TiltBank;
        internal float TiltPitch;

        // Physics
        internal readonly Collider[] ProbedColliders = new Collider[8];
        internal HashSet<Collider> IgnoredCollidersSet;

        // ── Private ───────────────────────────────────────────────────────────

        private PlayerStateBase _currentState;
        private Dictionary<CharacterState, PlayerStateBase> _states;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            Motor.CharacterController = this;
            IgnoredCollidersSet = new HashSet<Collider>(IgnoredColliders);
            SharedMana = Floating.MaxMana;

            _states = BuildStates();
            TransitionToState(CharacterState.Grounded);
        }

        /// <summary>
        /// Register all states here. Add new entries as the game grows.
        /// </summary>
        private Dictionary<CharacterState, PlayerStateBase> BuildStates()
        {
            return new Dictionary<CharacterState, PlayerStateBase>
            {
                [CharacterState.Grounded] = new GroundedState(),
                [CharacterState.Air] = new AirState(),
                [CharacterState.Gliding] = new GlidingState(),
                [CharacterState.Floating] = new FloatingState(),
                [CharacterState.Stunned] = new StunnedState(),
            };
        }

        // ── State Machine ─────────────────────────────────────────────────────

        public void TransitionToState(CharacterState newState)
        {
            if (!_states.TryGetValue(newState, out PlayerStateBase next))
            {
                Debug.LogError($"[FSM] State '{newState}' is not registered. Add it to BuildStates().");
                return;
            }

            CharacterState prev = CurrentCharacterState;
            _currentState?.Exit();
            CurrentCharacterState = newState;
            _currentState = next;
            _currentState.Enter(this);
            OnStateChanged?.Invoke(newState, prev);
        }

        // ── Input ─────────────────────────────────────────────────────────────

        public void SetInputs(ref PlayerCharacterInputs inputs)
        {
            // Build camera-relative move vector once — states read C.MoveInputVector
            Vector3 moveRaw = Vector3.ClampMagnitude(
                new Vector3(inputs.MoveAxisRight, 0f, inputs.MoveAxisForward), 1f);

            Vector3 camDir = Vector3.ProjectOnPlane(
                inputs.CameraRotation * Vector3.forward, Motor.CharacterUp).normalized;

            if (camDir.sqrMagnitude == 0f)
                camDir = Vector3.ProjectOnPlane(
                    inputs.CameraRotation * Vector3.up, Motor.CharacterUp).normalized;

            MoveInputVector = Quaternion.LookRotation(camDir, Motor.CharacterUp) * moveRaw;

            // LookInputVector resolved once here — no duplication across states
            LookInputVector = Movement.OrientationMethod == OrientationMethod.TowardsCamera
                ? camDir
                : MoveInputVector.normalized;

            // Raw input flags
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
        {
            // Reset once per frame here — never duplicated in individual states
            JumpEventFired = false;

            // Mana ticked centrally so it's never missed regardless of active state
            TickMana(dt);

            _currentState?.BeforeUpdate(dt);
        }

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
        // States call these — never touch events directly from outside.

        internal void FireGlideChanged(bool v) => OnGlideChanged?.Invoke(v);
        internal void FireSprintChanged(bool v) => OnSprintChanged?.Invoke(v);
        internal void FireFloatChanged(bool v) => OnFloatChanged?.Invoke(v);
        internal void FireManaChanged(float v) => OnManaChanged?.Invoke(v);
        internal void FireJumped() => OnJumpedEvent?.Invoke();

        // ── Private ───────────────────────────────────────────────────────────

        /// <summary>
        /// Mana ticked here so it is always correct regardless of active state.
        /// FloatingState.BeforeUpdate signals drain via IsFloating property.
        /// </summary>
        private void TickMana(float dt)
        {
            float prev = SharedMana;

            if (IsFloating)
                SharedMana = Mathf.Max(0f, SharedMana - Floating.ManaDepletionRate * dt);
            else if (Motor.GroundingStatus.IsStableOnGround)
                SharedMana = Mathf.Min(Floating.MaxMana, SharedMana + Floating.ManaRegenRate * dt);

            if (!Mathf.Approximately(SharedMana, prev))
                FireManaChanged(SharedMana / Floating.MaxMana);
        }
    }
}