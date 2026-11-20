using System;
using Networking.Dto;
using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    /// <summary>방 멤버 한 명의 이름표. 방장은 왕관, 준비한 멤버는 체크 표시와 회색 배경, 방장에게는 강퇴 버튼.</summary>
    public class RoomMemberSlot : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private Text nickNameText;
        [SerializeField] private GameObject crownIcon;
        [SerializeField] private GameObject readyIcon;

        /// <summary>방장이 보는 다른 멤버의 이름표에만 켜지는 X 버튼.</summary>
        [SerializeField] private Button kickButton;

        [SerializeField] private Color unreadyColor = new Color(0f, 0f, 0f, 0.4f);
        [SerializeField] private Color readyColor = new Color(0.5f, 0.5f, 0.5f, 0.8f);

        private RoomMemberDTO member;
        private Action<RoomMemberDTO> kickClicked;

        // ***************************************************************
        // MonoBehaviour
        // ***************************************************************

        private void Awake()
        {
            kickButton.onClick.AddListener(HandleKickClicked);
        }

        // ***************************************************************
        // 이벤트
        // ***************************************************************

        private void HandleKickClicked()
        {
            kickClicked?.Invoke(member);
        }

        // ***************************************************************
        // 공개
        // ***************************************************************

        /// <summary><paramref name="onKickClicked"/>가 null이면 강퇴 버튼을 숨김.</summary>
        public void Bind(RoomMemberDTO shownMember, bool isHost, Action<RoomMemberDTO> onKickClicked)
        {
            member = shownMember;
            kickClicked = onKickClicked;

            nickNameText.text = shownMember.nickName;
            crownIcon.SetActive(isHost);
            readyIcon.SetActive(shownMember.isReady);
            background.color = shownMember.isReady ? readyColor : unreadyColor;
            kickButton.gameObject.SetActive(onKickClicked != null);
        }
    }
}
