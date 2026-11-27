using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Timing;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using Networking;
using Networking.Dto;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>인게임 매치 상태 머신, 모드 전략 실행, 플레이어 스폰·리스폰 및 전적 동기화를 총괄한다.</summary>
[RequireComponent(typeof(NetworkObject))]
public class MatchManager : NetworkBehaviour
{
    // ***************************************************************
    // Types
    // ***************************************************************

    /// <summary>서버가 연결별 리스폰·로드아웃·프롭 교체 주기를 추적하기 위한 런타임 슬롯.</summary>
    private sealed class ServerPlayerSlot
    {
        public NetworkConnection Connection;
        public NetworkObject CurrentPlayerObject;
        public NetworkObject LastPropPrefab;
        public PlayerLoadoutSelection Loadout;
        public float RespawnAtServerTime;
    }

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>게임 모드·맵·프롭 풀·무기 DB 매핑 에셋 (비워두면 <c>Resources/GameModeDatabase</c> 로드).</summary>
    [Header("데이터베이스 및 기본 설정")]
    [SerializeField] private GameModeDatabaseSO gameModeDatabase;

    /// <summary>로비 방 정보 없이 단독 테스트할 때 사용할 기본 모드 설정 (비워두면 DB 기본값 사용).</summary>
    [SerializeField] private GameModeConfigSO standaloneFallbackConfig;

    /// <summary>씬의 다중 스폰 지점 관리자 (비워두면 자동 탐색).</summary>
    [Header("씬 참조")]
    [SerializeField] private SpawnPointManager spawnPointManager;

    /// <summary>드롭 무기 수명 및 투척체 정리를 담당하는 관리자 (비워두면 자동 탐색).</summary>
    [SerializeField] private WorldItemLifecycleManager worldItemLifecycleManager;

    /// <summary>모든 클라이언트에 동기화되는 현재 매치 진행 단계.</summary>
    private readonly SyncVar<MatchState> _state = new(MatchState.WaitingForPlayers, new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>현재 단계가 만료되는 동기화 서버 시각 (<see cref="TimeManager.TicksToTime(TickType)"/> 기준).</summary>
    private readonly SyncVar<float> _stateEndServerTime = new(0f, new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>현재 진행 중인 로비 <see cref="EGameMode"/> 종류.</summary>
    private readonly SyncVar<EGameMode> _activeModeType = new(EGameMode.Deathmatch, new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>매치 종료 시 확정된 우승자 ClientId (없으면 -1).</summary>
    private readonly SyncVar<int> _winnerClientId = new(-1, new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>매치 종료 시 확정된 우승자 닉네임.</summary>
    private readonly SyncVar<string> _winnerName = new(string.Empty, new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>대기 단계에서 로드 완료를 기다리는 기대 참가 인원수.</summary>
    private readonly SyncVar<int> _expectedPlayerCount = new(1, new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>모든 클라이언트에 동기화되는 참가자별 매치 전적 목록.</summary>
    private readonly SyncList<PlayerMatchRecord> _records = new(new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>서버에서 실행 중인 현재 게임 모드 전략 인스턴스.</summary>
    private BaseGameMode _activeMode;

    /// <summary>현재 활성 모드 설정 캐시.</summary>
    private GameModeConfigSO _resolvedConfig;

    /// <summary>서버 전용 연결별 런타임 슬롯 목록.</summary>
    private readonly List<ServerPlayerSlot> _serverSlots = new(16);

    /// <summary>안전 스폰 거리 계산에 재사용하는 활성 플레이어 버퍼.</summary>
    private readonly List<PlayerController> _activePlayerBuffer = new(16);

    /// <summary>로컬 플레이어가 UI에서 선택한 최신 로드아웃.</summary>
    private PlayerLoadoutSelection _localSelectedLoadout;

    /// <summary><see cref="_localSelectedLoadout"/>가 1회 이상 초기화되었는지 여부.</summary>
    private bool _hasInitializedLocalLoadout;

    // ***************************************************************
    // Events
    // ***************************************************************

    /// <summary>매치 진행 단계가 변경될 때 발생하는 이벤트.</summary>
    public event Action<MatchState> OnMatchStateChanged;

    /// <summary>참가자 전적 목록(<see cref="Records"/>)이 갱신될 때 발생하는 이벤트.</summary>
    public event Action OnRecordsUpdated;

    /// <summary>처치 킬피드 이벤트가 수신될 때 발생하는 이벤트.</summary>
    public event Action<KillFeedEntry> OnKillFeedReceived;

    /// <summary>로컬 로드아웃 선택값이 변경될 때 발생하는 이벤트.</summary>
    public event Action<PlayerLoadoutSelection> OnLocalLoadoutChanged;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 씬에 활성화된 <see cref="MatchManager"/> 싱글톤 인스턴스.</summary>
    public static MatchManager Instance { get; private set; }

    /// <summary>네트워크 오브젝트가 초기화되고 스폰 완료되었는지 안전하게 반환한다.</summary>
    public bool IsMatchNetworkActive => NetworkObject != null && IsSpawned;

    /// <summary>현재 매치 진행 단계.</summary>
    public MatchState State => _state.Value;

    /// <summary>현재 진행 중인 게임 모드 종류.</summary>
    public EGameMode ActiveModeType => _activeModeType.Value;

    /// <summary>현재 매치의 설정 에셋.</summary>
    public GameModeConfigSO ActiveConfig => ResolveActiveConfig();

    /// <summary>연결된 게임 모드 데이터베이스.</summary>
    public GameModeDatabaseSO Database => gameModeDatabase != null ? gameModeDatabase : GameModeDatabaseSO.LoadDefault();

    /// <summary>로드아웃 UI와 검증에 사용하는 무기 데이터베이스.</summary>
    public WeaponDatabaseSO WeaponDatabase => Database != null ? Database.WeaponDatabase : null;

    /// <summary>현재 동기화된 참가자 전적 목록.</summary>
    public IReadOnlyList<PlayerMatchRecord> Records => _records;

    /// <summary>기대 참가 인원수.</summary>
    public int ExpectedPlayerCount => _expectedPlayerCount.Value;

    /// <summary>확정된 우승자 ClientId (없으면 -1).</summary>
    public int WinnerClientId => _winnerClientId.Value;

    /// <summary>확정된 우승자 닉네임.</summary>
    public string WinnerName => _winnerName.Value;

    /// <summary>현재 FishNet 동기화 서버 시각 (초 단위).</summary>
    public float CurrentServerTime => TimeManager != null ? (float)TimeManager.TicksToTime(TickType.Tick) : Time.time;

    /// <summary>현재 단계 만료까지 남은 시간 (초 단위).</summary>
    public float StateRemainingTime => _stateEndServerTime.Value > 0f
        ? Mathf.Max(0f, _stateEndServerTime.Value - CurrentServerTime)
        : 0f;

    /// <summary>현재 로컬 플레이어 이동·사격·무기 조작 입력을 허용하는 상태인지 여부.</summary>
    public bool IsGameplayInputAllowed => _state.Value == MatchState.InProgress;

    /// <summary>현재 서버에서 전투 및 데미지 판정을 허용하는 상태인지 여부.</summary>
    public bool IsCombatAllowed => _state.Value == MatchState.InProgress;

    /// <summary>현재 모드에서 사망 후 관전하기 버튼 표시를 허용하는지 여부.</summary>
    public bool AllowSpectateButton => ActiveConfig != null && ActiveConfig.AllowSpectateButton;

    /// <summary>현재 모드에서 로드아웃 선택 UI를 허용하는지 여부.</summary>
    public bool AllowLoadoutSelection => ActiveConfig != null && ActiveConfig.AllowLoadoutSelection;

    /// <summary>로컬 클라이언트가 선택한 현재 로드아웃.</summary>
    public PlayerLoadoutSelection LocalSelectedLoadout => _localSelectedLoadout;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        Instance = this;

        if (gameModeDatabase == null)
        {
            gameModeDatabase = GameModeDatabaseSO.LoadDefault();
        }

        if (spawnPointManager == null)
        {
            spawnPointManager = FindAnyObjectByType<SpawnPointManager>();
        }

        if (worldItemLifecycleManager == null)
        {
            worldItemLifecycleManager = FindAnyObjectByType<WorldItemLifecycleManager>();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (NetworkObject == null || !IsServerInitialized || !IsSpawned) return;

        float now = CurrentServerTime;
        switch (_state.Value)
        {
            case MatchState.WaitingForPlayers:
                UpdateWaitingForPlayers(now);
                break;

            case MatchState.Countdown:
                if (now >= _stateEndServerTime.Value)
                {
                    EnterInProgress();
                }
                break;

            case MatchState.InProgress:
                ProcessPendingRespawns(now);
                _activeMode?.OnServerUpdate(Time.deltaTime);
                if (_activeMode != null && _activeMode.TryEvaluateMatchEnd(out MatchEndResult endResult))
                {
                    EnterMatchEnded(in endResult);
                }
                break;

            case MatchState.MatchEnded:
                if (now >= _stateEndServerTime.Value)
                {
                    BeginReturningToRoom();
                }
                break;
        }
    }

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartServer()
    {
        _serverSlots.Clear();
        _records.Clear();
        _winnerClientId.Value = -1;
        _winnerName.Value = string.Empty;

        GameModeDatabaseSO db = Database;
        GameModeConfigSO selectedConfig = null;
        if (LobbyManager.CurrentRoom != null && db != null)
        {
            selectedConfig = db.GetConfigByName(LobbyManager.CurrentRoom.gameMode);
        }

        if (selectedConfig == null)
        {
            selectedConfig = standaloneFallbackConfig != null
                ? standaloneFallbackConfig
                : (db != null ? db.DefaultGameMode : null);
        }

        _resolvedConfig = selectedConfig;
        _activeModeType.Value = selectedConfig != null ? selectedConfig.ModeType : EGameMode.Deathmatch;
        _activeMode = selectedConfig != null ? selectedConfig.CreateRuntimeMode() : new DeathmatchGameMode();
        _activeMode.Initialize(this, selectedConfig);

        int expected = LobbyManager.CurrentRoom != null && LobbyManager.CurrentRoom.members != null && LobbyManager.CurrentRoom.members.Count > 0
            ? LobbyManager.CurrentRoom.members.Count
            : 1;
        _expectedPlayerCount.Value = expected;

        float waitingTimeout = selectedConfig != null ? selectedConfig.WaitingTimeoutSeconds : 15f;
        _state.Value = MatchState.WaitingForPlayers;
        _stateEndServerTime.Value = CurrentServerTime + waitingTimeout;

        SceneManager.OnClientLoadedStartScenes += HandleClientLoadedStartScenes;
        ServerManager.OnRemoteConnectionState += HandleRemoteConnectionState;
        PlayerHealth.OnAnyPlayerDied += HandleServerPlayerDied;

        // 이미 시작 씬 로드를 마친 연결(단독 테스트 호스트 등)도 즉시 처리
        foreach (NetworkConnection conn in ServerManager.Clients.Values)
        {
            if (conn != null && conn.IsValid && conn.LoadedStartScenes(true))
            {
                EnsureServerSlotAndSpawn(conn);
            }
        }
    }

    public override void OnStopServer()
    {
        if (SceneManager != null)
        {
            SceneManager.OnClientLoadedStartScenes -= HandleClientLoadedStartScenes;
        }

        if (ServerManager != null)
        {
            ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionState;
        }

        PlayerHealth.OnAnyPlayerDied -= HandleServerPlayerDied;
        _serverSlots.Clear();
        _activeMode = null;
    }

    public override void OnStartNetwork()
    {
        _state.OnChange += HandleStateChanged;
        _activeModeType.OnChange += HandleActiveModeTypeChanged;
        _records.OnChange += HandleRecordsChanged;
    }

    public override void OnStopNetwork()
    {
        _state.OnChange -= HandleStateChanged;
        _activeModeType.OnChange -= HandleActiveModeTypeChanged;
        _records.OnChange -= HandleRecordsChanged;
    }

    public override void OnStartClient()
    {
        PlayerController.OnLocalPlayerStarted += HandleLocalPlayerStarted;
        EnsureLocalDefaultLoadout();

        string accountHash = LobbyManager.LocalAccountHashcode ?? string.Empty;
        string nickName = ResolveLocalNickName();
        RegisterPlayerServerRpc(accountHash, nickName, _localSelectedLoadout);

        ApplyLocalStateSideEffects(_state.Value);
    }

    public override void OnStopClient()
    {
        PlayerController.OnLocalPlayerStarted -= HandleLocalPlayerStarted;
    }

    // ***************************************************************
    // Public Query & Client Control API
    // ***************************************************************

    /// <summary>서버에서 <paramref name="attacker"/>가 <paramref name="victim"/>에게 피해를 줄 수 있는지 판정한다.</summary>
    public bool CanPlayerTakeDamage(NetworkConnection attacker, PlayerHealth victim)
    {
        if (_state.Value != MatchState.InProgress) return false;
        return _activeMode == null || _activeMode.CanDamage(attacker, victim);
    }

    /// <summary>로컬 클라이언트의 현재 매치 전적 레코드를 조회한다.</summary>
    public bool TryGetLocalRecord(out PlayerMatchRecord record)
    {
        int localId = LocalConnection != null && LocalConnection.IsValid ? LocalConnection.ClientId : -1;
        return TryGetRecordByClientId(localId, out record);
    }

    /// <summary><paramref name="clientId"/>에 해당하는 매치 전적 레코드를 조회한다.</summary>
    public bool TryGetRecordByClientId(int clientId, out PlayerMatchRecord record)
    {
        for (int i = 0; i < _records.Count; i++)
        {
            if (_records[i].ClientId == clientId)
            {
                record = _records[i];
                return true;
            }
        }

        record = default;
        return false;
    }

    /// <summary><paramref name="clientId"/>의 표시 닉네임을 반환한다.</summary>
    public string GetPlayerNickName(int clientId)
    {
        if (TryGetRecordByClientId(clientId, out PlayerMatchRecord record) && !string.IsNullOrEmpty(record.NickName))
        {
            return record.NickName;
        }

        return clientId >= 0 ? $"Player {clientId}" : "Unknown";
    }

    /// <summary>로컬 플레이어의 리스폰/시작 무기 로드아웃을 변경하고 서버에 전송한다.</summary>
    public void RequestSelectLoadout(PlayerLoadoutSelection selection)
    {
        _localSelectedLoadout = SanitizeLoadout(selection);
        _hasInitializedLocalLoadout = true;
        OnLocalLoadoutChanged?.Invoke(_localSelectedLoadout);

        if (NetworkObject != null && IsClientInitialized && IsSpawned)
        {
            SelectLoadoutServerRpc(_localSelectedLoadout);
        }
    }

    /// <summary>결과창에서 방장이 즉시 대기실 복귀를 요청한다.</summary>
    public void RequestReturnToRoom()
    {
        if (NetworkObject == null || !IsClientInitialized || !IsSpawned) return;
        if (_state.Value != MatchState.MatchEnded) return;
        if (LobbyManager.CurrentRoom != null && !LobbyManager.IsHost) return;

        RequestReturnToRoomServerRpc();
    }

    // ***************************************************************
    // Server Control API (BaseGameMode 연동)
    // ***************************************************************

    /// <summary>서버에서 <paramref name="clientId"/>의 킬 수와 도달 시각을 갱신한다.</summary>
    [Server]
    public void RecordPlayerKill(int clientId, float serverTime)
    {
        int index = FindRecordIndex(clientId);
        if (index < 0) return;

        PlayerMatchRecord record = _records[index];
        record.Kills++;
        record.Score = record.Kills;
        record.LastKillServerTime = serverTime;
        _records[index] = record;
    }

    /// <summary>서버에서 <paramref name="clientId"/>의 데스, 생존 상태 및 리스폰 예정 시각을 갱신한다.</summary>
    [Server]
    public void RecordPlayerDeath(int clientId, float respawnEndServerTime, bool penalizeKill)
    {
        int index = FindRecordIndex(clientId);
        if (index < 0) return;

        PlayerMatchRecord record = _records[index];
        record.Deaths++;
        record.IsAlive = false;
        record.RespawnEndServerTime = respawnEndServerTime;
        if (penalizeKill && record.Kills > 0)
        {
            record.Kills--;
            record.Score = record.Kills;
        }
        _records[index] = record;
    }

    /// <summary>서버에서 <paramref name="conn"/>의 리스폰 시각을 예약한다.</summary>
    [Server]
    public void ScheduleRespawn(NetworkConnection conn, float respawnAtServerTime)
    {
        ServerPlayerSlot slot = FindServerSlot(conn.ClientId);
        if (slot == null) return;

        slot.RespawnAtServerTime = respawnAtServerTime;
    }

    /// <summary>서버에서 처치 킬피드를 모든 클라이언트에 브로드캐스트한다.</summary>
    [Server]
    public void BroadcastKillFeed(KillFeedEntry entry)
    {
        BroadcastKillFeedObserversRpc(entry);
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>클라이언트가 씬 로드 후 자신의 계정 해시코드, 닉네임 및 초기 로드아웃을 서버에 등록한다.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void RegisterPlayerServerRpc(string accountHashcode, string nickName, PlayerLoadoutSelection loadout, NetworkConnection sender = null)
    {
        if (sender == null || !sender.IsValid) return;

        ServerPlayerSlot slot = EnsureServerSlotAndSpawn(sender);
        if (slot == null) return;

        slot.Loadout = SanitizeLoadout(loadout);

        string safeNick = !string.IsNullOrWhiteSpace(nickName)
            ? nickName.Trim()
            : $"Player {sender.ClientId}";

        int recordIndex = FindRecordIndex(sender.ClientId);
        if (recordIndex >= 0)
        {
            PlayerMatchRecord record = _records[recordIndex];
            record.AccountHashcode = accountHashcode ?? string.Empty;
            record.NickName = safeNick;
            record.IsLoaded = true;
            record.IsConnected = true;
            _records[recordIndex] = record;
        }

        // 아직 본 경기 전이라면 등록된 초기 로드아웃을 즉시 반영한다.
        if (_state.Value is MatchState.WaitingForPlayers or MatchState.Countdown && slot.CurrentPlayerObject != null && slot.CurrentPlayerObject.IsSpawned)
        {
            PlayerWeaponInventory inventory = slot.CurrentPlayerObject.GetComponent<PlayerWeaponInventory>();
            inventory?.ApplyLoadout(slot.Loadout);
        }

        _activeMode?.OnPlayerRegistered(sender);
    }

    /// <summary>클라이언트가 UI에서 선택한 무기 로드아웃을 서버에 반영한다.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void SelectLoadoutServerRpc(PlayerLoadoutSelection selection, NetworkConnection sender = null)
    {
        if (sender == null || !sender.IsValid) return;

        ServerPlayerSlot slot = FindServerSlot(sender.ClientId);
        if (slot == null) return;

        slot.Loadout = SanitizeLoadout(selection);

        // 대기/카운트다운 중에는 선택 즉시 손에 장착된 무기를 갱신해 미리보기를 제공한다.
        if (_state.Value is MatchState.WaitingForPlayers or MatchState.Countdown
            && slot.CurrentPlayerObject != null
            && slot.CurrentPlayerObject.IsSpawned)
        {
            PlayerWeaponInventory inventory = slot.CurrentPlayerObject.GetComponent<PlayerWeaponInventory>();
            inventory?.ApplyLoadout(slot.Loadout);
        }
    }

    /// <summary>방장이 결과창에서 즉시 대기실 복귀를 요청할 때 서버에서 전환을 시작한다.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestReturnToRoomServerRpc(NetworkConnection sender = null)
    {
        if (_state.Value != MatchState.MatchEnded) return;
        if (sender == null || !sender.IsValid) return;

        // 호스트(서버와 동일한 로컬 클라이언트 연결)만 즉시 복귀를 트리거할 수 있다.
        if (LocalConnection == null || sender.ClientId != LocalConnection.ClientId) return;

        BeginReturningToRoom();
    }

    /// <summary>모든 클라이언트에 킬피드 항목을 전달한다.</summary>
    [ObserversRpc]
    private void BroadcastKillFeedObserversRpc(KillFeedEntry entry)
    {
        OnKillFeedReceived?.Invoke(entry);
    }

    /// <summary>호스트가 웹 서버에 게임 종료를 통보하기 직전 클라이언트에 정상 복귀 의도를 선행 공지한다.</summary>
    [ObserversRpc]
    private void NotifyReturningToRoomObserversRpc()
    {
        MatchSessionBridge.Instance?.MarkReturningToRoom();
    }

    // ***************************************************************
    // Server State Machine Transitions
    // ***************************************************************

    /// <summary>대기 단계에서 기대 인원 로드 완료 또는 타임아웃을 검사해 카운트다운으로 전이한다.</summary>
    [Server]
    private void UpdateWaitingForPlayers(float now)
    {
        int loadedCount = 0;
        for (int i = 0; i < _records.Count; i++)
        {
            if (_records[i].IsConnected && _records[i].IsLoaded)
            {
                loadedCount++;
            }
        }

        bool allLoaded = loadedCount >= Mathf.Max(1, _expectedPlayerCount.Value);
        bool timedOut = loadedCount > 0 && now >= _stateEndServerTime.Value;
        if (!allLoaded && !timedOut) return;

        float countdown = ActiveConfig != null ? ActiveConfig.CountdownSeconds : 5f;
        if (countdown <= 0f)
        {
            EnterInProgress();
            return;
        }

        _state.Value = MatchState.Countdown;
        _stateEndServerTime.Value = CurrentServerTime + countdown;
    }

    /// <summary>본 경기(<see cref="MatchState.InProgress"/>)로 전환하고 생존 플레이어에 시작 무적을 부여한다.</summary>
    [Server]
    private void EnterInProgress()
    {
        float duration = ActiveConfig != null ? ActiveConfig.MatchDurationSeconds : 300f;
        float invuln = ActiveConfig != null ? ActiveConfig.SpawnInvulnerabilitySeconds : 2f;

        for (int i = 0; i < _serverSlots.Count; i++)
        {
            ServerPlayerSlot slot = _serverSlots[i];
            if (slot.CurrentPlayerObject != null && slot.CurrentPlayerObject.IsSpawned)
            {
                PlayerHealth health = slot.CurrentPlayerObject.GetComponent<PlayerHealth>();
                health?.SetInvulnerable(invuln);
            }
        }

        _state.Value = MatchState.InProgress;
        _stateEndServerTime.Value = duration > 0f ? CurrentServerTime + duration : 0f;
        _activeMode?.OnMatchStarted();
    }

    /// <summary>매치 종료(<see cref="MatchState.MatchEnded"/>)로 전환하고 승자를 확정한다.</summary>
    [Server]
    private void EnterMatchEnded(in MatchEndResult result)
    {
        // 대기 중인 리스폰 예약 및 비행 중인 투척체를 일괄 정리한다.
        for (int i = 0; i < _serverSlots.Count; i++)
        {
            _serverSlots[i].RespawnAtServerTime = 0f;
        }

        worldItemLifecycleManager?.CleanupActiveProjectiles();

        _winnerClientId.Value = result.WinnerClientId;
        _winnerName.Value = result.WinnerName ?? string.Empty;

        float resultDuration = ActiveConfig != null ? ActiveConfig.ResultDisplaySeconds : 8f;
        _state.Value = MatchState.MatchEnded;
        _stateEndServerTime.Value = CurrentServerTime + resultDuration;
        _activeMode?.OnMatchEnded(in result);
    }

    /// <summary>대기실 복귀(<see cref="MatchState.ReturningToRoom"/>) 시퀀스를 시작한다.</summary>
    [Server]
    private void BeginReturningToRoom()
    {
        if (_state.Value == MatchState.ReturningToRoom) return;

        _state.Value = MatchState.ReturningToRoom;
        _stateEndServerTime.Value = 0f;
        NotifyReturningToRoomObserversRpc();

        if (LobbyManager.CurrentRoom != null)
        {
            if (MatchSessionBridge.Instance != null)
            {
                MatchSessionBridge.Instance.BeginHostReturnToRoom();
            }
            else
            {
                _ = LobbyManager.EndGame();
            }
            return;
        }

        // 로비 방 없이 에디터에서 단독 실행한 경우 매치를 새 라운드로 재시작한다.
        RestartStandaloneMatch();
    }

    /// <summary>로비 없이 단독 테스트 중인 매치를 초기화해 다음 라운드를 시작한다.</summary>
    [Server]
    private void RestartStandaloneMatch()
    {
        _winnerClientId.Value = -1;
        _winnerName.Value = string.Empty;

        for (int i = 0; i < _records.Count; i++)
        {
            PlayerMatchRecord record = _records[i];
            record.Kills = 0;
            record.Deaths = 0;
            record.Score = 0;
            record.IsAlive = true;
            record.RespawnEndServerTime = 0f;
            record.LastKillServerTime = 0f;
            _records[i] = record;
        }

        for (int i = 0; i < _serverSlots.Count; i++)
        {
            ServerPlayerSlot slot = _serverSlots[i];
            slot.RespawnAtServerTime = 0f;
            if (slot.Connection != null && slot.Connection.IsValid && slot.Connection.IsActive)
            {
                SpawnOrReplacePlayerProp(slot);
            }
        }

        float countdown = ActiveConfig != null ? ActiveConfig.CountdownSeconds : 5f;
        _state.Value = MatchState.Countdown;
        _stateEndServerTime.Value = CurrentServerTime + countdown;
    }

    // ***************************************************************
    // Server Spawning & Respawn
    // ***************************************************************

    /// <summary>리스폰 대기 시간이 끝난 플레이어의 시체를 디스폰하고 새 랜덤 프롭으로 리스폰한다.</summary>
    [Server]
    private void ProcessPendingRespawns(float now)
    {
        for (int i = 0; i < _serverSlots.Count; i++)
        {
            ServerPlayerSlot slot = _serverSlots[i];
            if (slot.RespawnAtServerTime <= 0f || now < slot.RespawnAtServerTime) continue;

            slot.RespawnAtServerTime = 0f;
            if (slot.Connection == null || !slot.Connection.IsValid || !slot.Connection.IsActive) continue;

            SpawnOrReplacePlayerProp(slot);
        }
    }

    /// <summary><paramref name="conn"/>의 서버 슬롯과 전적 레코드를 확보하고 초기 프롭을 스폰한다.</summary>
    [Server]
    private ServerPlayerSlot EnsureServerSlotAndSpawn(NetworkConnection conn)
    {
        if (conn == null || !conn.IsValid) return null;

        ServerPlayerSlot slot = FindServerSlot(conn.ClientId);
        if (slot == null)
        {
            slot = new ServerPlayerSlot
            {
                Connection = conn,
                Loadout = BuildDefaultLoadout()
            };
            _serverSlots.Add(slot);
        }

        if (FindRecordIndex(conn.ClientId) < 0)
        {
            _records.Add(new PlayerMatchRecord
            {
                ClientId = conn.ClientId,
                AccountHashcode = string.Empty,
                NickName = $"Player {conn.ClientId}",
                TeamId = 0,
                Kills = 0,
                Deaths = 0,
                Score = 0,
                IsAlive = true,
                IsLoaded = false,
                IsConnected = true,
                RespawnEndServerTime = 0f,
                LastKillServerTime = 0f
            });
        }

        if (slot.CurrentPlayerObject == null || !slot.CurrentPlayerObject.IsSpawned)
        {
            SpawnOrReplacePlayerProp(slot);
        }

        return slot;
    }

    /// <summary>기존 플레이어 오브젝트를 디스폰하고 <see cref="PropPoolSO"/>에서 랜덤 프롭을 안전 스폰 지점에 스폰한다.</summary>
    [Server]
    private void SpawnOrReplacePlayerProp(ServerPlayerSlot slot)
    {
        if (slot == null || slot.Connection == null || !slot.Connection.IsValid) return;

        if (slot.CurrentPlayerObject != null && slot.CurrentPlayerObject.IsSpawned)
        {
            ServerManager.Despawn(slot.CurrentPlayerObject, DespawnType.Destroy);
            slot.CurrentPlayerObject = null;
        }

        PropPoolSO propPool = Database != null ? Database.DefaultPropPool : null;
        bool randomizeProp = ActiveConfig == null || ActiveConfig.RandomizePropOnRespawn;
        NetworkObject prefab = null;

        if (propPool != null)
        {
            prefab = randomizeProp || slot.LastPropPrefab == null
                ? propPool.GetRandomPropPrefab(slot.LastPropPrefab)
                : slot.LastPropPrefab;
        }

        if (prefab == null)
        {
            DynamicSpawner fallbackSpawner = FindAnyObjectByType<DynamicSpawner>();
            GameObject fallbackGo = fallbackSpawner != null ? fallbackSpawner.FallbackPlayerPrefab : null;
            prefab = fallbackGo != null ? fallbackGo.GetComponent<NetworkObject>() : null;
        }

        if (prefab == null)
        {
            Debug.LogError("[MatchManager] 스폰할 플레이어 프롭 프리팹을 찾을 수 없습니다. PropPoolSO를 확인하세요.");
            return;
        }

        slot.LastPropPrefab = prefab;

        _activePlayerBuffer.Clear();
        for (int i = 0; i < _serverSlots.Count; i++)
        {
            NetworkObject obj = _serverSlots[i].CurrentPlayerObject;
            if (obj == null || !obj.IsSpawned) continue;

            PlayerController pc = obj.GetComponent<PlayerController>();
            if (pc != null)
            {
                _activePlayerBuffer.Add(pc);
            }
        }

        Pose spawnPose = spawnPointManager != null
            ? spawnPointManager.SelectSafeSpawnPoint(slot.Connection, _activePlayerBuffer)
            : new Pose(new Vector3(0f, 1f, 0f), Quaternion.identity);

        NetworkObject instance = Instantiate(prefab, spawnPose.position, spawnPose.rotation);
        ServerManager.Spawn(instance, slot.Connection);
        slot.CurrentPlayerObject = instance;

        PlayerWeaponInventory inventory = instance.GetComponent<PlayerWeaponInventory>();
        inventory?.ApplyLoadout(slot.Loadout);

        if (_state.Value == MatchState.InProgress && ActiveConfig != null && ActiveConfig.SpawnInvulnerabilitySeconds > 0f)
        {
            PlayerHealth health = instance.GetComponent<PlayerHealth>();
            health?.SetInvulnerable(ActiveConfig.SpawnInvulnerabilitySeconds);
        }

        int recordIndex = FindRecordIndex(slot.Connection.ClientId);
        if (recordIndex >= 0)
        {
            PlayerMatchRecord record = _records[recordIndex];
            record.IsAlive = true;
            record.RespawnEndServerTime = 0f;
            _records[recordIndex] = record;
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>클라이언트가 시작 씬 로드를 완료하면 서버에서 초기 프롭을 스폰한다.</summary>
    private void HandleClientLoadedStartScenes(NetworkConnection conn, bool asServer)
    {
        if (!asServer || !IsServerInitialized) return;
        if (_state.Value is MatchState.MatchEnded or MatchState.ReturningToRoom) return;

        EnsureServerSlotAndSpawn(conn);
    }

    /// <summary>클라이언트 연결 해제 시 리스폰 예약을 취소하고 전적 상태를 갱신한다.</summary>
    private void HandleRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (!IsServerInitialized || conn == null) return;
        if (args.ConnectionState != RemoteConnectionState.Stopped) return;

        ServerPlayerSlot slot = FindServerSlot(conn.ClientId);
        if (slot != null)
        {
            slot.RespawnAtServerTime = 0f;
            slot.CurrentPlayerObject = null;
            _serverSlots.Remove(slot);
        }

        int recordIndex = FindRecordIndex(conn.ClientId);
        if (recordIndex >= 0)
        {
            PlayerMatchRecord record = _records[recordIndex];
            record.IsAlive = false;
            record.IsConnected = false;
            record.RespawnEndServerTime = 0f;
            _records[recordIndex] = record;
        }

        _activeMode?.OnPlayerDisconnected(conn);
    }

    /// <summary>서버에서 임의의 플레이어가 사망하면 현재 게임 모드 규칙에 위임한다.</summary>
    private void HandleServerPlayerDied(PlayerHealth victim, NetworkConnection attacker, NetworkObject killerObject)
    {
        if (!IsServerInitialized || _state.Value != MatchState.InProgress) return;
        _activeMode?.OnPlayerKilled(victim, attacker, killerObject);
    }

    /// <summary>로컬 소유자 플레이어가 새로 스폰되면 현재 매치 상태의 입력·커서 정책을 즉시 동기화한다.</summary>
    private void HandleLocalPlayerStarted(PlayerController player)
    {
        ApplyLocalStateSideEffects(_state.Value);
    }

    /// <summary><see cref="_state"/> 동기화 변경 시 로컬 입력·카메라·섬광 상태를 갱신하고 구독자에게 알린다.</summary>
    private void HandleStateChanged(MatchState previousValue, MatchState nextValue, bool asServer)
    {
        if (!asServer && IsServerInitialized) return;

        ApplyLocalStateSideEffects(nextValue);
        OnMatchStateChanged?.Invoke(nextValue);
    }

    /// <summary><see cref="_activeModeType"/> 변경 시 클라이언트 측 설정 캐시를 갱신한다.</summary>
    private void HandleActiveModeTypeChanged(EGameMode previousValue, EGameMode nextValue, bool asServer)
    {
        if (!asServer && IsServerInitialized) return;
        _resolvedConfig = null;
        ResolveActiveConfig();
    }

    /// <summary><see cref="_records"/> 변경 시 UI 구독자에게 알린다.</summary>
    private void HandleRecordsChanged(
        SyncListOperation op,
        int index,
        PlayerMatchRecord oldItem,
        PlayerMatchRecord newItem,
        bool asServer)
    {
        if (!asServer && IsServerInitialized) return;
        OnRecordsUpdated?.Invoke();
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>로컬 플레이어와 씬 카메라·섬광탄 효과를 <paramref name="state"/>에 맞게 동기화한다.</summary>
    private void ApplyLocalStateSideEffects(MatchState state)
    {
        bool allowInput = state == MatchState.InProgress;
        PlayerController localPlayer = PlayerController.LocalPlayer;
        if (localPlayer != null && localPlayer.IsSpawned && localPlayer.IsOwner)
        {
            localPlayer.SetInputLocked(!allowInput);
        }

        DeathCamController deathCam = FindAnyObjectByType<DeathCamController>(FindObjectsInactive.Include);
        if (deathCam != null)
        {
            deathCam.SetAllowSpectateButton(AllowSpectateButton);
            if (state is MatchState.MatchEnded or MatchState.ReturningToRoom)
            {
                deathCam.ResetDeathCam();
            }
        }

        if (state is MatchState.MatchEnded or MatchState.ReturningToRoom)
        {
            FlashbangEffectController.Instance?.ResetEffects();
            FpsAdsController ads = FindAnyObjectByType<FpsAdsController>(FindObjectsInactive.Include);
            ads?.ForceExitAds();
        }

        bool isLocalAlive = localPlayer != null && localPlayer.IsSpawned && !localPlayer.IsDead;
        if (allowInput && isLocalAlive && !MatchResultUI.IsPauseMenuOpen)
        {
            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            localPlayer.SetCameraInputEnabled(true);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            localPlayer?.SetCameraInputEnabled(false);

            if (localPlayer == null && deathCam != null && deathCam.PlayerCamera != null)
            {
                CinemachineInputAxisController axis = deathCam.PlayerCamera.GetComponent<CinemachineInputAxisController>();
                if (axis != null)
                {
                    axis.enabled = false;
                }
            }
        }
    }

    /// <summary>현재 활성 모드 설정 에셋을 해석해 캐시한다.</summary>
    private GameModeConfigSO ResolveActiveConfig()
    {
        if (_resolvedConfig != null && _resolvedConfig.ModeType == _activeModeType.Value)
        {
            return _resolvedConfig;
        }

        GameModeDatabaseSO db = Database;
        _resolvedConfig = db != null ? db.GetConfig(_activeModeType.Value) : standaloneFallbackConfig;
        if (_resolvedConfig == null)
        {
            _resolvedConfig = standaloneFallbackConfig;
        }
        return _resolvedConfig;
    }

    /// <summary>로컬 로드아웃 선택값이 비어 있으면 무기 DB 기준 기본값으로 초기화한다.</summary>
    private void EnsureLocalDefaultLoadout()
    {
        if (_hasInitializedLocalLoadout) return;

        _localSelectedLoadout = BuildDefaultLoadout();
        _hasInitializedLocalLoadout = true;
        OnLocalLoadoutChanged?.Invoke(_localSelectedLoadout);
    }

    /// <summary><see cref="WeaponDatabaseSO"/> 기준 슬롯별 기본 로드아웃을 구성한다.</summary>
    private PlayerLoadoutSelection BuildDefaultLoadout()
    {
        PlayerLoadoutSelection result = default;
        WeaponDatabaseSO db = WeaponDatabase;
        if (db == null) return result;

        if (db.DefaultMeleeWeapon != null)
        {
            result.MeleeWeaponId = db.DefaultMeleeWeapon.WeaponId;
        }

        if (db.DefaultThrowableWeapon != null)
        {
            result.ThrowableWeaponId = db.DefaultThrowableWeapon.WeaponId;
        }

        WeaponDataSO[] weapons = db.Weapons;
        if (weapons != null)
        {
            for (int i = 0; i < weapons.Length; i++)
            {
                WeaponDataSO weapon = weapons[i];
                if (weapon == null || string.IsNullOrEmpty(weapon.WeaponId)) continue;

                if (string.IsNullOrEmpty(result.PrimaryWeaponId) && weapon.SlotType == EWeaponSlotType.Primary)
                {
                    result.PrimaryWeaponId = weapon.WeaponId;
                }
                else if (string.IsNullOrEmpty(result.SecondaryWeaponId) && weapon.SlotType == EWeaponSlotType.Secondary)
                {
                    result.SecondaryWeaponId = weapon.WeaponId;
                }
                else if (string.IsNullOrEmpty(result.MeleeWeaponId) && weapon.SlotType == EWeaponSlotType.Melee)
                {
                    result.MeleeWeaponId = weapon.WeaponId;
                }
                else if (string.IsNullOrEmpty(result.ThrowableWeaponId) && weapon.SlotType == EWeaponSlotType.Throwable)
                {
                    result.ThrowableWeaponId = weapon.WeaponId;
                }
            }
        }

        return result;
    }

    /// <summary>전달받은 <paramref name="selection"/>의 각 무기 ID가 <see cref="WeaponDatabaseSO"/>와 슬롯 규격에 맞는지 검증한다.</summary>
    private PlayerLoadoutSelection SanitizeLoadout(PlayerLoadoutSelection selection)
    {
        PlayerLoadoutSelection fallback = BuildDefaultLoadout();
        WeaponDatabaseSO db = WeaponDatabase;
        if (db == null) return fallback;

        selection.PrimaryWeaponId = ValidateWeaponForSlot(db, selection.PrimaryWeaponId, EWeaponSlotType.Primary, fallback.PrimaryWeaponId);
        selection.SecondaryWeaponId = ValidateWeaponForSlot(db, selection.SecondaryWeaponId, EWeaponSlotType.Secondary, fallback.SecondaryWeaponId);
        selection.MeleeWeaponId = ValidateWeaponForSlot(db, selection.MeleeWeaponId, EWeaponSlotType.Melee, fallback.MeleeWeaponId);
        selection.ThrowableWeaponId = ValidateWeaponForSlot(db, selection.ThrowableWeaponId, EWeaponSlotType.Throwable, fallback.ThrowableWeaponId);
        return selection;
    }

    /// <summary><paramref name="weaponId"/>가 <paramref name="slot"/>에 유효하면 반환하고 아니면 <paramref name="fallbackId"/>를 반환한다.</summary>
    private static string ValidateWeaponForSlot(WeaponDatabaseSO db, string weaponId, EWeaponSlotType slot, string fallbackId)
    {
        if (!string.IsNullOrEmpty(weaponId)
            && db.TryGetById(weaponId, out WeaponDataSO data)
            && data != null
            && data.SlotType == slot)
        {
            return weaponId;
        }

        return fallbackId ?? string.Empty;
    }

    /// <summary>로컬 플레이어의 닉네임을 로비 멤버 목록 또는 로그인 정보에서 조회한다.</summary>
    private string ResolveLocalNickName()
    {
        if (LobbyManager.CurrentRoom != null && LobbyManager.CurrentRoom.members != null)
        {
            RoomMemberDTO me = LobbyManager.CurrentRoom.members.Find(
                m => m.gameAccountHashcode == LobbyManager.LocalAccountHashcode);
            if (me != null && !string.IsNullOrEmpty(me.nickName))
            {
                return me.nickName;
            }
        }

        if (!string.IsNullOrEmpty(LoginManager.NickName))
        {
            return LoginManager.NickName;
        }

        int clientId = LocalConnection != null && LocalConnection.IsValid ? LocalConnection.ClientId : 0;
        return $"Player {clientId}";
    }

    /// <summary><see cref="_serverSlots"/>에서 <paramref name="clientId"/> 슬롯을 찾는다.</summary>
    private ServerPlayerSlot FindServerSlot(int clientId)
    {
        for (int i = 0; i < _serverSlots.Count; i++)
        {
            if (_serverSlots[i].Connection != null && _serverSlots[i].Connection.ClientId == clientId)
            {
                return _serverSlots[i];
            }
        }

        return null;
    }

    /// <summary><see cref="_records"/>에서 <paramref name="clientId"/>의 인덱스를 찾는다.</summary>
    private int FindRecordIndex(int clientId)
    {
        for (int i = 0; i < _records.Count; i++)
        {
            if (_records[i].ClientId == clientId)
            {
                return i;
            }
        }

        return -1;
    }
}
