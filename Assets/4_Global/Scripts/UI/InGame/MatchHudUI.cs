using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>매치 제한 시간, 선두/내 점수, 시작 카운트다운, 리스폰 카운트다운 및 킬피드를 표시하는 인게임 매치 HUD.</summary>
public class MatchHudUI : MonoBehaviour
{
    // ***************************************************************
    // Types
    // ***************************************************************

    private struct ActiveKillFeedRow
    {
        public GameObject Root;
        public float ExpireTime;
    }

    // ***************************************************************
    // Constants
    // ***************************************************************

    private const int MaxKillFeedEntries = 5;
    private const float KillFeedDurationSeconds = 5f;

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>상단 타이머·점수 바 루트 오브젝트.</summary>
    [Header("상단 매치 바")]
    [SerializeField] private GameObject topBarRoot;

    /// <summary>남은 제한 시간 또는 대기 상태를 표시하는 텍스트.</summary>
    [SerializeField] private Text timerText;

    /// <summary>현재 1위 플레이어 정보를 표시하는 텍스트.</summary>
    [SerializeField] private Text leaderText;

    /// <summary>로컬 플레이어의 현재 순위와 킬/데스를 표시하는 텍스트.</summary>
    [SerializeField] private Text myScoreText;

    /// <summary>대기/카운트다운 중앙 안내 패널.</summary>
    [Header("대기 및 시작 카운트다운")]
    [SerializeField] private GameObject countdownRoot;

    /// <summary>대기 상태 또는 카운트다운 숫자를 표시하는 텍스트.</summary>
    [SerializeField] private Text countdownText;

    /// <summary>사망 후 리스폰 대기 카운트다운 패널.</summary>
    [Header("리스폰 카운트다운")]
    [SerializeField] private GameObject respawnRoot;

    /// <summary>리스폰까지 남은 시간을 표시하는 텍스트.</summary>
    [SerializeField] private Text respawnTimerText;

    /// <summary>킬피드 항목을 배치할 세로 컨테이너.</summary>
    [Header("킬피드 (우상단)")]
    [SerializeField] private Transform killFeedContainer;

    /// <summary>킬피드 한 줄에 사용할 비활성 텍스트 템플릿.</summary>
    [SerializeField] private Text killFeedRowTemplate;

    /// <summary>현재 화면에 표시 중인 킬피드 항목 목록.</summary>
    private readonly List<ActiveKillFeedRow> _killFeedRows = new(MaxKillFeedEntries);

    /// <summary>순위 계산에 재사용하는 레코드 버퍼.</summary>
    private readonly List<PlayerMatchRecord> _sortBuffer = new(16);

    /// <summary>이벤트를 구독한 <see cref="MatchManager"/> 참조.</summary>
    private MatchManager _matchManager;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        if (killFeedRowTemplate != null)
        {
            killFeedRowTemplate.gameObject.SetActive(false);
        }

        if (countdownRoot != null) countdownRoot.SetActive(false);
        if (respawnRoot != null) respawnRoot.SetActive(false);
    }

    private void OnEnable()
    {
        TryBindMatchManager();
    }

    private void OnDisable()
    {
        UnbindMatchManager();
    }

    private void Update()
    {
        if (_matchManager == null)
        {
            TryBindMatchManager();
            if (_matchManager == null) return;
        }

        UpdateTimersAndStateVisuals();
        UpdateKillFeedExpiry();
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>매치 상태가 바뀔 때 상단 바 및 카운트다운 표시를 즉시 갱신한다.</summary>
    private void HandleMatchStateChanged(MatchState state)
    {
        UpdateTimersAndStateVisuals();
        RefreshScoreSummary();
    }

    /// <summary>참가자 전적이 갱신될 때 선두 및 내 점수 요약을 다시 그린다.</summary>
    private void HandleRecordsUpdated()
    {
        RefreshScoreSummary();
    }

    /// <summary>처치 이벤트 수신 시 우상단 킬피드에 새 항목을 추가한다.</summary>
    private void HandleKillFeedReceived(KillFeedEntry entry)
    {
        if (killFeedContainer == null || killFeedRowTemplate == null) return;

        while (_killFeedRows.Count >= MaxKillFeedEntries)
        {
            if (_killFeedRows[0].Root != null)
            {
                Destroy(_killFeedRows[0].Root);
            }
            _killFeedRows.RemoveAt(0);
        }

        Text row = Instantiate(killFeedRowTemplate, killFeedContainer);
        row.gameObject.SetActive(true);

        if (entry.IsSuicide)
        {
            row.text = $"{entry.VictimName} 사망";
        }
        else
        {
            string weaponLabel = ResolveWeaponDisplayName(entry.WeaponId);
            row.text = string.IsNullOrEmpty(weaponLabel)
                ? $"{entry.KillerName}  →  {entry.VictimName}"
                : $"{entry.KillerName}  [{weaponLabel}]  {entry.VictimName}";
        }

        _killFeedRows.Add(new ActiveKillFeedRow
        {
            Root = row.gameObject,
            ExpireTime = Time.unscaledTime + KillFeedDurationSeconds
        });
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
        _matchManager.OnRecordsUpdated += HandleRecordsUpdated;
        _matchManager.OnKillFeedReceived += HandleKillFeedReceived;

        UpdateTimersAndStateVisuals();
        RefreshScoreSummary();
    }

    /// <summary>구독 중인 <see cref="MatchManager"/> 이벤트를 해제한다.</summary>
    private void UnbindMatchManager()
    {
        if (_matchManager == null) return;

        _matchManager.OnMatchStateChanged -= HandleMatchStateChanged;
        _matchManager.OnRecordsUpdated -= HandleRecordsUpdated;
        _matchManager.OnKillFeedReceived -= HandleKillFeedReceived;
        _matchManager = null;
    }

    /// <summary>현재 <see cref="MatchState"/>에 따라 제한 시간, 시작 카운트다운 및 리스폰 타이머를 갱신한다.</summary>
    private void UpdateTimersAndStateVisuals()
    {
        if (_matchManager == null || !_matchManager.IsMatchNetworkActive)
        {
            if (topBarRoot != null) topBarRoot.SetActive(false);
            if (countdownRoot != null) countdownRoot.SetActive(false);
            if (respawnRoot != null) respawnRoot.SetActive(false);
            return;
        }

        MatchState state = _matchManager.State;
        bool showTopBar = state is MatchState.WaitingForPlayers or MatchState.Countdown or MatchState.InProgress;
        if (topBarRoot != null)
        {
            topBarRoot.SetActive(showTopBar);
        }

        if (timerText != null)
        {
            if (state == MatchState.InProgress)
            {
                float remaining = _matchManager.StateRemainingTime;
                int totalSeconds = Mathf.CeilToInt(remaining);
                int minutes = totalSeconds / 60;
                int seconds = totalSeconds % 60;
                timerText.text = $"{minutes:00}:{seconds:00}";
            }
            else if (state == MatchState.Countdown)
            {
                timerText.text = $"시작 {Mathf.CeilToInt(_matchManager.StateRemainingTime)}초";
            }
            else if (state == MatchState.WaitingForPlayers)
            {
                timerText.text = "대기 중";
            }
        }

        bool showCountdown = state is MatchState.WaitingForPlayers or MatchState.Countdown;
        if (countdownRoot != null)
        {
            countdownRoot.SetActive(showCountdown);
        }

        if (showCountdown && countdownText != null)
        {
            if (state == MatchState.WaitingForPlayers)
            {
                int loaded = 0;
                IReadOnlyList<PlayerMatchRecord> records = _matchManager.Records;
                for (int i = 0; i < records.Count; i++)
                {
                    if (records[i].IsConnected && records[i].IsLoaded) loaded++;
                }
                countdownText.text = $"플레이어 대기 중 ({loaded} / {_matchManager.ExpectedPlayerCount})";
            }
            else
            {
                int sec = Mathf.Max(1, Mathf.CeilToInt(_matchManager.StateRemainingTime));
                countdownText.text = $"전투 시작까지 {sec}";
            }
        }

        bool showRespawn = false;
        float respawnRemaining = 0f;
        if (state == MatchState.InProgress
            && _matchManager.TryGetLocalRecord(out PlayerMatchRecord localRecord)
            && !localRecord.IsAlive
            && localRecord.RespawnEndServerTime > 0f)
        {
            respawnRemaining = Mathf.Max(0f, localRecord.RespawnEndServerTime - _matchManager.CurrentServerTime);
            showRespawn = true;
        }

        if (respawnRoot != null)
        {
            respawnRoot.SetActive(showRespawn);
        }

        if (showRespawn && respawnTimerText != null)
        {
            respawnTimerText.text = $"리스폰까지 {respawnRemaining:0.0}초";
        }
    }

    /// <summary>상단 바의 선두 플레이어와 로컬 플레이어 순위·킬/데스 표시를 갱신한다.</summary>
    private void RefreshScoreSummary()
    {
        if (_matchManager == null || !_matchManager.IsMatchNetworkActive) return;

        _sortBuffer.Clear();
        IReadOnlyList<PlayerMatchRecord> records = _matchManager.Records;
        for (int i = 0; i < records.Count; i++)
        {
            _sortBuffer.Add(records[i]);
        }

        _sortBuffer.Sort(DeathmatchGameMode.CompareRecords);

        if (leaderText != null)
        {
            if (_sortBuffer.Count > 0)
            {
                PlayerMatchRecord top = _sortBuffer[0];
                leaderText.text = $"1위 {top.NickName} ({top.Kills}K)";
            }
            else
            {
                leaderText.text = "1위 -";
            }
        }

        if (myScoreText != null)
        {
            int localId = _matchManager.LocalConnection != null && _matchManager.LocalConnection.IsValid
                ? _matchManager.LocalConnection.ClientId
                : -1;
            int myRank = -1;
            PlayerMatchRecord myRecord = default;
            for (int i = 0; i < _sortBuffer.Count; i++)
            {
                if (_sortBuffer[i].ClientId == localId)
                {
                    myRank = i + 1;
                    myRecord = _sortBuffer[i];
                    break;
                }
            }

            myScoreText.text = myRank > 0
                ? $"내 순위 {myRank}위  |  {myRecord.Kills}K / {myRecord.Deaths}D"
                : "0K / 0D";
        }
    }

    /// <summary>표시 시간이 만료된 킬피드 항목을 제거한다.</summary>
    private void UpdateKillFeedExpiry()
    {
        if (_killFeedRows.Count == 0) return;

        float now = Time.unscaledTime;
        for (int i = _killFeedRows.Count - 1; i >= 0; i--)
        {
            if (now >= _killFeedRows[i].ExpireTime)
            {
                if (_killFeedRows[i].Root != null)
                {
                    Destroy(_killFeedRows[i].Root);
                }
                _killFeedRows.RemoveAt(i);
            }
        }
    }

    /// <summary><paramref name="weaponId"/>에 대응하는 무기 표시 이름을 반환한다.</summary>
    private string ResolveWeaponDisplayName(string weaponId)
    {
        if (string.IsNullOrEmpty(weaponId)) return string.Empty;

        WeaponDatabaseSO db = _matchManager != null ? _matchManager.WeaponDatabase : null;
        WeaponDataSO data = db != null ? db.GetById(weaponId) : null;
        return data != null && !string.IsNullOrEmpty(data.WeaponName) ? data.WeaponName : weaponId;
    }
}
