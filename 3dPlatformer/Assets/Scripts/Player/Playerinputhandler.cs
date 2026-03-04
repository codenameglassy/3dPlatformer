using UnityEngine;
using KinematicCharacterController;

namespace JourneyGator.Player
{
    /// <summary>
    /// Reads raw Unity input and forwards it to the PlayerCharacterController and PlayerCamera.
    /// Responsible ONLY for input routing — no game logic lives here.
    /// </summary>
    public class PlayerInputHandler : MonoBehaviour
    {
        [Header("References")]
        public PlayerCharacterController Character;
        public PlayerCamera CharacterCamera;

        // Input axis names — centralized so renaming is trivial
        private const string MouseXInput = "Mouse X";
        private const string MouseYInput = "Mouse Y";
        private const string MouseScrollInput = "Mouse ScrollWheel";
        private const string HorizontalInput = "Horizontal";
        private const string VerticalInput = "Vertical";

        private void Start()
        {
            LockCursor();

            CharacterCamera.SetFollowTransform(Character.CameraFollowPoint);

            // Tell the camera to ignore the character's own colliders for obstruction checks
            CharacterCamera.IgnoredColliders.Clear();
            CharacterCamera.IgnoredColliders.AddRange(Character.GetComponentsInChildren<Collider>());
        }

        private void Update()
        {
            // Re-lock cursor when player clicks after focus loss
            if (Input.GetMouseButtonDown(0))
            {
                LockCursor();
            }

            HandleCharacterInput();
        }

        private void LateUpdate()
        {
            // Rotate camera with moving physics platforms BEFORE camera update
            if (CharacterCamera.RotateWithPhysicsMover && Character.Motor.AttachedRigidbody != null)
            {
                PhysicsMover mover = Character.Motor.AttachedRigidbody.GetComponent<PhysicsMover>();
                if (mover != null)
                {
                    CharacterCamera.PlanarDirection = mover.RotationDeltaFromInterpolation * CharacterCamera.PlanarDirection;
                    CharacterCamera.PlanarDirection = Vector3.ProjectOnPlane(
                        CharacterCamera.PlanarDirection,
                        Character.Motor.CharacterUp
                    ).normalized;
                }
            }

            HandleCameraInput();
        }

        // ─── Private Helpers ────────────────────────────────────────────────

        private void HandleCameraInput()
        {
            Vector3 lookInputVector = Vector3.zero;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                lookInputVector = new Vector3(
                    Input.GetAxisRaw(MouseXInput),
                    Input.GetAxisRaw(MouseYInput),
                    0f
                );
            }

            // Zoom disabled in WebGL to avoid browser scroll conflicts
            float scrollInput = 0f;
#if !UNITY_WEBGL
            scrollInput = -Input.GetAxis(MouseScrollInput);
#endif

            CharacterCamera.UpdateWithInput(Time.deltaTime, scrollInput, lookInputVector);

            // Middle-mouse toggles first-person / third-person zoom (right-click is reserved for Glide)
            if (Input.GetMouseButtonDown(2))
            {
                CharacterCamera.TargetDistance =
                    (CharacterCamera.TargetDistance == 0f) ? CharacterCamera.DefaultDistance : 0f;
            }
        }

        private void HandleCharacterInput()
        {
            PlayerCharacterInputs characterInputs = new PlayerCharacterInputs
            {
                MoveAxisForward = Input.GetAxisRaw(VerticalInput),
                MoveAxisRight = Input.GetAxisRaw(HorizontalInput),
                CameraRotation = CharacterCamera.Transform.rotation,
                JumpDown = Input.GetKeyDown(KeyCode.Space),
                GlideHeld = Input.GetMouseButton(1),             // Hold right-click to glide
                CrouchDown = Input.GetKeyDown(KeyCode.C),
                CrouchUp = Input.GetKeyUp(KeyCode.C),
            };

            Character.SetInputs(ref characterInputs);
        }

        private static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }
    }
}