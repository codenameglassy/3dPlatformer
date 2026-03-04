using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Active when the player is stunned.
    /// All input is blocked. Only gravity and drag apply.
    ///
    /// TRANSITIONS OUT:
    ///   Call C.TransitionToState(CharacterState.Grounded) from game logic
    ///   once the stun duration expires.
    ///
    /// Example usage:
    ///   controller.TransitionToState(CharacterState.Stunned);
    ///   await Task.Delay(stunDurationMs);
    ///   controller.TransitionToState(CharacterState.Grounded);
    /// </summary>
    public class StunnedState : PlayerStateBase
    {
        public override void UpdateVelocity(ref Vector3 v, float dt)
        {
            v += C.Gravity * dt;
            v *= 1f / (1f + C.Drag * dt);
        }

        // All other hooks intentionally empty — no input, no rotation change.
    }
}
