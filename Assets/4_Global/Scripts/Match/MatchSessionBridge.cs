using FishNet;
using FishNet.Managing;
using FishNet.Transporting;
using Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>인게임 종료·중도 퇴장·연결 끊김 시 FishNet/Relay 세션을 안전하게 정리하고 로비 씬으로 복귀한다.</summary>
public class MatchSessionBridge : MonoBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float StopConnectionTimeoutSeconds = 1.5f; // FishNet 연결 완전 종료 대기 최대 시간 (초)
    private const float GameEndedGraceTimeoutSeconds = 3.0f; // 정상 복귀 공지 후 웹 서버 GameEnded 대기 시간 (초)

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>복귀 시 로드할 로비 씬 이름 (비워두면 <see cref="GameModeDatabaseSO"/> 기본값 사용).</summary>
    [SerializeField] private string lobbySceneNameOverride;

    /// <summary>이벤트를 구독한 FishNet <see cref="NetworkManager"/> 참조.</summary>
    private NetworkManager _networkManager;

    /// <summary>매치 정상 종료로 웹 방 복귀가 예고된 상태인지 여부.</summary>
    private bool _isReturningToRoomExpected;

    /// <summary>로비 씬 복귀 시퀀스가 이미 진행 중인지 여부.</summary>
    private bool _isReturning;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 씬에 활성화된 <see cref="MatchSessionBridge"/> 인스턴스.</summary>
    public static MatchSessionBridge Instance { get; private set; }

    /// <summary>로비 복귀 절차가 진행 중인지 여부.</summary>
    public bool IsReturning => _isReturning;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        Instance = this;
    }

    private void OnEnable()
    {
        LobbyManager.GameEnded += HandleLobbyGameEnded;
        LobbyManager.RoomClosed += HandleRoomClosed;
        LobbyManager.Disconnected += HandleLobbyDisconnected;

        _networkManager = InstanceFinder.NetworkManager;
        if (_networkManager != null)
        {
            _networkManager.ClientManager.OnClientConnectionState += HandleClientConnectionState;
        }
    }

    private void OnDisable()
    {
        LobbyManager.GameEnded -= HandleLobbyGameEnded;
        LobbyManager.RoomClosed -= HandleRoomClosed;
        LobbyManager.Disconnected -= HandleLobbyDisconnected;

        if (_networkManager != null)
        {
            _networkManager.ClientManager.OnClientConnectionState -= HandleClientConnectionState;
            _networkManager = null;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>정상 매치 종료에 따른 대기실 복귀가 곧 실행됨을 표시해 연결 끊김 오인을 방지한다.</summary>
    public void MarkReturningToRoom()
    {
        _isReturningToRoomExpected = true;
    }

    /// <summary>호스트가 웹 로비 서버에 게임 종료(<c>GameEnd</c>)를 요청해 전원이 같은 방으로 복귀하도록 트리거한다.</summary>
    public async void BeginHostReturnToRoom()
    {
        _isReturningToRoomExpected = true;

        if (LobbyManager.CurrentRoom == null)
        {
            await ReturnToLobbySceneAsync();
            return;
        }

        if (!LobbyManager.IsHost) return;

        bool sent = await LobbyManager.EndGame();
        if (!sent && this != null)
        {
            Debug.LogWarning("[MatchSessionBridge] 웹 서버 GameEnd 요청 전송에 실패해 로컬 복귀를 진행합니다.");
            await ReturnToLobbySceneAsync();
        }
    }

    /// <summary>플레이어가 매치 도중 나가기를 선택했을 때 방장/멤버 역할에 맞춰 안전하게 종료한다.</summary>
    public async void LeaveMatchEarly()
    {
        if (_isReturning) return;

        if (LobbyManager.CurrentRoom != null)
        {
            if (LobbyManager.IsHost)
            {
                _isReturningToRoomExpected = true;
                if (!await LobbyManager.EndGame() && this != null)
                {
                    await ReturnToLobbySceneAsync();
                }
                return;
            }

            await LobbyManager.LeaveRoom();
        }

        if (this != null)
        {
            await ReturnToLobbySceneAsync();
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>웹 서버에서 <c>GameEnded</c>를 수신하면 FishNet 정리 후 기존 대기실로 복귀한다.</summary>
    private async void HandleLobbyGameEnded()
    {
        await ReturnToLobbySceneAsync();
    }

    /// <summary>방이 닫혔을 때 FishNet 정리 후 로비 씬으로 복귀한다.</summary>
    private async void HandleRoomClosed(string reason)
    {
        await ReturnToLobbySceneAsync();
    }

    /// <summary>웹 대기실 소켓 연결이 끊겼을 때 FishNet 정리 후 로비 씬으로 복귀한다.</summary>
    private async void HandleLobbyDisconnected()
    {
        await ReturnToLobbySceneAsync();
    }

    /// <summary>클라이언트의 FishNet 호스트 연결이 끊겼을 때 복귀 시퀀스를 처리한다.</summary>
    private async void HandleClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState != LocalConnectionState.Stopped) return;
        if (_isReturning) return;

        // 로비 방 없이 단독 에디터 테스트 중일 때는 자동으로 로비 씬으로 이동하지 않는다.
        if (LobbyManager.CurrentRoom == null) return;

        // 호스트가 먼저 StopConnection을 호출해 GameEnded보다 먼저 끊김이 도착한 경우 짧게 대기한다.
        if (_isReturningToRoomExpected)
        {
            float elapsed = 0f;
            while (!_isReturning && elapsed < GameEndedGraceTimeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                await Awaitable.NextFrameAsync();
                if (this == null) return;
            }

            if (_isReturning) return;
        }

        await ReturnToLobbySceneAsync();
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>이펙트·커서·FishNet 연결을 완전히 정리하고 로비 씬을 로드한다.</summary>
    private async Awaitable ReturnToLobbySceneAsync()
    {
        if (_isReturning) return;
        _isReturning = true;

        FlashbangEffectController.Instance?.ResetEffects();

        DeathCamController deathCam = FindAnyObjectByType<DeathCamController>(FindObjectsInactive.Include);
        deathCam?.ResetDeathCam();

        FpsAdsController ads = FindAnyObjectByType<FpsAdsController>(FindObjectsInactive.Include);
        ads?.ForceExitAds();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        NetworkManager nm = _networkManager != null ? _networkManager : InstanceFinder.NetworkManager;
        if (nm != null)
        {
            nm.ClientManager.OnClientConnectionState -= HandleClientConnectionState;

            if (nm.ClientManager.Started)
            {
                nm.ClientManager.StopConnection();
            }

            if (nm.ServerManager.Started)
            {
                nm.ServerManager.StopConnection(true);
            }

            float waitElapsed = 0f;
            while (nm != null
                && (nm.ClientManager.Started || nm.ServerManager.Started)
                && waitElapsed < StopConnectionTimeoutSeconds)
            {
                waitElapsed += Time.unscaledDeltaTime;
                await Awaitable.NextFrameAsync();
                if (this == null) return;
            }

            if (nm != null)
            {
                Destroy(nm.gameObject);
                // DontDestroyOnLoad NetworkManager가 완전히 파괴된 다음 로비 씬의 새 NetworkManager가 초기화되도록 1프레임 대기한다.
                await Awaitable.NextFrameAsync();
                if (this == null) return;
            }
        }

        string targetLobbyScene = !string.IsNullOrEmpty(lobbySceneNameOverride)
            ? lobbySceneNameOverride
            : (GameModeDatabaseSO.LoadDefault() != null ? GameModeDatabaseSO.LoadDefault().LobbySceneName : "LobbyScene");

        SceneManager.LoadScene(targetLobbyScene);
    }
}
