using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

/// <summary>클라이언트가 시작 씬 로드를 완료할 때 서버에서 플레이어 프리팹을 스폰한다.</summary>
public class DynamicSpawner : MonoBehaviour
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>서버에서 스폰할 플레이어 <see cref="NetworkObject"/> 프리팹.</summary>
    [Header("스폰할 플레이어 프리팹")]
    [SerializeField] private GameObject playerPrefab;

    /// <summary>플레이어가 스폰될 기준 위치 및 방향 트랜스폼 (비워두면 기본 좌표 사용).</summary>
    [Header("스폰 위치")]
    [SerializeField] private Transform spawnPoint;

    /// <summary>스폰 기준점 위로 띄울 높이 오프셋 (미터).</summary>
    [Min(0f)]
    [SerializeField] private float spawnHeightOffset = 0.5f;

    /// <summary>씬 로드 콜백을 구독한 <see cref="NetworkManager"/> 참조.</summary>
    private NetworkManager _networkManager;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Start()
    {
        _networkManager = InstanceFinder.NetworkManager;
        if (_networkManager != null)
        {
            _networkManager.SceneManager.OnClientLoadedStartScenes += HandleClientLoadedStartScenes;
        }
    }

    private void OnDestroy()
    {
        if (_networkManager != null)
        {
            _networkManager.SceneManager.OnClientLoadedStartScenes -= HandleClientLoadedStartScenes;
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>서버 측에서 <paramref name="conn"/>의 시작 씬 로드가 완료되면 플레이어를 스폰한다.</summary>
    private void HandleClientLoadedStartScenes(NetworkConnection conn, bool asServer)
    {
        if (!asServer || playerPrefab == null) return;

        Vector3 spawnPosition = spawnPoint != null
            ? spawnPoint.position + Vector3.up * spawnHeightOffset
            : new Vector3(0f, 1f, 0f);
        Quaternion spawnRotation = spawnPoint != null
            ? Quaternion.Euler(0f, spawnPoint.eulerAngles.y, 0f)
            : Quaternion.identity;
        GameObject playerInstance = Instantiate(playerPrefab, spawnPosition, spawnRotation);

        NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Destroy(playerInstance);
            return;
        }

        _networkManager.ServerManager.Spawn(netObj, conn);
    }
}
