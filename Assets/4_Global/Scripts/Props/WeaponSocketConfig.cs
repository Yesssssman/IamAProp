using System;
using UnityEngine;

/// <summary>플레이어 무기 장착 상태.</summary>
public enum EWeaponType
{
    Hide = 0,
    KnifeMode = 1,
    Pistol = 2,
    Ak = 3,
    Grenade = 4
}

/// <summary>무기 종류별 장착 프리팹, 애니메이터 컨트롤러, 그립 오프셋 및 재장전 클립 설정.</summary>
[Serializable]
public struct WeaponSocketConfig
{
    /// <summary>오른손 소켓에 장착할 무기 프리팹.</summary>
    public GameObject weaponPrefab;

    /// <summary>장착된 무기 오브젝트의 <see cref="Animator"/>에 할당할 컨트롤러.</summary>
    public RuntimeAnimatorController weaponAnimController;

    /// <summary>오른손 본 기준 무기 로컬 위치.</summary>
    public Vector3 gripPosition;

    /// <summary>오른손 본 기준 무기 로컬 오일러 회전각 (도 단위).</summary>
    public Vector3 gripRotation;

    /// <summary>오른손 본 기준 무기 로컬 스케일.</summary>
    public Vector3 gripScale;

    /// <summary>왼손 예비 탄창 위치를 샘플링할 손 재장전 애니메이션 클립.</summary>
    public AnimationClip reloadClip;

    /// <summary>장착 상태에서 이동 시 조준 달리기 클립(<c>AimRun</c>)을 사용하는지 여부.</summary>
    public bool usesAimLocomotion;

    /// <summary>이 무기 슬롯의 전투 수치 및 탄약 설정 데이터.</summary>
    public WeaponDataSO weaponData;
}
