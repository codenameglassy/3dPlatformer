using UnityEngine;

namespace JourneyGator.Player
{
    /// <summary>
    /// Active when the player is stunned.
    /// All input blocked. Only gravity and drag apply.
    ///
    /// TRANSITIONS OUT:
    ///   Driven externally — call C.TransitionToState(CharacterState.Grounded)
    ///   from game logic once stun expires.
    ///
    /// Example:
    ///   controller.TransitionToState(CharacterState.Stunned);
    ///   StartCoroutine(EndStunAfter(2f));
    /// </summary>
    public class StunnedState : PlayerStateBase
    {
        public override void UpdateVelocity(ref Vector3 v, float dt)
        {
            v += C.Misc.Gravity * dt;
            v *= 1f / (1f + C.Air.Drag * dt);
        }
    }
}