using System.Collections.Generic;
using FishNet.Connection;
using UnityEngine;

/// <summary>씬에 배치된 복수 스폰 지점 중 다른 생존 플레이어와 가장 안전하게 떨어진 지점을 선택한다.</summary>
public class SpawnPointManager : MonoBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float SpawnClearanceRadius = 0.65f; // 스폰 위치 겹침 검사 구체 반지름 (미터)
    private const int TopCandidateRandomPool = 2; // 동일 지점 반복 방지를 위한 상위 안전 후보 무작위 풀 크기

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>스폰 후보 트랜스폼 배열 (비워두면 하위 자식 트랜스폼을 자동 수집).</summary>
    [Header("스폰 포인트 목록")]
    [SerializeField] private Transform[] spawnPoints;

    /// <summary>스폰 기준점 위로 띄울 높이 오프셋 (미터).</summary>
    [Min(0f)]
    [SerializeField] private float spawnHeightOffset = 0.5f;

    /// <summary>다른 플레이어와 겹침 여부를 검사할 물리 레이어 마스크.</summary>
    [SerializeField] private LayerMask playerBlockMask = (1 << 6) | (1 << 7); // Player, Hitbox

    /// <summary>런타임에 유효한 스폰 포인트 목록.</summary>
    private readonly List<Transform> _points = new(16);

    /// <summary>거리 점수 순으로 정렬하기 위한 재사용 버퍼.</summary>
    private readonly List<(Transform point, float minEnemyDistSqr, bool isBlocked)> _scoredBuffer = new(16);

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        RefreshSpawnPoints();
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.85f);
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] == null) continue;
                Gizmos.DrawWireSphere(spawnPoints[i].position + Vector3.up * spawnHeightOffset, 0.45f);
                Gizmos.DrawRay(spawnPoints[i].position + Vector3.up * spawnHeightOffset, spawnPoints[i].forward * 1.2f);
            }
            return;
        }

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            Gizmos.DrawWireSphere(child.position + Vector3.up * spawnHeightOffset, 0.45f);
            Gizmos.DrawRay(child.position + Vector3.up * spawnHeightOffset, child.forward * 1.2f);
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>인스펙터 배열 또는 하위 자식 트랜스폼에서 유효한 스폰 포인트 목록을 갱신한다.</summary>
    public void RefreshSpawnPoints()
    {
        _points.Clear();

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            for (int i = 0; i < spawnPoints.Length; i++)
            {
                if (spawnPoints[i] != null)
                {
                    _points.Add(spawnPoints[i]);
                }
            }
        }

        if (_points.Count == 0)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                _points.Add(transform.GetChild(i));
            }
        }
    }

    /// <summary>생존 중인 다른 플레이어로부터 가장 안전하게 떨어진 스폰 위치와 수평 회전을 반환한다.</summary>
    public Pose SelectSafeSpawnPoint(NetworkConnection forConnection, IReadOnlyList<PlayerController> activePlayers)
    {
        if (_points.Count == 0)
        {
            RefreshSpawnPoints();
        }

        if (_points.Count == 0)
        {
            GameObject fallbackObj = GameObject.Find("SpawnPos");
            if (fallbackObj != null)
            {
                return new Pose(
                    fallbackObj.transform.position + Vector3.up * spawnHeightOffset,
                    Quaternion.Euler(0f, fallbackObj.transform.eulerAngles.y, 0f));
            }

            return new Pose(new Vector3(0f, 1f, 0f), Quaternion.identity);
        }

        int requesterId = forConnection != null && forConnection.IsValid ? forConnection.ClientId : -1;
        _scoredBuffer.Clear();

        for (int i = 0; i < _points.Count; i++)
        {
            Transform point = _points[i];
            if (point == null) continue;

            Vector3 spawnPos = point.position + Vector3.up * spawnHeightOffset;
            bool isBlocked = Physics.CheckSphere(spawnPos, SpawnClearanceRadius, playerBlockMask, QueryTriggerInteraction.Ignore);

            float minEnemyDistSqr = float.MaxValue;
            if (activePlayers != null)
            {
                for (int p = 0; p < activePlayers.Count; p++)
                {
                    PlayerController other = activePlayers[p];
                    if (other == null || !other.IsSpawned || other.IsDead) continue;
                    if (requesterId >= 0 && other.OwnerId == requesterId) continue;

                    float distSqr = (other.transform.position - spawnPos).sqrMagnitude;
                    if (distSqr < minEnemyDistSqr)
                    {
                        minEnemyDistSqr = distSqr;
                    }
                }
            }

            _scoredBuffer.Add((point, minEnemyDistSqr, isBlocked));
        }

        if (_scoredBuffer.Count == 0)
        {
            return new Pose(new Vector3(0f, 1f, 0f), Quaternion.identity);
        }

        // 막히지 않은 지점 우선, 그다음 가장 가까운 적까지의 거리가 먼 순으로 정렬
        _scoredBuffer.Sort((a, b) =>
        {
            int blockCmp = a.isBlocked.CompareTo(b.isBlocked);
            if (blockCmp != 0) return blockCmp;
            return b.minEnemyDistSqr.CompareTo(a.minEnemyDistSqr);
        });

        int pickRange = Mathf.Min(TopCandidateRandomPool, _scoredBuffer.Count);
        // 최상위 후보가 비어있다면 막힌 후보는 무작위 풀에서 제외
        if (!_scoredBuffer[0].isBlocked)
        {
            int validTop = 1;
            for (int i = 1; i < pickRange; i++)
            {
                if (!_scoredBuffer[i].isBlocked)
                {
                    validTop++;
                }
            }
            pickRange = validTop;
        }

        Transform chosen = _scoredBuffer[Random.Range(0, pickRange)].point;
        return new Pose(
            chosen.position + Vector3.up * spawnHeightOffset,
            Quaternion.Euler(0f, chosen.eulerAngles.y, 0f));
    }
}
