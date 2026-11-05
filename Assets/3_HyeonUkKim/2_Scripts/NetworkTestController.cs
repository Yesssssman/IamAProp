
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

public class NetworkTestController : NetworkBehaviour
{
    private Keyboard _keyboard;
    private const int MAX_CONNECTIONS = 6; 

    

    private string DedicatedServerEndPoint;
    private string HostServerEndPoint;


    [SerializeField] private string testJoinCode;
    [SerializeField] private Transform _spawnPos;
    [SerializeField] private GameObject _spawnPreFab;
    [SerializeField] private CinemachineCamera _cineCam;

    


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

    void Update()
    {

        if (_keyboard.pKey.wasPressedThisFrame)
        {
            //StartHost();
            Task<string> result = StartHostWithRelay(maxConnections:MAX_CONNECTIONS);

        }

        if (_keyboard.oKey.wasPressedThisFrame)
        {
            //StartClient();
            JoinClientWithRelay(testJoinCode);
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

    void StartHost()
    {

        var transfort = NetworkManager.GetComponent<UnityTransport>();



        NetworkManager.Singleton.StartHost();

        ulong myLocalClientId = NetworkManager.Singleton.LocalClientId;
        print($"내 로컬 ClientId: {myLocalClientId}");
        //PlayerSpawn(myLocalClientId);
    }

    void StartClient()
    {
        NetworkManager.Singleton.StartClient();
        ulong myLocalClientId = NetworkManager.Singleton.LocalClientId;
        print($"내 로컬 ClientId: {myLocalClientId}");
    }

    //void PlayerSpawn(ulong clientId)
    //{
    //    if (IsClient) return;

    //    GameObject playerInstance = Instantiate(_player, _spawnPos.position, _spawnPos.rotation);

    //    // 핵심: SpawnAsPlayerObject로 해당 클라이언트에게 소유권을 주며 스폰
    //    playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);

    //    _cineCam.Target.TrackingTarget = playerInstance.transform;
    //    _cineCam.Target.LookAtTarget = playerInstance.transform;


    //    print($"Client {clientId} 스폰 완료");
    //}

}
