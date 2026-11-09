using Networking;
using Networking.Dto;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>로그인·로비 흐름을 한 화면에서 시험하는 테스트 UI.</summary>
    public class LobbyTestScreen : MonoBehaviour
    {
        [Header("로그인")]
        [SerializeField] private InputField idInput;
        [SerializeField] private InputField pwInput;
        [SerializeField] private Button signUpButton;
        [SerializeField] private Button logInButton;
        [SerializeField] private Button autoLogInButton;

        /// <summary>로그인 결과와 https 요청 오류를 보여주는 텍스트.</summary>
        [SerializeField] private Text statusText;

        [Header("방 만들기")]
        [SerializeField] private InputField lobbyNameInput;
        [SerializeField] private InputField lobbyPasswordInput;
        [SerializeField] private Button createLobbyButton;

        /// <summary>새로 만드는 방의 최대 인원 (방장 포함).</summary>
        [SerializeField] private int maxPlayers = 6;

        [Header("방 목록")]
        [SerializeField] private Transform lobbyListContent;

        /// <summary><see cref="lobbyListContent"/> 안의 비활성 행. 방 하나마다 복제됨.</summary>
        [SerializeField] private Button lobbyRowTemplate;

        [SerializeField] private Button refreshLobbyListButton;

        /// <summary>참가할 방의 비밀번호.</summary>
        [SerializeField] private InputField joinPasswordInput;

        /// <summary>비밀번호가 틀렸을 때 <see cref="joinPasswordInput"/> 아래에 보이는 경고.</summary>
        [SerializeField] private Text joinPasswordAlarmText;

        [Header("대기실")]
        [SerializeField] private ScrollRect chatScrollRect;
        [SerializeField] private Text chatLogText;
        [SerializeField] private InputField chatInput;
        [SerializeField] private Button chatSendButton;
        [SerializeField] private Button readyButton;
        [SerializeField] private Button quitLobbyButton;
        [SerializeField] private Button gameStartButton;

        /// <summary>서버가 마지막으로 알려준 내 준비 상태.</summary>
        private bool isReady;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            signUpButton.onClick.AddListener(HandleSignUpClicked);
            logInButton.onClick.AddListener(HandleLogInClicked);
            autoLogInButton.onClick.AddListener(HandleAutoLogInClicked);
            createLobbyButton.onClick.AddListener(HandleCreateLobbyClicked);
            refreshLobbyListButton.onClick.AddListener(HandleRefreshLobbyListClicked);
            chatSendButton.onClick.AddListener(HandleChatSendClicked);
            readyButton.onClick.AddListener(HandleReadyClicked);
            quitLobbyButton.onClick.AddListener(HandleQuitLobbyClicked);
            gameStartButton.onClick.AddListener(HandleGameStartClicked);

            LobbyManager.MemberJoined += HandleMemberJoined;
            LobbyManager.MemberLeft += HandleMemberLeft;
            LobbyManager.ChatReceived += HandleChatReceived;
            LobbyManager.ReadyChanged += HandleReadyChanged;
            LobbyManager.GameStartFailed += HandleGameStartFailed;
            LobbyManager.LobbyClosed += HandleLobbyClosed;
            LobbyManager.ErrorReceived += HandleErrorReceived;
            LobbyManager.Disconnected += HandleDisconnected;

            lobbyRowTemplate.gameObject.SetActive(false);
            joinPasswordAlarmText.gameObject.SetActive(false);
            chatLogText.text = "";
            UpdateButtons();
        }

        private void OnDestroy()
        {
            LobbyManager.MemberJoined -= HandleMemberJoined;
            LobbyManager.MemberLeft -= HandleMemberLeft;
            LobbyManager.ChatReceived -= HandleChatReceived;
            LobbyManager.ReadyChanged -= HandleReadyChanged;
            LobbyManager.GameStartFailed -= HandleGameStartFailed;
            LobbyManager.LobbyClosed -= HandleLobbyClosed;
            LobbyManager.ErrorReceived -= HandleErrorReceived;
            LobbyManager.Disconnected -= HandleDisconnected;
        }

        // ***************************************************************
        // 로그인
        // ***************************************************************

        private async void HandleSignUpClicked()
        {
            GameUserRequestDTO request = new GameUserRequestDTO
            {
                memberType = 0,
                memberId = idInput.text,
                memberPw = pwInput.text,
                memberName = idInput.text,
                nickName = idInput.text
            };

            await ApplyLogInResult("Sign-up", await LoginManager.SignUp(request));
        }

        private async void HandleLogInClicked()
        {
            GameUserRequestDTO request = new GameUserRequestDTO { memberType = 0, memberId = idInput.text, memberPw = pwInput.text };
            await ApplyLogInResult("Log-in", await LoginManager.LogIn(request));
        }

        private async void HandleAutoLogInClicked()
        {
            statusText.text = "Auto log-in...";
            await ApplyLogInResult("Auto log-in", await LoginManager.AutoLogIn());
        }

        /// <summary>로그인 결과를 보여주고, 성공했으면 방 목록을 불러옴.</summary>
        private async Awaitable ApplyLogInResult(string action, GameUserResponseDTO response)
        {
            if (!ResponseMessageUtil.IsOk(response.responseMessage))
            {
                statusText.text = $"{action} failed: {response.responseMessage}";
                UpdateButtons();
                return;
            }

            statusText.text = $"{action} succeeded";
            UpdateButtons();
            await RefreshLobbyList();
        }

        // ***************************************************************
        // 방 목록
        // ***************************************************************

        private async void HandleRefreshLobbyListClicked()
        {
            await RefreshLobbyList();
        }

        private async void HandleCreateLobbyClicked()
        {
            LobbyCreateRequestDTO request = new LobbyCreateRequestDTO
            {
                lobbyName = lobbyNameInput.text,
                maxPlayers = maxPlayers,
                password = lobbyPasswordInput.text
            };

            ApplyEnterResult(await LobbyManager.CreateLobby(request));
        }

        private async void HandleJoinLobbyClicked(string lobbyId)
        {
            LobbyJoinRequestDTO request = new LobbyJoinRequestDTO { lobbyId = lobbyId, password = joinPasswordInput.text };
            LobbyEnterResponseDTO response = await LobbyManager.JoinLobby(request);

            joinPasswordAlarmText.gameObject.SetActive(response.responseMessage == LobbyMessages.INVALID_PASSWORD);
            ApplyEnterResult(response);
        }

        /// <summary>서버에서 방 목록을 받아 행을 다시 만듦.</summary>
        private async Awaitable RefreshLobbyList()
        {
            ClearLobbyRows();

            if (!LoginManager.IsOnline || LobbyManager.CurrentLobby != null)
            {
                return;
            }

            LobbyListResponseDTO response = await LobbyManager.GetLobbies();

            if (!ResponseMessageUtil.IsOk(response.responseMessage))
            {
                statusText.text = "Lobby list failed: " + response.responseMessage;
                return;
            }

            // 응답을 기다리는 동안 방에 들어갔거나 목록을 또 요청했을 수 있음
            ClearLobbyRows();

            if (LobbyManager.CurrentLobby != null || response.lobbies == null)
            {
                return;
            }

            foreach (LobbyInfoDTO lobby in response.lobbies)
            {
                Button row = Instantiate(lobbyRowTemplate, lobbyListContent);
                string lobbyId = lobby.lobbyId;
                string locked = lobby.hasPassword ? " [PW]" : "";

                row.GetComponentInChildren<Text>().text = $"{lobby.lobbyName}{locked}  {lobby.playerCount}/{lobby.maxPlayers}  host: {lobby.hostNickName}";
                row.onClick.AddListener(() => HandleJoinLobbyClicked(lobbyId));
                row.gameObject.SetActive(true);
            }
        }

        /// <summary><see cref="lobbyRowTemplate"/>을 뺀 방 목록의 모든 행을 지움.</summary>
        private void ClearLobbyRows()
        {
            foreach (Transform row in lobbyListContent)
            {
                if (row != lobbyRowTemplate.transform)
                {
                    Destroy(row.gameObject);
                }
            }
        }

        /// <summary>방 생성·참가 결과를 보여주고, 성공했으면 대기실 화면으로 바꿈.</summary>
        private void ApplyEnterResult(LobbyEnterResponseDTO response)
        {
            if (!ResponseMessageUtil.IsOk(response.responseMessage))
            {
                statusText.text = "Entering lobby failed: " + response.responseMessage;
                return;
            }

            ClearLobbyRows();
            chatLogText.text = "";
            isReady = false;
            joinPasswordAlarmText.gameObject.SetActive(false);
            statusText.text = "Entered lobby: " + LobbyManager.CurrentLobby.lobbyName;
            UpdateButtons();
        }

        // ***************************************************************
        // 대기실
        // ***************************************************************

        private async void HandleChatSendClicked()
        {
            if (string.IsNullOrWhiteSpace(chatInput.text))
            {
                return;
            }

            if (await LobbyManager.SendChat(chatInput.text))
            {
                chatInput.text = "";
            }
        }

        private async void HandleReadyClicked()
        {
            await LobbyManager.SetReady(!isReady);
        }

        private async void HandleQuitLobbyClicked()
        {
            if (LobbyManager.IsHost)
            {
                ResponseDTO response = await LobbyManager.CloseLobby();

                if (!ResponseMessageUtil.IsOk(response.responseMessage))
                {
                    AddChatLine("Closing lobby failed: " + response.responseMessage);
                    return;
                }
            }
            else
            {
                await LobbyManager.LeaveLobby();
            }

            await ReturnToLobbyList(null);
        }

        private async void HandleGameStartClicked()
        {
            gameStartButton.interactable = false;

            // 성공하면 LobbyManager가 YesmanTestScene을 불러와 이 화면은 사라짐
            if (!await LobbyManager.StartGame())
            {
                AddChatLine("Game start request failed");
            }

            UpdateButtons();
        }

        private void HandleMemberJoined(LobbyMemberDTO member)
        {
            AddChatLine($"{member.nickName} joined");
        }

        private void HandleMemberLeft(LobbyMemberDTO member)
        {
            AddChatLine($"{member.nickName} left");
        }

        private void HandleChatReceived(LobbyChatPayload chat)
        {
            AddChatLine($"{chat.nickName}: {chat.message}");
        }

        private void HandleReadyChanged(LobbyReadyPayload ready)
        {
            LobbyMemberDTO member = LobbyManager.CurrentLobby?.members.Find(m => m.gameAccountHashcode == ready.gameAccountHashcode);
            AddChatLine($"{member?.nickName ?? ready.gameAccountHashcode} is {(ready.isReady ? "ready" : "not ready")}");

            if (ready.gameAccountHashcode == LobbyManager.LocalAccountHashcode)
            {
                isReady = ready.isReady;
                readyButton.GetComponentInChildren<Text>().text = isReady ? "Unready" : "Ready";
            }
        }

        private void HandleGameStartFailed(string reason)
        {
            AddChatLine(reason == LobbyMessages.HOST_START_FAILED ? "Failed at hosting" : "Failed to connect to host: " + reason);
            UpdateButtons();
        }

        private void HandleErrorReceived(string message)
        {
            AddChatLine("Error: " + message);
        }

        private async void HandleLobbyClosed(string reason)
        {
            await ReturnToLobbyList("Lobby destroyed");
        }

        private async void HandleDisconnected()
        {
            await ReturnToLobbyList("Disconnected from lobby");
        }

        /// <summary>대기실 채팅을 비우고 <paramref name="message"/>를 남긴 뒤 방 목록 화면으로 돌아감.</summary>
        private async Awaitable ReturnToLobbyList(string message)
        {
            chatLogText.text = "";

            if (message != null)
            {
                AddChatLine(message);
            }

            isReady = false;
            readyButton.GetComponentInChildren<Text>().text = "Ready";
            UpdateButtons();
            await RefreshLobbyList();
        }

        private void AddChatLine(string line)
        {
            chatLogText.text += chatLogText.text.Length == 0 ? line : "\n" + line;
            Canvas.ForceUpdateCanvases();
            chatScrollRect.verticalNormalizedPosition = 0f; // 맨 아래로
        }

        /// <summary>로그인·대기실 상태에 맞춰 버튼을 켜고 끔.</summary>
        private void UpdateButtons()
        {
            bool online = LoginManager.IsOnline;
            bool inLobby = LobbyManager.CurrentLobby != null;

            signUpButton.interactable = !online;
            logInButton.interactable = !online;
            autoLogInButton.interactable = !online;
            createLobbyButton.interactable = online && !inLobby;
            refreshLobbyListButton.interactable = online && !inLobby;
            chatSendButton.interactable = inLobby;
            readyButton.interactable = inLobby;
            quitLobbyButton.interactable = inLobby;
            gameStartButton.interactable = LobbyManager.IsHost;
        }
    }
}
