using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Networking.Dto;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Networking
{
    /// <summary>JW 로비 서버 통신 라이브러리. 방 조회·생성·참가·닫기(https), 대기실 동기화(wss), Relay 게임 시작.</summary>
    public static class LobbyManager
    {
#if LOCAL
        private const string LOBBY_SERVER = "http://127.0.0.1:5050";
        private const string LOBBY_SOCKET = "ws://127.0.0.1:5050/ws/iamaprop/lobby";
#elif DEV
        private const string LOBBY_SERVER = "http://35.216.2.84:5050"; // PLACEHOLDER: 개발 서버 주소
        private const string LOBBY_SOCKET = "ws://35.216.2.84:5050/ws/iamaprop/lobby"; // PLACEHOLDER: 개발 서버 주소
#elif PROD
        private const string LOBBY_SERVER = "http://127.0.0.1/prod"; // PLACEHOLDER: 운영 서버 주소
        private const string LOBBY_SOCKET = "ws://127.0.0.1/prod/ws/iamaprop/lobby"; // PLACEHOLDER: 운영 서버 주소
#else
        private const string LOBBY_SERVER = "http://127.0.0.1:5050";
        private const string LOBBY_SOCKET = "ws://127.0.0.1:5050/ws/iamaprop/lobby";
#endif

        // PLACEHOLDER: 인게임 씬이 구현되기 전까지 쓰는 임시 씬
        private const string INGAME_SCENE = "YesmanTestScene";

        public const int MIN_PLAYERS = 2;  // 방장 포함, 서버 LobbyOptions.MinPlayers와 같음
        public const int MAX_PLAYERS = 16; // 방장 포함, 서버 LobbyOptions.MaxPlayers와 같음

        private const string RELAY_CONNECTION_TYPE = "dtls";
        private const int RECEIVE_BUFFER_BYTES = 4096;

        /// <summary>대기실 wss 연결. 대기실에 없으면 null.</summary>
        private static ClientWebSocket sSocket;

        /// <summary><see cref="sSocket"/>의 송수신을 중단시키는 토큰.</summary>
        private static CancellationTokenSource sSocketCancel;

        /// <summary>ClientWebSocket은 동시 송신을 허용하지 않으므로 송신을 한 번에 하나씩 처리.</summary>
        private static readonly SemaphoreSlim sSendLock = new(1, 1);

        /// <summary>방장이 게임 시작을 요청할 때 만든 Relay 할당.</summary>
        private static Allocation sHostAllocation;

        /// <summary>현재 대기실 상태. 대기실에 없으면 null.</summary>
        public static RoomStateDTO CurrentRoom { get; private set; }

        /// <summary>대기실 wss 인증으로 받은 내 게임 계정 해시코드.</summary>
        public static string LocalAccountHashcode { get; private set; }

        public static bool IsHost => CurrentRoom != null && CurrentRoom.hostAccountHashcode == LocalAccountHashcode;

        /// <summary>
        /// 플레이 시작 시 static 상태를 비움.
        ///
        /// 프로젝트 설정의 Enter Play Mode Options에서 도메인 리로드가 꺼져 있어 static 필드가 이전 플레이
        /// 세션의 값을 유지함. 비우지 않으면 이전 세션에서 열어 둔 대기실 wss 연결과 방 상태가 다음 세션에도 남음.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CloseSocket();
            sHostAllocation = null;
            CurrentRoom = null;
            LocalAccountHashcode = null;
        }

        // ***************************************************************
        // 대기실 이벤트 (메인 스레드에서 호출됨)
        // ***************************************************************

        public static event Action<RoomMemberDTO> MemberJoined;

        /// <summary>인자: <see cref="CurrentRoom"/>에서 제거된 멤버. 강퇴된 멤버는 <see cref="MemberKicked"/>로 알림.</summary>
        public static event Action<RoomMemberDTO> MemberLeft;

        /// <summary>인자: 방장이 강퇴해 <see cref="CurrentRoom"/>에서 제거된 멤버.</summary>
        public static event Action<RoomMemberDTO> MemberKicked;

        public static event Action<RoomChatPayload> ChatReceived;

        public static event Action<RoomReadyPayload> ReadyChanged;

        /// <summary>방장이 게임 모드·맵·최대 인원을 바꿨을 때 호출됨. <see cref="CurrentRoom"/>은 이미 갱신됨.</summary>
        public static event Action RoomSettingsChanged;

        /// <summary>방장은 호스트 시작 직후, 클라이언트는 호스트 접속 완료 후 호출됨.</summary>
        public static event Action GameStarted;

        /// <summary>인자: <see cref="LobbyMessages.HOST_START_FAILED"/> 또는 <see cref="CommonMessages.RELAY_CONNECTION_FAILED"/>.</summary>
        public static event Action<string> GameStartFailed;

        /// <summary>방장이 게임을 끝내 같은 방의 대기실로 돌아왔을 때 호출됨. 모든 멤버의 준비 상태는 해제됨.</summary>
        public static event Action GameEnded;

        /// <summary>인자: 서버가 보낸 닫힌 이유 (<see cref="RoomClosedPayload.reason"/>).</summary>
        public static event Action<string> RoomClosed;

        /// <summary>내가 방장에게 강퇴되어 대기실 wss 연결이 끊겼을 때 호출됨.</summary>
        public static event Action Kicked;

        /// <summary>인자: 서버의 Error 패킷 또는 Relay 오류의 메세지.</summary>
        public static event Action<string> ErrorReceived;

        /// <summary>대기실 wss 연결이 예기치 않게 끊겼을 때 호출됨.</summary>
        public static event Action Disconnected;

        // ***************************************************************
        // https
        // ***************************************************************

        /// <summary>열려 있는 방 목록 조회.</summary>
        public static async Awaitable<RoomListResponseDTO> GetRooms()
        {
            return await LoginManager.SendAuthorized<RoomListResponseDTO>(LOBBY_SERVER + "/api/lobby/list", "{}");
        }

        /// <summary>방을 만들고 방장으로 대기실에 입장.</summary>
        public static async Awaitable<RoomEnterResponseDTO> CreateRoom(RoomCreateRequestDTO request)
        {
            RoomEnterResponseDTO response = await LoginManager.SendAuthorized<RoomEnterResponseDTO>(LOBBY_SERVER + "/api/lobby/create", JsonUtility.ToJson(request));
            return await EnterRoom(response);
        }

        /// <summary>방에 참가하고 대기실에 입장.</summary>
        public static async Awaitable<RoomEnterResponseDTO> JoinRoom(RoomJoinRequestDTO request)
        {
            RoomEnterResponseDTO response = await LoginManager.SendAuthorized<RoomEnterResponseDTO>(LOBBY_SERVER + "/api/lobby/join", JsonUtility.ToJson(request));
            return await EnterRoom(response);
        }

        /// <summary>방장이 방을 닫고 대기실에서 나감. 다른 멤버는 <see cref="LobbyClosed"/>를 받음.</summary>
        public static async Awaitable<ResponseDTO> CloseRoom()
        {
            if (CurrentRoom == null)
            {
                return new ResponseDTO { responseMessage = CommonMessages.NOT_IN_ROOM };
            }

            string postData = JsonUtility.ToJson(new RoomCloseRequestDTO { lobbyId = CurrentRoom.lobbyId });
            ResponseDTO response = await LoginManager.SendAuthorized<ResponseDTO>(LOBBY_SERVER + "/api/lobby/close", postData);

            if (ResponseMessageUtil.IsOk(response.responseMessage))
            {
                CloseSocket();
            }

            return response;
        }

        /// <summary>방 생성·참가에 성공했으면 입장권으로 대기실 wss에 연결하고 인증함.</summary>
        private static async Awaitable<RoomEnterResponseDTO> EnterRoom(RoomEnterResponseDTO response)
        {
            if (!ResponseMessageUtil.IsOk(response.responseMessage))
            {
                return response;
            }

            CloseSocket();

            ClientWebSocket socket = new ClientWebSocket();
            CancellationTokenSource cancel = new CancellationTokenSource();
            sSocket = socket;
            sSocketCancel = cancel;

            try
            {
                await socket.ConnectAsync(new Uri(LOBBY_SOCKET), cancel.Token);
                await Send(RoomProtocol.Authenticate, new RoomAuthenticateRequest { lobbyTicket = response.lobbyTicket });

                // 인증 실패 시 서버는 Error(0) 패킷을 보내므로 공통 필드인 responseMessage만으로 판별
                string json = await ReceiveText(socket, cancel.Token);
                RoomAuthenticateResult result = json == null ? null : JsonUtility.FromJson<RoomPacket<RoomAuthenticateResult>>(json).payload;

                if (result == null || !ResponseMessageUtil.IsOk(result.responseMessage))
                {
                    CloseSocket();
                    return new RoomEnterResponseDTO
                    {
                        responseCode = result?.responseCode,
                        responseMessage = result?.responseMessage ?? CommonMessages.CONNECTION_ERROR
                    };
                }

                LocalAccountHashcode = result.gameAccountHashcode;
                CurrentRoom = result.lobby;
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or ArgumentException)
            {
                CloseSocket();
                return new RoomEnterResponseDTO { responseMessage = CommonMessages.CONNECTION_ERROR };
            }

            _ = ReceiveLoop(socket, cancel.Token);
            return response;
        }

        // ***************************************************************
        // wss
        // ***************************************************************

        public static async Awaitable<bool> SendChat(string message)
        {
            return await Send(RoomProtocol.Chat, new RoomChatRequest { message = message });
        }

        public static async Awaitable<bool> SetReady(bool isReady)
        {
            return await Send(RoomProtocol.Ready, new RoomReadyRequest { isReady = isReady });
        }

        /// <summary>방장이 게임 모드·맵·최대 인원을 바꿈. 서버가 승인하면 모든 멤버가 <see cref="RoomSettingsChanged"/>를 받음.</summary>
        public static async Awaitable<bool> ChangeRoomSettings(RoomSettingsPayload settings)
        {
            if (!IsHost)
            {
                return false;
            }

            return await Send(RoomProtocol.Settings, settings);
        }

        /// <summary>방장이 멤버를 강퇴함. 서버가 승인하면 남은 멤버는 <see cref="MemberKicked"/>, 강퇴된 멤버는 <see cref="Kicked"/>를 받음.</summary>
        public static async Awaitable<bool> KickMember(string gameAccountHashcode)
        {
            if (!IsHost)
            {
                return false;
            }

            return await Send(RoomProtocol.Kick, new RoomKickRequest { gameAccountHashcode = gameAccountHashcode });
        }

        /// <summary>대기실에서 나감. 방장이 나가면 서버가 방을 닫음.</summary>
        public static async Awaitable LeaveRoom()
        {
            await Send(RoomProtocol.Leave, new RoomEmptyPayload());
            CloseSocket();
        }

        /// <summary>
        /// 방장이 Relay 할당을 만들고 참여 코드와 함께 게임 시작을 요청함.
        /// 서버가 승인하면 모든 멤버가 <see cref="RoomProtocol.GameStarted"/>를 받아 Relay에 접속함.
        /// </summary>
        public static async Awaitable<bool> StartGame()
        {
            if (!IsHost)
            {
                return false;
            }

            try
            {
                await SignInUnityServices();
                sHostAllocation = await RelayService.Instance.CreateAllocationAsync(CurrentRoom.maxPlayers - 1); // 방장 제외 인원
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(sHostAllocation.AllocationId);
                return await Send(RoomProtocol.GameStart, new RoomGameStartPayload { relayJoinCode = joinCode });
            }
            catch (RequestFailedException e)
            {
                Debug.LogError("Relay 할당 실패: " + e.Message);
                ErrorReceived?.Invoke(e.Message);
                return false;
            }
        }

        /// <summary>방장이 게임을 끝냄. 서버가 승인하면 모든 멤버가 같은 방의 대기실로 돌아감 (<see cref="GameEnded"/>).</summary>
        public static async Awaitable<bool> EndGame()
        {
            if (!IsHost)
            {
                return false;
            }

            return await Send(RoomProtocol.GameEnd, new RoomGameEndPayload { reason = LobbyMessages.GAME_FINISHED });
        }

        private static async Awaitable<bool> Send<T>(RoomProtocol protocol, T payload)
        {
            ClientWebSocket socket = sSocket;
            CancellationTokenSource cancel = sSocketCancel;

            if (socket == null)
            {
                return false;
            }

            string json = JsonUtility.ToJson(new RoomPacket<T> { protocolId = (int)protocol, payload = payload });
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            await sSendLock.WaitAsync();

            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancel.Token);
                return true;
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                return false;
            }
            finally
            {
                sSendLock.Release();
            }
        }

        /// <summary>텍스트 메세지 하나를 끝까지 받아 반환. 서버가 연결을 닫았으면 null.</summary>
        private static async Task<string> ReceiveText(ClientWebSocket socket, CancellationToken cancel)
        {
            byte[] buffer = new byte[RECEIVE_BUFFER_BYTES];

            using (MemoryStream message = new MemoryStream())
            {
                WebSocketReceiveResult frame;

                do
                {
                    frame = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancel);

                    if (frame.MessageType == WebSocketMessageType.Close)
                    {
                        return null;
                    }

                    message.Write(buffer, 0, frame.Count);
                } while (!frame.EndOfMessage);

                return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }

        /// <summary><paramref name="socket"/>이 닫힐 때까지 서버 패킷을 받아 처리함.</summary>
        private static async Task ReceiveLoop(ClientWebSocket socket, CancellationToken cancel)
        {
            try
            {
                while (true)
                {
                    string json = await ReceiveText(socket, cancel);

                    if (json == null)
                    {
                        break;
                    }

                    switch ((RoomProtocol)JsonUtility.FromJson<RoomPacket<RoomEmptyPayload>>(json).protocolId)
                    {
                        case RoomProtocol.Error:
                        {
                            ErrorReceived?.Invoke(Payload<ResponseDTO>(json).responseMessage);
                            break;
                        }
                        case RoomProtocol.MemberJoined:
                        {
                            RoomMemberDTO member = Payload<RoomMemberDTO>(json);
                            CurrentRoom.members.Add(member);
                            MemberJoined?.Invoke(member);
                            break;
                        }
                        case RoomProtocol.MemberLeft:
                        {
                            RoomMemberLeftPayload left = Payload<RoomMemberLeftPayload>(json);
                            RoomMemberDTO member = CurrentRoom.members.Find(m => m.gameAccountHashcode == left.gameAccountHashcode);

                            if (member == null)
                            {
                                break;
                            }

                            CurrentRoom.members.Remove(member);

                            if (left.isKicked)
                            {
                                MemberKicked?.Invoke(member);
                            }
                            else
                            {
                                MemberLeft?.Invoke(member);
                            }

                            break;
                        }
                        case RoomProtocol.ChatReceived:
                        {
                            ChatReceived?.Invoke(Payload<RoomChatPayload>(json));
                            break;
                        }
                        case RoomProtocol.ReadyChanged:
                        {
                            RoomReadyPayload ready = Payload<RoomReadyPayload>(json);
                            RoomMemberDTO member = CurrentRoom.members.Find(m => m.gameAccountHashcode == ready.gameAccountHashcode);

                            if (member != null)
                            {
                                member.isReady = ready.isReady;
                            }

                            ReadyChanged?.Invoke(ready);
                            break;
                        }
                        case RoomProtocol.SettingsChanged:
                        {
                            RoomSettingsPayload settings = Payload<RoomSettingsPayload>(json);
                            CurrentRoom.gameMode = settings.gameMode;
                            CurrentRoom.map = settings.map;
                            CurrentRoom.maxPlayers = settings.maxPlayers;
                            RoomSettingsChanged?.Invoke();
                            break;
                        }
                        case RoomProtocol.Kicked:
                        {
                            CloseSocket();
                            Kicked?.Invoke();
                            return;
                        }
                        case RoomProtocol.RoomClosed:
                        {
                            CloseSocket();
                            RoomClosed?.Invoke(Payload<RoomClosedPayload>(json).reason);
                            return;
                        }
                        case RoomProtocol.GameStarted:
                        {
                            // 게임 중에도 대기실 wss 연결은 유지됨 (서버의 방 세션 유지)
                            string joinCode = Payload<RoomGameStartPayload>(json).relayJoinCode;
                            NetworkManager network = NetworkManager.Singleton;
                            UnityTransport transport = network.GetComponent<UnityTransport>();

                            if (IsHost)
                            {
                                transport.SetRelayServerData(sHostAllocation.ToRelayServerData(RELAY_CONNECTION_TYPE));
                                sHostAllocation = null;

                                if (!network.StartHost())
                                {
                                    // 방은 유지하고 대기실 상태로 되돌림 — 다른 멤버는 GameEnded로 실패를 알게 됨
                                    GameStartFailed?.Invoke(LobbyMessages.HOST_START_FAILED);
                                    await Send(RoomProtocol.GameEnd, new RoomGameEndPayload { reason = LobbyMessages.HOST_START_FAILED });
                                    break;
                                }

                                // 클라이언트는 접속 시 Netcode가 현재 씬으로 동기화해 줌
                                SceneEventProgressStatus status = network.SceneManager.LoadScene(INGAME_SCENE, LoadSceneMode.Single);

                                if (status != SceneEventProgressStatus.Started)
                                {
                                    Debug.LogError($"인게임 씬 로드 실패: {status}");
                                }

                                GameStarted?.Invoke();
                                break;
                            }

                            try
                            {
                                await SignInUnityServices();
                                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                                transport.SetRelayServerData(allocation.ToRelayServerData(RELAY_CONNECTION_TYPE));
                            }
                            catch (RequestFailedException e)
                            {
                                Debug.LogError("Relay 참가 실패: " + e.Message);
                                GameStartFailed?.Invoke(CommonMessages.RELAY_CONNECTION_FAILED);
                                break;
                            }

                            // 접속 결과는 HandleClientConnectionEvent가 알림 (실패는 Unity Transport 접속 시도 횟수 × 간격 후)
                            network.OnConnectionEvent += HandleClientConnectionEvent;

                            if (!network.StartClient())
                            {
                                network.OnConnectionEvent -= HandleClientConnectionEvent;
                                GameStartFailed?.Invoke(CommonMessages.RELAY_CONNECTION_FAILED);
                            }

                            break;
                        }
                        case RoomProtocol.GameEnded:
                        {
                            string reason = Payload<RoomGameEndPayload>(json).reason;
                            NetworkManager network = NetworkManager.Singleton;

                            // 아직 호스트에 접속 중인 클라이언트의 접속 시도도 여기서 중단됨
                            network.OnConnectionEvent -= HandleClientConnectionEvent;

                            if (network.IsListening)
                            {
                                network.Shutdown();
                            }

                            foreach (RoomMemberDTO member in CurrentRoom.members)
                            {
                                member.isReady = false;
                            }

                            if (reason != LobbyMessages.HOST_START_FAILED)
                            {
                                GameEnded?.Invoke();
                            }
                            else if (!IsHost) // 방장은 StartHost 실패 시점에 이미 알림
                            {
                                GameStartFailed?.Invoke(reason);
                            }

                            break;
                        }
                    }
                }
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // 연결 끊김 — 아래에서 처리
            }

            // CloseSocket으로 직접 닫은 경우 sSocket이 이미 다른 값이므로 이벤트를 보내지 않음
            if (sSocket == socket)
            {
                CloseSocket();
                Disconnected?.Invoke();
            }
        }

        /// <summary>클라이언트의 Relay 호스트 접속 성공·실패를 <see cref="GameStarted"/>, <see cref="GameStartFailed"/>로 알림.</summary>
        private static void HandleClientConnectionEvent(NetworkManager network, ConnectionEventData data)
        {
            if (data.EventType is not (ConnectionEvent.ClientConnected or ConnectionEvent.ClientDisconnected))
            {
                return;
            }

            network.OnConnectionEvent -= HandleClientConnectionEvent;

            if (data.EventType == ConnectionEvent.ClientConnected)
            {
                GameStarted?.Invoke();
            }
            else
            {
                Debug.LogError("호스트 접속 실패: " + network.DisconnectReason);
                GameStartFailed?.Invoke(CommonMessages.RELAY_CONNECTION_FAILED);
            }
        }

        private static T Payload<T>(string json)
        {
            return JsonUtility.FromJson<RoomPacket<T>>(json).payload;
        }

        /// <summary>대기실 wss 연결을 끊고 대기실 상태를 비움.</summary>
        private static void CloseSocket()
        {
            if (sSocket == null)
            {
                return;
            }

            sSocketCancel.Cancel();
            sSocket.Dispose();
            sSocketCancel.Dispose();
            sSocket = null;
            sSocketCancel = null;
            CurrentRoom = null;
        }

        /// <summary>Relay 사용에 필요한 Unity Services 초기화와 익명 로그인.</summary>
        private static async Task SignInUnityServices()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }
    }
}