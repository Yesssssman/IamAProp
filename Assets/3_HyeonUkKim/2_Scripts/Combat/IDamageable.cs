using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

/// <summary>피격 시 공격 방향, 충돌 지점 및 무기 유형을 담는 컨텍스트.</summary>
public struct DamageHitInfo
{
    /// <summary>공격자 시점의 정규화된 타격 진행 방향.</summary>
    public Vector3 HitDirection;

    /// <summary>월드 공간 피격 충돌 지점.</summary>
    public Vector3 HitPoint;

    /// <summary><see cref="HitPoint"/>가 유효한 충돌 지점인지 여부.</summary>
    public bool HasHitPoint;

    /// <summary>근접 무기 공격에 의한 피격인지 여부.</summary>
    public bool IsMelee;

    /// <summary>피격을 가한 공격자의 <see cref="NetworkObject"/> (환경 피해·자폭 시 <c>null</c>).</summary>
    public NetworkObject AttackerObject;
}

/// <summary>서버 권위 데미지를 수신할 수 있는 피격 대상 계약.</summary>
public interface IDamageable
{
    /// <summary>현재 체력.</summary>
    float CurrentHealth { get; }

    /// <summary>최대 체력.</summary>
    float MaxHealth { get; }

    /// <summary>사망 여부.</summary>
    bool IsDead { get; }

    /// <summary>서버에서 <paramref name="damage"/>만큼 데미지를 적용한다.</summary>
    void TakeDamage(float damage, NetworkConnection attacker);

    /// <summary>서버에서 피격 정보(<paramref name="hitInfo"/>)와 함께 <paramref name="damage"/>만큼 데미지를 적용한다.</summary>
    void TakeDamage(float damage, NetworkConnection attacker, in DamageHitInfo hitInfo);
}
