using System;
using UnityEngine;

/// <summary>개별 프롭의 이동, 물리, 래그돌 및 전용 애니메이션 클립 데이터를 정의한다.</summary>
[CreateAssetMenu(fileName = "PropData", menuName = "IamAProp/Prop Data")]
public class PropDataSO : ScriptableObject
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>이 데이터가 나타내는 프롭 종류.</summary>
    [Header("식별 및 템플릿")]
    [SerializeField] private EPropType propType;

    /// <summary>모든 프롭이 공유하는 베이스 컨트롤러 및 무기 소켓 템플릿.</summary>
    [SerializeField] private PropTemplateSO propTemplate;

    /// <summary>평면 이동 속력 (초당 월드 단위).</summary>
    [Header("이동 및 점프 설정")]
    [SerializeField] private float moveSpeed = 5f;

    /// <summary>이동 방향 회전 감쇠 시간 (초 단위).</summary>
    [SerializeField] private float rotationSmoothTime = 0.1f;

    /// <summary>점프 시 가하는 수직 임펄스 크기.</summary>
    [SerializeField] private float jumpForce = 12f;

    /// <summary>맨손 정지 쓰러짐 진입 시 수평으로 밀어내는 임펄스 크기.</summary>
    [Header("래그돌 및 물리 설정")]
    [SerializeField] private float pushForce = 2.5f;

    /// <summary>맨손 정지 쓰러짐 진입 시 부여하는 초기 각속도 토크 크기.</summary>
    [SerializeField] private float ragdollToppleSpeed = 3.5f;

    /// <summary>맨손 정지 쓰러짐 진입 시 바닥 쪽으로 누르는 하향 힘 크기.</summary>
    [SerializeField] private float downwardForce = 0.5f;

    /// <summary>프롭 <see cref="Rigidbody"/> 질량.</summary>
    [SerializeField] private float rigidbodyMass = 3f;

    /// <summary>프롭 <see cref="Rigidbody"/> 선형 감쇠.</summary>
    [SerializeField] private float rigidbodyLinearDamping = 1f;

    /// <summary>프롭 <see cref="Rigidbody"/> 각감쇠.</summary>
    [SerializeField] private float rigidbodyAngularDamping = 0.1f;

    /// <summary>바닥 체크 구체의 반지름 (월드 단위).</summary>
    [Header("바닥 체크 설정")]
    [SerializeField] private float groundCheckRadius = 0.2f;

    /// <summary>바닥으로 판정할 물리 레이어 마스크.</summary>
    [SerializeField] private LayerMask groundLayer = 1 << 3;

    /// <summary>이 프롭 전용 몸통 애니메이션 클립 묶음.</summary>
    [Header("프롭 전용 애니메이션 클립")]
    [SerializeField] private PropBodyClips bodyClips;

    /// <summary>이 프롭 전용 비무장 손 애니메이션 클립 묶음.</summary>
    [SerializeField] private PropUnarmedHandClips unarmedHandClips;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>프롭 종류.</summary>
    public EPropType PropType => propType;

    /// <summary>공유 프롭 템플릿.</summary>
    public PropTemplateSO PropTemplate => propTemplate;

    /// <summary>평면 이동 속력 (초당 월드 단위).</summary>
    public float MoveSpeed => moveSpeed;

    /// <summary>이동 방향 회전 감쇠 시간 (초 단위).</summary>
    public float RotationSmoothTime => rotationSmoothTime;

    /// <summary>점프 수직 임펄스 크기.</summary>
    public float JumpForce => jumpForce;

    /// <summary>쓰러짐 수평 임펄스 크기.</summary>
    public float PushForce => pushForce;

    /// <summary>쓰러짐 토크 크기.</summary>
    public float RagdollToppleSpeed => ragdollToppleSpeed;

    /// <summary>쓰러짐 하향 힘 크기.</summary>
    public float DownwardForce => downwardForce;

    /// <summary>프롭 <see cref="Rigidbody"/> 질량.</summary>
    public float RigidbodyMass => rigidbodyMass;

    /// <summary>프롭 <see cref="Rigidbody"/> 선형 감쇠.</summary>
    public float RigidbodyLinearDamping => rigidbodyLinearDamping;

    /// <summary>프롭 <see cref="Rigidbody"/> 각감쇠.</summary>
    public float RigidbodyAngularDamping => rigidbodyAngularDamping;

    /// <summary>바닥 체크 구체 반지름 (월드 단위).</summary>
    public float GroundCheckRadius => groundCheckRadius;

    /// <summary>바닥 판정 레이어 마스크.</summary>
    public LayerMask GroundLayer => groundLayer;

    /// <summary>공유 템플릿의 무기 소켓 설정 목록.</summary>
    public WeaponSocketConfig[] WeaponSockets =>
        propTemplate != null ? propTemplate.WeaponSockets : Array.Empty<WeaponSocketConfig>();

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>이 프롭의 몸통 클립이 매핑된 무기 모드별 <see cref="RuntimeAnimatorController"/> 배열을 생성한다.</summary>
    public RuntimeAnimatorController[] CreateBodyControllers()
    {
        return propTemplate != null
            ? propTemplate.CreateBodyControllers(bodyClips)
            : Array.Empty<RuntimeAnimatorController>();
    }

    /// <summary>이 프롭의 비무장 손 클립이 매핑된 <see cref="RuntimeAnimatorController"/>를 생성한다.</summary>
    public RuntimeAnimatorController CreateHandController()
    {
        return propTemplate != null
            ? propTemplate.CreateHandController(unarmedHandClips)
            : null;
    }
}
