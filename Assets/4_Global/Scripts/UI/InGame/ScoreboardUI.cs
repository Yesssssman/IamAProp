using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary><c>Tab</c> 키 입력 시 개인전 데스매치 실시간 순위표를 표시하는 스코어보드 UI.</summary>
public class ScoreboardUI : MonoBehaviour
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>표시/숨김을 일괄 전환할 스코어보드 루트 오브젝트.</summary>
    [Header("UI 참조")]
    [SerializeField] private GameObject contentRoot;

    /// <summary>순위 행을 배치할 세로 컨테이너.</summary>
    [SerializeField] private Transform rowContainer;

    /// <summary>각 플레이어 행 복제에 사용할 비활성 텍스트 템플릿.</summary>
    [SerializeField] private Text rowTemplate;

    /// <summary>로컬 플레이어 행 강조 색상.</summary>
    [Header("색상")]
    [SerializeField] private Color localPlayerColor = new Color(1f, 0.86f, 0.35f, 1f);

    /// <summary>일반 플레이어 행 색상.</summary>
    [SerializeField] private Color normalPlayerColor = Color.white;

    /// <summary>이탈 플레이어 행 색상.</summary>
    [SerializeField] private Color disconnectedPlayerColor = new Color(0.62f, 0.65f, 0.7f, 0.8f);

    /// <summary>재사용 중인 스코어보드 행 텍스트 풀.</summary>
    private readonly List<Text> _rows = new(16);

    /// <summary>순위 정렬에 재사용하는 레코드 버퍼.</summary>
    private readonly List<PlayerMatchRecord> _sortBuffer = new(16);

    /// <summary>이벤트를 구독한 <see cref="MatchManager"/> 참조.</summary>
    private MatchManager _matchManager;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        if (rowTemplate != null)
        {
            rowTemplate.gameObject.SetActive(false);
        }

        if (contentRoot != null)
        {
            contentRoot.SetActive(false);
        }
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
        }

        if (contentRoot == null) return;

        bool canOpen = _matchManager != null
            && _matchManager.IsMatchNetworkActive
            && _matchManager.State is MatchState.WaitingForPlayers or MatchState.Countdown or MatchState.InProgress;
        bool isTabHeld = Keyboard.current != null && Keyboard.current.tabKey.isPressed;
        bool shouldShow = canOpen && isTabHeld;

        if (contentRoot.activeSelf != shouldShow)
        {
            contentRoot.SetActive(shouldShow);
            if (shouldShow)
            {
                RefreshRows();
            }
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>전적 변경 시 스코어보드가 열려 있으면 즉시 갱신한다.</summary>
    private void HandleRecordsUpdated()
    {
        if (contentRoot != null && contentRoot.activeSelf)
        {
            RefreshRows();
        }
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>씬의 <see cref="MatchManager"/>를 찾아 전적 변경 이벤트를 구독한다.</summary>
    private void TryBindMatchManager()
    {
        _matchManager = MatchManager.Instance;
        if (_matchManager == null) return;

        _matchManager.OnRecordsUpdated += HandleRecordsUpdated;
    }

    /// <summary>구독 중인 <see cref="MatchManager"/> 이벤트를 해제한다.</summary>
    private void UnbindMatchManager()
    {
        if (_matchManager == null) return;

        _matchManager.OnRecordsUpdated -= HandleRecordsUpdated;
        _matchManager = null;
    }

    /// <summary>현재 매치 전적을 정렬해 스코어보드 행을 갱신한다.</summary>
    private void RefreshRows()
    {
        if (_matchManager == null || rowContainer == null || rowTemplate == null) return;

        _sortBuffer.Clear();
        IReadOnlyList<PlayerMatchRecord> records = _matchManager.Records;
        for (int i = 0; i < records.Count; i++)
        {
            _sortBuffer.Add(records[i]);
        }

        _sortBuffer.Sort(DeathmatchGameMode.CompareRecords);

        while (_rows.Count < _sortBuffer.Count)
        {
            Text created = Instantiate(rowTemplate, rowContainer);
            _rows.Add(created);
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
            string status = !rec.IsConnected ? "이탈" : (rec.IsAlive ? "생존" : "리스폰 대기");
            _rows[i].text = $"#{i + 1}   {rec.NickName}   |   {rec.Kills} K   /   {rec.Deaths} D   |   {status}";
            _rows[i].color = !rec.IsConnected
                ? disconnectedPlayerColor
                : (rec.ClientId == localId ? localPlayerColor : normalPlayerColor);
        }
    }
}
