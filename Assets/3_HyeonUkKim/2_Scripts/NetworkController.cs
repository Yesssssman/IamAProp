
using System.ComponentModel;
using System.Threading.Tasks;
using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.InputSystem;

public class NetworkController : NetworkBehaviour
{
    private Keyboard _keyboard;
    private const int MAX_CONNECTIONS = 6;



    private string DedicatedServerEndPoint;
    private string HostServerEndPoint;


    [SerializeField] private string JoinCode;
    [SerializeField] private Transform _spawnPos;
    [SerializeField] private GameObject _spawnPreFab;




    private void Awake()
    {
        _keyboard = Keyboard.current;
    }

    private async void Start()
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log("익명 로그인 완료. Player ID: " + AuthenticationService.Instance.PlayerId);
        }
    }

    private void OnEnable()
    {
        // 내 클라이언트가 서버로부터 연결이 끊겼을 때 실행될 콜백 등록
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnectedFromServer;
        }
    }

    private void OnDisable()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnDisconnectedFromServer;
        }
    }

    /// <summary>
    /// 호스트로 방 생성 (최대 접속 인원 3명 예시)
    /// </summary>
    public async Task<string> StartHostWithRelay(int maxConnections = MAX_CONNECTIONS)
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log("방 생성 완료! 참여 코드: " + joinCode);

            // [중요 변경점] 과거의 new RelayServerData() 대신 확장 메서드를 사용합니다.
            RelayServerData relayServerData = allocation.ToRelayServerData("dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartHost();
            return joinCode;
        }
        catch (RelayServiceException e)
        {
            Debug.LogError("호스트 생성 실패: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 발급받은 6자리 참여 코드로 클라이언트 접속
    /// </summary>
    public async void JoinClientWithRelay(string joinCode)
    {
        try
        {
            Debug.Log(joinCode + " 방에 접속 시도...");
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            // [중요 변경점] 클라이언트 역시 확장 메서드를 사용합니다.
            RelayServerData relayServerData = joinAllocation.ToRelayServerData("dtls");
            NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(relayServerData);

            NetworkManager.Singleton.StartClient();
        }
        catch (RelayServiceException e)
        {
            Debug.LogError("접속 실패: " + e.Message);
        }
    }

    /// <summary>
    /// 연결을 종료하고 방을 나갑니다. (호스트/클라이언트 공통)
    /// </summary>
    public void Disconnect()
    {
        if (NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer))
        {
            // 네트워크 세션을 완전히 종료합니다.
            NetworkManager.Singleton.Shutdown();
            Debug.Log("네트워크 연결이 성공적으로 종료되었습니다.");

            // TODO: 메인 메뉴 씬으로 이동하거나 UI를 초기화하는 코드를 여기에 추가하세요.
            // 예: UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }
        else
        {
            Debug.LogWarning("현재 연결된 네트워크 세션이 없습니다.");
        }
    }

    void Update()
    {

        // p키 누르면 호스트 시작
        if (_keyboard.pKey.wasPressedThisFrame)
        {
            Task<string> result = StartHostWithRelay(maxConnections: MAX_CONNECTIONS);
            //PlayerSpawn();
            print(result);
        }

        // o키 누르면 클라이언트 시작 (testJoinCode 세팅 필요)
        if (_keyboard.oKey.wasPressedThisFrame)
        {
            JoinClientWithRelay(JoinCode);
        }

        // 연결 종료
        if (_keyboard.iKey.wasPressedThisFrame)
        {
            Disconnect();
        }

        if (_keyboard.tKey.wasPressedThisFrame)
        {
            var connectedClientId = NetworkManager.Singleton.ConnectedClients;

            foreach (var clientId in connectedClientId)
            {
                print($" ClientKey : {clientId.Key} ClientIdValue : {clientId.Value.ClientId}, {clientId.Value}");
            }

        }
    }
    private void OnDisconnectedFromServer(ulong clientId)
    {
        // 0은 보통 호스트(서버)를 의미하며, 내 ID와 같은지 체크하여 처리
        if (clientId == NetworkManager.Singleton.LocalClientId)
        {
            Debug.Log("서버와의 연결이 끊어졌습니다. (방장 나감 또는 네트워크 오류)");
            // 에러 팝업을 띄우거나 로비 씬으로 되돌아가는 로직 추가
            // host가 끊기면 다 끈헝짐
        }
    }

    void PlayerSpawn(ulong clientId)
    {
        if (!IsHost) return;

        GameObject playerInstance = Instantiate(_spawnPreFab, _spawnPos.position, _spawnPos.rotation);

        // 핵심: SpawnAsPlayerObject로 해당 클라이언트에게 소유권을 주며 스폰
        playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);

        print($"Client {clientId} 스폰 완료");
    }

}
