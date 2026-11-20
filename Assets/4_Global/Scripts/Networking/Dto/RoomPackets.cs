using System;

namespace Networking.Dto
{
    /// <summary>대기실 wss 패킷 ID.</summary>
    public enum RoomProtocol
    {
        Error = 0,              // 서버 → 클라이언트, ResponseDTO
        Authenticate = 1,       // 클라이언트 → 서버, RoomAuthenticateRequest
        AuthenticateResult = 2, // 서버 → 클라이언트, RoomAuthenticateResult

        Chat = 100,      // 클라이언트 → 서버, RoomChatRequest
        Ready = 101,     // 클라이언트 → 서버, RoomReadyRequest
        GameStart = 102, // 클라이언트(방장) → 서버, RoomGameStartPayload
        Leave = 103,     // 클라이언트 → 서버, RoomEmptyPayload
        GameEnd = 104,   // 클라이언트(방장) → 서버, RoomGameEndPayload
        Settings = 105,  // 클라이언트(방장) → 서버, RoomSettingsPayload
        Kick = 106,      // 클라이언트(방장) → 서버, RoomKickRequest

        MemberJoined = 110, // 서버 → 클라이언트, RoomMemberDTO
        MemberLeft = 111,   // 서버 → 클라이언트, RoomMemberLeftPayload
        ChatReceived = 112, // 서버 → 클라이언트, RoomChatPayload
        ReadyChanged = 113, // 서버 → 클라이언트, RoomReadyPayload
        GameStarted = 114,  // 서버 → 클라이언트, RoomGameStartPayload
        RoomClosed = 115,   // 서버 → 클라이언트, RoomClosedPayload
        GameEnded = 116,    // 서버 → 클라이언트(방장 포함), RoomGameEndPayload
        SettingsChanged = 117, // 서버 → 클라이언트(방장 포함), RoomSettingsPayload
        Kicked = 118,          // 서버 → 강퇴된 멤버, RoomEmptyPayload
    }

    /// <summary>대기실 wss 패킷 봉투: <c>{"protocolId":int,"payload":{...}}</c>.</summary>
    [Serializable]
    public class RoomPacket<T>
    {
        public int protocolId;

        public T payload;
    }

    /// <summary>필드가 없는 payload (<c>{}</c>).</summary>
    [Serializable]
    public class RoomEmptyPayload
    {
    }

    [Serializable]
    public class RoomAuthenticateRequest
    {
        /// <summary>방 생성·참가 응답으로 받은 입장권.</summary>
        public string lobbyTicket;
    }

    [Serializable]
    public class RoomAuthenticateResult : ResponseDTO
    {
        /// <summary>인증된 내 게임 계정 해시코드.</summary>
        public string gameAccountHashcode;

        public RoomStateDTO lobby;
    }

    [Serializable]
    public class RoomChatRequest
    {
        public string message;
    }

    [Serializable]
    public class RoomChatPayload
    {
        /// <summary>보낸 사람의 게임 계정 해시코드.</summary>
        public string gameAccountHashcode;

        public string nickName;

        public string message;
    }

    [Serializable]
    public class RoomReadyRequest
    {
        public bool isReady;
    }

    [Serializable]
    public class RoomReadyPayload
    {
        public string gameAccountHashcode;

        public bool isReady;
    }

    [Serializable]
    public class RoomMemberLeftPayload
    {
        public string gameAccountHashcode;

        /// <summary>방장이 강퇴해서 나갔으면 true.</summary>
        public bool isKicked;
    }

    [Serializable]
    public class RoomKickRequest
    {
        /// <summary>강퇴할 멤버의 게임 계정 해시코드.</summary>
        public string gameAccountHashcode;
    }

    [Serializable]
    public class RoomGameStartPayload
    {
        /// <summary>방장이 만든 Unity Relay 참여 코드.</summary>
        public string relayJoinCode;
    }

    [Serializable]
    public class RoomGameEndPayload
    {
        /// <summary>끝난 이유: <see cref="LobbyMessages.GAME_FINISHED"/>, <see cref="LobbyMessages.HOST_START_FAILED"/>.</summary>
        public string reason;
    }

    [Serializable]
    public class RoomClosedPayload
    {
        /// <summary>닫힌 이유: HOST_LEFT, HOST_CLOSED.</summary>
        public string reason;
    }

    [Serializable]
    public class RoomSettingsPayload
    {
        /// <summary><see cref="EGameMode"/> 이름.</summary>
        public string gameMode;

        /// <summary><see cref="EGameMap"/> 이름.</summary>
        public string map;

        /// <summary>최대 인원 (방장 포함).</summary>
        public int maxPlayers;
    }
}