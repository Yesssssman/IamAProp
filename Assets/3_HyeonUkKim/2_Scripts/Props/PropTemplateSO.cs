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

    /// <summary>총기 조준 전방 달리기 상태 클립.</summary>
    public AnimationClip aimRunClip;

    /// <summary>총기 조준 후방 달리기 상태 클립.</summary>
    public AnimationClip aimRunBackwardClip;

    /// <summary>총기 조준 좌후방 달리기 상태 클립.</summary>
    public AnimationClip aimRunBackwardLeftClip;

    /// <summary>총기 조준 우후방 달리기 상태 클립.</summary>
    public AnimationClip aimRunBackwardRightClip;

    /// <summary>총기 조준 좌전방 달리기 상태 클립.</summary>
    public AnimationClip aimRunForwardLeftClip;

    /// <summary>총기 조준 우전방 달리기 상태 클립.</summary>
    public AnimationClip aimRunForwardRightClip;

    /// <summary>총기 조준 좌측 달리기 상태 클립.</summary>
    public AnimationClip aimRunLeftClip;

    /// <summary>총기 조준 우측 달리기 상태 클립.</summary>
    public AnimationClip aimRunRightClip;

    /// <summary>피격 1번 모션 클립.</summary>
    public AnimationClip hit1Clip;

    /// <summary>피격 2번 모션 클립.</summary>
    public AnimationClip hit2Clip;

    /// <summary>피격 3번 모션 클립.</summary>
    public AnimationClip hit3Clip;

    /// <summary>피격 4번 모션 클립.</summary>
    public AnimationClip hit4Clip;
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

/// <summary>손 베이스 컨트롤러의 슬롯별 무기 애니메이션 기준 클립 묶음.</summary>
[Serializable]
public struct WeaponSlotBaseHandClips
{
    /// <summary>드로우 상태 기준 클립.</summary>
    public AnimationClip drawClip;

    /// <summary>대기 또는 조준 상태 기준 클립.</summary>
    public AnimationClip idleClip;

    /// <summary>기본 공격 또는 발사 상태 기준 클립.</summary>
    public AnimationClip attackClip;

    /// <summary>2타 근접 공격 상태 기준 클립.</summary>
    public AnimationClip secondaryAttackClip;

    /// <summary>재장전 상태 기준 클립.</summary>
    public AnimationClip reloadClip;
}

/// <summary>무기 슬롯 및 하이드(맨손) 모드별 공통 평면 이동속도 설정 (초당 월드 단위).</summary>
[Serializable]
public struct PropMoveSpeedSettings
{
    /// <summary>1번 슬롯(기본 맨손 / 하이드 모드) 평면 이동속도 (초당 월드 단위).</summary>
    [Min(0f)]
    public float hideUnarmed;

    /// <summary>2번 슬롯(근접무기 모드) 평면 이동속도 (초당 월드 단위).</summary>
    [Min(0f)]
    public float melee;

    /// <summary>3번 슬롯(보조무기 모드) 평면 이동속도 (초당 월드 단위).</summary>
    [Min(0f)]
    public float secondary;

    /// <summary>4번 슬롯(주무기 모드) 평면 이동속도 (초당 월드 단위).</summary>
    [Min(0f)]
    public float primary;

    /// <summary>모드별 기본 평면 이동속도 프리셋.</summary>
    public static PropMoveSpeedSettings Default => new PropMoveSpeedSettings
    {
        hideUnarmed = 3.0f,
        melee = 3.0f,
        secondary = 2.4f,
        primary = 2.1f
    };

    /// <summary><paramref name="slot"/>에 해당하는 평면 이동속도를 반환한다.</summary>
    public float GetSpeed(EWeaponSlotType slot)
    {
        return slot switch
        {
            EWeaponSlotType.Unarmed => hideUnarmed,
            EWeaponSlotType.Melee => melee,
            EWeaponSlotType.Secondary => secondary,
            EWeaponSlotType.Primary => primary,
            _ => hideUnarmed
        };
    }
}

/// <summary>모든 프롭이 공유하는 베이스 애니메이터 컨트롤러, 기준 클립, 공통 이동속도 및 무기 소켓 로드아웃을 정의한다.</summary>
[CreateAssetMenu(fileName = "PropTemplate", menuName = "IamAProp/Prop Template")]
public class PropTemplateSO : ScriptableObject
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>모든 프롭이 공유하는 무기 슬롯 및 하이드 모드별 평면 이동속도 설정.</summary>
    [Header("공통 모드별 이동속도")]
    [SerializeField] private PropMoveSpeedSettings moveSpeeds = PropMoveSpeedSettings.Default;

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

    /// <summary>몸통 베이스 컨트롤러의 무장 대기 상태 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyAimIdleClip;

    /// <summary>몸통 베이스 컨트롤러의 조준 전방 달리기 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyAimRunClip;

    /// <summary>몸통 베이스 컨트롤러의 조준 후방 달리기 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyAimRunBackwardClip;

    /// <summary>몸통 베이스 컨트롤러의 조준 좌후방 달리기 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyAimRunBackwardLeftClip;

    /// <summary>몸통 베이스 컨트롤러의 조준 우후방 달리기 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyAimRunBackwardRightClip;

    /// <summary>몸통 베이스 컨트롤러의 조준 좌전방 달리기 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyAimRunForwardLeftClip;

    /// <summary>몸통 베이스 컨트롤러의 조준 우전방 달리기 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyAimRunForwardRightClip;

    /// <summary>몸통 베이스 컨트롤러의 조준 좌측 달리기 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyAimRunLeftClip;

    /// <summary>몸통 베이스 컨트롤러의 조준 우측 달리기 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyAimRunRightClip;

    /// <summary>몸통 베이스 컨트롤러의 피격 1번 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyHit1Clip;

    /// <summary>몸통 베이스 컨트롤러의 피격 2번 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyHit2Clip;

    /// <summary>몸통 베이스 컨트롤러의 피격 3번 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyHit3Clip;

    /// <summary>몸통 베이스 컨트롤러의 피격 4번 기준 클립.</summary>
    [SerializeField] private AnimationClip baseBodyHit4Clip;

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

    /// <summary><see cref="EWeaponSlotType"/> 순서에 대응하는 손 베이스 컨트롤러의 슬롯별 기준 클립.</summary>
    [Header("슬롯별 손 무기 오버라이드 기준 클립")]
    [SerializeField] private WeaponSlotBaseHandClips[] slotBaseHandClips;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>공통 모드별 평면 이동속도 설정.</summary>
    public PropMoveSpeedSettings MoveSpeeds => moveSpeeds;

    /// <summary>몸통 기본 애니메이터 컨트롤러.</summary>
    public RuntimeAnimatorController BaseBodyController => baseBodyController;

    /// <summary>손 기본 애니메이터 컨트롤러.</summary>
    public RuntimeAnimatorController BaseHandController => baseHandController;

    /// <summary>공통 무기 소켓 설정 배열.</summary>
    public WeaponSocketConfig[] WeaponSockets => weaponSockets;

    /// <summary>슬롯별 손 무기 오버라이드 기준 클립 배열.</summary>
    public WeaponSlotBaseHandClips[] SlotBaseHandClips => slotBaseHandClips;

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary><paramref name="slot"/>에 해당하는 공통 평면 이동속도를 반환한다.</summary>
    public float GetMoveSpeed(EWeaponSlotType slot) => moveSpeeds.GetSpeed(slot);

    /// <summary><paramref name="bodyClips"/>를 매핑한 무기 모드별 몸통 <see cref="AnimatorOverrideController"/> 배열을 생성한다.</summary>
    public RuntimeAnimatorController[] CreateBodyControllers(in PropBodyClips bodyClips)
    {
        if (baseBodyController == null) return Array.Empty<RuntimeAnimatorController>();

        int weaponCount = weaponSockets != null && weaponSockets.Length > 0
            ? weaponSockets.Length
            : Enum.GetValues(typeof(EWeaponType)).Length;

        RuntimeAnimatorController[] controllers = new RuntimeAnimatorController[weaponCount];
        List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new(17);

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
            AnimationClip aimMoveClip = bodyClips.aimRunClip != null
                ? bodyClips.aimRunClip
                : moveClip;

            overrides.Clear();
            AddOverrideIfValid(overrides, baseBodyHideClip, idleClip);
            AddOverrideIfValid(overrides, baseBodyRunClip, moveClip);
            AddOverrideIfValid(overrides, baseBodyJumpClip, bodyClips.jumpClip);
            AddOverrideIfValid(overrides, baseBodyFallClip, bodyClips.fallClip);
            AddOverrideIfValid(overrides, baseBodyAimIdleClip, idleClip);
            AddOverrideIfValid(overrides, baseBodyAimRunClip, aimMoveClip);
            AddOverrideIfValid(overrides, baseBodyAimRunBackwardClip, bodyClips.aimRunBackwardClip);
            AddOverrideIfValid(overrides, baseBodyAimRunBackwardLeftClip, bodyClips.aimRunBackwardLeftClip);
            AddOverrideIfValid(overrides, baseBodyAimRunBackwardRightClip, bodyClips.aimRunBackwardRightClip);
            AddOverrideIfValid(overrides, baseBodyAimRunForwardLeftClip, bodyClips.aimRunForwardLeftClip);
            AddOverrideIfValid(overrides, baseBodyAimRunForwardRightClip, bodyClips.aimRunForwardRightClip);
            AddOverrideIfValid(overrides, baseBodyAimRunLeftClip, bodyClips.aimRunLeftClip);
            AddOverrideIfValid(overrides, baseBodyAimRunRightClip, bodyClips.aimRunRightClip);
            AddOverrideIfValid(overrides, baseBodyHit1Clip, bodyClips.hit1Clip);
            AddOverrideIfValid(overrides, baseBodyHit2Clip, bodyClips.hit2Clip);
            AddOverrideIfValid(overrides, baseBodyHit3Clip, bodyClips.hit3Clip);
            AddOverrideIfValid(overrides, baseBodyHit4Clip, bodyClips.hit4Clip);

            AnimatorOverrideController overrideController = new AnimatorOverrideController(baseBodyController);
            overrideController.name = $"{baseBodyController.name}_{weaponType}_Runtime";
            overrideController.ApplyOverrides(overrides);
            controllers[i] = overrideController;
        }

        return controllers;
    }

    /// <summary><paramref name="handClips"/>를 매핑한 비무장 손 <see cref="AnimatorOverrideController"/>를 생성한다.</summary>
    public AnimatorOverrideController CreateHandController(in PropUnarmedHandClips handClips)
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
