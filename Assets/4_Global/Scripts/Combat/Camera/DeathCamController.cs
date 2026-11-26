using FishNet.Object;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>로컬 플레이어 사망 후 킬러 방향 포커스, 관전하기 UI 표시 및 킬러 3인칭 자유 회전 관전 카메라 전환을 제어한다.</summary>
[DefaultExecutionOrder(-90)]
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
public class DeathCamController : MonoBehaviour
{
    // ***************************************************************
    // Types
    // ***************************************************************

    private enum EDeathCamState
    {
        Inactive,
        FocusingCorpse,
        WaitingForSpectateClick,
        Spectating
    }

    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float DefaultSpectateVerticalAngle = 20f; // 관전 시작 시 기본 수직 오빗 각도 (도 단위)

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>일반 플레이 중 사용하는 TPS 시네머신 카메라.</summary>
    [Header("카메라 참조")]
    [SerializeField] private CinemachineCamera playerCamera;

    /// <summary>킬러 3인칭 자유 회전 관전에 사용하는 전용 시네머신 카메라.</summary>
    [SerializeField] private CinemachineCamera spectateCamera;

    /// <summary>사망 또는 관전 진입 시 1인칭 정조준을 강제 해제할 ADS 컨트롤러.</summary>
    [SerializeField] private FpsAdsController fpsAdsController;

    /// <summary>사망 후 3초 경과 시 표시할 관전하기 UI 프리팹.</summary>
    [Header("데스캠 UI 및 타이밍")]
    [SerializeField] private DeathCamUI deathCamUIPrefab;

    /// <summary>사망 및 관전 중 숨길 화면 중앙 크로스헤어 오브젝트.</summary>
    [SerializeField] private GameObject crosshairObject;

    /// <summary>사망 직후 킬러 방향을 바라보며 대기하는 시간 (초 단위).</summary>
    [Min(0f)]
    [SerializeField] private float deathFocusDuration = 3f;

    /// <summary>사망 직후 카메라 시선이 킬러 방향으로 회전하는 보간 속도.</summary>
    [Min(0.1f)]
    [SerializeField] private float killerLookTurnSpeed = 10f;

    /// <summary>비활성 대기 상태에서 <see cref="spectateCamera"/>에 적용할 우선순위.</summary>
    [Header("카메라 우선순위")]
    [SerializeField] private int inactiveSpectatePriority = 0;

    /// <summary>관전 활성화 상태에서 <see cref="spectateCamera"/>에 적용할 우선순위.</summary>
    [SerializeField] private int activeSpectatePriority = 10;

    /// <summary><see cref="playerCamera"/>의 마우스 시점 입력 컨트롤러.</summary>
    private CinemachineInputAxisController _playerInputAxisController;

    /// <summary><see cref="playerCamera"/>의 시선 조준 컴포저.</summary>
    private CinemachineRotationComposer _playerRotationComposer;

    /// <summary><see cref="spectateCamera"/>의 3인칭 공전 추적 컴포넌트.</summary>
    private CinemachineOrbitalFollow _spectateOrbitalFollow;

    /// <summary><see cref="spectateCamera"/>의 마우스 시점 입력 컨트롤러.</summary>
    private CinemachineInputAxisController _spectateInputAxisController;

    /// <summary>런타임에 생성된 데스캠 UI 인스턴스.</summary>
    private DeathCamUI _deathCamUIInstance;

    /// <summary>관전 대상의 회전과 무관하게 수직 자세를 유지하는 로컬 전용 카메라 추적 앵커.</summary>
    private Transform _spectateAnchor;

    /// <summary>사망 직후 기존 카메라 위치에서 킬러 방향만 바라보게 유도하는 로컬 전용 주시 앵커.</summary>
    private Transform _killerLookAtAnchor;

    /// <summary>사망 시퀀스를 시작한 로컬 플레이어 참조.</summary>
    private PlayerController _localDeadPlayer;

    /// <summary>서버로부터 전달받은 킬러의 <see cref="NetworkObject"/> 참조.</summary>
    private NetworkObject _killerNetworkObject;

    /// <summary>킬러의 카메라 기준 좌표를 조회하기 위한 <see cref="PlayerController"/> 참조.</summary>
    private PlayerController _killerPlayer;

    /// <summary>현재 관전 중인 대상 플레이어 컨트롤러.</summary>
    private PlayerController _spectateTargetPlayer;

    /// <summary>현재 관전 중인 대상 트랜스폼.</summary>
    private Transform _spectateTargetTransform;

    /// <summary>현재 관전 중인 대상의 체력 컴포넌트.</summary>
    private PlayerHealth _spectateTargetHealth;

    /// <summary>사망 직전 <see cref="_playerRotationComposer"/>의 원래 화면 구성 오프셋.</summary>
    private Vector2 _savedPlayerScreenPosition;

    /// <summary><see cref="_savedPlayerScreenPosition"/> 백업 여부.</summary>
    private bool _hasSavedPlayerScreenPosition;

    /// <summary>사망 포커스 중 현재 카메라가 바라보는 정규화 시선 방향.</summary>
    private Vector3 _currentKillerLookDir;

    /// <summary>사망 포커스 중 킬러 방향 주시가 활성화되었는지 여부.</summary>
    private bool _hasKillerLookDir;

    /// <summary>현재 데스캠 진행 상태.</summary>
    private EDeathCamState _state;

    /// <summary>사망 포커스 종료까지 남은 시간 (초 단위).</summary>
    private float _focusRemainingTime;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>일반 플레이용 <see cref="CinemachineCamera"/> 참조.</summary>
    public CinemachineCamera PlayerCamera => playerCamera;

    /// <summary>현재 킬러 관전 모드가 활성화되어 있는지 여부.</summary>
    public bool IsSpectating => _state == EDeathCamState.Spectating;

    /// <summary>사망 포커스, 관전 버튼 대기 또는 관전 상태 중 하나라도 활성화되어 있는지 여부.</summary>
    public bool IsDeathCamActive => _state != EDeathCamState.Inactive;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        if (spectateCamera == null)
        {
            spectateCamera = GetComponent<CinemachineCamera>();
        }

        if (fpsAdsController == null)
        {
            fpsAdsController = FindAnyObjectByType<FpsAdsController>(FindObjectsInactive.Include);
        }

        if (spectateCamera != null)
        {
            _spectateOrbitalFollow = spectateCamera.GetComponent<CinemachineOrbitalFollow>();
            _spectateInputAxisController = spectateCamera.GetComponent<CinemachineInputAxisController>();
            spectateCamera.Priority = new PrioritySettings
            {
                Enabled = true,
                Value = inactiveSpectatePriority
            };
        }

        if (_spectateInputAxisController != null)
        {
            _spectateInputAxisController.enabled = false;
        }

        if (playerCamera != null)
        {
            _playerInputAxisController = playerCamera.GetComponent<CinemachineInputAxisController>();
            _playerRotationComposer = playerCamera.GetComponent<CinemachineRotationComposer>();
        }

        _spectateAnchor = new GameObject("SpectateCameraAnchor").transform;
        _killerLookAtAnchor = new GameObject("KillerLookAtAnchor").transform;
    }

    private void Update()
    {
        if (_state != EDeathCamState.FocusingCorpse) return;

        _focusRemainingTime -= Time.deltaTime;
        if (_focusRemainingTime > 0f) return;

        _state = EDeathCamState.WaitingForSpectateClick;

        if (EventSystem.current == null)
        {
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<InputSystemUIInputModule>();
        }

        if (_deathCamUIInstance == null && deathCamUIPrefab != null)
        {
            _deathCamUIInstance = Instantiate(deathCamUIPrefab);
            _deathCamUIInstance.OnSpectateClicked += HandleSpectateClicked;
        }

        if (_playerInputAxisController != null)
        {
            _playerInputAxisController.enabled = false;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _deathCamUIInstance?.Show();
    }

    private void LateUpdate()
    {
        if ((_state == EDeathCamState.FocusingCorpse || _state == EDeathCamState.WaitingForSpectateClick)
            && _hasKillerLookDir
            && _killerLookAtAnchor != null)
        {
            if (_killerNetworkObject != null && _killerNetworkObject.IsSpawned)
            {
                Vector3 camPos = Camera.main != null
                    ? Camera.main.transform.position
                    : (playerCamera != null ? playerCamera.transform.position : Vector3.zero);
                Vector3 killerPos = _killerPlayer != null
                    ? _killerPlayer.GetCameraTargetPosition()
                    : _killerNetworkObject.transform.position;
                Vector3 toKiller = killerPos - camPos;
                float dist = toKiller.magnitude;
                if (dist > 0.01f)
                {
                    Vector3 targetDir = toKiller / dist;
                    Quaternion currentRot = Quaternion.LookRotation(_currentKillerLookDir, Vector3.up);
                    Quaternion targetRot = Quaternion.LookRotation(targetDir, Vector3.up);
                    float turnT = 1f - Mathf.Exp(-killerLookTurnSpeed * Time.deltaTime);
                    _currentKillerLookDir = (Quaternion.Slerp(currentRot, targetRot, turnT) * Vector3.forward).normalized;
                    _killerLookAtAnchor.SetPositionAndRotation(camPos + _currentKillerLookDir * Mathf.Max(1f, dist), Quaternion.identity);
                }
            }
            return;
        }

        if (_state != EDeathCamState.Spectating || _spectateAnchor == null) return;

        bool isSpectatingOther = _spectateTargetPlayer != _localDeadPlayer;
        bool isTargetInvalid = _spectateTargetTransform == null
            || (_spectateTargetPlayer != null && !_spectateTargetPlayer.IsSpawned)
            || (isSpectatingOther && ((_spectateTargetPlayer != null && _spectateTargetPlayer.IsDead)
                || (_spectateTargetHealth != null && _spectateTargetHealth.IsDead)));

        if (isTargetInvalid)
        {
            TrySelectSpectateTarget(null);
        }

        if (_spectateTargetPlayer != null)
        {
            _spectateAnchor.SetPositionAndRotation(_spectateTargetPlayer.GetCameraTargetPosition(), Quaternion.identity);
        }
        else if (_spectateTargetTransform != null)
        {
            _spectateAnchor.SetPositionAndRotation(_spectateTargetTransform.position, Quaternion.identity);
        }
    }

    private void OnDestroy()
    {
        RestorePlayerComposerScreenPosition();

        if (_deathCamUIInstance != null)
        {
            _deathCamUIInstance.OnSpectateClicked -= HandleSpectateClicked;
        }

        if (_spectateAnchor != null)
        {
            Destroy(_spectateAnchor.gameObject);
            _spectateAnchor = null;
        }

        if (_killerLookAtAnchor != null)
        {
            Destroy(_killerLookAtAnchor.gameObject);
            _killerLookAtAnchor = null;
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>플레이어 기본 카메라 참조를 연결한다.</summary>
    public void BindPlayerCamera(CinemachineCamera cam)
    {
        if (cam == null || cam == spectateCamera || cam.GetComponent<FpsAdsController>() != null) return;

        playerCamera = cam;
        _playerInputAxisController = playerCamera.GetComponent<CinemachineInputAxisController>();
        _playerRotationComposer = playerCamera.GetComponent<CinemachineRotationComposer>();
    }

    /// <summary>로컬 플레이어 사망 시 크로스헤어를 숨기고 현재 카메라 위치에서 킬러 방향을 바라보는 포커스 단계를 시작한다.</summary>
    public void BeginDeathSequence(PlayerController localDeadPlayer, NetworkObject killerNetworkObject)
    {
        if (localDeadPlayer == null) return;

        fpsAdsController?.ForceExitAds();

        if (playerCamera == null)
        {
            CinemachineCamera[] cameras = FindObjectsByType<CinemachineCamera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null
                    && cameras[i] != spectateCamera
                    && cameras[i].GetComponent<FpsAdsController>() == null)
                {
                    BindPlayerCamera(cameras[i]);
                    break;
                }
            }
        }

        _localDeadPlayer = localDeadPlayer;
        _killerNetworkObject = killerNetworkObject;
        _killerPlayer = killerNetworkObject != null ? killerNetworkObject.GetComponent<PlayerController>() : null;
        _spectateTargetPlayer = null;
        _spectateTargetTransform = null;
        _spectateTargetHealth = null;
        _hasKillerLookDir = false;

        _deathCamUIInstance?.Hide();

        if (crosshairObject != null)
        {
            crosshairObject.SetActive(false);
        }

        if (spectateCamera != null)
        {
            spectateCamera.Priority = new PrioritySettings
            {
                Enabled = true,
                Value = inactiveSpectatePriority
            };
            spectateCamera.Follow = null;
            spectateCamera.LookAt = null;
        }

        if (_spectateInputAxisController != null)
        {
            _spectateInputAxisController.enabled = false;
        }

        bool hasValidKiller = _killerNetworkObject != null && _killerNetworkObject.IsSpawned;

        if (playerCamera != null && _localDeadPlayer.CameraAnchor != null)
        {
            playerCamera.Follow = _localDeadPlayer.CameraAnchor;

            if (hasValidKiller && _killerLookAtAnchor != null)
            {
                if (_playerRotationComposer != null)
                {
                    if (!_hasSavedPlayerScreenPosition)
                    {
                        _savedPlayerScreenPosition = _playerRotationComposer.Composition.ScreenPosition;
                        _hasSavedPlayerScreenPosition = true;
                    }

                    ScreenComposerSettings composition = _playerRotationComposer.Composition;
                    composition.ScreenPosition = Vector2.zero;
                    _playerRotationComposer.Composition = composition;
                }

                Vector3 camPos = Camera.main != null ? Camera.main.transform.position : playerCamera.transform.position;
                Vector3 initialForward = Camera.main != null ? Camera.main.transform.forward : playerCamera.transform.forward;
                Vector3 killerPos = _killerPlayer != null
                    ? _killerPlayer.GetCameraTargetPosition()
                    : _killerNetworkObject.transform.position;

                _currentKillerLookDir = initialForward.sqrMagnitude > 0.0001f ? initialForward.normalized : Vector3.forward;
                _hasKillerLookDir = true;
                _killerLookAtAnchor.SetPositionAndRotation(
                    camPos + _currentKillerLookDir * Mathf.Max(1f, Vector3.Distance(camPos, killerPos)),
                    Quaternion.identity);
                playerCamera.LookAt = _killerLookAtAnchor;
            }
            else
            {
                RestorePlayerComposerScreenPosition();
                playerCamera.LookAt = _localDeadPlayer.CameraAnchor;
            }
        }

        if (_playerInputAxisController != null)
        {
            _playerInputAxisController.enabled = !hasValidKiller;
        }

        _focusRemainingTime = Mathf.Max(0f, deathFocusDuration);
        _state = EDeathCamState.FocusingCorpse;
    }

    /// <summary>리스폰 또는 라운드 초기화 시 카메라, 우선순위, 타겟, 커서, 크로스헤어 및 데스캠 UI를 원래 상태로 원복한다.</summary>
    public void ResetDeathCam()
    {
        PlayerController previousLocalPlayer = _localDeadPlayer;

        _state = EDeathCamState.Inactive;
        _focusRemainingTime = 0f;
        _localDeadPlayer = null;
        _killerNetworkObject = null;
        _killerPlayer = null;
        _spectateTargetPlayer = null;
        _spectateTargetTransform = null;
        _spectateTargetHealth = null;
        _hasKillerLookDir = false;

        _deathCamUIInstance?.Hide();
        fpsAdsController?.ForceExitAds();
        RestorePlayerComposerScreenPosition();

        if (crosshairObject != null)
        {
            crosshairObject.SetActive(true);
        }

        if (spectateCamera != null)
        {
            spectateCamera.Priority = new PrioritySettings
            {
                Enabled = true,
                Value = inactiveSpectatePriority
            };
            spectateCamera.Follow = null;
            spectateCamera.LookAt = null;
        }

        if (_spectateInputAxisController != null)
        {
            _spectateInputAxisController.enabled = false;
        }

        if (playerCamera != null)
        {
            if (previousLocalPlayer != null && previousLocalPlayer.CameraAnchor != null)
            {
                playerCamera.Follow = previousLocalPlayer.CameraAnchor;
                playerCamera.LookAt = previousLocalPlayer.CameraAnchor;
            }

            if (_playerInputAxisController != null)
            {
                _playerInputAxisController.enabled = true;
            }

            CinemachineWeaponFeedback feedback = playerCamera.GetComponent<CinemachineWeaponFeedback>();
            if (feedback != null)
            {
                feedback.ResetFeedback();
            }
        }

        if (previousLocalPlayer != null && previousLocalPlayer.IsSpawned && previousLocalPlayer.IsOwner)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>관전하기 버튼 클릭 시 UI를 숨기고 킬러 3인칭 자유 회전 관전으로 전환한다.</summary>
    private void HandleSpectateClicked()
    {
        if (_state != EDeathCamState.WaitingForSpectateClick) return;

        _deathCamUIInstance?.Hide();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        TrySelectSpectateTarget(_killerNetworkObject);

        if (_spectateAnchor != null)
        {
            if (_spectateTargetPlayer != null)
            {
                _spectateAnchor.SetPositionAndRotation(_spectateTargetPlayer.GetCameraTargetPosition(), Quaternion.identity);
            }
            else if (_spectateTargetTransform != null)
            {
                _spectateAnchor.SetPositionAndRotation(_spectateTargetTransform.position, Quaternion.identity);
            }
        }

        if (_spectateOrbitalFollow != null)
        {
            float initialYaw = Camera.main != null
                ? Camera.main.transform.eulerAngles.y
                : (_spectateTargetTransform != null ? _spectateTargetTransform.eulerAngles.y : 0f);
            _spectateOrbitalFollow.HorizontalAxis.Value = initialYaw;
            _spectateOrbitalFollow.VerticalAxis.Value = DefaultSpectateVerticalAngle;
        }

        if (_playerInputAxisController != null)
        {
            _playerInputAxisController.enabled = false;
        }

        if (spectateCamera != null)
        {
            spectateCamera.Follow = _spectateAnchor;
            spectateCamera.LookAt = _spectateAnchor;
            spectateCamera.Priority = new PrioritySettings
            {
                Enabled = true,
                Value = activeSpectatePriority
            };
        }

        if (_spectateInputAxisController != null)
        {
            _spectateInputAxisController.enabled = true;
        }

        _state = EDeathCamState.Spectating;
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>사망 포커스 중 변경한 <see cref="_playerRotationComposer"/>의 화면 구성 오프셋을 원래 값으로 복원한다.</summary>
    private void RestorePlayerComposerScreenPosition()
    {
        if (!_hasSavedPlayerScreenPosition) return;

        if (_playerRotationComposer != null)
        {
            ScreenComposerSettings composition = _playerRotationComposer.Composition;
            composition.ScreenPosition = _savedPlayerScreenPosition;
            _playerRotationComposer.Composition = composition;
        }

        _hasSavedPlayerScreenPosition = false;
    }

    /// <summary><paramref name="preferredKiller"/> 또는 생존 중인 다른 플레이어를 관전 대상으로 선택하고, 없으면 본인 시체를 유지한다.</summary>
    private bool TrySelectSpectateTarget(NetworkObject preferredKiller)
    {
        if (preferredKiller != null && preferredKiller.IsSpawned)
        {
            PlayerController killerPlayer = preferredKiller.GetComponent<PlayerController>();
            PlayerHealth killerHealth = preferredKiller.GetComponent<PlayerHealth>();
            bool isKillerAlive = (killerPlayer == null || !killerPlayer.IsDead)
                && (killerHealth == null || !killerHealth.IsDead);

            if (killerPlayer != _localDeadPlayer && isKillerAlive)
            {
                _spectateTargetPlayer = killerPlayer;
                _spectateTargetTransform = preferredKiller.transform;
                _spectateTargetHealth = killerHealth;
                return true;
            }
        }

        PlayerController[] candidates = FindObjectsByType<PlayerController>();
        for (int i = 0; i < candidates.Length; i++)
        {
            PlayerController candidate = candidates[i];
            if (candidate == null || candidate == _localDeadPlayer || !candidate.IsSpawned || candidate.IsDead)
            {
                continue;
            }

            _spectateTargetPlayer = candidate;
            _spectateTargetTransform = candidate.transform;
            _spectateTargetHealth = candidate.GetComponent<PlayerHealth>();
            return true;
        }

        if (_localDeadPlayer != null && _localDeadPlayer.IsSpawned)
        {
            _spectateTargetPlayer = _localDeadPlayer;
            _spectateTargetTransform = _localDeadPlayer.transform;
            _spectateTargetHealth = _localDeadPlayer.GetComponent<PlayerHealth>();
            return true;
        }

        _spectateTargetPlayer = null;
        _spectateTargetTransform = null;
        _spectateTargetHealth = null;
        return false;
    }
}
