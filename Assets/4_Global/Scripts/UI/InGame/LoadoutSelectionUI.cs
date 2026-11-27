using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>카운트다운 및 사망 후 리스폰 대기 중 주무기·보조무기·근접·투척 로드아웃을 선택하는 UI.</summary>
public class LoadoutSelectionUI : MonoBehaviour
{
    // ***************************************************************
    // Types
    // ***************************************************************

    private struct OptionButtonEntry
    {
        public EWeaponSlotType Slot;
        public string WeaponId;
        public Image Background;
    }

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>표시/숨김을 일괄 전환할 로드아웃 선택 패널 루트.</summary>
    [Header("패널 및 컨테이너")]
    [SerializeField] private GameObject contentRoot;

    /// <summary>주무기(4번 슬롯) 선택 버튼 컨테이너.</summary>
    [SerializeField] private Transform primaryContainer;

    /// <summary>보조무기(3번 슬롯) 선택 버튼 컨테이너.</summary>
    [SerializeField] private Transform secondaryContainer;

    /// <summary>근접무기(2번 슬롯) 선택 버튼 컨테이너.</summary>
    [SerializeField] private Transform meleeContainer;

    /// <summary>투척무기(5번 슬롯) 선택 버튼 컨테이너.</summary>
    [SerializeField] private Transform throwableContainer;

    /// <summary>각 무기 선택 버튼 생성에 사용할 비활성 버튼 템플릿.</summary>
    [SerializeField] private Button optionButtonTemplate;

    /// <summary>미선택 상태 버튼 배경 색상.</summary>
    [Header("선택 강조 색상")]
    [SerializeField] private Color normalButtonColor = new Color(0.16f, 0.19f, 0.25f, 0.92f);

    /// <summary>선택된 무기 버튼 배경 색상.</summary>
    [SerializeField] private Color selectedButtonColor = new Color(0.88f, 0.64f, 0.16f, 0.98f);

    /// <summary>생성된 모든 무기 선택 버튼 목록.</summary>
    private readonly List<OptionButtonEntry> _optionButtons = new(16);

    /// <summary>이벤트를 구독한 <see cref="MatchManager"/> 참조.</summary>
    private MatchManager _matchManager;

    /// <summary>무기 버튼 목록이 1회 구축되었는지 여부.</summary>
    private bool _isBuilt;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        if (optionButtonTemplate != null)
        {
            optionButtonTemplate.gameObject.SetActive(false);
        }

        if (contentRoot != null)
        {
            contentRoot.SetActive(false);
        }
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

        UpdateVisibility();
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>매치 상태 전환 시 로드아웃 패널 표시 여부를 갱신한다.</summary>
    private void HandleMatchStateChanged(MatchState state)
    {
        UpdateVisibility();
    }

    /// <summary>로컬 로드아웃 선택값 변경 시 선택 하이라이트를 갱신한다.</summary>
    private void HandleLocalLoadoutChanged(PlayerLoadoutSelection selection)
    {
        RefreshHighlights(selection);
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>씬의 <see cref="MatchManager"/>를 찾아 이벤트를 구독하고 버튼 목록을 구축한다.</summary>
    private void TryBindMatchManager()
    {
        if (_matchManager != null) return;

        _matchManager = MatchManager.Instance;
        if (_matchManager == null) return;

        _matchManager.OnMatchStateChanged += HandleMatchStateChanged;
        _matchManager.OnLocalLoadoutChanged += HandleLocalLoadoutChanged;

        BuildOptionButtonsIfNeeded();
        RefreshHighlights(_matchManager.LocalSelectedLoadout);
        UpdateVisibility();
    }

    /// <summary>구독 중인 <see cref="MatchManager"/> 이벤트를 해제한다.</summary>
    private void UnbindMatchManager()
    {
        if (_matchManager == null) return;

        _matchManager.OnMatchStateChanged -= HandleMatchStateChanged;
        _matchManager.OnLocalLoadoutChanged -= HandleLocalLoadoutChanged;
        _matchManager = null;
    }

    /// <summary><see cref="WeaponDatabaseSO"/>의 등록 무기를 슬롯 카테고리별로 버튼 생성한다.</summary>
    private void BuildOptionButtonsIfNeeded()
    {
        if (_isBuilt || _matchManager == null || optionButtonTemplate == null) return;

        WeaponDatabaseSO db = _matchManager.WeaponDatabase;
        if (db == null || db.Weapons == null) return;

        _isBuilt = true;
        WeaponDataSO[] weapons = db.Weapons;
        for (int i = 0; i < weapons.Length; i++)
        {
            WeaponDataSO data = weapons[i];
            if (data == null || string.IsNullOrEmpty(data.WeaponId)) continue;

            Transform parent = data.SlotType switch
            {
                EWeaponSlotType.Primary => primaryContainer,
                EWeaponSlotType.Secondary => secondaryContainer,
                EWeaponSlotType.Melee => meleeContainer,
                EWeaponSlotType.Throwable => throwableContainer,
                _ => null
            };
            if (parent == null) continue;

            Button btn = Instantiate(optionButtonTemplate, parent);
            btn.gameObject.SetActive(true);
            btn.name = $"Btn_{data.SlotType}_{data.WeaponId}";

            Text label = btn.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.text = !string.IsNullOrEmpty(data.WeaponName) ? data.WeaponName : data.WeaponId;
            }

            EWeaponSlotType capturedSlot = data.SlotType;
            string capturedId = data.WeaponId;
            btn.onClick.AddListener(() => SelectWeapon(capturedSlot, capturedId));

            _optionButtons.Add(new OptionButtonEntry
            {
                Slot = capturedSlot,
                WeaponId = capturedId,
                Background = btn.GetComponent<Image>()
            });
        }
    }

    /// <summary>선택된 카테고리의 무기를 <see cref="MatchManager"/>에 반영한다.</summary>
    private void SelectWeapon(EWeaponSlotType slot, string weaponId)
    {
        if (_matchManager == null) return;

        PlayerLoadoutSelection current = _matchManager.LocalSelectedLoadout;
        switch (slot)
        {
            case EWeaponSlotType.Primary:
                current.PrimaryWeaponId = weaponId;
                break;
            case EWeaponSlotType.Secondary:
                current.SecondaryWeaponId = weaponId;
                break;
            case EWeaponSlotType.Melee:
                current.MeleeWeaponId = weaponId;
                break;
            case EWeaponSlotType.Throwable:
                current.ThrowableWeaponId = weaponId;
                break;
        }

        _matchManager.RequestSelectLoadout(current);
    }

    /// <summary>카운트다운 중이거나 본 경기에서 사망 리스폰 대기 중일 때만 로드아웃 패널을 표시한다.</summary>
    private void UpdateVisibility()
    {
        if (contentRoot == null) return;

        if (_matchManager == null || !_matchManager.IsMatchNetworkActive || !_matchManager.AllowLoadoutSelection)
        {
            if (contentRoot.activeSelf) contentRoot.SetActive(false);
            return;
        }

        MatchState state = _matchManager.State;
        bool shouldShow = false;

        if (state is MatchState.WaitingForPlayers or MatchState.Countdown)
        {
            shouldShow = true;
        }
        else if (state == MatchState.InProgress
            && _matchManager.TryGetLocalRecord(out PlayerMatchRecord localRecord)
            && !localRecord.IsAlive)
        {
            shouldShow = true;
        }

        if (contentRoot.activeSelf != shouldShow)
        {
            contentRoot.SetActive(shouldShow);
            if (shouldShow)
            {
                BuildOptionButtonsIfNeeded();
                RefreshHighlights(_matchManager.LocalSelectedLoadout);
            }
        }
    }

    /// <summary>현재 선택된 로드아웃에 맞춰 각 무기 버튼의 하이라이트 색상을 갱신한다.</summary>
    private void RefreshHighlights(PlayerLoadoutSelection selection)
    {
        for (int i = 0; i < _optionButtons.Count; i++)
        {
            OptionButtonEntry entry = _optionButtons[i];
            if (entry.Background == null) continue;

            string selectedId = entry.Slot switch
            {
                EWeaponSlotType.Primary => selection.PrimaryWeaponId,
                EWeaponSlotType.Secondary => selection.SecondaryWeaponId,
                EWeaponSlotType.Melee => selection.MeleeWeaponId,
                EWeaponSlotType.Throwable => selection.ThrowableWeaponId,
                _ => string.Empty
            };

            bool isSelected = string.Equals(entry.WeaponId, selectedId, StringComparison.Ordinal);
            entry.Background.color = isSelected ? selectedButtonColor : normalButtonColor;
        }
    }
}
