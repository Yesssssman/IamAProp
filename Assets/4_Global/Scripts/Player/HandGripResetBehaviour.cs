using UnityEngine;

/// <summary>상태를 벗어날 때 손이 옮겨 쥔 총기와 탄창을 원래 자리로 되돌림.</summary>
public class HandGripResetBehaviour : StateMachineBehaviour
{
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        HandScript hand = animator.GetComponent<HandScript>();

        if (hand)
        {
            hand.ResetGrip();
        }
    }
}
