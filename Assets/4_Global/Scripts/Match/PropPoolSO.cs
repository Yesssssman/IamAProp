using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>플레이어 스폰 및 리스폰 시 랜덤 배정할 프롭 <see cref="NetworkObject"/> 프리팹 풀.</summary>
[CreateAssetMenu(fileName = "PropPool", menuName = "IamAProp/Match/Prop Pool")]
public class PropPoolSO : ScriptableObject
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>랜덤 배정 대상 프롭 프리팹 목록 (<c>DefaultPrefabObjects</c>에 등록되어 있어야 함).</summary>
    [Header("프롭 프리팹 목록")]
    [SerializeField] private NetworkObject[] propPrefabs;

    /// <summary>2종 이상 등록되었을 때 직전 프롭이 연속으로 배정되지 않도록 제외할지 여부.</summary>
    [SerializeField] private bool avoidImmediateRepeat = false;

    /// <summary>유효한 프리팹 필터링에 재사용하는 버퍼.</summary>
    private readonly List<NetworkObject> _validBuffer = new(16);

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>등록된 전체 프롭 프리팹 목록.</summary>
    public IReadOnlyList<NetworkObject> PropPrefabs => propPrefabs;

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>풀에서 유효한 프롭 프리팹 하나를 무작위로 선택해 반환한다.</summary>
    public NetworkObject GetRandomPropPrefab(NetworkObject previousPrefab = null)
    {
        if (propPrefabs == null || propPrefabs.Length == 0) return null;

        _validBuffer.Clear();
        for (int i = 0; i < propPrefabs.Length; i++)
        {
            NetworkObject candidate = propPrefabs[i];
            if (candidate == null) continue;
            if (avoidImmediateRepeat && previousPrefab != null && candidate == previousPrefab) continue;
            _validBuffer.Add(candidate);
        }

        if (_validBuffer.Count == 0)
        {
            for (int i = 0; i < propPrefabs.Length; i++)
            {
                if (propPrefabs[i] != null)
                {
                    _validBuffer.Add(propPrefabs[i]);
                }
            }
        }

        if (_validBuffer.Count == 0) return null;

        int index = Random.Range(0, _validBuffer.Count);
        return _validBuffer[index];
    }
}
