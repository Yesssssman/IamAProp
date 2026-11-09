using System;
using System.Collections.Generic;

namespace Networking.Dto
{
    // ***************************************************************
    // https 요청
    // ***************************************************************

    /// <summary>방 생성 요청 DTO.</summary>
    [Serializable]
    public struct LobbyCreateRequestDTO
    {
        /// <summary>방 제목.</summary>
        public string lobbyName;

        /// <summary>방장을 포함한 최대 인원.</summary>
        public int maxPlayers;

        /// <summary>방 비밀번호. 빈 문자열이면 공개 방.</summary>
        public string password;
    }

    /// <summary>방 참가 요청 DTO.</summary>
    [Serializable]
    public struct LobbyJoinRequestDTO
    {
        public string lobbyId;

        /// <summary>방 비밀번호. 공개 방이면 빈 문자열.</summary>
        public string password;
    }

    /// <summary>방 닫기 요청 DTO.</summary>
    [Serializable]
    public struct LobbyCloseRequestDTO
    {
        public string lobbyId;
    }

    // ***************************************************************
    // https 응답
    // ***************************************************************

    /// <summary>방 목록의 한 항목.</summary>
    [Serializable]
    public class LobbyInfoDTO
    {
        public string lobbyId;

        public string lobbyName;

        public string hostNickName;

        /// <summary>현재 인원 (방장 포함).</summary>
        public int playerCount;

        /// <summary>최대 인원 (방장 포함).</summary>
        public int maxPlayers;

        public bool hasPassword;
    }

    /// <summary>방 목록 조회 응답 DTO.</summary>
    [Serializable]
    public class LobbyListResponseDTO : ResponseDTO
    {
        public LobbyInfoDTO[] lobbies;
    }

    /// <summary>방 생성·참가 응답 DTO.</summary>
    [Serializable]
    public class LobbyEnterResponseDTO : ResponseDTO
    {
        /// <summary>대기실 wss 인증에 한 번 쓰는 입장권.</summary>
        public string lobbyTicket;
    }

    // ***************************************************************
    // 대기실 상태
    // ***************************************************************

    /// <summary>대기실 멤버 한 명.</summary>
    [Serializable]
    public class LobbyMemberDTO
    {
        public string gameAccountHashcode;

        public string nickName;

        public bool isReady;
    }

    /// <summary>대기실 전체 상태. wss 인증 결과로 받고 이후 패킷으로 갱신됨.</summary>
    [Serializable]
    public class LobbyStateDTO
    {
        public string lobbyId;

        public string lobbyName;

        public string hostAccountHashcode;

        /// <summary>최대 인원 (방장 포함).</summary>
        public int maxPlayers;

        public List<LobbyMemberDTO> members;
    }
}
