using System;
using Networking.Dto;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI
{
    /// <summary>로비 방 목록의 한 줄. 클릭하면 선택, 더블 클릭하면 참가.</summary>
    public class RoomListRow : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image background;
        [SerializeField] private Text roomNameText;
        [SerializeField] private Text ownerText;

        /// <summary>"현재 인원/최대 인원".</summary>
        [SerializeField] private Text playerCountText;

        /// <summary>비밀번호가 걸린 방이면 켜지는 자물쇠 아이콘.</summary>
        [SerializeField] private GameObject lockIcon;

        [SerializeField] private Color normalColor = new Color(0f, 0f, 0f, 0.4f);
        [SerializeField] private Color selectedColor = new Color(0.5f, 0.5f, 0.5f, 0.7f);

        private Action<RoomListRow> clicked;
        private Action<RoomListRow> doubleClicked;

        public RoomInfoDTO Room { get; private set; }

        public void Bind(RoomInfoDTO room, Action<RoomListRow> onClicked, Action<RoomListRow> onDoubleClicked)
        {
            Room = room;
            clicked = onClicked;
            doubleClicked = onDoubleClicked;

            roomNameText.text = room.lobbyName;
            ownerText.text = room.hostNickName;
            playerCountText.text = $"{room.playerCount}/{room.maxPlayers}";
            lockIcon.SetActive(room.hasPassword);
            SetSelected(false);
        }

        public void SetSelected(bool isSelected)
        {
            background.color = isSelected ? selectedColor : normalColor;
        }

        // ***************************************************************
        // IPointerClickHandler
        // ***************************************************************

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            if (eventData.clickCount == 2)
            {
                doubleClicked?.Invoke(this);
            }
            else
            {
                clicked?.Invoke(this);
            }
        }
    }
}
