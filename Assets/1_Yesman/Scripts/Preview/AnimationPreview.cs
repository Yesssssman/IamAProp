using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>프롭 클립, 손 클립, 무기 클립을 함께 반복 재생함.</summary>
public class AnimationPreview : MonoBehaviour
{
    private PlayableGraph graph;

    private AnimationClipPlayable propPlayable;

    /// <summary>손 클립이 없으면 유효하지 않은 Playable.</summary>
    private AnimationClipPlayable handPlayable;

    /// <summary>무기 클립이 없으면 유효하지 않은 Playable.</summary>
    private AnimationClipPlayable weaponPlayable;

    /// <summary>무기 클립이 시작하는 손 클립의 시각 (초).</summary>
    private float weaponClipTime;

    /// <summary>프롭과 손의 <see cref="Animator"/>에 두 클립을 재생하기 시작함.</summary>
    /// <param name="handClip">손 클립. null이면 손은 재생하지 않음.</param>
    public void Play(AnimationClip propClip, AnimationClip handClip)
    {
        graph = PlayableGraph.Create(name);
        propPlayable = AnimationClipPlayable.Create(graph, propClip);
        AnimationPlayableOutput.Create(graph, "Prop", GetComponent<Animator>()).SetSourcePlayable(propPlayable);

        if (handClip)
        {
            handPlayable = AnimationClipPlayable.Create(graph, handClip);
            AnimationPlayableOutput.Create(graph, "Hand", GetComponentInChildren<HandScript>().GetComponent<Animator>())
                .SetSourcePlayable(handPlayable);
        }

        graph.Play();
    }

    /// <summary>손 클립이 <paramref name="startTime"/> (초)을 지날 때부터 무기 클립을 한 번 재생하게 함.</summary>
    public void PlayWeapon(Animator weaponAnimator, AnimationClip weaponClip, float startTime)
    {
        weaponPlayable = AnimationClipPlayable.Create(graph, weaponClip);
        weaponPlayable.Pause();
        AnimationPlayableOutput.Create(graph, "Weapon", weaponAnimator).SetSourcePlayable(weaponPlayable);
        weaponClipTime = startTime;
    }

    private void Update()
    {
        AnimationClip propClip = propPlayable.GetAnimationClip();

        if (!propClip.isLooping && propPlayable.GetTime() >= propClip.length)
        {
            propPlayable.SetTime(0d);
        }

        if (!handPlayable.IsValid())
        {
            return;
        }

        AnimationClip handClip = handPlayable.GetAnimationClip();

        if (!handClip.isLooping && handPlayable.GetTime() >= handClip.length)
        {
            // SetTime 한 번은 이전 시각에서 0까지 거꾸로 훑으며 그 사이의 애니메이션 이벤트를 역순으로 호출함
            // (재장전 클립이면 Equip → Show → Drop 순서로 불려 탄창이 빠진 채로 시작함).
            // 같은 시각으로 두 번 호출하면 이전 시각도 0이 되어 이벤트를 호출하지 않음.
            handPlayable.SetTime(0d);
            handPlayable.SetTime(0d);
        }

        if (weaponPlayable.IsValid())
        {
            weaponPlayable.SetTime(Math.Clamp(handPlayable.GetTime() - weaponClipTime, 0d,
                weaponPlayable.GetAnimationClip().length));
        }
    }

    private void OnDestroy()
    {
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }
}
