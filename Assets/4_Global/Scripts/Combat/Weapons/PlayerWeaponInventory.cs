using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

/// <summary>플레이어의 5슬롯 무기 보유 현황, 장착 전환 및 서버 권위 줍기/버리기를 관리한다.</summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerWeaponInventory : NetworkBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    /// <summary>1~5번 키 입력에 대응하는 총 무기 슬롯 수.</summary>
    public const int SlotCount = 5;

    private const float ServerPickupRangeTolerance = 0.25f; // 네트워크 지연을 감안한 서버 줍기 거리 허용 오차 (미터)
    private const float CrosshairCameraRangeBonus = 6.0f; // TPS 카메라 후방 거리를 보정하기 위한 크로스헤어 탐색 여유 거리 (미터)
    private const float DropWallMargin = 0.2f; // 벽 앞에서 무기를 버릴 때 관통을 막는 여유 거리 (미터)

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>무기 데이터 조회 및 드롭 프리팹 생성에 사용할 데이터베이스.</summary>
    [Header("무기 데이터베이스")]
    [SerializeField] private WeaponDatabaseSO weaponDatabase;

    /// <summary>바닥의 월드 무기 아이템을 주울 수 있는 최대 표면 간 거리 (미터).</summary>
    [Header("줍기 및 버리기 설정")]
    [Min(0.05f)]
    [SerializeField] private float pickupRange = 0.2f;

    /// <summary>크로스헤어 조준 줍기 구체 캐스트 반지름 (미터).</summary>
    [Min(0.05f)]
    [SerializeField] private float crosshairPickupRadius = 0.25f;

    /// <summary>무기를 버릴 때 플레이어 가슴/손 높이 기준 전방으로 띄울 스폰 거리 (미터).</summary>
    [Min(0f)]
    [SerializeField] private float dropForwardOffset = 0.65f;

    /// <summary>무기를 버릴 때 플레이어 가슴/손 높이 기준 위로 띄울 오프셋 (미터).</summary>
    [Min(0f)]
    [SerializeField] private float dropUpwardOffset = 0.1f;

    /// <summary>무기를 버릴 때 전방으로 가할 임펄스 힘 크기 (<see cref="ForceMode.Impulse"/>).</summary>
    [Min(0f)]
    [SerializeField] private float dropForwardForce = 1.6f;

    /// <summary>무기를 버릴 때 상방으로 가할 임펄스 힘 크기 (<see cref="ForceMode.Impulse"/>).</summary>
    [Min(0f)]
    [SerializeField] private float dropUpwardForce = 0.8f;

    /// <summary>무기를 버릴 때 각 축에 무작위로 가할 회전 토크 최대 크기 (<see cref="ForceMode.VelocityChange"/>, rad/s).</summary>
    [Min(0f)]
    [SerializeField] private float dropRandomTorque = 2.5f;

    /// <summary>무기 드롭 시 벽 관통 여부를 검사할 지형 레이어 마스크.</summary>
    [SerializeField] private LayerMask dropObstacleMask = (1 << 0) | (1 << 3) | (1 << 8); // Default, Ground, Wall

    /// <summary>서버와 소유자 간 동기화되는 5슬롯 무기 및 개별 탄약 목록.</summary>
    private readonly SyncList<WeaponSlotState> _slots = new(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly, 0f, Channel.Reliable));

    /// <summary>서버와 소유자 간 동기화되는 보유 투척무기 종류별 수량 목록.</summary>
    private readonly SyncList<WeaponSlotState> _throwableItems = new(
        new SyncTypeSettings(WritePermission.ServerOnly, ReadPermission.OwnerOnly, 0f, Channel.Reliable));

    /// <summary>호스트와 모든 클라이언트에 동기화되는 현재 장착 무기 스냅샷.</summary>
    private readonly SyncVar<EquippedWeaponState> _equippedState = new(
        new EquippedWeaponState { Slot = EWeaponSlotType.Unarmed, WeaponId = string.Empty, ChangeSequence = 0 },
        new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>서버에서 현재 5번 슬롯에 선택된 <see cref="_throwableItems"/> 인덱스.</summary>
    private int _activeThrowableIndex;

    /// <summary>조준 원점 및 전방 방향을 제공하는 플레이어 컨트롤러.</summary>
    private PlayerController _playerController;

    /// <summary>생존 여부를 조회하는 체력 컴포넌트.</summary>
    private PlayerHealth _playerHealth;

    /// <summary>줍기 거리 계산에 사용하는 플레이어 본체 콜라이더.</summary>
    private Collider _bodyCollider;

    /// <summary>주변 월드 무기 탐색용 콜라이더 재사용 버퍼.</summary>
    private readonly Collider[] _pickupOverlapBuffer = new Collider[24];

    /// <summary>크로스헤어 줍기 구체 캐스트 결과 재사용 버퍼.</summary>
    private readonly RaycastHit[] _pickupRayBuffer = new RaycastHit[16];

    // ***************************************************************
    // Events
    // ***************************************************************

    /// <summary>현재 장착 중인 무기 상태가 변경될 때 발생하는 이벤트.</summary>
    public event Action<EquippedWeaponState> OnEquippedWeaponChanged;

    /// <summary>특정 슬롯의 보유 무기 또는 탄약 상태가 변경될 때 발생하는 이벤트.</summary>
    public event Action<EWeaponSlotType, WeaponSlotState> OnSlotUpdated;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>연결된 무기 데이터베이스.</summary>
    public WeaponDatabaseSO WeaponDatabase => weaponDatabase;

    /// <summary>현재 동기화된 장착 무기 스냅샷.</summary>
    public EquippedWeaponState EquippedState => _equippedState.Value;

    /// <summary>현재 활성화된 무기 슬롯.</summary>
    public EWeaponSlotType ActiveSlot => _equippedState.Value.Slot;

    /// <summary>현재 손에 장착된 무기의 고유 식별자.</summary>
    public string EquippedWeaponId => _equippedState.Value.WeaponId;

    /// <summary>현재 손에 장착된 무기의 <see cref="WeaponDataSO"/> 에셋.</summary>
    public WeaponDataSO EquippedWeaponData => GetWeaponDataById(_equippedState.Value.WeaponId);

    /// <summary>2종류 이상의 투척무기를 보유 중이어서 5번 키 재입력으로 순환 스왑할 수 있는지 여부.</summary>
    public bool CanCycleThrowable => _throwableItems.Count > 1;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _playerController = GetComponent<PlayerController>();
        _playerHealth = GetComponent<PlayerHealth>();
        _bodyCollider = GetComponentInChildren<Collider>();
    }

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartServer()
    {
        _slots.Clear();
        _throwableItems.Clear();
        _activeThrowableIndex = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            _slots.Add(WeaponSlotState.Empty);
        }

        // 스폰 시 2번 근접 슬롯에만 기본 Knife를 지급하고 장착 상태로 시작한다.
        WeaponDataSO defaultMelee = weaponDatabase != null ? weaponDatabase.DefaultMeleeWeapon : null;
        if (defaultMelee != null && !string.IsNullOrEmpty(defaultMelee.WeaponId))
        {
            int meleeIndex = (int)EWeaponSlotType.Melee;
            _slots[meleeIndex] = new WeaponSlotState
            {
                WeaponId = defaultMelee.WeaponId,
                CurrentAmmo = defaultMelee.UsesAmmo ? defaultMelee.MaxAmmo : 0,
                ReserveAmmo = defaultMelee.UsesAmmo ? defaultMelee.MaxReserveAmmo : 0
            };

            _equippedState.Value = new EquippedWeaponState
            {
                Slot = EWeaponSlotType.Melee,
                WeaponId = defaultMelee.WeaponId,
                ChangeSequence = 1
            };
        }
        else
        {
            _equippedState.Value = new EquippedWeaponState
            {
                Slot = EWeaponSlotType.Unarmed,
                WeaponId = string.Empty,
                ChangeSequence = 1
            };
        }
        // 스폰 시 5번 투척 슬롯에 설정된 기본 투척무기와 초기 보유 개수를 지급한다.
        WeaponDataSO defaultThrowable = weaponDatabase != null ? weaponDatabase.DefaultThrowableWeapon : null;
        int initialThrowableCount = weaponDatabase != null ? weaponDatabase.InitialThrowableCount : 0;
        if (defaultThrowable != null
            && !string.IsNullOrEmpty(defaultThrowable.WeaponId)
            && initialThrowableCount > 0)
        {
            int maxStack = defaultThrowable.MaxAmmo > 0 ? defaultThrowable.MaxAmmo : initialThrowableCount;
            _throwableItems.Add(new WeaponSlotState
            {
                WeaponId = defaultThrowable.WeaponId,
                CurrentAmmo = Mathf.Clamp(initialThrowableCount, 1, maxStack),
                ReserveAmmo = 0
            });
            ApplyActiveThrowableSelection(0, equipThrowableSlot: false, forceSequenceBump: false);
        }

        if (_playerHealth != null)
        {
            _playerHealth.OnDied += HandleServerDied;
        }
    }

    public override void OnStopServer()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnDied -= HandleServerDied;
        }
    }

    public override void OnStartNetwork()
    {
        _equippedState.OnChange += HandleEquippedStateChanged;
        _slots.OnChange += HandleSlotsChanged;
        OnEquippedWeaponChanged?.Invoke(_equippedState.Value);
    }

    public override void OnStopNetwork()
    {
        _equippedState.OnChange -= HandleEquippedStateChanged;
        _slots.OnChange -= HandleSlotsChanged;
    }

    // ***************************************************************
    // Public Query API
    // ***************************************************************

    /// <summary><paramref name="slot"/>에 소지 중인 무기가 있는지 여부를 반환한다 (맨손 슬롯은 항상 참).</summary>
    public bool HasWeaponInSlot(EWeaponSlotType slot)
    {
        if (slot == EWeaponSlotType.Unarmed) return true;

        int index = (int)slot;
        if (index < 0 || index >= _slots.Count || _slots[index].IsEmpty) return false;
        if (slot == EWeaponSlotType.Throwable && _slots[index].CurrentAmmo <= 0) return false;

        return true;
    }

    /// <summary><paramref name="slot"/>의 현재 보유 무기 및 탄약 상태를 반환한다.</summary>
    public WeaponSlotState GetSlotState(EWeaponSlotType slot)
    {
        int index = (int)slot;
        if (index < 0 || index >= _slots.Count) return WeaponSlotState.Empty;
        return _slots[index];
    }

    /// <summary>보유 중인 투척무기 목록에서 <paramref name="weaponId"/>의 수량 상태를 반환한다.</summary>
    public WeaponSlotState GetThrowableState(string weaponId)
    {
        int idx = FindThrowableIndex(weaponId);
        return idx >= 0 ? _throwableItems[idx] : WeaponSlotState.Empty;
    }

    /// <summary><paramref name="slot"/>에 보유 중인 무기의 <see cref="WeaponDataSO"/>를 반환한다.</summary>
    public WeaponDataSO GetWeaponDataInSlot(EWeaponSlotType slot)
    {
        if (slot == EWeaponSlotType.Unarmed) return null;

        if (slot == _equippedState.Value.Slot && !string.IsNullOrEmpty(_equippedState.Value.WeaponId))
        {
            return GetWeaponDataById(_equippedState.Value.WeaponId);
        }

        WeaponSlotState state = GetSlotState(slot);
        return state.IsEmpty ? null : GetWeaponDataById(state.WeaponId);
    }

    /// <summary><paramref name="weaponId"/>에 해당하는 <see cref="WeaponDataSO"/>를 반환한다.</summary>
    public WeaponDataSO GetWeaponDataById(string weaponId)
    {
        if (weaponDatabase == null || string.IsNullOrEmpty(weaponId)) return null;
        return weaponDatabase.GetById(weaponId);
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>소유자가 지정한 <paramref name="slot"/>으로 무기 전환(또는 5번 투척무기 순환 스왑)을 요청한다.</summary>
    public bool TrySelectSlot(EWeaponSlotType slot)
    {
        if (!IsOwner || !IsSpawned) return false;
        if (_playerHealth != null && _playerHealth.IsDead) return false;
        if (slot != EWeaponSlotType.Unarmed && !HasWeaponInSlot(slot)) return false;

        if (_equippedState.Value.Slot == slot)
        {
            if (slot != EWeaponSlotType.Throwable || !CanCycleThrowable) return false;
        }

        RequestSelectSlotServerRpc(slot);
        return true;
    }

    /// <summary>소유자가 크로스헤어 또는 주변 범위 내 가장 가까운 월드 무기의 획득을 서버에 요청한다.</summary>
    public void TryPickupNearbyWeapon()
    {
        if (!IsOwner || !IsSpawned) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;

        WorldWeaponPickup bestPickup = null;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int rayHits = Physics.SphereCastNonAlloc(
                ray,
                crosshairPickupRadius,
                _pickupRayBuffer,
                pickupRange + CrosshairCameraRangeBonus,
                ~0,
                QueryTriggerInteraction.Collide);

            float closestRayDist = float.MaxValue;
            for (int i = 0; i < rayHits; i++)
            {
                RaycastHit hit = _pickupRayBuffer[i];
                if (hit.collider == null) continue;

                WorldWeaponPickup candidate = hit.collider.GetComponentInParent<WorldWeaponPickup>();
                if (candidate == null || !candidate.IsSpawned || string.IsNullOrEmpty(candidate.WeaponId)) continue;
                if (GetPickupDistance(candidate) > pickupRange) continue;

                if (hit.distance < closestRayDist)
                {
                    closestRayDist = hit.distance;
                    bestPickup = candidate;
                }
            }
        }

        if (bestPickup == null)
        {
            Vector3 searchCenter = _bodyCollider != null ? _bodyCollider.bounds.center : transform.position;
            float searchRadius = (_bodyCollider != null ? _bodyCollider.bounds.extents.magnitude : 0.5f) + pickupRange;
            int overlapCount = Physics.OverlapSphereNonAlloc(
                searchCenter,
                searchRadius,
                _pickupOverlapBuffer,
                ~0,
                QueryTriggerInteraction.Collide);

            float closestDist = float.MaxValue;
            for (int i = 0; i < overlapCount; i++)
            {
                Collider col = _pickupOverlapBuffer[i];
                if (col == null) continue;

                WorldWeaponPickup candidate = col.GetComponentInParent<WorldWeaponPickup>();
                if (candidate == null || !candidate.IsSpawned || string.IsNullOrEmpty(candidate.WeaponId)) continue;

                float dist = GetPickupDistance(candidate);
                if (dist > pickupRange) continue;

                if (dist < closestDist)
                {
                    closestDist = dist;
                    bestPickup = candidate;
                }
            }
        }

        if (bestPickup != null && bestPickup.NetworkObject != null)
        {
            RequestPickupServerRpc(bestPickup.NetworkObject);
        }
    }

    /// <summary>소유자가 현재 장착 중인 무기를 바닥에 버리도록 서버에 요청한다 (맨손이면 무시).</summary>
    public void TryDropCurrentWeapon()
    {
        if (!IsOwner || !IsSpawned) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_equippedState.Value.Slot == EWeaponSlotType.Unarmed) return;
        if (!HasWeaponInSlot(_equippedState.Value.Slot)) return;

        RequestDropCurrentWeaponServerRpc();
    }

    /// <summary>서버에서 보유 슬롯 데이터를 유지한 채 장착 슬롯만 맨손(<see cref="EWeaponSlotType.Unarmed"/>)으로 강제 전환한다.</summary>
    [Server]
    public void ForceSelectUnarmed()
    {
        if (!IsServerInitialized) return;
        if (_equippedState.Value.Slot == EWeaponSlotType.Unarmed && string.IsNullOrEmpty(_equippedState.Value.WeaponId))
        {
            return;
        }

        _equippedState.Value = new EquippedWeaponState
        {
            Slot = EWeaponSlotType.Unarmed,
            WeaponId = string.Empty,
            ChangeSequence = (ushort)(_equippedState.Value.ChangeSequence + 1)
        };
    }

    /// <summary>서버에서 지정 슬롯 무기의 탄창 탄약을 1발 차감하고 슬롯 상태를 갱신한다.</summary>
    [Server]
    public bool TryConsumeAmmo(EWeaponSlotType slot, string expectedWeaponId, out WeaponSlotState updatedState)
    {
        updatedState = WeaponSlotState.Empty;
        int index = (int)slot;
        if (index < 0 || index >= _slots.Count) return false;

        WeaponSlotState current = _slots[index];
        if (current.IsEmpty || !string.Equals(current.WeaponId, expectedWeaponId, StringComparison.Ordinal)) return false;
        if (current.CurrentAmmo <= 0)
        {
            updatedState = current;
            return false;
        }

        current.CurrentAmmo--;
        _slots[index] = current;
        updatedState = current;
        return true;
    }

    /// <summary>서버에서 지정 슬롯 무기의 재장전을 완료하고 탄창 및 예비 탄약을 갱신한다.</summary>
    [Server]
    public bool TryApplyReload(EWeaponSlotType slot, string expectedWeaponId, int maxAmmo, out WeaponSlotState updatedState)
    {
        updatedState = WeaponSlotState.Empty;
        int index = (int)slot;
        if (index < 0 || index >= _slots.Count) return false;

        WeaponSlotState current = _slots[index];
        if (current.IsEmpty || !string.Equals(current.WeaponId, expectedWeaponId, StringComparison.Ordinal)) return false;

        int needed = maxAmmo - current.CurrentAmmo;
        int loaded = Mathf.Min(needed, current.ReserveAmmo);
        if (loaded > 0)
        {
            current.CurrentAmmo += loaded;
            current.ReserveAmmo -= loaded;
            _slots[index] = current;
        }

        updatedState = current;
        return true;
    }

    /// <summary>서버에서 지정 슬롯 무기의 탄약 1발을 장전하고 탄창 및 예비 탄약을 갱신한다.</summary>
    [Server]
    public bool TryInsertSingleShell(EWeaponSlotType slot, string expectedWeaponId, int maxAmmo, out WeaponSlotState updatedState)
    {
        updatedState = WeaponSlotState.Empty;
        int index = (int)slot;
        if (index < 0 || index >= _slots.Count) return false;

        WeaponSlotState current = _slots[index];
        if (current.IsEmpty || !string.Equals(current.WeaponId, expectedWeaponId, StringComparison.Ordinal)) return false;
        if (current.CurrentAmmo >= maxAmmo || current.ReserveAmmo <= 0)
        {
            updatedState = current;
            return false;
        }

        current.CurrentAmmo++;
        current.ReserveAmmo--;
        _slots[index] = current;
        updatedState = current;
        return true;
    }

    /// <summary>서버에서 현재 투척무기 보유 개수를 1개 차감하고, 0개가 되면 남은 투척무기 또는 맨손으로 전환한다.</summary>
    [Server]
    public bool TryConsumeThrowable(string expectedWeaponId, out WeaponSlotState updatedState, out bool becameEmpty)
    {
        updatedState = WeaponSlotState.Empty;
        becameEmpty = false;

        int itemIndex = FindThrowableIndex(expectedWeaponId);
        if (itemIndex < 0) return false;

        WeaponSlotState current = _throwableItems[itemIndex];
        if (current.CurrentAmmo <= 0)
        {
            updatedState = current;
            return false;
        }

        current.CurrentAmmo--;
        if (current.CurrentAmmo <= 0)
        {
            _throwableItems.RemoveAt(itemIndex);
            bool wasEquipped = _equippedState.Value.Slot == EWeaponSlotType.Throwable;
            ApplyActiveThrowableSelection(_activeThrowableIndex, equipThrowableSlot: wasEquipped, forceSequenceBump: true);
            updatedState = GetSlotState(EWeaponSlotType.Throwable);
            becameEmpty = _throwableItems.Count == 0;
        }
        else
        {
            _throwableItems[itemIndex] = current;
            ApplyActiveThrowableSelection(itemIndex, equipThrowableSlot: false, forceSequenceBump: false);
            updatedState = current;
        }

        return true;
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>서버에서 슬롯 보유 여부를 검증한 뒤 장착 슬롯 변경 또는 5번 투척무기 순환 스왑을 수행한다.</summary>
    [ServerRpc]
    private void RequestSelectSlotServerRpc(EWeaponSlotType slot, Channel channel = Channel.Reliable)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (!Enum.IsDefined(typeof(EWeaponSlotType), slot)) return;
        if (slot != EWeaponSlotType.Unarmed && !HasWeaponInSlot(slot)) return;

        if (slot == EWeaponSlotType.Throwable)
        {
            if (_throwableItems.Count == 0) return;

            if (_equippedState.Value.Slot == EWeaponSlotType.Throwable)
            {
                if (_throwableItems.Count <= 1) return;
                ApplyActiveThrowableSelection(_activeThrowableIndex + 1, equipThrowableSlot: true, forceSequenceBump: true);
            }
            else
            {
                ApplyActiveThrowableSelection(_activeThrowableIndex, equipThrowableSlot: true, forceSequenceBump: true);
            }
            return;
        }

        if (_equippedState.Value.Slot == slot) return;

        string nextWeaponId = slot == EWeaponSlotType.Unarmed ? string.Empty : _slots[(int)slot].WeaponId;
        _equippedState.Value = new EquippedWeaponState
        {
            Slot = slot,
            WeaponId = nextWeaponId,
            ChangeSequence = (ushort)(_equippedState.Value.ChangeSequence + 1)
        };
    }

    /// <summary>서버에서 거리와 아이템 유효성을 검증한 뒤 월드 무기를 획득한다 (투척무기는 종류별 다중 소지 및 스택 합산).</summary>
    [ServerRpc]
    private void RequestPickupServerRpc(NetworkObject pickupObject, Channel channel = Channel.Reliable)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (pickupObject == null || !pickupObject.IsSpawned) return;

        WorldWeaponPickup pickup = pickupObject.GetComponent<WorldWeaponPickup>();
        if (pickup == null || pickup.IsConsumed) return;

        if (GetPickupDistance(pickup) > pickupRange + ServerPickupRangeTolerance)
        {
            return;
        }

        WeaponDataSO newData = GetWeaponDataById(pickup.WeaponId);
        if (newData == null || newData.SlotType == EWeaponSlotType.Unarmed) return;

        int slotIndex = (int)newData.SlotType;
        if (slotIndex < 0 || slotIndex >= _slots.Count) return;

        // 투척무기는 수류탄·섬광탄 등 여러 종류를 동시에 보관하며 같은 종류는 MaxAmmo까지 합산한다.
        if (newData.IsThrowable)
        {
            int maxStack = Mathf.Max(1, newData.MaxAmmo);
            int existingIndex = FindThrowableIndex(newData.WeaponId);

            if (existingIndex >= 0)
            {
                WeaponSlotState existing = _throwableItems[existingIndex];
                if (existing.CurrentAmmo >= maxStack) return;

                if (!pickup.TryConsume(out WeaponSlotState pickedThrowable)) return;

                int addCount = Mathf.Max(1, pickedThrowable.CurrentAmmo);
                int total = existing.CurrentAmmo + addCount;
                int kept = Mathf.Min(total, maxStack);
                int excess = total - kept;

                existing.CurrentAmmo = kept;
                existing.ReserveAmmo = 0;
                _throwableItems[existingIndex] = existing;
                ApplyActiveThrowableSelection(existingIndex, equipThrowableSlot: true, forceSequenceBump: false);

                if (excess > 0)
                {
                    SpawnDroppedWeapon(new WeaponSlotState
                    {
                        WeaponId = existing.WeaponId,
                        CurrentAmmo = excess,
                        ReserveAmmo = 0
                    });
                }
                return;
            }

            if (!pickup.TryConsume(out WeaponSlotState pickedNewThrowable)) return;

            int initialCount = Mathf.Clamp(Mathf.Max(1, pickedNewThrowable.CurrentAmmo), 1, maxStack);
            _throwableItems.Add(new WeaponSlotState
            {
                WeaponId = newData.WeaponId,
                CurrentAmmo = initialCount,
                ReserveAmmo = 0
            });
            ApplyActiveThrowableSelection(_throwableItems.Count - 1, equipThrowableSlot: true, forceSequenceBump: true);
            return;
        }

        WeaponSlotState previousSlotState = _slots[slotIndex];
        if (!pickup.TryConsume(out WeaponSlotState pickedState)) return;

        _slots[slotIndex] = pickedState;
        _equippedState.Value = new EquippedWeaponState
        {
            Slot = newData.SlotType,
            WeaponId = pickedState.WeaponId,
            ChangeSequence = (ushort)(_equippedState.Value.ChangeSequence + 1)
        };

        if (!previousSlotState.IsEmpty)
        {
            SpawnDroppedWeapon(previousSlotState);
        }
    }

    /// <summary>서버에서 현재 장착 무기를 바닥에 드롭한다 (투척무기는 1개씩 버리고 남은 수량을 유지).</summary>
    [ServerRpc]
    private void RequestDropCurrentWeaponServerRpc(Channel channel = Channel.Reliable)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;

        EWeaponSlotType currentSlot = _equippedState.Value.Slot;
        if (currentSlot == EWeaponSlotType.Unarmed) return;

        if (currentSlot == EWeaponSlotType.Throwable)
        {
            int itemIndex = FindThrowableIndex(_equippedState.Value.WeaponId);
            if (itemIndex < 0) return;

            WeaponSlotState currentThrowable = _throwableItems[itemIndex];
            if (currentThrowable.CurrentAmmo <= 0) return;

            string droppedWeaponId = currentThrowable.WeaponId;
            currentThrowable.CurrentAmmo--;

            if (currentThrowable.CurrentAmmo > 0)
            {
                _throwableItems[itemIndex] = currentThrowable;
                ApplyActiveThrowableSelection(itemIndex, equipThrowableSlot: false, forceSequenceBump: false);
            }
            else
            {
                _throwableItems.RemoveAt(itemIndex);
                ApplyActiveThrowableSelection(_activeThrowableIndex, equipThrowableSlot: true, forceSequenceBump: true);
            }

            SpawnDroppedWeapon(new WeaponSlotState
            {
                WeaponId = droppedWeaponId,
                CurrentAmmo = 1,
                ReserveAmmo = 0
            });
            return;
        }

        int slotIndex = (int)currentSlot;
        if (slotIndex < 0 || slotIndex >= _slots.Count || _slots[slotIndex].IsEmpty) return;

        WeaponSlotState droppedState = _slots[slotIndex];
        _slots[slotIndex] = WeaponSlotState.Empty;
        _equippedState.Value = new EquippedWeaponState
        {
            Slot = EWeaponSlotType.Unarmed,
            WeaponId = string.Empty,
            ChangeSequence = (ushort)(_equippedState.Value.ChangeSequence + 1)
        };

        SpawnDroppedWeapon(droppedState);
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>서버에서 플레이어가 사망할 때 장착 슬롯을 HideMode(<see cref="EWeaponSlotType.Unarmed"/>)로 전환한다.</summary>
    private void HandleServerDied(NetworkConnection attacker)
    {
        ForceSelectUnarmed();
    }

    /// <summary><see cref="_equippedState"/> 변경 시 구독 컴포넌트에 최신 장착 상태를 알린다.</summary>
    private void HandleEquippedStateChanged(EquippedWeaponState previousValue, EquippedWeaponState nextValue, bool asServer)
    {
        if (!asServer && IsServerInitialized) return;
        OnEquippedWeaponChanged?.Invoke(nextValue);
    }

    /// <summary><see cref="_slots"/> 변경 시 구독 컴포넌트에 갱신된 슬롯 상태를 알린다.</summary>
    private void HandleSlotsChanged(
        SyncListOperation op,
        int index,
        WeaponSlotState oldItem,
        WeaponSlotState newItem,
        bool asServer)
    {
        if (!asServer && IsServerInitialized) return;
        if (op != SyncListOperation.Set && op != SyncListOperation.Add) return;
        if (index < 0 || index >= SlotCount) return;

        OnSlotUpdated?.Invoke((EWeaponSlotType)index, newItem);
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary><see cref="_throwableItems"/>에서 <paramref name="weaponId"/>의 인덱스를 반환한다 (없으면 -1).</summary>
    private int FindThrowableIndex(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return -1;

        for (int i = 0; i < _throwableItems.Count; i++)
        {
            if (string.Equals(_throwableItems[i].WeaponId, weaponId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>서버에서 <see cref="_throwableItems"/> 기준 활성 투척무기 슬롯과 장착 상태를 동기화한다.</summary>
    [Server]
    private void ApplyActiveThrowableSelection(int nextIndex, bool equipThrowableSlot, bool forceSequenceBump)
    {
        int throwableSlotIndex = (int)EWeaponSlotType.Throwable;
        if (throwableSlotIndex < 0 || throwableSlotIndex >= _slots.Count) return;

        if (_throwableItems.Count == 0)
        {
            _activeThrowableIndex = 0;
            _slots[throwableSlotIndex] = WeaponSlotState.Empty;
            if (_equippedState.Value.Slot == EWeaponSlotType.Throwable)
            {
                ForceSelectUnarmed();
            }
            return;
        }

        _activeThrowableIndex = ((nextIndex % _throwableItems.Count) + _throwableItems.Count) % _throwableItems.Count;
        WeaponSlotState activeThrowable = _throwableItems[_activeThrowableIndex];
        _slots[throwableSlotIndex] = activeThrowable;

        bool shouldEquip = equipThrowableSlot || _equippedState.Value.Slot == EWeaponSlotType.Throwable;
        if (!shouldEquip) return;

        bool needsStateChange = forceSequenceBump
            || _equippedState.Value.Slot != EWeaponSlotType.Throwable
            || !string.Equals(_equippedState.Value.WeaponId, activeThrowable.WeaponId, StringComparison.Ordinal);
        if (needsStateChange)
        {
            _equippedState.Value = new EquippedWeaponState
            {
                Slot = EWeaponSlotType.Throwable,
                WeaponId = activeThrowable.WeaponId,
                ChangeSequence = (ushort)(_equippedState.Value.ChangeSequence + 1)
            };
        }
    }

    /// <summary>플레이어 콜라이더와 <paramref name="pickup"/> 복합 콜라이더 사이의 최단 표면 거리(미터)를 계산한다.</summary>
    private float GetPickupDistance(WorldWeaponPickup pickup)
    {
        IReadOnlyList<Collider> pickupColliders = pickup.PickupColliders;
        if (pickupColliders == null || pickupColliders.Count == 0)
        {
            Vector3 fallbackPickupPos = pickup.transform.position;
            Vector3 fallbackPlayerPoint = _bodyCollider != null ? _bodyCollider.ClosestPoint(fallbackPickupPos) : transform.position;
            return Vector3.Distance(fallbackPlayerPoint, fallbackPickupPos);
        }

        float closestDistance = float.MaxValue;
        for (int i = 0; i < pickupColliders.Count; i++)
        {
            Collider pickupCollider = pickupColliders[i];
            if (pickupCollider == null || !pickupCollider.enabled) continue;

            Vector3 pickupCenter = pickupCollider.bounds.center;
            Vector3 playerPoint = _bodyCollider != null ? _bodyCollider.ClosestPoint(pickupCenter) : transform.position;
            Vector3 pickupPoint = pickupCollider.ClosestPoint(playerPoint);
            if (_bodyCollider != null)
            {
                playerPoint = _bodyCollider.ClosestPoint(pickupPoint);
            }

            float dist = Vector3.Distance(playerPoint, pickupPoint);
            if (dist < closestDistance)
            {
                closestDistance = dist;
            }
        }

        return closestDistance;
    }

    /// <summary>서버에서 플레이어 전방 안전 위치에 <paramref name="slotState"/>를 담은 월드 픽업을 스폰한다.</summary>
    [Server]
    private void SpawnDroppedWeapon(WeaponSlotState slotState)
    {
        if (slotState.IsEmpty || weaponDatabase == null || weaponDatabase.WorldPickupPrefab == null) return;

        Vector3 origin = _playerController != null
            ? _playerController.GetFireOrigin()
            : transform.position + Vector3.up * 0.45f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;

        float spawnDistance = dropForwardOffset;
        if (Physics.Raycast(origin, forward, out RaycastHit wallHit, dropForwardOffset + DropWallMargin, dropObstacleMask, QueryTriggerInteraction.Ignore))
        {
            spawnDistance = Mathf.Max(0.1f, wallHit.distance - DropWallMargin);
        }

        Vector3 spawnPosition = origin + forward * spawnDistance + Vector3.up * dropUpwardOffset;
        Quaternion spawnRotation = Quaternion.LookRotation(forward, Vector3.up);

        WorldWeaponPickup pickup = Instantiate(weaponDatabase.WorldPickupPrefab, spawnPosition, spawnRotation);
        ServerManager.Spawn(pickup.NetworkObject);

        Vector3 launchImpulse = forward * dropForwardForce + Vector3.up * dropUpwardForce;
        Vector3 launchTorque = new Vector3(
            UnityEngine.Random.Range(-dropRandomTorque, dropRandomTorque),
            UnityEngine.Random.Range(-dropRandomTorque, dropRandomTorque),
            UnityEngine.Random.Range(-dropRandomTorque, dropRandomTorque));

        pickup.Initialize(slotState.WeaponId, slotState.CurrentAmmo, slotState.ReserveAmmo, launchImpulse, launchTorque);
    }
}
