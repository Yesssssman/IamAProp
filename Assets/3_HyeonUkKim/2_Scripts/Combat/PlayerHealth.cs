using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>서버 권위 기반으로 플레이어 체력을 동기화하고 피격 데미지를 처리한다.</summary>
public class PlayerHealth : NetworkBehaviour, IDamageable
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float DefaultMaxHealth = 100f;

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>초기 최대 체력과 방어 스탯을 제공하는 플레이어 스탯 에셋.</summary>
    [Header("플레이어 스탯")]
    [SerializeField] private PlayerStatsSO playerStats;

    /// <summary>모든 클라이언트에 동기화되는 현재 체력.</summary>
    private readonly SyncVar<float> _currentHealth = new();

    // ***************************************************************
    // Events
    // ***************************************************************

    /// <summary>체력이 변경될 때 (현재 체력, 최대 체력)을 알리는 이벤트.</summary>
    public event Action<float, float> OnHealthChanged;

    /// <summary>서버에서 실제 데미지가 적용된 직후 (최종 데미지, 공격자 연결)을 알리는 이벤트.</summary>
    public event Action<float, NetworkConnection> OnDamaged;

    /// <summary>서버에서 체력이 0에 도달했을 때 공격자 연결과 함께 발생하는 이벤트.</summary>
    public event Action<NetworkConnection> OnDied;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <inheritdoc/>
    public float CurrentHealth => _currentHealth.Value;

    /// <inheritdoc/>
    public float MaxHealth => playerStats != null ? playerStats.MaxHealth : DefaultMaxHealth;

    /// <inheritdoc/>
    public bool IsDead => _currentHealth.Value <= 0f;

    /// <summary>할당된 플레이어 스탯 데이터.</summary>
    public PlayerStatsSO PlayerStats => playerStats;

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartServer()
    {
        _currentHealth.Value = MaxHealth;
    }

    public override void OnStartNetwork()
    {
        _currentHealth.OnChange += HandleHealthChanged;
        OnHealthChanged?.Invoke(_currentHealth.Value, MaxHealth);
    }

    public override void OnStopNetwork()
    {
        _currentHealth.OnChange -= HandleHealthChanged;
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <inheritdoc/>
    [Server]
    public void TakeDamage(float damage, NetworkConnection attacker)
    {
        if (damage <= 0f || IsDead) return;

        float reduction = playerStats != null ? Mathf.Clamp01(playerStats.DamageReduction) : 0f;
        float finalDamage = damage * (1f - reduction);
        if (finalDamage <= 0f) return;

        _currentHealth.Value = Mathf.Max(0f, _currentHealth.Value - finalDamage);

        int attackerId = attacker != null ? attacker.ClientId : -1;
        Debug.Log($"[PlayerHealth] {name}(Owner:{OwnerId}) 피격: -{finalDamage:F1} (체력: {_currentHealth.Value:F1}/{MaxHealth:F1}, 공격자 ClientId:{attackerId})");

        OnDamaged?.Invoke(finalDamage, attacker);

        if (_currentHealth.Value <= 0f)
        {
            Debug.Log($"[PlayerHealth] {name}(Owner:{OwnerId}) 사망 (공격자 ClientId:{attackerId})");
            OnDied?.Invoke(attacker);
        }
    }

    /// <summary>서버에서 현재 체력을 최대 체력으로 초기화한다.</summary>
    [Server]
    public void ResetHealth()
    {
        _currentHealth.Value = MaxHealth;
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary><see cref="_currentHealth"/> 변경 시 구독자에게 최신 체력을 전달한다.</summary>
    private void HandleHealthChanged(float previousValue, float nextValue, bool asServer)
    {
        if (!asServer && IsServerInitialized) return;
        OnHealthChanged?.Invoke(nextValue, MaxHealth);
    }
}
