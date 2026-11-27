using System.Collections.Generic;
using Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>매치 종료 결과창(승자·순위표·하이브리드 대기실 복귀) 및 <c>Esc</c> 중도 나가기 메뉴를 제어한다.</summary>
public class MatchResultUI : MonoBehaviour
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>매치 종료 결과창 루트 오브젝트.</summary>
    [Header("결과창")]
    [SerializeField] private GameObject resultRoot;

    /// <summary>우승자 안내 제목 텍스트.</summary>
    [SerializeField] private Text winnerTitleText;

    /// <summary>대기실 자동 복귀 남은 시간 안내 텍스트.</summary>
    [SerializeField] private Text returnCountdownText;

    /// <summary>최종 순위 행을 배치할 세로 컨테이너.</summary>
    [SerializeField] private Transform resultRowContainer;

    /// <summary>순위 행 복제에 사용할 비활성 텍스트 템플릿.</summary>
    [SerializeField] private Text resultRowTemplate;

    /// <summary>방장이 즉시 대기실로 복귀하기 위해 클릭하는 버튼.</summary>
    [SerializeField] private Button returnToRoomButton;

    /// <summary><c>Esc</c> 키로 여닫는 일시정지/중도 나가기 메뉴 루트.</summary>
    [Header("Esc 일시정지 메뉴")]
    [SerializeField] private GameObject pauseMenuRoot;

    /// <summary>일시정지 메뉴를 닫고 전투로 돌아가는 버튼.</summary>
    [SerializeField] private Button resumeButton;

    /// <summary>방장 종료 또는 클라이언트 방 나가기 버튼.</summary>
    [SerializeField] private Button leaveMatchButton;

    /// <summary>로컬 플레이어 행 강조 색상.</summary>
    [Header("색상")]
    [SerializeField] private Color localPlayerColor = new Color(1f, 0.86f, 0.35f, 1f);

    /// <summary>일반 플레이어 행 색상.</summary>
    [SerializeField] private Color normalPlayerColor = Color.white;

    /// <summary>재사용 중인 결과창 순위 행 풀.</summary>
    private readonly List<Text> _rows = new(16);

    /// <summary>순위 정렬에 재사용하는 레코드 버퍼.</summary>
    private readonly List<PlayerMatchRecord> _sortBuffer = new(16);

    /// <summary>이벤트를 구독한 <see cref="MatchManager"/> 참조.</summary>
    private MatchManager _matchManager;

    /// <summary>일시정지 메뉴가 열려 있는지 여부.</summary>
    private bool _isPauseMenuOpen;

    /// <summary>현재 <c>Esc</c> 일시정지 메뉴가 열려 있는지 여부.</summary>
    public static bool IsPauseMenuOpen { get; private set; }

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        if (resultRowTemplate != null)
        {
            resultRowTemplate.gameObject.SetActive(false);
        }

        if (resultRoot != null) resultRoot.SetActive(false);
        if (pauseMenuRoot != null) pauseMenuRoot.SetActive(false);

        if (returnToRoomButton != null)
        {
            returnToRoomButton.onClick.AddListener(HandleReturnToRoomClicked);
        }

        if (resumeButton != null)
        {
            resumeButton.onClick.AddListener(HandleResumeClicked);
        }

        if (leaveMatchButton != null)
        {
            leaveMatchButton.onClick.AddListener(HandleLeaveMatchClicked);
        }
    }

    private void OnEnable()
    {
        TryBindMatchManager();
    }

    private void OnDisable()
    {
        IsPauseMenuOpen = false;
        _isPauseMenuOpen = false;
        UnbindMatchManager();
    }

    private void Update()
    {
        if (_matchManager == null)
        {
            TryBindMatchManager();
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            bool canTogglePause = _matchManager == null
                || !_matchManager.IsMatchNetworkActive
                || _matchManager.State is MatchState.WaitingForPlayers or MatchState.Countdown or MatchState.InProgress;
            if (canTogglePause)
            {
                SetPauseMenuOpen(!_isPauseMenuOpen);
            }
        }

        if (_matchManager != null && _matchManager.IsMatchNetworkActive && resultRoot != null && resultRoot.activeSelf)
        {
            UpdateReturnCountdown();
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>매치 상태 전환 시 결과창 표시 여부를 갱신한다.</summary>
    private void HandleMatchStateChanged(MatchState state)
    {
        bool showResult = state is MatchState.MatchEnded or MatchState.ReturningToRoom;
        if (showResult && _isPauseMenuOpen)
        {
            SetPauseMenuOpen(false);
        }

        if (resultRoot != null)
        {
            resultRoot.SetActive(showResult);
        }

        if (showResult)
        {
            RefreshResultBoard();
            UpdateReturnCountdown();
        }
    }

    /// <summary>방장의 즉시 대기실 복귀 버튼 클릭을 서버에 전달한다.</summary>
    private void HandleReturnToRoomClicked()
    {
        if (returnToRoomButton != null)
        {
            returnToRoomButton.interactable = false;
        }

        _matchManager?.RequestReturnToRoom();
    }

    /// <summary>일시정지 메뉴 닫기 버튼 클릭을 처리한다.</summary>
    private void HandleResumeClicked()
    {
        SetPauseMenuOpen(false);
    }

    /// <summary>중도 나가기 버튼 클릭 시 안전 종료 시퀀스를 실행한다.</summary>
    private void HandleLeaveMatchClicked()
    {
        if (leaveMatchButton != null)
        {
            leaveMatchButton.interactable = false;
        }

        SetPauseMenuOpen(false);
        MatchSessionBridge.Instance?.LeaveMatchEarly();
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>씬의 <see cref="MatchManager"/>를 찾아 이벤트를 구독한다.</summary>
    private void TryBindMatchManager()
    {
        if (_matchManager != null) return;

        _matchManager = MatchManager.Instance;
        if (_matchManager == null) return;

        _matchManager.OnMatchStateChanged += HandleMatchStateChanged;
        HandleMatchStateChanged(_matchManager.State);
    }

    /// <summary>구독 중인 <see cref="MatchManager"/> 이벤트를 해제한다.</summary>
    private void UnbindMatchManager()
    {
        if (_matchManager == null) return;

        _matchManager.OnMatchStateChanged -= HandleMatchStateChanged;
        _matchManager = null;
    }

    /// <summary>우승자 제목, 최종 순위표 및 방장 전용 복귀 버튼을 갱신한다.</summary>
    private void RefreshResultBoard()
    {
        if (_matchManager == null) return;

        if (winnerTitleText != null)
        {
            winnerTitleText.text = !string.IsNullOrEmpty(_matchManager.WinnerName)
                ? $"우승: {_matchManager.WinnerName}"
                : "매치 종료";
        }

        if (returnToRoomButton != null)
        {
            bool isHost = LobbyManager.CurrentRoom == null
                ? _matchManager.IsServerInitialized
                : LobbyManager.IsHost;
            returnToRoomButton.gameObject.SetActive(isHost);
            returnToRoomButton.interactable = _matchManager.State == MatchState.MatchEnded;
        }

        if (resultRowContainer == null || resultRowTemplate == null) return;

        _sortBuffer.Clear();
        IReadOnlyList<PlayerMatchRecord> records = _matchManager.Records;
        for (int i = 0; i < records.Count; i++)
        {
            _sortBuffer.Add(records[i]);
        }

        _sortBuffer.Sort(DeathmatchGameMode.CompareRecords);

        while (_rows.Count < _sortBuffer.Count)
        {
            _rows.Add(Instantiate(resultRowTemplate, resultRowContainer));
        }

        int localId = _matchManager.LocalConnection != null && _matchManager.LocalConnection.IsValid
            ? _matchManager.LocalConnection.ClientId
            : -1;

        for (int i = 0; i < _rows.Count; i++)
        {
            bool active = i < _sortBuffer.Count;
            _rows[i].gameObject.SetActive(active);
            if (!active) continue;

            PlayerMatchRecord rec = _sortBuffer[i];
            _rows[i].text = $"{i + 1}위   {rec.NickName}   —   {rec.Kills} 킬  /  {rec.Deaths} 데스";
            _rows[i].color = rec.ClientId == localId ? localPlayerColor : normalPlayerColor;
        }
    }

    /// <summary>대기실 자동 복귀까지 남은 카운트다운 문구를 갱신한다.</summary>
    private void UpdateReturnCountdown()
    {
        if (returnCountdownText == null || _matchManager == null) return;

        if (_matchManager.State == MatchState.ReturningToRoom)
        {
            returnCountdownText.text = LobbyManager.CurrentRoom != null
                ? "대기실로 복귀하는 중..."
                : "다음 라운드 준비 중...";
            return;
        }

        int sec = Mathf.CeilToInt(_matchManager.StateRemainingTime);
        returnCountdownText.text = LobbyManager.CurrentRoom != null
            ? $"{sec}초 후 대기실로 자동 복귀합니다."
            : $"{sec}초 후 다음 라운드가 시작됩니다.";
    }

    /// <summary><c>Esc</c> 일시정지 메뉴를 여닫고 플레이어 입력 및 커서 상태를 동기화한다.</summary>
    private void SetPauseMenuOpen(bool open)
    {
        _isPauseMenuOpen = open;
        IsPauseMenuOpen = open;
        if (pauseMenuRoot != null)
        {
            pauseMenuRoot.SetActive(open);
        }

        PlayerController localPlayer = PlayerController.LocalPlayer;
        bool isPlayingAlive = _matchManager == null
            || (_matchManager.IsGameplayInputAllowed
                && localPlayer != null
                && localPlayer.IsSpawned
                && !localPlayer.IsDead);

        if (open)
        {
            localPlayer?.SetInputLocked(true);
            localPlayer?.SetCameraInputEnabled(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (isPlayingAlive)
        {
            localPlayer?.SetInputLocked(false);
            localPlayer?.SetCameraInputEnabled(true);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}
