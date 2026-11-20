using System;
using System.Collections.Generic;
using Localization;
using Networking;
using Networking.Dto;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>방(대기실) 화면. 멤버 목록, 게임 정보, 채팅, 준비·게임 시작, 나가기.</summary>
    public class RoomScreen : MonoBehaviour
    {
        private const float READY_COOLDOWN_SECONDS = 2f;
        private const int MAX_CHAT_LINES = 100;

        [SerializeField] private Text roomNameText;

        [Header("멤버")]
        [SerializeField] private Transform memberContent;

        /// <summary><see cref="memberContent"/> 안의 비활성 이름표. 멤버 한 명마다 복제됨.</summary>
        [SerializeField] private RoomMemberSlot memberSlotTemplate;

        /// <summary>방장이 아닌 멤버에게 보이는 게임 정보 영역.</summary>
        [Header("게임 정보")]
        [SerializeField] private GameObject guestInfoRoot;

        [SerializeField] private Text gameModeText;
        [SerializeField] private MapPreview mapPreview;

        /// <summary>방장에게만 보이는 게임 모드·최대 인원·맵 입력.</summary>
        [SerializeField] private RoomSettingsFields ownerSettingsFields;

        [Header("채팅")]
        [SerializeField] private ScrollRect chatScrollRect;
        [SerializeField] private Transform chatContent;

        /// <summary><see cref="chatContent"/> 안의 비활성 채팅 줄. 메세지 하나마다 복제됨.</summary>
        [SerializeField] private Text chatLineTemplate;

        [SerializeField] private InputField chatInput;
        [SerializeField] private Button sendButton;

        [SerializeField] private Color chatColor = Color.black;
        [SerializeField] private Color systemMessageColor = Color.red;

        /// <summary>방장에게는 게임 시작, 나머지에게는 준비·준비 취소 버튼.</summary>
        [Header("버튼")]
        [SerializeField] private Button readyButton;

        [SerializeField] private Text readyButtonText;
        [SerializeField] private Button quitButton;

        private LobbySceneUI ui;

        private readonly List<RoomMemberSlot> memberSlots = new();
        private readonly Queue<Text> chatLines = new();

        /// <summary>준비 버튼을 누른 뒤 <see cref="READY_COOLDOWN_SECONDS"/>가 지나지 않았으면 true.</summary>
        private bool isReadyCoolingDown;

        /// <summary>방장이 게임 시작을 요청해 결과를 기다리는 중이면 true.</summary>
        private bool isStarting;

        /// <summary>나가기 요청을 보내는 중이면 true.</summary>
        private bool isQuitting;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            ui = GetComponentInParent<LobbySceneUI>(true);
            memberSlotTemplate.gameObject.SetActive(false);
            chatLineTemplate.gameObject.SetActive(false);

            ownerSettingsFields.Changed += HandleOwnerSettingsChanged;
            chatInput.onSubmit.AddListener(HandleChatSubmitted);
            sendButton.onClick.AddListener(HandleSendClicked);
            readyButton.onClick.AddListener(HandleReadyClicked);
            quitButton.onClick.AddListener(HandleQuitClicked);
        }

        private void OnEnable()
        {
            LobbyManager.MemberJoined += HandleMemberJoined;
            LobbyManager.MemberLeft += HandleMemberLeft;
            LobbyManager.MemberKicked += HandleMemberKicked;
            LobbyManager.ChatReceived += HandleChatReceived;
            LobbyManager.ReadyChanged += HandleReadyChanged;
            LobbyManager.RoomSettingsChanged += HandleRoomSettingsChanged;
            LobbyManager.GameStartFailed += HandleGameStartFailed;
            LobbyManager.ErrorReceived += HandleErrorReceived;
            Language.Changed += HandleLanguageChanged;

            foreach (Text line in chatLines)
            {
                Destroy(line.gameObject);
            }

            chatLines.Clear();
            chatInput.text = "";
            isReadyCoolingDown = false;
            isStarting = false;
            isQuitting = false;
            Render();
        }

        private void OnDisable()
        {
            LobbyManager.MemberJoined -= HandleMemberJoined;
            LobbyManager.MemberLeft -= HandleMemberLeft;
            LobbyManager.MemberKicked -= HandleMemberKicked;
            LobbyManager.ChatReceived -= HandleChatReceived;
            LobbyManager.ReadyChanged -= HandleReadyChanged;
            LobbyManager.RoomSettingsChanged -= HandleRoomSettingsChanged;
            LobbyManager.GameStartFailed -= HandleGameStartFailed;
            LobbyManager.ErrorReceived -= HandleErrorReceived;
            Language.Changed -= HandleLanguageChanged;
        }

        private void OnDestroy()
        {
            ownerSettingsFields.Changed -= HandleOwnerSettingsChanged;
        }

        // ***************************************************************
        // 대기실 이벤트
        // ***************************************************************

        private void HandleMemberJoined(RoomMemberDTO member)
        {
            AddChatLine(Language.Format(LanguageTable.ROOM_JOINED, member.nickName), systemMessageColor);
            Render();
        }

        private void HandleMemberLeft(RoomMemberDTO member)
        {
            AddChatLine(Language.Format(LanguageTable.ROOM_LEFT, member.nickName), systemMessageColor);
            Render();
        }

        private void HandleMemberKicked(RoomMemberDTO member)
        {
            AddChatLine(Language.Format(LanguageTable.ROOM_MEMBER_KICKED, member.nickName), systemMessageColor);
            Render();
        }

        private void HandleChatReceived(RoomChatPayload chat)
        {
            AddChatLine($"{chat.nickName}: {chat.message}", chatColor);
        }

        private void HandleReadyChanged(RoomReadyPayload ready)
        {
            RoomMemberDTO member = LobbyManager.CurrentRoom?.members.Find(m => m.gameAccountHashcode == ready.gameAccountHashcode);

            if (member != null)
            {
                string key = ready.isReady ? LanguageTable.ROOM_READIED : LanguageTable.ROOM_CANCELED_READY;
                AddChatLine(Language.Format(key, member.nickName), systemMessageColor);
            }

            Render();
        }

        private void HandleRoomSettingsChanged()
        {
            Render();
        }

        private void HandleGameStartFailed(string reason)
        {
            isStarting = false;
            AddChatLine(Language.Get(LanguageTable.ROOM_GAME_START_FAILED), systemMessageColor);
            Render();
        }

        /// <summary>서버가 요청을 거절함. 방장이 바꾼 설정이 거절됐을 수 있으므로 방 상태대로 다시 그림.</summary>
        private void HandleErrorReceived(string message)
        {
            isStarting = false;
            AddChatLine(Language.GetResponseMessage(message), systemMessageColor);
            Render();
        }

        private void HandleLanguageChanged()
        {
            Render();
        }

        // ***************************************************************
        // 입력
        // ***************************************************************

        private void HandleOwnerSettingsChanged()
        {
            RoomStateDTO room = LobbyManager.CurrentRoom;

            // 현재 인원보다 적은 최대 인원은 보내지 않고 되돌림
            if (room == null || ownerSettingsFields.MaxPlayers < room.members.Count)
            {
                Render();
                return;
            }

            _ = LobbyManager.ChangeRoomSettings(new RoomSettingsPayload
            {
                gameMode = ownerSettingsFields.GameMode.ToString(),
                map = ownerSettingsFields.Map.ToString(),
                maxPlayers = ownerSettingsFields.MaxPlayers,
            });
        }

        private void HandleChatSubmitted(string message)
        {
            HandleSendClicked();
        }

        private async void HandleSendClicked()
        {
            string message = chatInput.text.Trim();
            chatInput.text = "";
            chatInput.ActivateInputField();

            if (message.Length > 0)
            {
                await LobbyManager.SendChat(message);
            }
        }

        private async void HandleReadyClicked()
        {
            if (LobbyManager.IsHost)
            {
                isStarting = true;
                Render();

                if (!await LobbyManager.StartGame() && this != null)
                {
                    isStarting = false;
                    Render();
                }

                return;
            }

            RoomMemberDTO me = LocalMember();

            if (me == null || isReadyCoolingDown)
            {
                return;
            }

            isReadyCoolingDown = true;
            Render();
            await LobbyManager.SetReady(!me.isReady);
            await Awaitable.WaitForSecondsAsync(READY_COOLDOWN_SECONDS);

            if (this == null)
            {
                return;
            }

            isReadyCoolingDown = false;
            Render();
        }

        private void HandleKickClicked(RoomMemberDTO member)
        {
            _ = LobbyManager.KickMember(member.gameAccountHashcode);
        }

        private async void HandleQuitClicked()
        {
            isQuitting = true;
            Render();
            await LobbyManager.LeaveRoom();

            if (this != null)
            {
                ui.ShowLobby();
            }
        }

        // ***************************************************************
        // 그리기
        // ***************************************************************

        /// <summary><see cref="LobbyManager.CurrentRoom"/>대로 멤버·게임 정보·버튼을 다시 그림.</summary>
        private void Render()
        {
            RoomStateDTO room = LobbyManager.CurrentRoom;

            if (room == null)
            {
                return;
            }

            bool isHost = LobbyManager.IsHost;
            Enum.TryParse(room.gameMode, out EGameMode gameMode);
            Enum.TryParse(room.map, out EGameMap map);

            roomNameText.text = room.lobbyName;

            // 멤버
            while (memberSlots.Count < room.members.Count)
            {
                memberSlots.Add(Instantiate(memberSlotTemplate, memberContent));
            }

            for (int i = 0; i < memberSlots.Count; i++)
            {
                bool isShown = i < room.members.Count;
                memberSlots[i].gameObject.SetActive(isShown);

                if (isShown)
                {
                    bool isHostMember = room.members[i].gameAccountHashcode == room.hostAccountHashcode;
                    bool canKick = isHost && !isHostMember && !isStarting;
                    memberSlots[i].Bind(room.members[i], isHostMember, canKick ? (Action<RoomMemberDTO>)HandleKickClicked : null);
                }
            }

            // 게임 정보
            guestInfoRoot.SetActive(!isHost);
            ownerSettingsFields.gameObject.SetActive(isHost);
            gameModeText.text = Language.Format(LanguageTable.LOBBY_GAME_MODE, Language.Get(LanguageTable.GAME_MODE_PREFIX + gameMode));
            mapPreview.Show(map);
            ownerSettingsFields.SetValues(gameMode, map, room.maxPlayers);

            // 버튼
            RoomMemberDTO me = LocalMember();
            bool isReady = me != null && me.isReady;

            if (isHost)
            {
                bool allReady = room.members.TrueForAll(m => m.isReady || m.gameAccountHashcode == room.hostAccountHashcode);
                readyButtonText.text = Language.Get(LanguageTable.ROOM_GAME_START);
                readyButton.interactable = allReady && !isStarting && !isQuitting;
            }
            else
            {
                readyButtonText.text = Language.Get(isReady ? LanguageTable.ROOM_CANCEL_READY : LanguageTable.ROOM_READY);
                readyButton.interactable = !isReadyCoolingDown && !isQuitting;
            }

            ownerSettingsFields.Interactable = !isStarting;
            quitButton.interactable = !isReady && !isStarting && !isQuitting;
        }

        private void AddChatLine(string message, Color color)
        {
            Text line = Instantiate(chatLineTemplate, chatContent);
            line.text = message;
            line.color = color;
            line.gameObject.SetActive(true);
            chatLines.Enqueue(line);

            if (chatLines.Count > MAX_CHAT_LINES)
            {
                Destroy(chatLines.Dequeue().gameObject);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)chatContent);
            chatScrollRect.verticalNormalizedPosition = 0f;
        }

        /// <summary><see cref="LobbyManager.CurrentRoom"/>에서 나의 멤버 정보. 방에 없으면 null.</summary>
        private static RoomMemberDTO LocalMember()
        {
            return LobbyManager.CurrentRoom?.members.Find(m => m.gameAccountHashcode == LobbyManager.LocalAccountHashcode);
        }
    }
}
