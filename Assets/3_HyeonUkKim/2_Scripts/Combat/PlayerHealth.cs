using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

/// <summary>서버 권위 기반으로 플레이어 체력과 사망 상태를 동기화하고 피격 데미지를 처리한다.</summary>
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

    /// <summary>모든 클라이언트에 동기화되는 사망 상태.</summary>
    private readonly SyncVar<bool> _isDead = new(new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>서버에서 마지막 치명타를 입힌 킬러의 <see cref="NetworkObject"/>.</summary>
    private NetworkObject _lastKiller;

    // ***************************************************************
    // Events
    // ***************************************************************

    /// <summary>체력이 변경될 때 (현재 체력, 최대 체력)을 알리는 이벤트.</summary>
    public event Action<float, float> OnHealthChanged;

    /// <summary>서버에서 실제 데미지가 적용된 직후 (최종 데미지, 공격자 연결)을 알리는 이벤트.</summary>
    public event Action<float, NetworkConnection> OnDamaged;

    /// <summary>서버에서 체력이 0에 도달했을 때 공격자 연결과 함께 발생하는 이벤트.</summary>
    public event Action<NetworkConnection> OnDied;

    /// <summary>사망 확정 시 치명타 피격 정보와 함께 서버 및 모든 클라이언트에서 발생하는 이벤트.</summary>
    public event Action<DamageHitInfo> OnDeathEffectsTriggered;

    /// <summary>사망 상태가 전환될 때 최신 사망 여부를 알리는 이벤트.</summary>
    public event Action<bool> OnDeadStateChanged;

    /// <summary>로컬 소유자 클라이언트에서 사망 확정 시 킬러 <see cref="NetworkObject"/>를 전달하는 이벤트.</summary>
    public event Action<NetworkObject> OnOwnerDiedWithKiller;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <inheritdoc/>
    public float CurrentHealth => _currentHealth.Value;

    /// <inheritdoc/>
    public float MaxHealth => playerStats != null ? playerStats.MaxHealth : DefaultMaxHealth;

    /// <inheritdoc/>
    public bool IsDead => _isDead.Value;

    /// <summary>할당된 플레이어 스탯 데이터.</summary>
    public PlayerStatsSO PlayerStats => playerStats;

    /// <summary>서버에 기록된 마지막 치명타 킬러 <see cref="NetworkObject"/>.</summary>
    public NetworkObject LastKiller => _lastKiller;

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartServer()
    {
        _currentHealth.Value = MaxHealth;
        _isDead.Value = false;
        _lastKiller = null;
    }

    public override void OnStartNetwork()
    {
        _currentHealth.OnChange += HandleHealthChanged;
        _isDead.OnChange += HandleIsDeadChanged;
        OnHealthChanged?.Invoke(_currentHealth.Value, MaxHealth);
    }

    public override void OnStopNetwork()
    {
        _currentHealth.OnChange -= HandleHealthChanged;
        _isDead.OnChange -= HandleIsDeadChanged;
        _lastKiller = null;
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <inheritdoc/>
    [Server]
    public void TakeDamage(float damage, NetworkConnection attacker)
    {
        DamageHitInfo defaultHitInfo = default;
        TakeDamage(damage, attacker, in defaultHitInfo);
    }

    /// <inheritdoc/>
    [Server]
    public void TakeDamage(float damage, NetworkConnection attacker, in DamageHitInfo hitInfo)
    {
        if (damage <= 0f || IsDead) return;

        float reduction = playerStats != null ? Mathf.Clamp01(playerStats.DamageReduction) : 0f;
        float finalDamage = damage * (1f - reduction);
        if (finalDamage <= 0f) return;

        float nextHealth = Mathf.Max(0f, _currentHealth.Value - finalDamage);
        _currentHealth.Value = nextHealth;
        bool diedNow = nextHealth <= 0f;
        if (diedNow)
        {
            _isDead.Value = true;
        }

        int attackerId = attacker != null ? attacker.ClientId : -1;
        Debug.Log($"[PlayerHealth] {name}(Owner:{OwnerId}) 피격: -{finalDamage:F1} (체력: {_currentHealth.Value:F1}/{MaxHealth:F1}, 공격자 ClientId:{attackerId})");

        OnDamaged?.Invoke(finalDamage, attacker);

        if (diedNow)
        {
            NetworkObject killerObject = hitInfo.AttackerObject;
            if (killerObject == null && attacker != null && attacker.IsValid)
            {
                foreach (NetworkObject ownedObject in attacker.Objects)
                {
                    if (ownedObject != null && ownedObject.GetComponent<PlayerController>() != null)
                    {
                        killerObject = ownedObject;
                        break;
                    }
                }

                if (killerObject == null)
                {
                    killerObject = attacker.FirstObject;
                }
            }

            if (killerObject == NetworkObject)
            {
                killerObject = null;
            }

            _lastKiller = killerObject;

            Debug.Log($"[PlayerHealth] {name}(Owner:{OwnerId}) 사망 (공격자 ClientId:{attackerId})");
            OnDied?.Invoke(attacker);
            OnDeathEffectsTriggered?.Invoke(hitInfo);
            BroadcastDeathObserversRpc(hitInfo.HitDirection, hitInfo.HitPoint, hitInfo.HasHitPoint, hitInfo.IsMelee);

            if (Owner != null && Owner.IsValid)
            {
                NotifyOwnerDeathTargetRpc(Owner, killerObject);
            }
        }
    }

    /// <summary>서버에서 체력을 최대 체력으로 복원하고 사망 상태를 해제한다.</summary>
    [Server]
    public void ResetHealth()
    {
        _currentHealth.Value = MaxHealth;
        _isDead.Value = false;
        _lastKiller = null;
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>사망한 플레이어의 소유자 클라이언트에만 킬러 <see cref="NetworkObject"/> 정보를 전달한다.</summary>
    [TargetRpc]
    private void NotifyOwnerDeathTargetRpc(NetworkConnection target, NetworkObject killerNetworkObject)
    {
        OnOwnerDiedWithKiller?.Invoke(killerNetworkObject);
    }

    /// <summary>모든 원격 클라이언트에 치명타 피격 정보와 사망 연출을 전파한다.</summary>
    [ObserversRpc]
    private void BroadcastDeathObserversRpc(Vector3 hitDirection, Vector3 hitPoint, bool hasHitPoint, bool isMelee)
    {
        if (IsServerInitialized) return;

        DamageHitInfo hitInfo = new DamageHitInfo
        {
            HitDirection = hitDirection,
            HitPoint = hitPoint,
            HasHitPoint = hasHitPoint,
            IsMelee = isMelee
        };
        OnDeathEffectsTriggered?.Invoke(hitInfo);
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

    /// <summary><see cref="_isDead"/> 변경 시 구독자에게 사망 상태 전환을 전달한다.</summary>
    private void HandleIsDeadChanged(bool previousValue, bool nextValue, bool asServer)
    {
        if (!asServer && IsServerInitialized) return;
        if (previousValue == nextValue) return;

        OnDeadStateChanged?.Invoke(nextValue);
    }
}
