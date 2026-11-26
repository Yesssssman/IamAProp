using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishNet.Transporting.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Unity Relay 세션을 생성/참가하고 FishNet 호스트 및 클라이언트 연결을 제어한다.</summary>
public class NetworkController : MonoBehaviour
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    private const int MAX_CONNECTIONS = 6; // 호스트 1명 + 클라이언트 5명

    /// <summary>로컬 키보드 입력 장치 참조.</summary>
    private Keyboard _keyboard;

    /// <summary>클라이언트 참가 테스트에 사용할 Relay 참여 코드.</summary>
    [SerializeField] private string JoinCode;

    /// <summary>테스트 스폰 위치 트랜스폼.</summary>
    [SerializeField] private Transform _spawnPos;

    /// <summary>테스트 스폰 프리팹.</summary>
    [SerializeField] private GameObject _spawnPreFab;

    /// <summary>연결 상태 콜백을 구독한 <see cref="NetworkManager"/> 참조.</summary>
    private NetworkManager _networkManager;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _keyboard = Keyboard.current;
    }

    private async void Start()
    {
        _networkManager = InstanceFinder.NetworkManager;
        if (_networkManager != null)
        {
            _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionStateChanged;
        }

        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log("익명 로그인 완료. Player ID: " + AuthenticationService.Instance.PlayerId);
        }
    }

    private void OnDestroy()
    {
        if (_networkManager != null)
        {
            _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionStateChanged;
        }
    }

    private void Update()
    {
        if (_keyboard == null) return;

        // P 키: 호스트 시작
        if (_keyboard.pKey.wasPressedThisFrame)
        {
            _ = StartHostWithRelay(maxConnections: MAX_CONNECTIONS);
        }

        // O 키: 클라이언트 참가 (JoinCode 설정 필요)
        if (_keyboard.oKey.wasPressedThisFrame)
        {
            JoinClientWithRelay(JoinCode);
        }

        // I 키: 연결 종료
        if (_keyboard.iKey.wasPressedThisFrame)
        {
            Disconnect();
        }

        // T 키: 서버에 접속 중인 클라이언트 목록 출력
        if (_keyboard.tKey.wasPressedThisFrame && InstanceFinder.ServerManager != null)
        {
            foreach (KeyValuePair<int, NetworkConnection> clientPair in InstanceFinder.ServerManager.Clients)
            {
                print($"ClientKey : {clientPair.Key} ClientId : {clientPair.Value.ClientId}, {clientPair.Value}");
            }
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>Unity Relay 할당을 생성하고 FishNet 서버와 로컬 클라이언트를 순차 시작한다.</summary>
    public async Task<string> StartHostWithRelay(int maxConnections = MAX_CONNECTIONS)
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log("방 생성 완료! 참여 코드: " + joinCode);

            RelayServerData relayServerData = allocation.ToRelayServerData("dtls");
            UnityTransport transport = InstanceFinder.TransportManager != null
                ? InstanceFinder.TransportManager.GetTransport<UnityTransport>()
                : null;
            if (transport == null)
            {
                Debug.LogError("FishyUnityTransport(UnityTransport) 컴포넌트를 찾을 수 없습니다.");
                return null;
            }

            transport.SetRelayServerData(relayServerData);

            // 서버 시작 후 로컬 클라이언트를 연달아 시작해 호스트 모드로 동작시킨다.
            if (InstanceFinder.ServerManager.StartConnection())
            {
                InstanceFinder.ClientManager.StartConnection();
            }

            return joinCode;
        }
        catch (RelayServiceException e)
        {
            Debug.LogError("호스트 생성 실패: " + e.Message);
            return null;
        }
    }

    /// <summary>전달받은 참여 코드로 Relay 방에 접속해 FishNet 클라이언트를 시작한다.</summary>
    public async void JoinClientWithRelay(string joinCode)
    {
        try
        {
            Debug.Log(joinCode + " 방에 접속 시도...");
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            RelayServerData relayServerData = joinAllocation.ToRelayServerData("dtls");
            UnityTransport transport = InstanceFinder.TransportManager != null
                ? InstanceFinder.TransportManager.GetTransport<UnityTransport>()
                : null;
            if (transport == null)
            {
                Debug.LogError("FishyUnityTransport(UnityTransport) 컴포넌트를 찾을 수 없습니다.");
                return;
            }

            transport.SetRelayServerData(relayServerData);
            InstanceFinder.ClientManager.StartConnection();
        }
        catch (RelayServiceException e)
        {
            Debug.LogError("접속 실패: " + e.Message);
        }
    }

    /// <summary>활성화된 FishNet 클라이언트 및 서버 연결을 종료한다.</summary>
    public void Disconnect()
    {
        bool stopped = false;

        if (InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Started)
        {
            InstanceFinder.ClientManager.StopConnection();
            stopped = true;
        }

        if (InstanceFinder.ServerManager != null && InstanceFinder.ServerManager.Started)
        {
            InstanceFinder.ServerManager.StopConnection(true);
            stopped = true;
        }

        if (stopped)
        {
            Debug.Log("네트워크 연결이 성공적으로 종료되었습니다.");
        }
        else
        {
            Debug.LogWarning("현재 연결된 네트워크 세션이 없습니다.");
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>로컬 클라이언트 연결이 끊어졌을 때 로그를 기록한다.</summary>
    private void OnClientConnectionStateChanged(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            Debug.Log("서버와의 연결이 끊어졌습니다. (방장 나감 또는 네트워크 오류)");
        }
    }
}
