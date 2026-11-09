using System;

namespace Networking.Dto
{
    /// <summary>대기실 wss 패킷 ID.</summary>
    public enum LobbyProtocol
    {
        Error = 0,              // 서버 → 클라이언트, ResponseDTO
        Authenticate = 1,       // 클라이언트 → 서버, LobbyAuthenticateRequest
        AuthenticateResult = 2, // 서버 → 클라이언트, LobbyAuthenticateResult

        Chat = 100,      // 클라이언트 → 서버, LobbyChatRequest
        Ready = 101,     // 클라이언트 → 서버, LobbyReadyRequest
        GameStart = 102, // 클라이언트(방장) → 서버, LobbyGameStartPayload
        Leave = 103,     // 클라이언트 → 서버, LobbyEmptyPayload
        GameEnd = 104,   // 클라이언트(방장) → 서버, LobbyGameEndPayload

        MemberJoined = 110, // 서버 → 클라이언트, LobbyMemberDTO
        MemberLeft = 111,   // 서버 → 클라이언트, LobbyMemberLeftPayload
        ChatReceived = 112, // 서버 → 클라이언트, LobbyChatPayload
        ReadyChanged = 113, // 서버 → 클라이언트, LobbyReadyPayload
        GameStarted = 114,  // 서버 → 클라이언트, LobbyGameStartPayload
        LobbyClosed = 115,  // 서버 → 클라이언트, LobbyClosedPayload
        GameEnded = 116,    // 서버 → 클라이언트(방장 포함), LobbyGameEndPayload
    }

    /// <summary>대기실 wss 패킷 봉투: <c>{"protocolId":int,"payload":{...}}</c>.</summary>
    [Serializable]
    public class LobbyPacket<T>
    {
        public int protocolId;

        public T payload;
    }

    /// <summary>필드가 없는 payload (<c>{}</c>).</summary>
    [Serializable]
    public class LobbyEmptyPayload
    {
    }

    [Serializable]
    public class LobbyAuthenticateRequest
    {
        /// <summary>방 생성·참가 응답으로 받은 입장권.</summary>
        public string lobbyTicket;
    }

    [Serializable]
    public class LobbyAuthenticateResult : ResponseDTO
    {
        /// <summary>인증된 내 게임 계정 해시코드.</summary>
        public string gameAccountHashcode;

        public LobbyStateDTO lobby;
    }

    [Serializable]
    public class LobbyChatRequest
    {
        public string message;
    }

    [Serializable]
    public class LobbyChatPayload
    {
        /// <summary>보낸 사람의 게임 계정 해시코드.</summary>
        public string gameAccountHashcode;

        public string nickName;

        public string message;
    }

    [Serializable]
    public class LobbyReadyRequest
    {
        public bool isReady;
    }

    [Serializable]
    public class LobbyReadyPayload
    {
        public string gameAccountHashcode;

        public bool isReady;
    }

    [Serializable]
    public class LobbyMemberLeftPayload
    {
        public string gameAccountHashcode;
    }

    [Serializable]
    public class LobbyGameStartPayload
    {
        /// <summary>방장이 만든 Unity Relay 참여 코드.</summary>
        public string relayJoinCode;
    }

    [Serializable]
    public class LobbyGameEndPayload
    {
        /// <summary>끝난 이유: <see cref="LobbyMessages.GAME_FINISHED"/>, <see cref="LobbyMessages.HOST_START_FAILED"/>.</summary>
        public string reason;
    }

    [Serializable]
    public class LobbyClosedPayload
    {
        /// <summary>닫힌 이유: HOST_LEFT, HOST_CLOSED.</summary>
        public string reason;
    }
}
