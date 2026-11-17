using Unity.Netcode;
using UnityEngine;

/// <summary>특정 네트워크 틱의 플레이어 입력 스냅샷.</summary>
public struct PlayerInputPayload : INetworkSerializable
{
    /// <summary>입력이 샘플링된 네트워크 틱 번호.</summary>
    public int Tick;

    /// <summary>정규화 전 2D 이동 입력 벡터.</summary>
    public Vector2 MoveInput;

    /// <summary>입력 시점의 카메라 수평 회전 각도 (도 단위, 월드 Y축).</summary>
    public float CameraYaw;

    /// <summary>해당 틱에 점프가 요청되었는지 여부.</summary>
    public bool JumpRequested;

    /// <summary>네트워크 버퍼에 입력 페이로드를 직렬화한다.</summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref MoveInput);
        serializer.SerializeValue(ref CameraYaw);
        serializer.SerializeValue(ref JumpRequested);
    }
}

/// <summary>특정 네트워크 틱의 플레이어 시뮬레이션 결과 상태 스냅샷.</summary>
public struct PlayerStatePayload : INetworkSerializable
{
    /// <summary>시뮬레이션이 수행된 네트워크 틱 번호.</summary>
    public int Tick;

    /// <summary>시뮬레이션 직후 월드 위치.</summary>
    public Vector3 Position;

    /// <summary>시뮬레이션 직후 수평 회전 각도 (도 단위, 월드 Y축).</summary>
    public float RotationY;

    /// <summary>시뮬레이션 직후 월드 속도 벡터 (초당 월드 단위).</summary>
    public Vector3 Velocity;

    /// <summary>시뮬레이션 직후 바닥 접촉 여부.</summary>
    public bool IsGrounded;

    /// <summary>네트워크 버퍼에 상태 페이로드를 직렬화한다.</summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Tick);
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref RotationY);
        serializer.SerializeValue(ref Velocity);
        serializer.SerializeValue(ref IsGrounded);
    }
}
