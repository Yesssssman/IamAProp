using System;
using UnityEngine;

/// <summary>월드 무기 픽업의 복합 물리 충돌을 구성하는 개별 <see cref="BoxCollider"/> 정의.</summary>
[Serializable]
public struct WeaponColliderBox
{
    /// <summary>무기 모델 로컬 공간 기준 중심 위치.</summary>
    public Vector3 center;

    /// <summary>무기 모델 로컬 공간 기준 박스 크기.</summary>
    public Vector3 size;

    /// <summary>무기 모델 로컬 공간 기준 오일러 회전각 (도 단위).</summary>
    public Vector3 rotation;
}

/// <summary>키 입력(1~5) 기준 무기 슬롯 카테고리.</summary>
public enum EWeaponSlotType : byte
{
    Unarmed = 0, // 1번: 맨손
    Melee = 1, // 2번: 근접무기
    Secondary = 2, // 3번: 보조무기
    Primary = 3, // 4번: 주무기
    Throwable = 4 // 5번: 투척/특수무기
}

/// <summary>개별 무기 인스턴스의 식별자와 탄창/예비 탄약 상태.</summary>
[Serializable]
public struct WeaponSlotState : IEquatable<WeaponSlotState>
{
    /// <summary>소지 중인 무기의 고유 식별자 (비어 있으면 빈 슬롯).</summary>
    public string WeaponId;

    /// <summary>현재 탄창에 남아 있는 탄약 수.</summary>
    public int CurrentAmmo;

    /// <summary>보유 중인 예비 탄약 수.</summary>
    public int ReserveAmmo;

    /// <summary>이 슬롯이 비어 있는지 여부.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(WeaponId);

    /// <summary>비어 있는 슬롯 기본값.</summary>
    public static WeaponSlotState Empty => new WeaponSlotState
    {
        WeaponId = string.Empty,
        CurrentAmmo = 0,
        ReserveAmmo = 0
    };

    public bool Equals(WeaponSlotState other)
    {
        return string.Equals(WeaponId, other.WeaponId, StringComparison.Ordinal)
            && CurrentAmmo == other.CurrentAmmo
            && ReserveAmmo == other.ReserveAmmo;
    }

    public override bool Equals(object obj)
    {
        return obj is WeaponSlotState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(WeaponId ?? string.Empty, CurrentAmmo, ReserveAmmo);
    }

    public static bool operator ==(WeaponSlotState left, WeaponSlotState right) => left.Equals(right);
    public static bool operator !=(WeaponSlotState left, WeaponSlotState right) => !left.Equals(right);
}

/// <summary>모든 클라이언트에 원자적으로 동기화되는 현재 장착 무기 스냅샷.</summary>
[Serializable]
public struct EquippedWeaponState : IEquatable<EquippedWeaponState>
{
    /// <summary>현재 선택된 무기 슬롯.</summary>
    public EWeaponSlotType Slot;

    /// <summary>현재 손에 장착된 무기의 고유 식별자 (맨손이면 빈 문자열).</summary>
    public string WeaponId;

    /// <summary>동일 슬롯·동일 무기 교체 시에도 변경 이벤트를 보장하는 순번.</summary>
    public ushort ChangeSequence;

    public bool Equals(EquippedWeaponState other)
    {
        return Slot == other.Slot
            && string.Equals(WeaponId, other.WeaponId, StringComparison.Ordinal)
            && ChangeSequence == other.ChangeSequence;
    }

    public override bool Equals(object obj)
    {
        return obj is EquippedWeaponState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine((byte)Slot, WeaponId ?? string.Empty, ChangeSequence);
    }

    public static bool operator ==(EquippedWeaponState left, EquippedWeaponState right) => left.Equals(right);
    public static bool operator !=(EquippedWeaponState left, EquippedWeaponState right) => !left.Equals(right);
}
