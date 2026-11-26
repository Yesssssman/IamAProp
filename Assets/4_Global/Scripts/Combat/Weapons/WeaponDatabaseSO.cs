using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>고유 <c>WeaponId</c>로 <see cref="WeaponDataSO"/>를 조회하고 기본 시작 무기 및 드롭 프리팹을 관리한다.</summary>
[CreateAssetMenu(fileName = "WeaponDatabase", menuName = "IamAProp/Combat/Weapon Database")]
public class WeaponDatabaseSO : ScriptableObject
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>게임에서 사용할 모든 무기 데이터 목록.</summary>
    [Header("등록 무기 목록")]
    [SerializeField] private WeaponDataSO[] weapons;

    /// <summary>스폰 시 2번 근접 슬롯에 기본 지급할 무기 데이터.</summary>
    [Header("기본 설정")]
    [SerializeField] private WeaponDataSO defaultMeleeWeapon;

    /// <summary>바닥에 무기를 드롭할 때 서버에서 스폰할 공용 월드 픽업 프리팹.</summary>
    [SerializeField] private WorldWeaponPickup worldPickupPrefab;

    /// <summary><c>WeaponId</c> 기준 빠른 조회용 런타임 사전.</summary>
    private Dictionary<string, WeaponDataSO> _lookupById;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>등록된 전체 무기 데이터 배열.</summary>
    public WeaponDataSO[] Weapons => weapons;

    /// <summary>스폰 시 기본 지급되는 근접 무기 데이터.</summary>
    public WeaponDataSO DefaultMeleeWeapon => defaultMeleeWeapon;

    /// <summary>드롭 시 서버가 스폰할 월드 픽업 프리팹.</summary>
    public WorldWeaponPickup WorldPickupPrefab => worldPickupPrefab;

    // ***************************************************************
    // ScriptableObject
    // ***************************************************************

    private void OnEnable()
    {
        _lookupById = null;
    }

    private void OnValidate()
    {
        _lookupById = null;
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary><paramref name="weaponId"/>에 대응하는 <see cref="WeaponDataSO"/>를 반환한다.</summary>
    public WeaponDataSO GetById(string weaponId)
    {
        return TryGetById(weaponId, out WeaponDataSO data) ? data : null;
    }

    /// <summary><paramref name="weaponId"/>에 대응하는 <see cref="WeaponDataSO"/> 조회를 시도한다.</summary>
    public bool TryGetById(string weaponId, out WeaponDataSO weaponData)
    {
        weaponData = null;
        if (string.IsNullOrEmpty(weaponId)) return false;

        if (_lookupById == null)
        {
            _lookupById = new Dictionary<string, WeaponDataSO>(StringComparer.Ordinal);
            if (weapons != null)
            {
                for (int i = 0; i < weapons.Length; i++)
                {
                    WeaponDataSO entry = weapons[i];
                    if (entry == null || string.IsNullOrEmpty(entry.WeaponId)) continue;
                    _lookupById[entry.WeaponId] = entry;
                }
            }
        }

        return _lookupById.TryGetValue(weaponId, out weaponData);
    }
}
