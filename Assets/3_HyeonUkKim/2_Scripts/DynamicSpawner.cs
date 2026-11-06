using Unity.Netcode;
using UnityEngine;

public class DynamicSpawner : MonoBehaviour
{
    [Header("스폰할 플레이어 프리팹")]
    [SerializeField] private GameObject playerPrefab;

    private void Start()
    {
        // NetworkManager가 준비된 후 서버 시작 이벤트를 구독합니다.
        NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
    }

    private void HandleServerStarted()
    {
        // 1. 호스트(서버+클라이언트)가 방을 팠을 때 호스트 자신의 캐릭터 스폰
        if (NetworkManager.Singleton.IsHost)
        {
            SpawnPlayer(NetworkManager.Singleton.LocalClientId);
        }

        // 2. 다른 클라이언트가 접속할 때마다 캐릭터 스폰 이벤트 구독
        NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        // 방금 접속한 유저가 호스트 본인이 아닐 때만 스폰 (호스트는 위에서 이미 스폰함)
        if (clientId != NetworkManager.Singleton.LocalClientId)
        {
            SpawnPlayer(clientId);
        }
    }

    private void SpawnPlayer(ulong clientId)
    {
        // [중요] 생성(Instantiate)은 오직 서버에서만 이루어집니다.
        // 스폰 위치는 필요에 따라 수정하세요 (예: 특정 스폰 포인트의 position).
        Vector3 spawnPosition = new Vector3(0f, 1f, 0f);
        GameObject playerInstance = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);

        // NetworkObject 컴포넌트를 가져와서 해당 클라이언트에게 소유권을 주고 네트워크에 스폰합니다.
        NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
        netObj.SpawnAsPlayerObject(clientId);
    }

    private void OnDestroy()
    {
        // 씬 전환이나 오브젝트 파괴 시 메모리 누수 방지를 위해 이벤트 구독 해제
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        }
    }
}
