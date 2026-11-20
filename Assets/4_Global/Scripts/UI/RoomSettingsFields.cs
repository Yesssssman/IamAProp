using System;
using Localization;
using Networking;
using Networking.Dto;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>방의 게임 모드·최대 인원·맵을 고르는 입력 묶음.</summary>
    public class RoomSettingsFields : MonoBehaviour
    {
        [SerializeField] private Dropdown gameModeDropdown;

        /// <summary>항목 i는 <see cref="LobbyManager.MIN_PLAYERS"/> + i명. 항목은 프리팹에 들어 있음.</summary>
        [SerializeField] private Dropdown maxPlayersDropdown;

        [SerializeField] private Dropdown mapDropdown;
        [SerializeField] private MapPreview mapPreview;

        /// <summary>사용자가 값을 바꿨을 때 호출됨. <see cref="SetValues"/>로 바꾼 값에는 호출되지 않음.</summary>
        public event Action Changed;

        public EGameMode GameMode => (EGameMode)gameModeDropdown.value;

        public EGameMap Map => (EGameMap)mapDropdown.value;

        public int MaxPlayers => LobbyManager.MIN_PLAYERS + maxPlayersDropdown.value;

        public bool Interactable
        {
            set
            {
                gameModeDropdown.interactable = value;
                maxPlayersDropdown.interactable = value;
                mapDropdown.interactable = value;
            }
        }

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            gameModeDropdown.onValueChanged.AddListener(HandleValueChanged);
            maxPlayersDropdown.onValueChanged.AddListener(HandleValueChanged);
            mapDropdown.onValueChanged.AddListener(HandleValueChanged);
        }

        private void OnEnable()
        {
            Language.Changed += HandleLanguageChanged;
            HandleLanguageChanged();
        }

        private void OnDisable()
        {
            Language.Changed -= HandleLanguageChanged;
        }

        // ***************************************************************
        // 이벤트
        // ***************************************************************

        private void HandleLanguageChanged()
        {
            int gameMode = gameModeDropdown.value;
            int map = mapDropdown.value;

            gameModeDropdown.ClearOptions();
            mapDropdown.ClearOptions();

            foreach (EGameMode mode in Enum.GetValues(typeof(EGameMode)))
            {
                gameModeDropdown.options.Add(new Dropdown.OptionData(Language.Get(LanguageTable.GAME_MODE_PREFIX + mode)));
            }

            foreach (EGameMap each in Enum.GetValues(typeof(EGameMap)))
            {
                mapDropdown.options.Add(new Dropdown.OptionData(Language.Get(LanguageTable.MAP_PREFIX + each)));
            }

            gameModeDropdown.SetValueWithoutNotify(gameMode);
            mapDropdown.SetValueWithoutNotify(map);
            gameModeDropdown.RefreshShownValue();
            mapDropdown.RefreshShownValue();
        }

        private void HandleValueChanged(int value)
        {
            mapPreview.Show(Map);
            Changed?.Invoke();
        }

        // ***************************************************************
        // 공개
        // ***************************************************************

        public void SetValues(EGameMode gameMode, EGameMap map, int maxPlayers)
        {
            gameModeDropdown.SetValueWithoutNotify((int)gameMode);
            mapDropdown.SetValueWithoutNotify((int)map);
            maxPlayersDropdown.SetValueWithoutNotify(Mathf.Clamp(maxPlayers, LobbyManager.MIN_PLAYERS, LobbyManager.MAX_PLAYERS) - LobbyManager.MIN_PLAYERS);
            mapPreview.Show(map);
        }
    }
}
