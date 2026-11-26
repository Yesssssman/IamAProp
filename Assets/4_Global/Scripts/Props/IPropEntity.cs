using UnityEngine;

/// <summary>네트워크 프롭 엔티티의 공통 상태와 데이터 계약.</summary>
public interface IPropEntity
{
    /// <summary>프롭의 종류.</summary>
    EPropType PropType { get; }

    /// <summary>프롭에 연결된 설정 데이터 에셋.</summary>
    PropDataSO PropData { get; }

    /// <summary>현재 프롭의 월드 공간 평면 이동 속도 벡터 (초당 월드 단위).</summary>
    Vector3 PlanarVelocity { get; }

    /// <summary>현재 무기 모드 기준 프롭의 최대 평면 이동 속력 (초당 월드 단위).</summary>
    float MoveSpeed { get; }

    /// <summary>최대 이동 속력 대비 정규화된 현재 평면 이동 속력 비율 (0~1).</summary>
    float PlanarSpeedRatio { get; }

    /// <summary>현재 물리 틱의 바닥 접촉 여부.</summary>
    bool IsGrounded { get; }

    /// <summary>맨손 정지 상태에서 루트 회전 제약이 해제된 쓰러짐 상태 여부.</summary>
    bool IsRagdollIdle { get; }

    /// <summary>현재 틱에 사격(조준) 상태인지 여부.</summary>
    bool IsFiring { get; }

    /// <summary>프롭이 사망 상태인지 여부.</summary>
    bool IsDead { get; }
}
