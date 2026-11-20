using System;
using System.Collections.Generic;
using Localization;
using Networking;
using Networking.Dto;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>참가할 수 있는 방 목록(왼쪽)과 선택한 방의 정보(오른쪽). 비밀번호 방은 입력 팝업을 띄움.</summary>
    public class LobbyScreen : MonoBehaviour
    {
        [Header("방 목록")]
        [SerializeField] private Transform roomListContent;

        /// <summary><see cref="roomListContent"/> 안의 비활성 행. 방 하나마다 복제됨.</summary>
        [SerializeField] private RoomListRow rowTemplate;

        [SerializeField] private Button refreshButton;

        /// <summary>목록 불러오기 상태·오류와 <see cref="Notice"/>를 보여주는 텍스트.</summary>
        [SerializeField] private Text statusText;

        /// <summary>방을 선택했을 때만 켜지는 정보 영역.</summary>
        [Header("방 정보")]
        [SerializeField] private GameObject detailRoot;

        [SerializeField] private Text detailRoomNameText;
        [SerializeField] private Text detailOwnerText;
        [SerializeField] private Text detailGameModeText;
        [SerializeField] private MapPreview detailMapPreview;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button returnButton;

        /// <summary>화면 전체를 덮는 배경을 포함한 팝업.</summary>
        [Header("비밀번호 팝업")]
        [SerializeField] private GameObject passwordPopup;

        [SerializeField] private InputField passwordInput;
        [SerializeField] private Button passwordOkButton;
        [SerializeField] private Button passwordCancelButton;

        /// <summary>비밀번호가 틀렸을 때 등 참가 실패를 보여주는 텍스트.</summary>
        [SerializeField] private Text passwordStatusText;

        private LobbySceneUI ui;

        private readonly List<RoomListRow> rows = new();

        private RoomListRow selectedRow;

        /// <summary>방 참가 또는 목록 응답을 기다리는 중이면 true.</summary>
        private bool isBusy;

        /// <summary>목록을 불러온 뒤 상태 줄에 보여줄 알림 (예: 방이 닫힘). 없으면 null.</summary>
        public string Notice { get; set; }

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            ui = GetComponentInParent<LobbySceneUI>(true);
            rowTemplate.gameObject.SetActive(false);
            refreshButton.onClick.AddListener(HandleRefreshClicked);
            joinButton.onClick.AddListener(HandleJoinClicked);
            returnButton.onClick.AddListener(ui.ShowMain);
            passwordOkButton.onClick.AddListener(HandlePasswordOkClicked);
            passwordCancelButton.onClick.AddListener(HandlePasswordCancelClicked);
            passwordInput.onSubmit.AddListener(HandlePasswordSubmitted);
        }

        private void OnEnable()
        {
            Language.Changed += HandleLanguageChanged;
            passwordPopup.SetActive(false);
            _ = RefreshList();
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
            Select(selectedRow);
        }

        private void HandleRefreshClicked()
        {
            Notice = null;
            _ = RefreshList();
        }

        private void HandleRowClicked(RoomListRow row)
        {
            Select(row);
        }

        private void HandleRowDoubleClicked(RoomListRow row)
        {
            Select(row);
            HandleJoinClicked();
        }

        private async void HandleJoinClicked()
        {
            if (selectedRow == null || isBusy)
            {
                return;
            }

            if (selectedRow.Room.hasPassword)
            {
                passwordInput.text = "";
                passwordStatusText.text = "";
                passwordPopup.SetActive(true);
                passwordInput.ActivateInputField();
                return;
            }

            string message = await JoinRoom(selectedRow.Room, "");

            if (message != null)
            {
                statusText.text = message;
            }
        }

        private void HandlePasswordSubmitted(string password)
        {
            HandlePasswordOkClicked();
        }

        private async void HandlePasswordOkClicked()
        {
            if (selectedRow == null || isBusy || passwordInput.text.Length == 0)
            {
                return;
            }

            passwordStatusText.text = "";
            string message = await JoinRoom(selectedRow.Room, passwordInput.text);

            if (message != null)
            {
                passwordStatusText.text = message;
            }
        }

        private void HandlePasswordCancelClicked()
        {
            passwordPopup.SetActive(false);
        }

        // ***************************************************************
        // 방 목록
        // ***************************************************************

        private async Awaitable RefreshList()
        {
            if (isBusy)
            {
                return;
            }

            isBusy = true;
            refreshButton.interactable = false;
            ClearRows();
            statusText.text = Language.Get(LanguageTable.COMMON_LOADING);

            RoomListResponseDTO response = await LobbyManager.GetRooms();
            isBusy = false;

            if (this == null || !isActiveAndEnabled)
            {
                return;
            }

            refreshButton.interactable = true;

            if (!ResponseMessageUtil.IsOk(response.responseMessage))
            {
                statusText.text = Language.GetResponseMessage(response.responseMessage);
                return;
            }

            ClearRows();

            foreach (RoomInfoDTO room in response.lobbies ?? Array.Empty<RoomInfoDTO>())
            {
                RoomListRow row = Instantiate(rowTemplate, roomListContent);
                row.gameObject.SetActive(true);
                row.Bind(room, HandleRowClicked, HandleRowDoubleClicked);
                rows.Add(row);
            }

            statusText.text = Notice ?? (rows.Count == 0 ? Language.Get(LanguageTable.LOBBY_EMPTY) : "");
        }

        private void ClearRows()
        {
            foreach (RoomListRow row in rows)
            {
                Destroy(row.gameObject);
            }

            rows.Clear();
            Select(null);
        }

        /// <summary><paramref name="row"/>를 선택하고 오른쪽 정보 영역을 채움. null이면 선택 해제.</summary>
        private void Select(RoomListRow row)
        {
            if (selectedRow != null)
            {
                selectedRow.SetSelected(false);
            }

            selectedRow = row;
            detailRoot.SetActive(row != null);
            joinButton.interactable = row != null;

            if (row == null)
            {
                return;
            }

            RoomInfoDTO room = row.Room;
            Enum.TryParse(room.map, out EGameMap map);

            row.SetSelected(true);
            detailRoomNameText.text = room.lobbyName;
            detailOwnerText.text = Language.Format(LanguageTable.LOBBY_OWNER, room.hostNickName);
            detailGameModeText.text = Language.Format(LanguageTable.LOBBY_GAME_MODE, Language.Get(LanguageTable.GAME_MODE_PREFIX + room.gameMode));
            detailMapPreview.Show(map);
        }

        /// <summary>방에 참가하고 성공하면 방 화면으로 넘어감. 실패하면 사용자에게 보여줄 메세지를 반환.</summary>
        private async Awaitable<string> JoinRoom(RoomInfoDTO room, string password)
        {
            isBusy = true;
            joinButton.interactable = false;
            passwordOkButton.interactable = false;

            RoomEnterResponseDTO response = await LobbyManager.JoinRoom(new RoomJoinRequestDTO { lobbyId = room.lobbyId, password = password });
            isBusy = false;

            if (this == null)
            {
                return null;
            }

            joinButton.interactable = selectedRow != null;
            passwordOkButton.interactable = true;

            if (ResponseMessageUtil.IsOk(response.responseMessage))
            {
                ui.ShowRoom();
                return null;
            }

            return Language.GetResponseMessage(response.responseMessage);
        }
    }
}
