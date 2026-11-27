using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>모드 설정에 따라 바닥 드롭 무기의 수명 소멸(데스매치 전용)과 매치 종료 시 투척체 정리를 서버에서 관리한다.</summary>
public class WorldItemLifecycleManager : NetworkBehaviour
{
    // ***************************************************************
    // Types
    // ***************************************************************

    private struct TrackedPickup
    {
        public WorldWeaponPickup Pickup;
        public float ExpireTime;
    }

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>자동 소멸 타이머가 부여된 월드 무기 픽업 목록 (서버 전용).</summary>
    private readonly List<TrackedPickup> _trackedPickups = new(32);

    /// <summary>초기 씬 배치 픽업 등록을 1회만 수행했는지 여부.</summary>
    private bool _hasRegisteredInitialPickups;

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartServer()
    {
        _trackedPickups.Clear();
        _hasRegisteredInitialPickups = false;
        PlayerWeaponInventory.OnServerPickupSpawned += HandleServerPickupSpawned;
    }

    public override void OnStopServer()
    {
        PlayerWeaponInventory.OnServerPickupSpawned -= HandleServerPickupSpawned;
        _trackedPickups.Clear();
        _hasRegisteredInitialPickups = false;
    }

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Update()
    {
        if (NetworkObject == null || !IsServerInitialized) return;

        if (!_hasRegisteredInitialPickups && MatchManager.Instance != null && MatchManager.Instance.ActiveConfig != null)
        {
            _hasRegisteredInitialPickups = true;
            GameModeConfigSO config = MatchManager.Instance.ActiveConfig;
            if (config.DespawnInitialScenePickups && config.InitialScenePickupLifetimeSeconds > 0f)
            {
                WorldWeaponPickup[] scenePickups = FindObjectsByType<WorldWeaponPickup>();
                float expireAt = Time.time + config.InitialScenePickupLifetimeSeconds;
                for (int i = 0; i < scenePickups.Length; i++)
                {
                    WorldWeaponPickup pickup = scenePickups[i];
                    if (pickup == null || !pickup.IsSpawned || pickup.IsConsumed) continue;
                    _trackedPickups.Add(new TrackedPickup { Pickup = pickup, ExpireTime = expireAt });
                }
            }
        }

        if (_trackedPickups.Count == 0) return;

        float now = Time.time;
        for (int i = _trackedPickups.Count - 1; i >= 0; i--)
        {
            TrackedPickup entry = _trackedPickups[i];
            if (entry.Pickup == null || !entry.Pickup.IsSpawned || entry.Pickup.IsConsumed)
            {
                _trackedPickups.RemoveAt(i);
                continue;
            }

            if (now >= entry.ExpireTime)
            {
                ServerManager.Despawn(entry.Pickup.NetworkObject, DespawnType.Destroy);
                _trackedPickups.RemoveAt(i);
            }
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>매치 종료 시 비행·대기 중인 모든 수류탄/섬광탄 투척체를 서버에서 즉시 정리한다.</summary>
    [Server]
    public void CleanupActiveProjectiles()
    {
        if (!IsServerInitialized) return;

        NetworkGrenade[] grenades = FindObjectsByType<NetworkGrenade>();
        for (int i = 0; i < grenades.Length; i++)
        {
            NetworkGrenade grenade = grenades[i];
            if (grenade != null && grenade.IsSpawned)
            {
                ServerManager.Despawn(grenade.NetworkObject, DespawnType.Destroy);
            }
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>서버에서 동적 드롭 무기가 스폰될 때 현재 모드가 자동 소멸 정책을 켜 둔 경우에만 타이머를 등록한다.</summary>
    private void HandleServerPickupSpawned(WorldWeaponPickup pickup)
    {
        if (!IsServerInitialized || pickup == null || !pickup.IsSpawned) return;

        GameModeConfigSO config = MatchManager.Instance != null ? MatchManager.Instance.ActiveConfig : null;
        if (config == null || !config.DespawnDroppedWeapons) return;

        float lifetime = Mathf.Max(1f, config.DroppedWeaponLifetimeSeconds);
        _trackedPickups.Add(new TrackedPickup
        {
            Pickup = pickup,
            ExpireTime = Time.time + lifetime
        });
    }
}
