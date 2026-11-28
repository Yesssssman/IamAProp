using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>1인칭 정조준(ADS) 입력 전환 방식.</summary>
public enum EAdsInputMode
{
    Toggle,
    Hold
}

/// <summary>로컬 플레이어의 3인칭(TPS) ↔ 1인칭 정조준(FPS ADS) 시점 전환, 각도 동기화, 로컬 몸체 숨김 및 1인칭 반동 배율을 제어한다.</summary>
[DefaultExecutionOrder(-95)]
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
public class FpsAdsController : MonoBehaviour
{
    // ***************************************************************
    // Types
    // ***************************************************************

    /// <summary>로컬 몸체 숨김 복원용 렌더러와 원본 그림자 캐스팅 모드 쌍.</summary>
    private struct BodyRendererRecord
    {
        public Renderer Renderer;
        public ShadowCastingMode OriginalShadowMode;
    }

    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float BodyHideBlendThreshold = 0.35f; // 카메라가 몸체 근처로 진입할 때 숨김을 전환하는 블렌드 임계값
    private const float KickbackEpsilon = 0.00001f; // Z축 후퇴 반동을 0으로 스냅할 임계값 (미터)

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>일반 3인칭 플레이에 사용하는 시네머신 카메라.</summary>
    [Header("카메라 참조")]
    [SerializeField] private CinemachineCamera tpsCamera;

    /// <summary>1인칭 정조준에 사용하는 전용 시네머신 카메라.</summary>
    [SerializeField] private CinemachineCamera fpsAdsCamera;

    /// <summary>사망 및 관전 상태를 조회하기 위한 씬 데스캠 컨트롤러.</summary>
    [SerializeField] private DeathCamController deathCamController;

    /// <summary>1인칭 정조준 중 숨길 화면 중앙 크로스헤어 오브젝트.</summary>
    [Header("UI 참조")]
    [SerializeField] private GameObject crosshairObject;

    /// <summary>1인칭 정조준 중 <see cref="crosshairObject"/>를 숨길지 여부.</summary>
    [SerializeField] private bool hideCrosshairInAds = true;

    /// <summary>스코프 정조준 시 표시할 전체 화면 스코프 오버레이 루트 오브젝트.</summary>
    [SerializeField] private GameObject scopeOverlayObject;

    /// <summary>무기별 스코프 레티클 스프라이트를 교체 적용할 메인 스코프 이미지.</summary>
    [SerializeField] private Image scopeOverlayImage;

    /// <summary>1인칭 블렌드 중 스코프 오버레이와 손 숨김을 활성화할 블렌드 임계값 (0.5~1).</summary>
    [Range(0.5f, 1f)]
    [SerializeField] private float scopeOverlayBlendThreshold = 0.9f;

    /// <summary>마우스 우클릭 1인칭 정조준 전환 방식 (토글 또는 홀드).</summary>
    [Header("전환 및 시야각 설정")]
    [SerializeField] private EAdsInputMode inputMode = EAdsInputMode.Toggle;

    /// <summary>1인칭 정조준 기본 수직 시야각 (도 단위).</summary>
    [Range(10f, 90f)]
    [SerializeField] private float adsFov = 32f;

    /// <summary>1인칭 정조준 시 근거리 총기 클리핑을 방지하는 카메라 Near Clip Plane (미터).</summary>
    [Range(0.005f, 0.1f)]
    [SerializeField] private float adsNearClipPlane = 0.01f;

    /// <summary>비활성 대기 상태에서 <see cref="fpsAdsCamera"/>에 적용할 우선순위.</summary>
    [Header("카메라 우선순위")]
    [SerializeField] private int inactiveAdsPriority = 0;

    /// <summary>1인칭 정조준 활성화 상태에서 <see cref="fpsAdsCamera"/>에 적용할 우선순위.</summary>
    [SerializeField] private int activeAdsPriority = 5;

    /// <summary>1인칭 정조준 시 3인칭 회전 반동에 곱하는 배율.</summary>
    [Header("1인칭 반동 배율")]
    [Range(0f, 1f)]
    [SerializeField] private float adsRecoilMultiplier = 0.3f;

    /// <summary>1인칭 정조준 시 3인칭 미세 펄스 셰이크에 곱하는 배율.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float adsShakeMultiplier = 0.3f;

    /// <summary>1인칭 정조준 발사 시 총기가 카메라 뒤쪽(-Z)으로 밀리는 기본 거리 (미터).</summary>
    [Header("1인칭 총기 Z축 반동")]
    [Min(0f)]
    [SerializeField] private float adsWeaponKickZ = 0.018f;

    /// <summary>연사 시 누적 가능한 총기 Z축 최대 후퇴 거리 (미터).</summary>
    [Min(0f)]
    [SerializeField] private float adsWeaponMaxKickZ = 0.04f;

    /// <summary>총기 Z축 반동이 목표 후퇴 거리로 튕기는 반응 속도.</summary>
    [Min(0.01f)]
    [SerializeField] private float adsWeaponKickSnappiness = 35f;

    /// <summary>총기 Z축 반동이 원래 조준 위치로 복귀하는 속도.</summary>
    [Min(0.01f)]
    [SerializeField] private float adsWeaponKickReturnSpeed = 14f;

    /// <summary><see cref="tpsCamera"/>의 3인칭 공전 추적 컴포넌트.</summary>
    private CinemachineOrbitalFollow _tpsOrbitalFollow;

    /// <summary><see cref="tpsCamera"/>의 마우스 시점 입력 컨트롤러.</summary>
    private CinemachineInputAxisController _tpsInputAxisController;

    /// <summary><see cref="tpsCamera"/>의 반동/셰이크 확장 컴포넌트.</summary>
    private CinemachineWeaponFeedback _tpsWeaponFeedback;

    /// <summary><see cref="fpsAdsCamera"/>의 1인칭 팬/틸트 회전 컴포넌트.</summary>
    private CinemachinePanTilt _fpsPanTilt;

    /// <summary><see cref="fpsAdsCamera"/>의 마우스 시점 입력 컨트롤러.</summary>
    private CinemachineInputAxisController _fpsInputAxisController;

    /// <summary><see cref="fpsAdsCamera"/>의 반동/셰이크 확장 컴포넌트.</summary>
    private CinemachineWeaponFeedback _fpsWeaponFeedback;

    /// <summary>1인칭 카메라가 위치 추적으로 사용하는 로컬 전용 눈 높이 앵커.</summary>
    private Transform _fpsEyeAnchor;

    /// <summary>현재 바인딩된 로컬 소유자 플레이어 컨트롤러.</summary>
    private PlayerController _localPlayer;

    /// <summary>현재 바인딩된 로컬 소유자 플레이어 애니메이터.</summary>
    private PlayerAnim _localPlayerAnim;

    /// <summary>로컬 1인칭 숨김 대상으로 수집된 몸체 렌더러 목록.</summary>
    private readonly List<BodyRendererRecord> _bodyRenderers = new(4);

    /// <summary>스코프 정조준 중 숨겨진 1인칭 손·무기 렌더러 목록.</summary>
    private readonly List<BodyRendererRecord> _handsRenderers = new(16);

    /// <summary><see cref="_fpsInputAxisController"/>의 원본 축별 감도 배열.</summary>
    private float[] _baseFpsAxisGains;

    /// <summary><see cref="scopeOverlayImage"/>의 기본 스코프 레티클 스프라이트.</summary>
    private Sprite _defaultScopeSprite;

    /// <summary>현재 1인칭 정조준 모드가 활성화되어 있는지 여부.</summary>
    private bool _isAdsActive;

    /// <summary>로컬 몸체 렌더러가 현재 <see cref="ShadowCastingMode.ShadowsOnly"/>로 숨겨져 있는지 여부.</summary>
    private bool _isBodyHidden;

    /// <summary>스코프 정조준 중 손·무기 렌더러가 숨겨져 있는지 여부.</summary>
    private bool _isHandsHidden;

    /// <summary>현재 스코프 UI 오버레이가 표시 중인지 여부.</summary>
    private bool _isScopeOverlayActive;

    /// <summary>3인칭 <see cref="CinemachineRotationComposer"/> 화면 오프셋으로 인한 실제 피치와 오빗 수직 축의 차이 (도 단위).</summary>
    private float _tpsComposerPitchDelta;

    /// <summary><see cref="_tpsComposerPitchDelta"/>가 측정되었는지 여부.</summary>
    private bool _hasTpsComposerPitchDelta;

    /// <summary>1인칭 총기 Z축 후퇴 반동의 목표 거리 (미터).</summary>
    private float _targetWeaponKickZ;

    /// <summary>현재 프레임에 적용 중인 1인칭 총기 Z축 후퇴 반동 거리 (미터).</summary>
    private float _currentWeaponKickZ;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 설정된 1인칭 정조준 입력 전환 방식.</summary>
    public EAdsInputMode InputMode => inputMode;

    /// <summary>1인칭 정조준 모드가 활성화되어 있는지 여부.</summary>
    public bool IsAdsActive => _isAdsActive;

    /// <summary>스코프 UI 오버레이가 현재 활성화되어 있는지 여부.</summary>
    public bool IsScopeOverlayActive => _isScopeOverlayActive;

    /// <summary>일반 3인칭 플레이용 <see cref="CinemachineCamera"/> 참조.</summary>
    public CinemachineCamera TpsCamera => tpsCamera;

    /// <summary>1인칭 정조준 전용 <see cref="CinemachineCamera"/> 참조.</summary>
    public CinemachineCamera FpsAdsCamera => fpsAdsCamera;

    /// <summary>1인칭 카메라의 현재 수평 시점 각도 (도 단위).</summary>
    public float CurrentAimYaw => _fpsPanTilt != null
        ? _fpsPanTilt.PanAxis.Value
        : (Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f);

    /// <summary>1인칭 카메라의 현재 수직 조준 피치 각도 (도 단위).</summary>
    public float CurrentAimPitch => _fpsPanTilt != null ? _fpsPanTilt.TiltAxis.Value : 0f;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        if (fpsAdsCamera == null)
        {
            fpsAdsCamera = GetComponent<CinemachineCamera>();
        }

        if (deathCamController == null)
        {
            deathCamController = FindAnyObjectByType<DeathCamController>(FindObjectsInactive.Include);
        }

        if (fpsAdsCamera != null)
        {
            _fpsPanTilt = fpsAdsCamera.GetComponent<CinemachinePanTilt>();
            _fpsInputAxisController = fpsAdsCamera.GetComponent<CinemachineInputAxisController>();
            _fpsWeaponFeedback = fpsAdsCamera.GetComponent<CinemachineWeaponFeedback>();
            if (_fpsWeaponFeedback == null)
            {
                _fpsWeaponFeedback = fpsAdsCamera.gameObject.AddComponent<CinemachineWeaponFeedback>();
            }

            fpsAdsCamera.Priority = new PrioritySettings
            {
                Enabled = true,
                Value = inactiveAdsPriority
            };
        }

        if (_fpsInputAxisController != null)
        {
            var controllers = _fpsInputAxisController.Controllers;
            _baseFpsAxisGains = new float[controllers.Count];
            for (int i = 0; i < controllers.Count; i++)
            {
                _baseFpsAxisGains[i] = controllers[i].Input.Gain;
            }
            _fpsInputAxisController.enabled = false;
        }

        if (scopeOverlayImage != null)
        {
            _defaultScopeSprite = scopeOverlayImage.sprite;
        }
        SetScopeOverlayVisible(false);

        if (tpsCamera != null)
        {
            BindTpsCamera(tpsCamera);
        }

        _fpsEyeAnchor = new GameObject("FpsAdsEyeAnchor").transform;
    }

    private void OnEnable()
    {
        CinemachineCore.CameraUpdatedEvent.AddListener(HandleCameraUpdated);
    }

    private void OnDisable()
    {
        CinemachineCore.CameraUpdatedEvent.RemoveListener(HandleCameraUpdated);
        _targetWeaponKickZ = 0f;
        _currentWeaponKickZ = 0f;
        ApplyFpsSensitivityMultiplier(1f);
        SetScopeOverlayVisible(false);
        SetHandsHidden(false);
        SetBodyHidden(false);
    }

    private void LateUpdate()
    {
        if (_localPlayer == null || _fpsEyeAnchor == null) return;

        _fpsEyeAnchor.SetPositionAndRotation(_localPlayer.GetCameraTargetPosition(), Quaternion.identity);

        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            if (_targetWeaponKickZ > 0f)
            {
                float returnLerp = 1f - Mathf.Exp(-adsWeaponKickReturnSpeed * dt);
                _targetWeaponKickZ = Mathf.Lerp(_targetWeaponKickZ, 0f, returnLerp);
                if (_targetWeaponKickZ < KickbackEpsilon)
                {
                    _targetWeaponKickZ = 0f;
                }
            }

            if (_currentWeaponKickZ > 0f || _targetWeaponKickZ > 0f)
            {
                float snapLerp = 1f - Mathf.Exp(-adsWeaponKickSnappiness * dt);
                _currentWeaponKickZ = Mathf.Lerp(_currentWeaponKickZ, _targetWeaponKickZ, snapLerp);
                if (_targetWeaponKickZ <= 0f && _currentWeaponKickZ < KickbackEpsilon)
                {
                    _currentWeaponKickZ = 0f;
                }
            }
        }

        if (_isAdsActive && fpsAdsCamera != null)
        {
            WeaponDataSO weaponData = _localPlayer.CurrentEquippedWeaponData;
            float targetFov = weaponData != null && weaponData.AdsFovOverride > 0f
                ? weaponData.AdsFovOverride
                : adsFov;
            LensSettings lens = fpsAdsCamera.Lens;
            if (!Mathf.Approximately(lens.FieldOfView, targetFov)
                || !Mathf.Approximately(lens.NearClipPlane, adsNearClipPlane))
            {
                lens.FieldOfView = targetFov;
                lens.NearClipPlane = adsNearClipPlane;
                fpsAdsCamera.Lens = lens;
            }
        }
    }

    private void OnDestroy()
    {
        CinemachineCore.CameraUpdatedEvent.RemoveListener(HandleCameraUpdated);
        ApplyFpsSensitivityMultiplier(1f);
        SetScopeOverlayVisible(false);
        SetHandsHidden(false);
        SetBodyHidden(false);
        _bodyRenderers.Clear();
        _handsRenderers.Clear();

        if (_fpsEyeAnchor != null)
        {
            Destroy(_fpsEyeAnchor.gameObject);
            _fpsEyeAnchor = null;
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>3인칭 기본 카메라 참조와 하위 시네머신 컴포넌트를 연결한다.</summary>
    public void BindTpsCamera(CinemachineCamera cam)
    {
        if (cam == null || cam == fpsAdsCamera) return;

        tpsCamera = cam;
        _tpsOrbitalFollow = tpsCamera.GetComponent<CinemachineOrbitalFollow>();
        _tpsInputAxisController = tpsCamera.GetComponent<CinemachineInputAxisController>();
        _tpsWeaponFeedback = tpsCamera.GetComponent<CinemachineWeaponFeedback>();
    }

    /// <summary>로컬 소유자 플레이어와 몸체 숨김 대상 렌더러를 바인딩한다.</summary>
    public void BindLocalPlayer(PlayerController localPlayer, CinemachineCamera activeTpsCamera)
    {
        SetBodyHidden(false);
        _bodyRenderers.Clear();

        _localPlayer = localPlayer;
        _localPlayerAnim = localPlayer != null ? localPlayer.GetComponent<PlayerAnim>() : null;

        if (activeTpsCamera != null)
        {
            BindTpsCamera(activeTpsCamera);
        }

        if (_localPlayer != null && _localPlayer.VisualRoot != null)
        {
            Transform handsRoot = _localPlayerAnim != null && _localPlayerAnim.HandScript != null
                ? _localPlayerAnim.HandScript.transform
                : null;

            Renderer[] renderers = _localPlayer.VisualRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;
                if (handsRoot != null && (r.transform == handsRoot || r.transform.IsChildOf(handsRoot))) continue;

                _bodyRenderers.Add(new BodyRendererRecord
                {
                    Renderer = r,
                    OriginalShadowMode = r.shadowCastingMode
                });
            }
        }

        ForceExitAds();
    }

    /// <summary>로컬 소유자 플레이어 바인딩을 해제하고 카메라 및 가시성을 원복한다.</summary>
    public void UnbindLocalPlayer(PlayerController localPlayer)
    {
        if (localPlayer != null && _localPlayer != localPlayer) return;

        ForceExitAds();
        _localPlayer = null;
        _localPlayerAnim = null;
        _bodyRenderers.Clear();
    }

    /// <summary>현재 시점 각도를 유지한 채 1인칭 정조준(ADS) 모드로 진입한다.</summary>
    public void EnterAds(WeaponDataSO weaponData)
    {
        if (_isAdsActive || _localPlayer == null || fpsAdsCamera == null) return;
        if (deathCamController != null && deathCamController.IsDeathCamActive) return;

        if (_tpsWeaponFeedback == null && tpsCamera != null)
        {
            _tpsWeaponFeedback = tpsCamera.GetComponent<CinemachineWeaponFeedback>();
        }
        _tpsWeaponFeedback?.ResetFeedback();
        _fpsWeaponFeedback?.ResetFeedback();
        _targetWeaponKickZ = 0f;
        _currentWeaponKickZ = 0f;

        Camera mainCamera = Camera.main;
        Vector3 lookForward = mainCamera != null
            ? mainCamera.transform.forward
            : (tpsCamera != null ? tpsCamera.transform.forward : _localPlayer.transform.forward);

        float horizontalMag = new Vector2(lookForward.x, lookForward.z).magnitude;
        float exactYaw = Mathf.DeltaAngle(0f, Mathf.Atan2(lookForward.x, lookForward.z) * Mathf.Rad2Deg);
        float exactPitch = horizontalMag > 0.0001f || Mathf.Abs(lookForward.y) > 0.0001f
            ? -Mathf.Atan2(lookForward.y, horizontalMag) * Mathf.Rad2Deg
            : 0f;

        if (_tpsOrbitalFollow != null)
        {
            CinemachineBrain brain = mainCamera != null ? mainCamera.GetComponent<CinemachineBrain>() : null;
            bool isBlendingFromAds = brain != null && brain.IsBlending && brain.ActiveBlend != null
                && brain.ActiveBlend.CamA == (ICinemachineCamera)fpsAdsCamera;
            if (!isBlendingFromAds || !_hasTpsComposerPitchDelta)
            {
                _tpsComposerPitchDelta = exactPitch - _tpsOrbitalFollow.VerticalAxis.Value;
                _hasTpsComposerPitchDelta = true;
            }
        }

        if (_fpsEyeAnchor != null)
        {
            _fpsEyeAnchor.SetPositionAndRotation(_localPlayer.GetCameraTargetPosition(), Quaternion.identity);
        }

        if (_fpsPanTilt != null)
        {
            _fpsPanTilt.PanAxis.Value = _fpsPanTilt.PanAxis.ClampValue(exactYaw);
            _fpsPanTilt.TiltAxis.Value = _fpsPanTilt.TiltAxis.ClampValue(exactPitch);
        }

        LensSettings lens = fpsAdsCamera.Lens;
        lens.FieldOfView = weaponData != null && weaponData.AdsFovOverride > 0f
            ? weaponData.AdsFovOverride
            : adsFov;
        lens.NearClipPlane = adsNearClipPlane;
        fpsAdsCamera.Lens = lens;

        fpsAdsCamera.Follow = _fpsEyeAnchor;
        fpsAdsCamera.LookAt = null;
        fpsAdsCamera.Priority = new PrioritySettings
        {
            Enabled = true,
            Value = activeAdsPriority
        };

        if (_tpsInputAxisController != null)
        {
            _tpsInputAxisController.enabled = false;
        }

        if (_fpsInputAxisController != null)
        {
            float sensitivityScale = weaponData != null && weaponData.UsesScopeOverlay
                ? weaponData.ScopeSensitivityMultiplier
                : 1f;
            ApplyFpsSensitivityMultiplier(sensitivityScale);
            _fpsInputAxisController.enabled = true;
        }

        if (scopeOverlayImage != null && weaponData != null && weaponData.UsesScopeOverlay)
        {
            scopeOverlayImage.sprite = weaponData.ScopeOverlaySprite != null
                ? weaponData.ScopeOverlaySprite
                : _defaultScopeSprite;
        }

        if (hideCrosshairInAds && crosshairObject != null)
        {
            crosshairObject.SetActive(false);
        }

        _isAdsActive = true;
    }

    /// <summary>현재 1인칭 시점 각도를 3인칭 카메라에 동기화하며 3인칭(TPS) 모드로 복귀한다.</summary>
    public void ExitAds()
    {
        if (!_isAdsActive) return;

        _fpsWeaponFeedback?.ResetFeedback();
        _targetWeaponKickZ = 0f;
        _currentWeaponKickZ = 0f;

        if (_tpsOrbitalFollow != null)
        {
            float yaw = CurrentAimYaw;
            float pitch = CurrentAimPitch;
            float targetVertical = _hasTpsComposerPitchDelta ? pitch - _tpsComposerPitchDelta : pitch;

            _tpsOrbitalFollow.HorizontalAxis.Value = _tpsOrbitalFollow.HorizontalAxis.ClampValue(yaw);
            _tpsOrbitalFollow.VerticalAxis.Value = _tpsOrbitalFollow.VerticalAxis.ClampValue(targetVertical);
        }

        _isAdsActive = false;
        SetScopeOverlayVisible(false);
        SetHandsHidden(false);
        ApplyFpsSensitivityMultiplier(1f);

        if (_fpsInputAxisController != null)
        {
            _fpsInputAxisController.enabled = false;
        }

        if (fpsAdsCamera != null)
        {
            fpsAdsCamera.Priority = new PrioritySettings
            {
                Enabled = true,
                Value = inactiveAdsPriority
            };
        }

        bool isDeathCamActive = deathCamController != null && deathCamController.IsDeathCamActive;
        if (!isDeathCamActive)
        {
            if (_tpsInputAxisController != null)
            {
                _tpsInputAxisController.enabled = true;
            }

            if (hideCrosshairInAds && crosshairObject != null)
            {
                crosshairObject.SetActive(true);
            }
        }
    }

    /// <summary>사망·리스폰·연결 종료 시 1인칭 정조준 상태와 로컬 몸체 가시성을 즉시 원복한다.</summary>
    public void ForceExitAds()
    {
        if (_isAdsActive)
        {
            ExitAds();
        }
        else
        {
            _fpsWeaponFeedback?.ResetFeedback();

            if (_fpsInputAxisController != null)
            {
                _fpsInputAxisController.enabled = false;
            }

            if (fpsAdsCamera != null)
            {
                fpsAdsCamera.Priority = new PrioritySettings
                {
                    Enabled = true,
                    Value = inactiveAdsPriority
                };
            }
        }

        _targetWeaponKickZ = 0f;
        _currentWeaponKickZ = 0f;
        ApplyFpsSensitivityMultiplier(1f);
        SetScopeOverlayVisible(false);
        SetHandsHidden(false);
        SetBodyHidden(false);
    }

    /// <summary>1인칭 정조준 배율을 적용해 <paramref name="weaponData"/> 발사 반동, 미세 펄스 셰이크 및 총기 Z축 후퇴 반동을 실행한다.</summary>
    public void ApplyWeaponFeedback(WeaponDataSO weaponData)
    {
        if (!_isAdsActive || weaponData == null) return;

        _fpsWeaponFeedback?.ApplyWeaponFeedback(weaponData, adsRecoilMultiplier, adsShakeMultiplier);

        float kickAmount = weaponData.AdsKickbackZOverride > 0f
            ? weaponData.AdsKickbackZOverride
            : adsWeaponKickZ;
        if (kickAmount > 0f && adsWeaponMaxKickZ > 0f)
        {
            _targetWeaponKickZ = Mathf.Min(_targetWeaponKickZ + kickAmount, adsWeaponMaxKickZ);
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary><see cref="CinemachineBrain"/> 갱신 직후 로컬 몸체 가시성과 총기 조준기 정중앙 정렬을 반영한다.</summary>
    private void HandleCameraUpdated(CinemachineBrain brain)
    {
        if (_localPlayer == null || !_localPlayer.IsOwner || fpsAdsCamera == null) return;

        if (_localPlayer.IsDead || (deathCamController != null && deathCamController.IsDeathCamActive))
        {
            SetScopeOverlayVisible(false);
            SetHandsHidden(false);
            SetBodyHidden(false);
            return;
        }

        bool isBlendingToAds = brain != null && brain.IsBlending && brain.ActiveBlend != null
            && brain.ActiveBlend.CamB == (ICinemachineCamera)fpsAdsCamera;
        bool isBlendingFromAds = brain != null && brain.IsBlending && brain.ActiveBlend != null
            && brain.ActiveBlend.CamA == (ICinemachineCamera)fpsAdsCamera;

        WeaponDataSO weaponData = _localPlayer.CurrentEquippedWeaponData;
        bool shouldHideBody;
        bool shouldShowScope = false;
        float alignWeight;

        if (_isAdsActive)
        {
            float blendWeight = isBlendingToAds ? brain.ActiveBlend.BlendWeight : 1f;
            shouldHideBody = blendWeight >= BodyHideBlendThreshold;
            shouldShowScope = weaponData != null && weaponData.UsesScopeOverlay && blendWeight >= scopeOverlayBlendThreshold;
            alignWeight = Mathf.SmoothStep(0f, 1f, blendWeight);
        }
        else if (isBlendingFromAds)
        {
            float blendWeight = brain.ActiveBlend.BlendWeight;
            shouldHideBody = blendWeight < BodyHideBlendThreshold;
            alignWeight = Mathf.SmoothStep(0f, 1f, 1f - blendWeight);
        }
        else
        {
            shouldHideBody = false;
            alignWeight = 0f;
        }

        SetBodyHidden(shouldHideBody);
        SetHandsHidden(shouldShowScope);
        SetScopeOverlayVisible(shouldShowScope);

        if (alignWeight <= 0.0001f) return;

        Transform aimPivot = _localPlayer.AimPitchTarget;
        Transform adsAnchor = _localPlayerAnim != null ? _localPlayerAnim.CurrentAdsCameraAnchor : null;
        if (aimPivot == null || adsAnchor == null) return;

        Vector3 localOffset = weaponData != null ? weaponData.AdsAnchorLocalOffset : Vector3.zero;
        Vector3 localRotOffset = weaponData != null ? weaponData.AdsAnchorLocalRotationOffset : Vector3.zero;

        Vector3 anchorWorldPos = adsAnchor.TransformPoint(localOffset);
        Quaternion anchorWorldRot = adsAnchor.rotation * Quaternion.Euler(localRotOffset);

        Vector3 originalPivotPos = aimPivot.position;
        Quaternion originalPivotRot = aimPivot.rotation;

        Quaternion targetRot = fpsAdsCamera.State.GetFinalOrientation();
        Vector3 targetPos = fpsAdsCamera.State.GetFinalPosition();

        Quaternion rotDelta = targetRot * Quaternion.Inverse(anchorWorldRot);
        Quaternion alignedPivotRot = rotDelta * originalPivotRot;
        Vector3 rotatedAnchorOffset = rotDelta * (anchorWorldPos - originalPivotPos);
        float totalPullbackZ = _currentWeaponKickZ + _localPlayer.CurrentWallRetractDistance;
        Vector3 pullbackOffset = totalPullbackZ > 0f
            ? (targetRot * Vector3.forward) * (totalPullbackZ * alignWeight)
            : Vector3.zero;
        Vector3 alignedPivotPos = targetPos - rotatedAnchorOffset - pullbackOffset;

        if (alignWeight >= 0.999f)
        {
            aimPivot.SetPositionAndRotation(alignedPivotPos, alignedPivotRot);
        }
        else
        {
            aimPivot.SetPositionAndRotation(
                Vector3.Lerp(originalPivotPos, alignedPivotPos, alignWeight),
                Quaternion.Slerp(originalPivotRot, alignedPivotRot, alignWeight));
        }
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>로컬 소유자 몸체 렌더러의 <see cref="ShadowCastingMode"/>를 숨김 또는 원본으로 전환한다.</summary>
    private void SetBodyHidden(bool hidden)
    {
        if (_isBodyHidden == hidden) return;
        _isBodyHidden = hidden;

        for (int i = 0; i < _bodyRenderers.Count; i++)
        {
            BodyRendererRecord record = _bodyRenderers[i];
            if (record.Renderer == null) continue;

            record.Renderer.shadowCastingMode = hidden
                ? ShadowCastingMode.ShadowsOnly
                : record.OriginalShadowMode;
        }
    }

    /// <summary>스코프 정조준 중 1인칭 손·장착 무기 렌더러의 <see cref="ShadowCastingMode"/>를 숨김 또는 원본으로 전환한다.</summary>
    private void SetHandsHidden(bool hidden)
    {
        if (_isHandsHidden == hidden) return;
        _isHandsHidden = hidden;

        if (hidden)
        {
            _handsRenderers.Clear();
            Transform handsRoot = _localPlayerAnim != null && _localPlayerAnim.HandScript != null
                ? _localPlayerAnim.HandScript.transform
                : null;
            if (handsRoot == null) return;

            Renderer[] renderers = handsRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null) continue;

                _handsRenderers.Add(new BodyRendererRecord
                {
                    Renderer = r,
                    OriginalShadowMode = r.shadowCastingMode
                });
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }
            return;
        }

        for (int i = 0; i < _handsRenderers.Count; i++)
        {
            BodyRendererRecord record = _handsRenderers[i];
            if (record.Renderer == null) continue;

            record.Renderer.shadowCastingMode = record.OriginalShadowMode;
        }
        _handsRenderers.Clear();
    }

    /// <summary>스코프 UI 오버레이 루트 오브젝트의 활성 상태를 전환한다.</summary>
    private void SetScopeOverlayVisible(bool visible)
    {
        _isScopeOverlayActive = visible;
        if (scopeOverlayObject != null && scopeOverlayObject.activeSelf != visible)
        {
            scopeOverlayObject.SetActive(visible);
        }
    }

    /// <summary>1인칭 카메라 입력 축 감도에 <paramref name="multiplier"/> 배율을 적용한다.</summary>
    private void ApplyFpsSensitivityMultiplier(float multiplier)
    {
        if (_fpsInputAxisController == null || _baseFpsAxisGains == null) return;

        var controllers = _fpsInputAxisController.Controllers;
        int count = Mathf.Min(controllers.Count, _baseFpsAxisGains.Length);
        float safeScale = Mathf.Clamp(multiplier, 0.05f, 1f);
        for (int i = 0; i < count; i++)
        {
            controllers[i].Input.Gain = _baseFpsAxisGains[i] * safeScale;
        }
    }
}
