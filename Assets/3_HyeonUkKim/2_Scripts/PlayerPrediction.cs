using FishNet.Object.Prediction;
using UnityEngine;

/// <summary>플레이어 예측 시뮬레이션에 전달할 네트워크 틱 입력 데이터.</summary>
public struct PlayerInputData : IReplicateData
{
    /// <summary>정규화 전 2D 이동 입력 벡터.</summary>
    public Vector2 MoveInput;

    /// <summary>입력 시점의 카메라 수평 회전 각도 (도 단위, 월드 Y축).</summary>
    public float CameraYaw;

    /// <summary>해당 틱에 점프가 요청되었는지 여부.</summary>
    public bool JumpRequested;

    /// <summary>해당 틱에 사격(조준) 상태인지 여부.</summary>
    public bool IsFiring;

    /// <summary>사격 중 캐릭터 몸체가 바라볼 목표 수평 회전 각도 (도 단위, 월드 Y축).</summary>
    public float AimYaw;

    /// <summary>FishNet 런타임이 할당하는 로컬 틱 번호.</summary>
    private uint _tick;

    /// <summary>지정한 입력 값으로 <see cref="PlayerInputData"/>를 초기화한다.</summary>
    public PlayerInputData(
        Vector2 moveInput,
        float cameraYaw,
        bool jumpRequested,
        bool isFiring = false,
        float aimYaw = 0f)
    {
        MoveInput = moveInput;
        CameraYaw = cameraYaw;
        JumpRequested = jumpRequested;
        IsFiring = isFiring;
        AimYaw = aimYaw;
        _tick = 0;
    }

    /// <inheritdoc/>
    public void Dispose() { }

    /// <inheritdoc/>
    public uint GetTick() => _tick;

    /// <inheritdoc/>
    public void SetTick(uint value) => _tick = value;
}

/// <summary>서버 권한 물리 상태를 클라이언트에 롤백 적용하기 위한 조정 데이터.</summary>
public struct PlayerReconcileData : IReconcileData
{
    /// <summary>시뮬레이션 직후 <see cref="Rigidbody"/>의 월드 위치.</summary>
    public Vector3 Position;

    /// <summary>시뮬레이션 직후 <see cref="Rigidbody"/>의 월드 회전.</summary>
    public Quaternion Rotation;

    /// <summary>시뮬레이션 직후 <see cref="Rigidbody"/>의 선형 속도 벡터 (초당 월드 단위).</summary>
    public Vector3 Velocity;

    /// <summary>시뮬레이션 직후 <see cref="Rigidbody"/>의 각속도 벡터 (초당 라디안).</summary>
    public Vector3 AngularVelocity;

    /// <summary>시뮬레이션 직후 월드 공간 평면 이동 속도 벡터 (초당 월드 단위).</summary>
    public Vector3 PlanarVelocity;

    /// <summary>시뮬레이션 직후 평면 이동 속력 (초당 월드 단위).</summary>
    public float PlanarSpeed;

    /// <summary>현재 물리 틱의 바닥 접촉 여부.</summary>
    public bool IsGrounded;

    /// <summary>직전 물리 틱의 바닥 접촉 여부.</summary>
    public bool WasGrounded;

    /// <summary>맨손 쓰러짐 대기 상태 활성화 여부.</summary>
    public bool IsRagdollIdle;

    /// <summary>현재 물리 틱의 사격(조준) 상태 활성화 여부.</summary>
    public bool IsFiring;

    /// <summary><see cref="Mathf.SmoothDampAngle"/> 내부 회전 각속도 상태값.</summary>
    public float TurnSmoothVelocity;

    /// <summary>직전 시뮬레이션 틱 시작 시점의 <see cref="Rigidbody"/> 월드 위치.</summary>
    public Vector3 LastTickPosition;

    /// <summary>FishNet 런타임이 할당하는 로컬 틱 번호.</summary>
    private uint _tick;

    /// <summary>지정한 물리 및 이동 상태로 <see cref="PlayerReconcileData"/>를 초기화한다.</summary>
    public PlayerReconcileData(
        Vector3 position,
        Quaternion rotation,
        Vector3 velocity,
        Vector3 angularVelocity,
        Vector3 planarVelocity,
        float planarSpeed,
        bool isGrounded,
        bool wasGrounded,
        bool isRagdollIdle,
        bool isFiring,
        float turnSmoothVelocity,
        Vector3 lastTickPosition)
    {
        Position = position;
        Rotation = rotation;
        Velocity = velocity;
        AngularVelocity = angularVelocity;
        PlanarVelocity = planarVelocity;
        PlanarSpeed = planarSpeed;
        IsGrounded = isGrounded;
        WasGrounded = wasGrounded;
        IsRagdollIdle = isRagdollIdle;
        IsFiring = isFiring;
        TurnSmoothVelocity = turnSmoothVelocity;
        LastTickPosition = lastTickPosition;
        _tick = 0;
    }

    /// <inheritdoc/>
    public void Dispose() { }

    /// <inheritdoc/>
    public uint GetTick() => _tick;

    /// <inheritdoc/>
    public void SetTick(uint value) => _tick = value;
}
