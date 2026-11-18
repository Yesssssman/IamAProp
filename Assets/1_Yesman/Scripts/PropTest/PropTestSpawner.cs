using Unity.Cinemachine;
using UnityEngine;

/// <summary>네트워크 연결 없이 플레이어 프롭 프리팹을 스폰하고 카메라가 따라가게 함.</summary>
public class PropTestSpawner : MonoBehaviour
{
    /// <summary>스폰할 프롭 프리팹. <see cref="PropTestController"/>가 붙어 있어야 함.</summary>
    [SerializeField] private GameObject playerPrefab;

    /// <summary>프롭을 스폰할 위치와 방향.</summary>
    [SerializeField] private Transform spawnPoint;

    /// <summary>스폰한 프롭을 따라가는 3인칭 카메라.</summary>
    [SerializeField] private CinemachineCamera followCamera;

    private void Start()
    {
        GameObject player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);

        followCamera.Follow = player.transform;
        followCamera.LookAt = player.transform;

        if (followCamera.TryGetComponent(out CinemachineOrbitalFollow orbitalFollow))
        {
            orbitalFollow.HorizontalAxis.Value = player.transform.eulerAngles.y;
        }
    }
}
