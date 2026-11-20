using Audio;
using Localization;
using Networking;
using Networking.Dto;
using UnityEngine;

namespace UI
{
    /// <summary>로비 씬의 화면 전환. 한 번에 화면 하나만 켬.</summary>
    public class LobbySceneUI : MonoBehaviour
    {
        /// <summary>자동 로그인 응답을 기다리는 동안 보여주는 화면. 메인 화면이 처음 켜질 때 파괴되어 null이 됨.</summary>
        [SerializeField] private GameObject loadingScreen;

        [SerializeField] private LoginScreen loginScreen;
        [SerializeField] private SignUpScreen signUpScreen;
        [SerializeField] private MainScreen mainScreen;
        [SerializeField] private CreateGameScreen createGameScreen;
        [SerializeField] private LobbyScreen lobbyScreen;
        [SerializeField] private RoomScreen roomScreen;
        [SerializeField] private SettingsScreen settingsScreen;

        /// <summary>로딩 화면이 아닌 화면이 처음 켜질 때 재생을 시작하는 배경 음악.</summary>
        [SerializeField] private BackgroundMusic backgroundMusic;

        /// <summary>모든 화면 위에 뜨는 알림 팝업.</summary>
        [SerializeField] private MessagePopup messagePopup;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            LobbyManager.RoomClosed += HandleRoomClosed;
            LobbyManager.Disconnected += HandleDisconnected;
            LobbyManager.Kicked += HandleKicked;
        }

        private async void Start()
        {
            // 게임이 끝나 같은 방으로 돌아온 경우
            if (LobbyManager.CurrentRoom != null)
            {
                ShowRoom();
                return;
            }

            if (LoginManager.IsOnline)
            {
                ShowMain();
                return;
            }

            Show(loadingScreen);
            GameUserResponseDTO response = await LoginManager.AutoLogIn();

            if (this == null)
            {
                return;
            }

            if (ResponseMessageUtil.IsOk(response.responseMessage))
            {
                ShowMain();
            }
            else
            {
                ShowLogin();
            }
        }

        private void OnDestroy()
        {
            LobbyManager.RoomClosed -= HandleRoomClosed;
            LobbyManager.Disconnected -= HandleDisconnected;
            LobbyManager.Kicked -= HandleKicked;
        }

        // ***************************************************************
        // 이벤트
        // ***************************************************************

        private void HandleRoomClosed(string reason)
        {
            ShowLobby(Language.Get(LanguageTable.ROOM_CLOSED));
        }

        private void HandleDisconnected()
        {
            ShowLobby(Language.Get(LanguageTable.ROOM_DISCONNECTED));
        }

        private void HandleKicked()
        {
            ShowLobby();
            messagePopup.Show(Language.Get(LanguageTable.ROOM_KICKED));
        }

        // ***************************************************************
        // 화면 전환
        // ***************************************************************

        public void ShowLogin()
        {
            Show(loginScreen.gameObject);
        }

        public void ShowSignUp()
        {
            Show(signUpScreen.gameObject);
        }

        public void ShowMain()
        {
            if (loadingScreen != null)
            {
                Destroy(loadingScreen);
                loadingScreen = null;
            }

            Show(mainScreen.gameObject);
        }

        public void ShowCreateGame()
        {
            Show(createGameScreen.gameObject);
        }

        /// <summary>로비 화면을 켬. <paramref name="notice"/>는 방 목록 아래 상태 줄에 보여줌.</summary>
        public void ShowLobby(string notice = null)
        {
            lobbyScreen.Notice = notice;
            Show(lobbyScreen.gameObject);
        }

        public void ShowRoom()
        {
            Show(roomScreen.gameObject);
        }

        public void ShowSettings()
        {
            Show(settingsScreen.gameObject);
        }

        private void Show(GameObject screen)
        {
            if (screen != loadingScreen)
            {
                backgroundMusic.Play();
            }

            if (loadingScreen != null)
            {
                loadingScreen.SetActive(screen == loadingScreen);
            }

            loginScreen.gameObject.SetActive(screen == loginScreen.gameObject);
            signUpScreen.gameObject.SetActive(screen == signUpScreen.gameObject);
            mainScreen.gameObject.SetActive(screen == mainScreen.gameObject);
            createGameScreen.gameObject.SetActive(screen == createGameScreen.gameObject);
            lobbyScreen.gameObject.SetActive(screen == lobbyScreen.gameObject);
            roomScreen.gameObject.SetActive(screen == roomScreen.gameObject);
            settingsScreen.gameObject.SetActive(screen == settingsScreen.gameObject);
        }
    }
}
