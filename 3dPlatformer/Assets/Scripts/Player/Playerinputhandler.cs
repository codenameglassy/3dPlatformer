using UnityEngine;
using KinematicCharacterController;

namespace JourneyGator.Player
{
    /// <summary>
    /// Reads raw Unity input and forwards it to PlayerCharacterController and PlayerCamera.
    /// Responsible ONLY for input routing — no game logic lives here.
    ///
    /// Keybindings are serialized so they can be changed in the Inspector
    /// and eventually driven by a remapping system without touching this file.
    /// </summary>
    public class PlayerInputHandler : MonoBehaviour
    {
        [Header("References")]
        public PlayerCharacterController Character;
        public PlayerCamera CharacterCamera;

        [Header("Keybindings")]
        public KeyCode JumpKey = KeyCode.Space;
        public KeyCode SprintKey = KeyCode.LeftShift;
        public KeyCode FloatKey = KeyCode.F;
        public KeyCode CrouchKey = KeyCode.C;
        public int GlideMouseButton = 1;  // Right-click

        // ── Input Axis Names ──────────────────────────────────────────────────
        // Centralized so renaming is trivial if the Input Manager axes are changed.

        private const string AxisMouseX = "Mouse X";
        private const string AxisMouseY = "Mouse Y";
        private const string AxisMouseScroll = "Mouse ScrollWheel";
        private const string AxisHorizontal = "Horizontal";
        private const string AxisVertical = "Vertical";

        // ── Lifecycle ────────────────────────────────────────────────────────

        private void Start()
        {
            LockCursor();

            CharacterCamera.SetFollowTransform(Character.CameraFollowPoint);
            CharacterCamera.IgnoredColliders.Clear();
            CharacterCamera.IgnoredColliders.AddRange(Character.GetComponentsInChildren<Collider>());
        }

        private void Update()
        {
            if (Input.GetMouseButtonDown(0))
                LockCursor();

            HandleCharacterInput();
        }

        private void LateUpdate()
        {
            HandlePlatformRotation();
            HandleCameraInput();
        }

        // ── Character Input ───────────────────────────────────────────────────

        private void HandleCharacterInput()
        {
            PlayerCharacterInputs inputs = new PlayerCharacterInputs
            {
                MoveAxisForward = Input.GetAxisRaw(AxisVertical),
                MoveAxisRight = Input.GetAxisRaw(AxisHorizontal),
                CameraRotation = CharacterCamera.Transform.rotation,
                JumpDown = Input.GetKeyDown(JumpKey),
                GlideHeld = Input.GetMouseButton(GlideMouseButton),
                SprintHeld = Input.GetKey(SprintKey),
                FloatHeld = Input.GetKey(FloatKey),
                CrouchDown = Input.GetKeyDown(CrouchKey),
                CrouchUp = Input.GetKeyUp(CrouchKey),
            };

            Character.SetInputs(ref inputs);
        }

        // ── Camera Input ──────────────────────────────────────────────────────

        private void HandleCameraInput()
        {
            Vector3 lookInput = Vector3.zero;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                lookInput = new Vector3(
                    Input.GetAxisRaw(AxisMouseX),
                    Input.GetAxisRaw(AxisMouseY),
                    0f);
            }

            float scrollInput = 0f;
#if !UNITY_WEBGL
            scrollInput = -Input.GetAxis(AxisMouseScroll);
#endif

            CharacterCamera.UpdateWithInput(Time.deltaTime, scrollInput, lookInput);

            if (Input.GetMouseButtonDown(2))
            {
                CharacterCamera.TargetDistance = CharacterCamera.TargetDistance == 0f
                    ? CharacterCamera.DefaultDistance
                    : 0f;
            }
        }

        // ── Platform Rotation ─────────────────────────────────────────────────

        private void HandlePlatformRotation()
        {
            if (!CharacterCamera.RotateWithPhysicsMover) return;
            if (Character.Motor.AttachedRigidbody == null) return;

            PhysicsMover mover = Character.Motor.AttachedRigidbody.GetComponent<PhysicsMover>();
            if (mover == null) return;

            CharacterCamera.PlanarDirection = mover.RotationDeltaFromInterpolation * CharacterCamera.PlanarDirection;
            CharacterCamera.PlanarDirection = Vector3.ProjectOnPlane(
                CharacterCamera.PlanarDirection, Character.Motor.CharacterUp).normalized;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void LockCursor() => Cursor.lockState = CursorLockMode.Locked;
    }
}