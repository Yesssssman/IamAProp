using Localization;
using Networking;
using Networking.Dto;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>방 이름·비밀번호·게임 모드·최대 인원·맵을 정해 방을 만드는 화면.</summary>
    public class CreateGameScreen : MonoBehaviour
    {
        private const int DEFAULT_MAX_PLAYERS = 8;

        [SerializeField] private InputField roomNameInput;

        /// <summary>켜져 있을 때만 <see cref="passwordInput"/>을 입력할 수 있음.</summary>
        [SerializeField] private Toggle passwordToggle;

        /// <summary>영문·숫자만 받는 비밀번호 입력.</summary>
        [SerializeField] private InputField passwordInput;

        [SerializeField] private RoomSettingsFields settingsFields;
        [SerializeField] private Button createButton;
        [SerializeField] private Button cancelButton;

        /// <summary>입력 오류와 방 생성 실패를 보여주는 텍스트.</summary>
        [SerializeField] private Text statusText;

        private LobbySceneUI ui;

        /// <summary>방 생성 응답을 기다리는 중이면 true.</summary>
        private bool isCreating;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            ui = GetComponentInParent<LobbySceneUI>(true);
            passwordToggle.onValueChanged.AddListener(HandlePasswordToggled);
            createButton.onClick.AddListener(HandleCreateClicked);
            cancelButton.onClick.AddListener(ui.ShowMain);
        }

        private async void OnEnable()
        {
            statusText.text = "";
            passwordToggle.SetIsOnWithoutNotify(false);
            HandlePasswordToggled(false);
            settingsFields.SetValues(EGameMode.BattleRoyale, EGameMap.Desert, DEFAULT_MAX_PLAYERS);
            roomNameInput.text = "";

            if (LoginManager.NickName == null)
            {
                await LoginManager.GetAccountInfo();

                if (this == null || LoginManager.NickName == null)
                {
                    return;
                }
            }

            // 닉네임을 기다리는 동안 사용자가 입력한 이름은 덮어쓰지 않음
            if (roomNameInput.text.Length == 0)
            {
                roomNameInput.text = Language.Format(LanguageTable.CREATE_DEFAULT_ROOM_NAME, LoginManager.NickName);
            }
        }

        // ***************************************************************
        // 이벤트
        // ***************************************************************

        private void HandlePasswordToggled(bool isOn)
        {
            passwordInput.interactable = isOn;

            if (!isOn)
            {
                passwordInput.text = "";
            }
        }

        private async void HandleCreateClicked()
        {
            if (isCreating)
            {
                return;
            }

            string roomName = roomNameInput.text.Trim();

            if (roomName.Length == 0)
            {
                statusText.text = Language.Get(LanguageTable.CREATE_ENTER_ROOM_NAME);
                return;
            }

            if (passwordToggle.isOn && passwordInput.text.Length == 0)
            {
                statusText.text = Language.Get(LanguageTable.CREATE_ENTER_PASSWORD);
                return;
            }

            RoomCreateRequestDTO request = new RoomCreateRequestDTO
            {
                lobbyName = roomName,
                maxPlayers = settingsFields.MaxPlayers,
                password = passwordInput.text,
                gameMode = settingsFields.GameMode.ToString(),
                map = settingsFields.Map.ToString(),
            };

            isCreating = true;
            createButton.interactable = false;
            statusText.text = "";

            RoomEnterResponseDTO response = await LobbyManager.CreateRoom(request);
            isCreating = false;

            if (this == null)
            {
                return;
            }

            createButton.interactable = true;

            if (ResponseMessageUtil.IsOk(response.responseMessage))
            {
                ui.ShowRoom();
            }
            else
            {
                statusText.text = Language.GetResponseMessage(response.responseMessage);
            }
        }
    }
}
