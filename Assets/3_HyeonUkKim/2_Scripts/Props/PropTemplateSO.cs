using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>프롭별 몸통 상태 애니메이션 클립 묶음.</summary>
[Serializable]
public struct PropBodyClips
{
    /// <summary>비무장 대기 상태 클립.</summary>
    public AnimationClip hideClip;

    /// <summary>기본 달리기 상태 클립.</summary>
    public AnimationClip runClip;

    /// <summary>점프 시작 상태 클립.</summary>
    public AnimationClip jumpClip;

    /// <summary>낙하 상태 클립.</summary>
    public AnimationClip fallClip;

    /// <summary>무기 장착 대기 상태 클립.</summary>
    public AnimationClip aimIdleClip;

    /// <summary>총기 조준 달리기 상태 클립.</summary>
    public AnimationClip aimRunClip;
}

/// <summary>프롭별 비무장 손 상태 애니메이션 클립 묶음.</summary>
[Serializable]
public struct PropUnarmedHandClips
{
    /// <summary>비무장 대기 상태 손 클립 (비워두면 기본 숨김 클립 유지).</summary>
    public AnimationClip idleClip;

    /// <summary>비무장 달리기 상태 손 클립.</summary>
    public AnimationClip runClip;

    /// <summary>비무장 점프 상태 손 클립.</summary>
    public AnimationClip jumpClip;

    /// <summary>비무장 낙하 상태 손 클립.</summary>
    public AnimationClip fallClip;
}

/// <summary>모든 프롭이 공유하는 베이스 애니메이터 컨트롤러, 기준 클립 및 무기 소켓 로드아웃을 정의한다.</summary>
[CreateAssetMenu(fileName = "PropTemplate", menuName = "IamAProp/Prop Template")]
public class PropTemplateSO : ScriptableObject
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>프롭 몸통 애니메이션 상태 머신의 기준 컨트롤러.</summary>
    [Header("공통 베이스 컨트롤러")]
    [SerializeField] private RuntimeAnimatorController baseBodyController;

    /// <summary>손 및 무기 애니메이션 상태 머신의 기준 컨트롤러.</summary>
    [SerializeField] private RuntimeAnimatorController baseHandController;

    /// <summary>몸통 베이스 컨트롤러의 대기 상태 기준 클립.</summary>
    [Header("몸통 오버라이드 기준 클립")]
    [SerializeField] private AnimationClip baseBodyHideClip;

    /// <summary>몸통 베이스 컨트롤러의 달리기 상태 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyRunClip;

    /// <summary>몸통 베이스 컨트롤러의 점프 상태 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyJumpClip;

    /// <summary>몸통 베이스 컨트롤러의 낙하 상태 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyFallClip;

    /// <summary>손 베이스 컨트롤러의 비무장 대기 상태 기준 클립.</summary>
    [Header("손 오버라이드 기준 클립")]
    [SerializeField] private AnimationClip baseHandIdleClip;

    /// <summary>손 베이스 컨트롤러의 비무장 달리기 상태 기준 클립.</summary>
    [SerializeField] private AnimationClip baseHandRunClip;

    /// <summary>손 베이스 컨트롤러의 비무장 점프 상태 기준 클립.</summary>
    [SerializeField] private AnimationClip baseHandJumpClip;

    /// <summary>손 베이스 컨트롤러의 비무장 낙하 상태 기준 클립.</summary>
    [SerializeField] private AnimationClip baseHandFallClip;

    /// <summary><see cref="EWeaponType"/> 순서에 대응하는 공통 무기 소켓 설정 목록.</summary>
    [Header("공통 무기 소켓 로드아웃")]
    [SerializeField] private WeaponSocketConfig[] weaponSockets;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>몸통 기본 애니메이터 컨트롤러.</summary>
    public RuntimeAnimatorController BaseBodyController => baseBodyController;

    /// <summary>손 기본 애니메이터 컨트롤러.</summary>
    public RuntimeAnimatorController BaseHandController => baseHandController;

    /// <summary>공통 무기 소켓 설정 배열.</summary>
    public WeaponSocketConfig[] WeaponSockets => weaponSockets;

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary><paramref name="bodyClips"/>를 매핑한 무기 모드별 몸통 <see cref="AnimatorOverrideController"/> 배열을 생성한다.</summary>
    public RuntimeAnimatorController[] CreateBodyControllers(in PropBodyClips bodyClips)
    {
        if (baseBodyController == null) return Array.Empty<RuntimeAnimatorController>();

        int weaponCount = weaponSockets != null && weaponSockets.Length > 0
            ? weaponSockets.Length
            : Enum.GetValues(typeof(EWeaponType)).Length;

        RuntimeAnimatorController[] controllers = new RuntimeAnimatorController[weaponCount];
        List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new(4);

        for (int i = 0; i < weaponCount; i++)
        {
            EWeaponType weaponType = (EWeaponType)i;
            bool isHidden = weaponType == EWeaponType.Hide;
            bool usesAimLocomotion = weaponSockets != null
                && i < weaponSockets.Length
                && weaponSockets[i].usesAimLocomotion;

            AnimationClip idleClip = isHidden
                ? bodyClips.hideClip
                : (bodyClips.aimIdleClip != null ? bodyClips.aimIdleClip : bodyClips.hideClip);
            AnimationClip moveClip = usesAimLocomotion && bodyClips.aimRunClip != null
                ? bodyClips.aimRunClip
                : bodyClips.runClip;

            overrides.Clear();
            AddOverrideIfValid(overrides, baseBodyHideClip, idleClip);
            AddOverrideIfValid(overrides, baseBodyRunClip, moveClip);
            AddOverrideIfValid(overrides, baseBodyJumpClip, bodyClips.jumpClip);
            AddOverrideIfValid(overrides, baseBodyFallClip, bodyClips.fallClip);

            AnimatorOverrideController overrideController = new AnimatorOverrideController(baseBodyController);
            overrideController.name = $"{baseBodyController.name}_{weaponType}_Runtime";
            overrideController.ApplyOverrides(overrides);
            controllers[i] = overrideController;
        }

        return controllers;
    }

    /// <summary><paramref name="handClips"/>를 매핑한 비무장 손 <see cref="AnimatorOverrideController"/>를 생성한다.</summary>
    public RuntimeAnimatorController CreateHandController(in PropUnarmedHandClips handClips)
    {
        if (baseHandController == null) return null;

        List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new(4);
        AddOverrideIfValid(overrides, baseHandIdleClip, handClips.idleClip);
        AddOverrideIfValid(overrides, baseHandRunClip, handClips.runClip);
        AddOverrideIfValid(overrides, baseHandJumpClip, handClips.jumpClip);
        AddOverrideIfValid(overrides, baseHandFallClip, handClips.fallClip);

        AnimatorOverrideController overrideController = new AnimatorOverrideController(baseHandController);
        overrideController.name = $"{baseHandController.name}_Runtime";
        overrideController.ApplyOverrides(overrides);
        return overrideController;
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>기준 클립과 대상 클립이 유효하고 서로 다를 때만 오버라이드 목록에 추가한다.</summary>
    private static void AddOverrideIfValid(
        List<KeyValuePair<AnimationClip, AnimationClip>> overrides,
        AnimationClip baseClip,
        AnimationClip targetClip)
    {
        if (baseClip != null && targetClip != null && baseClip != targetClip)
        {
            overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(baseClip, targetClip));
        }
    }
}
