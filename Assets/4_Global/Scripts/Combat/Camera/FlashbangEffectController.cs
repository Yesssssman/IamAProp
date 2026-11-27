using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>로컬 소유자 화면의 섬광탄 화이트 플래시, <see cref="RenderTexture"/> 잔상 및 청각 차단/이명 효과를 제어한다.</summary>
public class FlashbangEffectController : MonoBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float MinValidCutoffFrequency = 50f; // 로우패스 컷오프 주파수 하한 (Hz)
    private const float AlphaEpsilon = 0.001f;

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>폭발 직전 카메라 화면 잔상을 표시하는 전체 화면 <see cref="RawImage"/> (하위 레이어).</summary>
    [Header("UI 오버레이 참조")]
    [SerializeField] private RawImage afterimageRawImage;

    /// <summary>화이트 플래시 실명을 표시하는 전체 화면 흰색 <see cref="Image"/> (상위 레이어).</summary>
    [SerializeField] private Image whiteFlashImage;

    /// <summary>최소 강도 피격 시 완전 실명(Alpha 1.0) 유지 시간 (초 단위).</summary>
    [Header("시각 연출 설정")]
    [Min(0f)]
    [SerializeField] private float minHoldDuration = 0.35f;

    /// <summary>최대 강도 피격 시 완전 실명(Alpha 1.0) 유지 시간 (초 단위).</summary>
    [Min(0f)]
    [SerializeField] private float maxHoldDuration = 1.4f;

    /// <summary>캡처된 잔상 <see cref="RawImage"/>의 초기 최대 투명도 (0~1).</summary>
    [Range(0f, 1f)]
    [SerializeField] private float afterimageMaxAlpha = 0.92f;

    /// <summary>흰 화면이 걷히기 시작한 뒤 잔상이 추가로 지연 페이드아웃되는 비율 (0~0.5).</summary>
    [Range(0f, 0.5f)]
    [SerializeField] private float afterimageFadeLagRatio = 0.2f;

    /// <summary>플랫폼 UV 기준(<see cref="SystemInfo.graphicsUVStartsAtTop"/>)에 따라 캡처 잔상의 상하 반전을 자동 보정할지 여부.</summary>
    [SerializeField] private bool autoFixCaptureVerticalFlip = true;

    /// <summary>평상시 <see cref="AudioLowPassFilter"/> 복원 컷오프 주파수 (Hz).</summary>
    [Header("청각 차단 및 이명 설정")]
    [Range(5000f, 22000f)]
    [SerializeField] private float normalCutoffFrequency = 22000f;

    /// <summary>정면 피격 시 주변 월드 사운드를 먹먹하게 차단할 컷오프 주파수 (Hz).</summary>
    [Range(100f, 2000f)]
    [SerializeField] private float mutedCutoffFrequency = 350f;

    /// <summary>정면 피격 시 <see cref="AudioLowPassFilter"/> 공명 Q 값.</summary>
    [Range(1f, 4f)]
    [SerializeField] private float mutedResonanceQ = 1.15f;

    /// <summary>정면 피격 시 이명 사운드 최대 볼륨.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float tinnitusMaxVolume = 0.75f;

    /// <summary>시각 복구 이후 청각 필터가 완전히 복구되기까지의 추가 여유 시간 (초 단위).</summary>
    [Min(0f)]
    [SerializeField] private float audioRecoveryExtraDuration = 0.4f;

    /// <summary>호출 측 클립이 비어 있을 때 사용할 기본 이명 <see cref="AudioClip"/> (선택).</summary>
    [SerializeField] private AudioClip defaultTinnitusSfx;

    /// <summary>폭발 직전 화면을 보관하고 재사용하는 <see cref="RenderTexture"/>.</summary>
    private RenderTexture _afterimageRT;

    /// <summary><see cref="AudioLowPassFilter"/>를 우회해 이명만 선명하게 재생하는 2D <see cref="AudioSource"/>.</summary>
    private AudioSource _tinnitusAudioSource;

    /// <summary>메인 카메라(<see cref="AudioListener"/>)에 부착된 주변 청각 차단용 <see cref="AudioLowPassFilter"/>.</summary>
    private AudioLowPassFilter _cameraLowPassFilter;

    /// <summary>섬광 진입 전 <see cref="_cameraLowPassFilter"/>의 원래 활성 상태.</summary>
    private bool _savedLowPassEnabled;

    /// <summary>섬광 진입 전 <see cref="_cameraLowPassFilter"/>의 원래 컷오프 주파수 (Hz).</summary>
    private float _savedLowPassCutoff;

    /// <summary>섬광 진입 전 <see cref="_cameraLowPassFilter"/>의 원래 공명 Q 값.</summary>
    private float _savedLowPassResonance;

    /// <summary><see cref="_cameraLowPassFilter"/>의 원본 상태가 백업되어 있는지 여부.</summary>
    private bool _hasSavedLowPassState;

    /// <summary>현재 실행 중인 섬광 연출 코루틴.</summary>
    private Coroutine _flashCoroutine;

    /// <summary>현재 섬광 효과가 활성 상태인지 여부.</summary>
    private bool _isFlashed;

    /// <summary>현재 바인딩된 로컬 소유자 플레이어.</summary>
    private PlayerController _boundPlayer;

    /// <summary>바인딩된 플레이어의 사망/리스폰 상태를 감시하는 체력 컴포넌트.</summary>
    private PlayerHealth _playerHealth;

    /// <summary>사망 및 관전 모드 여부를 조회하는 씬 데스캠 컨트롤러.</summary>
    private DeathCamController _deathCamController;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 씬에 활성화된 로컬 <see cref="FlashbangEffectController"/> 인스턴스.</summary>
    public static FlashbangEffectController Instance { get; private set; }

    /// <summary>현재 섬광 효과가 진행 중인지 여부.</summary>
    public bool IsFlashed => _isFlashed;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        if (afterimageRawImage != null)
        {
            afterimageRawImage.raycastTarget = false;
            SetGraphicAlpha(afterimageRawImage, 0f);
            afterimageRawImage.gameObject.SetActive(false);
        }

        if (whiteFlashImage != null)
        {
            whiteFlashImage.raycastTarget = false;
            SetGraphicAlpha(whiteFlashImage, 0f);
            whiteFlashImage.gameObject.SetActive(false);
        }

        _tinnitusAudioSource = GetComponent<AudioSource>();
        if (_tinnitusAudioSource == null)
        {
            _tinnitusAudioSource = gameObject.AddComponent<AudioSource>();
        }
        _tinnitusAudioSource.playOnAwake = false;
        _tinnitusAudioSource.loop = true;
        _tinnitusAudioSource.spatialBlend = 0f;
        _tinnitusAudioSource.bypassListenerEffects = true;
        _tinnitusAudioSource.bypassEffects = true;
        _tinnitusAudioSource.bypassReverbZones = true;
    }

    private void OnEnable()
    {
        Instance = this;
        PlayerController.OnLocalPlayerStarted += HandleLocalPlayerStarted;
        PlayerController.OnLocalPlayerStopped += HandleLocalPlayerStopped;

        PlayerController localPlayer = PlayerController.LocalPlayer;
        if (localPlayer != null && localPlayer.IsSpawned && localPlayer.IsOwner)
        {
            BindPlayer(localPlayer);
        }
    }

    private void OnDisable()
    {
        PlayerController.OnLocalPlayerStarted -= HandleLocalPlayerStarted;
        PlayerController.OnLocalPlayerStopped -= HandleLocalPlayerStopped;
        UnbindPlayer();
        ResetEffects();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDestroy()
    {
        ResetEffects();

        if (_afterimageRT != null)
        {
            _afterimageRT.Release();
            Destroy(_afterimageRT);
            _afterimageRT = null;
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>메인 카메라 시선 각도를 판별해 정면 피격이면 섬광 시각/청각 연출을 실행하고 <c>true</c>를 반환한다.</summary>
    public bool TryApplyFlashHit(
        Vector3 explosionPos,
        float flashRadius,
        float flashLookAngle,
        float minDuration,
        float maxDuration,
        AudioClip tinnitusClip,
        float tinnitusVolumeScale = 1f)
    {
        if (_boundPlayer == null || !_boundPlayer.IsSpawned || !_boundPlayer.IsOwner)
        {
            PlayerController localPlayer = PlayerController.LocalPlayer;
            if (localPlayer != null && localPlayer.IsSpawned && localPlayer.IsOwner)
            {
                BindPlayer(localPlayer);
            }
            else
            {
                return false;
            }
        }

        if (_boundPlayer.IsDead || (_playerHealth != null && _playerHealth.IsDead)) return false;

        if (_deathCamController == null)
        {
            _deathCamController = FindAnyObjectByType<DeathCamController>(FindObjectsInactive.Include);
        }
        if (_deathCamController != null && _deathCamController.IsDeathCamActive) return false;

        Camera mainCamera = Camera.main;
        if (mainCamera == null) return false;

        Vector3 toFlash = explosionPos - mainCamera.transform.position;
        if (toFlash.sqrMagnitude <= 0.0001f) return false;

        float safeLookAngle = Mathf.Clamp(flashLookAngle, 1f, 180f);
        float lookAngle = Vector3.Angle(mainCamera.transform.forward, toFlash);
        if (lookAngle > safeLookAngle)
        {
            return false;
        }

        float safeRadius = Mathf.Max(0.1f, flashRadius);
        float playerDist = Vector3.Distance(_boundPlayer.GetCameraTargetPosition(), explosionPos);
        float distFactor = 1f - Mathf.Clamp01(playerDist / safeRadius);
        float angleFactor = 1f - Mathf.Clamp01(lookAngle / safeLookAngle);
        float intensity = Mathf.Clamp01((0.4f + 0.6f * distFactor) * (0.55f + 0.45f * angleFactor));

        float clampedMin = Mathf.Max(0.2f, minDuration);
        float clampedMax = Mathf.Max(clampedMin, maxDuration);
        float totalDuration = Mathf.Lerp(clampedMin, clampedMax, intensity);

        if (_flashCoroutine != null)
        {
            StopCoroutine(_flashCoroutine);
            _flashCoroutine = null;
        }

        AudioClip resolvedClip = tinnitusClip != null ? tinnitusClip : defaultTinnitusSfx;
        float peakVolume = Mathf.Clamp01(tinnitusMaxVolume * tinnitusVolumeScale * Mathf.Lerp(0.6f, 1f, intensity));
        _flashCoroutine = StartCoroutine(FlashSequenceRoutine(intensity, totalDuration, resolvedClip, peakVolume));
        return true;
    }

    /// <summary>진행 중인 섬광 코루틴, 화이트 플래시/잔상 UI, 이명 사운드 및 청각 차단 필터를 즉시 초기화한다.</summary>
    public void ResetEffects()
    {
        if (_flashCoroutine != null)
        {
            StopCoroutine(_flashCoroutine);
            _flashCoroutine = null;
        }

        _isFlashed = false;

        if (whiteFlashImage != null)
        {
            SetGraphicAlpha(whiteFlashImage, 0f);
            whiteFlashImage.gameObject.SetActive(false);
        }

        if (afterimageRawImage != null)
        {
            SetGraphicAlpha(afterimageRawImage, 0f);
            afterimageRawImage.gameObject.SetActive(false);
        }

        if (_tinnitusAudioSource != null && _tinnitusAudioSource.isPlaying)
        {
            _tinnitusAudioSource.Stop();
            _tinnitusAudioSource.volume = 0f;
        }

        if (_cameraLowPassFilter != null && _hasSavedLowPassState)
        {
            _cameraLowPassFilter.cutoffFrequency = _savedLowPassCutoff;
            _cameraLowPassFilter.lowpassResonanceQ = _savedLowPassResonance;
            _cameraLowPassFilter.enabled = _savedLowPassEnabled;
        }
        _hasSavedLowPassState = false;
    }

    // ***************************************************************
    // Coroutine
    // ***************************************************************

    /// <summary>화면 캡처, 화이트 플래시 유지/페이드아웃, 잔상 페이드아웃 및 청각 차단 복구를 순차 실행한다.</summary>
    private IEnumerator FlashSequenceRoutine(
        float intensity,
        float totalDuration,
        AudioClip tinnitusClip,
        float tinnitusPeakVolume)
    {
        // 이미 강한 백색 실명 중(연속 피격)이면 기존 잔상을 유지해 1프레임 깜빡임을 방지한다.
        bool shouldCaptureFreshFrame = !_isFlashed
            || _afterimageRT == null
            || (whiteFlashImage != null && whiteFlashImage.color.a <= 0.35f);

        if (shouldCaptureFreshFrame && afterimageRawImage != null)
        {
            if (whiteFlashImage != null)
            {
                SetGraphicAlpha(whiteFlashImage, 0f);
                whiteFlashImage.gameObject.SetActive(false);
            }
            SetGraphicAlpha(afterimageRawImage, 0f);
            afterimageRawImage.gameObject.SetActive(false);

            yield return new WaitForEndOfFrame();

            // 프레임 대기 중 사망·관전 전환이 발생했으면 즉시 중단한다.
            if (_boundPlayer == null
                || _boundPlayer.IsDead
                || (_playerHealth != null && _playerHealth.IsDead)
                || (_deathCamController != null && _deathCamController.IsDeathCamActive))
            {
                ResetEffects();
                yield break;
            }

            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            if (_afterimageRT == null || _afterimageRT.width != width || _afterimageRT.height != height)
            {
                if (_afterimageRT != null)
                {
                    _afterimageRT.Release();
                    Destroy(_afterimageRT);
                }

                _afterimageRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "FlashbangAfterimageRT",
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear
                };
                _afterimageRT.Create();
            }

            ScreenCapture.CaptureScreenshotIntoRenderTexture(_afterimageRT);
            afterimageRawImage.texture = _afterimageRT;
            afterimageRawImage.uvRect = autoFixCaptureVerticalFlip && SystemInfo.graphicsUVStartsAtTop
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
        }

        _isFlashed = true;

        // 시각: 잔상(아래)과 완전 백색 플래시(위)를 즉시 점등한다.
        if (afterimageRawImage != null && _afterimageRT != null)
        {
            afterimageRawImage.gameObject.SetActive(true);
            SetGraphicAlpha(afterimageRawImage, afterimageMaxAlpha);
        }

        if (whiteFlashImage != null)
        {
            whiteFlashImage.gameObject.SetActive(true);
            SetGraphicAlpha(whiteFlashImage, 1f);
        }

        // 청각: 메인 카메라 로우패스 필터로 주변 사운드를 먹먹하게 차단하고 이명을 재생한다.
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            AudioLowPassFilter filter = mainCamera.GetComponent<AudioLowPassFilter>();
            if (filter == null)
            {
                filter = mainCamera.gameObject.AddComponent<AudioLowPassFilter>();
                filter.cutoffFrequency = normalCutoffFrequency;
                filter.enabled = false;
            }

            if (_cameraLowPassFilter != filter)
            {
                if (_cameraLowPassFilter != null && _hasSavedLowPassState)
                {
                    _cameraLowPassFilter.cutoffFrequency = _savedLowPassCutoff;
                    _cameraLowPassFilter.lowpassResonanceQ = _savedLowPassResonance;
                    _cameraLowPassFilter.enabled = _savedLowPassEnabled;
                    _hasSavedLowPassState = false;
                }
                _cameraLowPassFilter = filter;
            }

            if (!_hasSavedLowPassState)
            {
                _savedLowPassEnabled = _cameraLowPassFilter.enabled;
                _savedLowPassCutoff = _cameraLowPassFilter.cutoffFrequency;
                _savedLowPassResonance = _cameraLowPassFilter.lowpassResonanceQ;
                _hasSavedLowPassState = true;
            }

            _cameraLowPassFilter.enabled = true;
            _cameraLowPassFilter.cutoffFrequency = Mathf.Max(MinValidCutoffFrequency, mutedCutoffFrequency);
            _cameraLowPassFilter.lowpassResonanceQ = mutedResonanceQ;
        }

        if (_tinnitusAudioSource != null && tinnitusClip != null && tinnitusPeakVolume > 0f)
        {
            _tinnitusAudioSource.Stop();
            _tinnitusAudioSource.clip = tinnitusClip;
            _tinnitusAudioSource.volume = tinnitusPeakVolume;
            _tinnitusAudioSource.Play();
        }

        float holdDuration = Mathf.Clamp(
            Mathf.Lerp(minHoldDuration, maxHoldDuration, intensity),
            0f,
            totalDuration * 0.6f);
        float visualFadeDuration = Mathf.Max(0.2f, totalDuration - holdDuration);
        float afterimageDelay = visualFadeDuration * afterimageFadeLagRatio;
        float afterimageFadeDuration = Mathf.Max(0.2f, visualFadeDuration);
        float audioRecoveryDuration = visualFadeDuration + audioRecoveryExtraDuration;
        float totalRecoveryDuration = Mathf.Max(afterimageDelay + afterimageFadeDuration, audioRecoveryDuration);

        // 1단계: 완전 실명 유지 구간
        float holdElapsed = 0f;
        while (holdElapsed < holdDuration)
        {
            holdElapsed += Time.deltaTime;
            yield return null;
        }

        // 2단계: 화이트 플래시 → 잔상 → 청각 차단/이명 순차 페이드아웃
        float startCutoff = Mathf.Max(MinValidCutoffFrequency, mutedCutoffFrequency);
        float targetCutoff = _hasSavedLowPassState && _savedLowPassEnabled
            ? Mathf.Max(startCutoff, _savedLowPassCutoff)
            : normalCutoffFrequency;
        float cutoffRatio = targetCutoff / startCutoff;

        float elapsed = 0f;
        while (elapsed < totalRecoveryDuration)
        {
            elapsed += Time.deltaTime;

            if (whiteFlashImage != null)
            {
                float whiteT = Mathf.Clamp01(elapsed / visualFadeDuration);
                float whiteAlpha = Mathf.Pow(1f - whiteT, 1.4f);
                SetGraphicAlpha(whiteFlashImage, whiteAlpha);
                if (whiteAlpha <= AlphaEpsilon && whiteFlashImage.gameObject.activeSelf)
                {
                    whiteFlashImage.gameObject.SetActive(false);
                }
            }

            if (afterimageRawImage != null && afterimageRawImage.gameObject.activeSelf)
            {
                float afterT = Mathf.Clamp01(Mathf.Max(0f, elapsed - afterimageDelay) / afterimageFadeDuration);
                float afterAlpha = afterimageMaxAlpha * (1f - afterT);
                SetGraphicAlpha(afterimageRawImage, afterAlpha);
                if (afterAlpha <= AlphaEpsilon)
                {
                    afterimageRawImage.gameObject.SetActive(false);
                }
            }

            float audioT = Mathf.Clamp01(elapsed / audioRecoveryDuration);
            if (_cameraLowPassFilter != null)
            {
                // 사람 청각의 로그 주파수 특성에 맞춰 컷오프 주파수를 지수 곡선으로 복원한다.
                _cameraLowPassFilter.cutoffFrequency = startCutoff * Mathf.Pow(cutoffRatio, audioT);
                _cameraLowPassFilter.lowpassResonanceQ = Mathf.Lerp(mutedResonanceQ, _savedLowPassResonance, audioT);
            }

            if (_tinnitusAudioSource != null && _tinnitusAudioSource.isPlaying)
            {
                _tinnitusAudioSource.volume = tinnitusPeakVolume * (1f - audioT);
            }

            yield return null;
        }

        ResetEffects();
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>로컬 소유자 플레이어가 시작되면 사망/리스폰 이벤트를 바인딩한다.</summary>
    private void HandleLocalPlayerStarted(PlayerController player)
    {
        BindPlayer(player);
    }

    /// <summary>바인딩된 로컬 소유자 플레이어가 정지되면 효과를 초기화하고 구독을 해제한다.</summary>
    private void HandleLocalPlayerStopped(PlayerController player)
    {
        if (_boundPlayer == player)
        {
            UnbindPlayer();
            ResetEffects();
        }
    }

    /// <summary>로컬 플레이어 사망 또는 리스폰 시 진행 중인 섬광 효과를 즉시 초기화한다.</summary>
    private void HandleDeadStateChanged(bool isDead)
    {
        ResetEffects();
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>로컬 소유자 <paramref name="player"/>에 체력·데스캠 참조를 바인딩한다.</summary>
    private void BindPlayer(PlayerController player)
    {
        if (player == null || !player.IsOwner) return;

        UnbindPlayer();
        _boundPlayer = player;
        _playerHealth = player.GetComponent<PlayerHealth>();
        _deathCamController = FindAnyObjectByType<DeathCamController>(FindObjectsInactive.Include);

        if (_playerHealth != null)
        {
            _playerHealth.OnDeadStateChanged += HandleDeadStateChanged;
        }
    }

    /// <summary>바인딩된 플레이어의 이벤트 구독을 해제한다.</summary>
    private void UnbindPlayer()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnDeadStateChanged -= HandleDeadStateChanged;
        }

        _boundPlayer = null;
        _playerHealth = null;
    }

    /// <summary>지정한 UI <paramref name="graphic"/>의 투명도만 <paramref name="alpha"/>로 갱신한다.</summary>
    private static void SetGraphicAlpha(Graphic graphic, float alpha)
    {
        Color color = graphic.color;
        color.a = Mathf.Clamp01(alpha);
        graphic.color = color;
    }
}
