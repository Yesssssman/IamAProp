using FishNet.Connection;

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
}
