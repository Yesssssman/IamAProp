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
        private const string LOBBY_SERVER = "http://127.0.0.1/dev"; // PLACEHOLDER: 개발 서버 주소
        private const string LOBBY_SOCKET = "ws://127.0.0.1/dev/ws/iamaprop/lobby"; // PLACEHOLDER: 개발 서버 주소
#elif PROD
        private const string LOBBY_SERVER = "http://127.0.0.1/prod"; // PLACEHOLDER: 운영 서버 주소
        private const string LOBBY_SOCKET = "ws://127.0.0.1/prod/ws/iamaprop/lobby"; // PLACEHOLDER: 운영 서버 주소
#else
        private const string LOBBY_SERVER = "http://127.0.0.1:5050";
        private const string LOBBY_SOCKET = "ws://127.0.0.1:5050/ws/iamaprop/lobby";
#endif

        // PLACEHOLDER: 인게임 씬이 구현되기 전까지 쓰는 임시 씬
        private const string INGAME_SCENE = "YesmanTestScene";

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
        public static LobbyStateDTO CurrentLobby { get; private set; }

        /// <summary>대기실 wss 인증으로 받은 내 게임 계정 해시코드.</summary>
        public static string LocalAccountHashcode { get; private set; }

        public static bool IsHost => CurrentLobby != null && CurrentLobby.hostAccountHashcode == LocalAccountHashcode;

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
            CurrentLobby = null;
            LocalAccountHashcode = null;
        }

        // ***************************************************************
        // 대기실 이벤트 (메인 스레드에서 호출됨)
        // ***************************************************************

        public static event Action<LobbyMemberDTO> MemberJoined;

        /// <summary>인자: <see cref="CurrentLobby"/>에서 제거된 멤버.</summary>
        public static event Action<LobbyMemberDTO> MemberLeft;

        public static event Action<LobbyChatPayload> ChatReceived;

        public static event Action<LobbyReadyPayload> ReadyChanged;

        /// <summary>방장은 호스트 시작 직후, 클라이언트는 호스트 접속 완료 후 호출됨.</summary>
        public static event Action GameStarted;

        /// <summary>인자: <see cref="LobbyMessages.HOST_START_FAILED"/> 또는 <see cref="CommonMessages.RELAY_CONNECTION_FAILED"/>.</summary>
        public static event Action<string> GameStartFailed;

        /// <summary>방장이 게임을 끝내 같은 방의 대기실로 돌아왔을 때 호출됨. 모든 멤버의 준비 상태는 해제됨.</summary>
        public static event Action GameEnded;

        /// <summary>인자: 서버가 보낸 닫힌 이유 (<see cref="LobbyClosedPayload.reason"/>).</summary>
        public static event Action<string> LobbyClosed;

        /// <summary>인자: 서버의 Error 패킷 또는 Relay 오류의 메세지.</summary>
        public static event Action<string> ErrorReceived;

        /// <summary>대기실 wss 연결이 예기치 않게 끊겼을 때 호출됨.</summary>
        public static event Action Disconnected;

        // ***************************************************************
        // https
        // ***************************************************************

        /// <summary>열려 있는 방 목록 조회.</summary>
        public static async Awaitable<LobbyListResponseDTO> GetLobbies()
        {
            return await LoginManager.SendAuthorized<LobbyListResponseDTO>(LOBBY_SERVER + "/api/lobby/list", "{}");
        }

        /// <summary>방을 만들고 방장으로 대기실에 입장.</summary>
        public static async Awaitable<LobbyEnterResponseDTO> CreateLobby(LobbyCreateRequestDTO request)
        {
            LobbyEnterResponseDTO response = await LoginManager.SendAuthorized<LobbyEnterResponseDTO>(LOBBY_SERVER + "/api/lobby/create", JsonUtility.ToJson(request));
            return await EnterLobby(response);
        }

        /// <summary>방에 참가하고 대기실에 입장.</summary>
        public static async Awaitable<LobbyEnterResponseDTO> JoinLobby(LobbyJoinRequestDTO request)
        {
            LobbyEnterResponseDTO response = await LoginManager.SendAuthorized<LobbyEnterResponseDTO>(LOBBY_SERVER + "/api/lobby/join", JsonUtility.ToJson(request));
            return await EnterLobby(response);
        }

        /// <summary>방장이 방을 닫고 대기실에서 나감. 다른 멤버는 <see cref="LobbyClosed"/>를 받음.</summary>
        public static async Awaitable<ResponseDTO> CloseLobby()
        {
            if (CurrentLobby == null)
            {
                return new ResponseDTO { responseMessage = CommonMessages.NOT_IN_LOBBY };
            }

            string postData = JsonUtility.ToJson(new LobbyCloseRequestDTO { lobbyId = CurrentLobby.lobbyId });
            ResponseDTO response = await LoginManager.SendAuthorized<ResponseDTO>(LOBBY_SERVER + "/api/lobby/close", postData);

            if (ResponseMessageUtil.IsOk(response.responseMessage))
            {
                CloseSocket();
            }

            return response;
        }

        /// <summary>방 생성·참가에 성공했으면 입장권으로 대기실 wss에 연결하고 인증함.</summary>
        private static async Awaitable<LobbyEnterResponseDTO> EnterLobby(LobbyEnterResponseDTO response)
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
                await Send(LobbyProtocol.Authenticate, new LobbyAuthenticateRequest { lobbyTicket = response.lobbyTicket });

                // 인증 실패 시 서버는 Error(0) 패킷을 보내므로 공통 필드인 responseMessage만으로 판별
                string json = await ReceiveText(socket, cancel.Token);
                LobbyAuthenticateResult result = json == null ? null : JsonUtility.FromJson<LobbyPacket<LobbyAuthenticateResult>>(json).payload;

                if (result == null || !ResponseMessageUtil.IsOk(result.responseMessage))
                {
                    CloseSocket();
                    return new LobbyEnterResponseDTO
                    {
                        responseCode = result?.responseCode,
                        responseMessage = result?.responseMessage ?? CommonMessages.CONNECTION_ERROR
                    };
                }

                LocalAccountHashcode = result.gameAccountHashcode;
                CurrentLobby = result.lobby;
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException or ArgumentException)
            {
                CloseSocket();
                return new LobbyEnterResponseDTO { responseMessage = CommonMessages.CONNECTION_ERROR };
            }

            _ = ReceiveLoop(socket, cancel.Token);
            return response;
        }

        // ***************************************************************
        // wss
        // ***************************************************************

        public static async Awaitable<bool> SendChat(string message)
        {
            return await Send(LobbyProtocol.Chat, new LobbyChatRequest { message = message });
        }

        public static async Awaitable<bool> SetReady(bool isReady)
        {
            return await Send(LobbyProtocol.Ready, new LobbyReadyRequest { isReady = isReady });
        }

        /// <summary>대기실에서 나감. 방장이 나가면 서버가 방을 닫음.</summary>
        public static async Awaitable LeaveLobby()
        {
            await Send(LobbyProtocol.Leave, new LobbyEmptyPayload());
            CloseSocket();
        }

        /// <summary>
        /// 방장이 Relay 할당을 만들고 참여 코드와 함께 게임 시작을 요청함.
        /// 서버가 승인하면 모든 멤버가 <see cref="LobbyProtocol.GameStarted"/>를 받아 Relay에 접속함.
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
                sHostAllocation = await RelayService.Instance.CreateAllocationAsync(CurrentLobby.maxPlayers - 1); // 방장 제외 인원
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(sHostAllocation.AllocationId);
                return await Send(LobbyProtocol.GameStart, new LobbyGameStartPayload { relayJoinCode = joinCode });
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

            return await Send(LobbyProtocol.GameEnd, new LobbyGameEndPayload { reason = LobbyMessages.GAME_FINISHED });
        }

        private static async Awaitable<bool> Send<T>(LobbyProtocol protocol, T payload)
        {
            ClientWebSocket socket = sSocket;
            CancellationTokenSource cancel = sSocketCancel;

            if (socket == null)
            {
                return false;
            }

            string json = JsonUtility.ToJson(new LobbyPacket<T> { protocolId = (int)protocol, payload = payload });
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

                    switch ((LobbyProtocol)JsonUtility.FromJson<LobbyPacket<LobbyEmptyPayload>>(json).protocolId)
                    {
                        case LobbyProtocol.Error:
                        {
                            ErrorReceived?.Invoke(Payload<ResponseDTO>(json).responseMessage);
                            break;
                        }
                        case LobbyProtocol.MemberJoined:
                        {
                            LobbyMemberDTO member = Payload<LobbyMemberDTO>(json);
                            CurrentLobby.members.Add(member);
                            MemberJoined?.Invoke(member);
                            break;
                        }
                        case LobbyProtocol.MemberLeft:
                        {
                            string hashcode = Payload<LobbyMemberLeftPayload>(json).gameAccountHashcode;
                            LobbyMemberDTO member = CurrentLobby.members.Find(m => m.gameAccountHashcode == hashcode);

                            if (member != null)
                            {
                                CurrentLobby.members.Remove(member);
                                MemberLeft?.Invoke(member);
                            }

                            break;
                        }
                        case LobbyProtocol.ChatReceived:
                        {
                            ChatReceived?.Invoke(Payload<LobbyChatPayload>(json));
                            break;
                        }
                        case LobbyProtocol.ReadyChanged:
                        {
                            LobbyReadyPayload ready = Payload<LobbyReadyPayload>(json);
                            LobbyMemberDTO member = CurrentLobby.members.Find(m => m.gameAccountHashcode == ready.gameAccountHashcode);

                            if (member != null)
                            {
                                member.isReady = ready.isReady;
                            }

                            ReadyChanged?.Invoke(ready);
                            break;
                        }
                        case LobbyProtocol.LobbyClosed:
                        {
                            CloseSocket();
                            LobbyClosed?.Invoke(Payload<LobbyClosedPayload>(json).reason);
                            return;
                        }
                        case LobbyProtocol.GameStarted:
                        {
                            // 게임 중에도 대기실 wss 연결은 유지됨 (서버의 방 세션 유지)
                            string joinCode = Payload<LobbyGameStartPayload>(json).relayJoinCode;
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
                                    await Send(LobbyProtocol.GameEnd, new LobbyGameEndPayload { reason = LobbyMessages.HOST_START_FAILED });
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
                        case LobbyProtocol.GameEnded:
                        {
                            string reason = Payload<LobbyGameEndPayload>(json).reason;
                            NetworkManager network = NetworkManager.Singleton;

                            // 아직 호스트에 접속 중인 클라이언트의 접속 시도도 여기서 중단됨
                            network.OnConnectionEvent -= HandleClientConnectionEvent;

                            if (network.IsListening)
                            {
                                network.Shutdown();
                            }

                            foreach (LobbyMemberDTO member in CurrentLobby.members)
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
            return JsonUtility.FromJson<LobbyPacket<T>>(json).payload;
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
            CurrentLobby = null;
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
