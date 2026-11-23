using Unity.Cinemachine;
using UnityEngine;

/// <summary>무기 발사 시 <see cref="CinemachineOrbitalFollow"/> 축 임시 주입으로 반동·원점 복귀를 적용하고, 보정 채널에 미세 펄스 화면 흔들림을 더한다.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineCamera))]
public class CinemachineWeaponFeedback : CinemachineExtension
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float RecoilEpsilonSqr = 0.000001f; // 반동 오프셋을 0으로 스냅할 제곱 임계값
    private const float OverlappingShakeBoost = 0.25f; // 연사 중 기존 펄스와 겹칠 때 가산할 강도 비율

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>무기 데이터 값이 비정상일 때 사용할 기본 반동 반응 속도.</summary>
    [Header("반동 기본 및 제한 설정")]
    [Min(0.01f)]
    [SerializeField] private float defaultSnappiness = 20f;

    /// <summary>무기 데이터 값이 비정상일 때 사용할 기본 원점 복귀 속도.</summary>
    [Min(0.01f)]
    [SerializeField] private float defaultReturnSpeed = 10f;

    /// <summary>모든 무기 공통으로 제한할 상하 반동 최대 각도 (도 단위).</summary>
    [Min(0f)]
    [SerializeField] private float maxRecoilPitchLimit = 15f;

    /// <summary>모든 무기 공통으로 제한할 좌우 반동 최대 절대 각도 (도 단위).</summary>
    [Min(0f)]
    [SerializeField] private float maxRecoilYawLimit = 8f;

    /// <summary>무기 데이터에 지속 시간이 없을 때 사용할 기본 펄스 지속 시간 (초 단위).</summary>
    [Header("미세 펄스 셰이크 설정")]
    [Min(0.005f)]
    [SerializeField] private float defaultShakeDuration = 0.05f;

    /// <summary>모든 무기의 펄스 셰이크 강도에 곱하는 전역 배율.</summary>
    [Min(0f)]
    [SerializeField] private float shakeStrengthMultiplier = 1f;

    /// <summary>기준 강도(1.0)일 때 카메라 로컬 축 위치 미세 진폭 (미터).</summary>
    [SerializeField] private Vector3 shakePositionAmplitude = new Vector3(0.012f, 0.016f, 0.02f);

    /// <summary>기준 강도(1.0)일 때 카메라 로컬 축 회전 미세 진폭 (도 단위: Pitch, Yaw, Roll).</summary>
    [SerializeField] private Vector3 shakeRotationAmplitude = new Vector3(0.45f, 0.3f, 0.35f);

    /// <summary>연사 시에도 초과하지 않는 카메라 로컬 위치 셰이크 최대 진폭 (미터).</summary>
    [SerializeField] private Vector3 maxShakePositionAmplitude = new Vector3(0.02f, 0.025f, 0.03f);

    /// <summary>연사 시에도 초과하지 않는 카메라 로컬 회전 셰이크 최대 진폭 (도 단위: Pitch, Yaw, Roll).</summary>
    [SerializeField] private Vector3 maxShakeRotationAmplitude = new Vector3(0.8f, 0.55f, 0.65f);

    /// <summary>연사 중 중첩 펄스의 최대 강도 상한.</summary>
    [Min(0f)]
    [SerializeField] private float maxShakeIntensity = 1.5f;

    /// <summary>같은 카메라에 부착된 오빗 추적 컴포넌트 참조.</summary>
    private CinemachineOrbitalFollow _orbitalFollow;

    /// <summary>파이프라인 전처리에서 백업한 순수 마우스 기준 수평 축 각도 (도 단위).</summary>
    private float _savedHorizontalAxisValue;

    /// <summary>파이프라인 전처리에서 백업한 순수 마우스 기준 수직 축 각도 (도 단위).</summary>
    private float _savedVerticalAxisValue;

    /// <summary>직전 프레임에 기록된 순수 마우스 기준 수직 축 각도 (도 단위).</summary>
    private float _lastBaseVerticalAxisValue;

    /// <summary><see cref="_lastBaseVerticalAxisValue"/>가 초기화되었는지 여부.</summary>
    private bool _hasLastBaseVerticalAxis;

    /// <summary>현재 파이프라인 틱에서 <see cref="_orbitalFollow"/> 축에 반동이 임시 주입된 상태인지 여부.</summary>
    private bool _isAxisInjected;

    /// <summary>반동이 도달하려는 목표 오프셋 (x: 좌우 Yaw 도, y: 상하 Pitch 도).</summary>
    private Vector2 _targetRecoilOffset;

    /// <summary>현재 프레임에 카메라에 적용 중인 반동 오프셋 (x: 좌우 Yaw 도, y: 상하 Pitch 도).</summary>
    private Vector2 _currentRecoilOffset;

    /// <summary>최근 발사 무기의 반동 반응 속도.</summary>
    private float _activeSnappiness = 20f;

    /// <summary>최근 발사 무기의 원점 복귀 속도.</summary>
    private float _activeReturnSpeed = 10f;

    /// <summary>현재 진행 중인 미세 펄스 셰이크의 총 지속 시간 (초 단위).</summary>
    private float _shakeDuration = 0.05f;

    /// <summary>현재 진행 중인 미세 펄스 셰이크의 남은 시간 (초 단위).</summary>
    private float _shakeTimeRemaining;

    /// <summary>현재 진행 중인 미세 펄스 셰이크의 유효 강도.</summary>
    private float _activeShakeIntensity;

    /// <summary>현재 펄스의 카메라 로컬 위치 진동 방향 가중치.</summary>
    private Vector3 _shakePositionDir;

    /// <summary>현재 펄스의 카메라 로컬 회전 진동 방향 가중치.</summary>
    private Vector3 _shakeRotationDir;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 적용 중인 반동 오프셋 (x: Yaw 도, y: Pitch 도).</summary>
    public Vector2 CurrentRecoilOffset => _currentRecoilOffset;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    protected override void Awake()
    {
        base.Awake();
        _orbitalFollow = GetComponent<CinemachineOrbitalFollow>();
    }

    private void OnDisable()
    {
        RestoreInjectedAxes();
    }

    protected override void OnDestroy()
    {
        RestoreInjectedAxes();
        base.OnDestroy();
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary><paramref name="weaponData"/>의 수치로 1발 발사 반동과 미세 펄스 셰이크를 누적 적용한다.</summary>
    public void ApplyWeaponFeedback(WeaponDataSO weaponData)
    {
        if (weaponData == null) return;

        bool hasRecoil = weaponData.RecoilPitch > 0f
            || !Mathf.Approximately(weaponData.RecoilYawMin, 0f)
            || !Mathf.Approximately(weaponData.RecoilYawMax, 0f);

        if (hasRecoil)
        {
            _activeSnappiness = weaponData.RecoilSnappiness > 0f ? weaponData.RecoilSnappiness : defaultSnappiness;
            _activeReturnSpeed = weaponData.RecoilReturnSpeed > 0f ? weaponData.RecoilReturnSpeed : defaultReturnSpeed;

            float yawMin = Mathf.Min(weaponData.RecoilYawMin, weaponData.RecoilYawMax);
            float yawMax = Mathf.Max(weaponData.RecoilYawMin, weaponData.RecoilYawMax);
            float yawKick = Random.Range(yawMin, yawMax);

            float effectiveMaxPitch = weaponData.MaxRecoilPitch > 0f
                ? Mathf.Min(weaponData.MaxRecoilPitch, maxRecoilPitchLimit)
                : maxRecoilPitchLimit;
            float effectiveMaxYaw = weaponData.MaxRecoilYaw > 0f
                ? Mathf.Min(weaponData.MaxRecoilYaw, maxRecoilYawLimit)
                : maxRecoilYawLimit;

            if (_orbitalFollow == null)
            {
                _orbitalFollow = GetComponent<CinemachineOrbitalFollow>();
            }

            if (_orbitalFollow != null)
            {
                float baseVertical = _isAxisInjected ? _savedVerticalAxisValue : _orbitalFollow.VerticalAxis.Value;
                float minAllowedVertical = Mathf.Max(_orbitalFollow.VerticalAxis.Range.x, -effectiveMaxPitch);
                float maxAllowedRecoilY = Mathf.Max(0f, baseVertical - minAllowedVertical);

                if (_targetRecoilOffset.y < maxAllowedRecoilY)
                {
                    _targetRecoilOffset.y = Mathf.Min(_targetRecoilOffset.y + weaponData.RecoilPitch, maxAllowedRecoilY);
                }
            }
            else
            {
                _targetRecoilOffset.y = Mathf.Clamp(_targetRecoilOffset.y + weaponData.RecoilPitch, -effectiveMaxPitch, effectiveMaxPitch);
            }

            _targetRecoilOffset.x = Mathf.Clamp(_targetRecoilOffset.x + yawKick, -effectiveMaxYaw, effectiveMaxYaw);
        }

        float incomingShake = weaponData.ShakeIntensity * shakeStrengthMultiplier;
        if (incomingShake > 0f && maxShakeIntensity > 0f)
        {
            float duration = weaponData.ShakeDuration > 0f ? weaponData.ShakeDuration : defaultShakeDuration;
            float remainingRatio = _shakeDuration > 0f ? Mathf.Clamp01(_shakeTimeRemaining / _shakeDuration) : 0f;
            float residualIntensity = _activeShakeIntensity * remainingRatio;

            _activeShakeIntensity = Mathf.Min(
                Mathf.Max(incomingShake, residualIntensity + incomingShake * OverlappingShakeBoost),
                maxShakeIntensity);
            _shakeDuration = Mathf.Max(0.005f, duration);
            _shakeTimeRemaining = _shakeDuration;

            _shakePositionDir = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-1f, 0.5f),
                -1f);
            _shakeRotationDir = new Vector3(
                Random.Range(-1f, -0.4f),
                Random.Range(-1f, 1f),
                Random.Range(-1f, 1f));
        }
    }

    /// <summary>진행 중인 반동 오프셋과 미세 펄스 셰이크 상태를 즉시 초기화한다.</summary>
    public void ResetFeedback()
    {
        RestoreInjectedAxes();
        _hasLastBaseVerticalAxis = false;
        _targetRecoilOffset = Vector2.zero;
        _currentRecoilOffset = Vector2.zero;
        _shakeTimeRemaining = 0f;
        _activeShakeIntensity = 0f;
        _shakePositionDir = Vector3.zero;
        _shakeRotationDir = Vector3.zero;
    }

    // ***************************************************************
    // CinemachineExtension
    // ***************************************************************

    public override void PrePipelineMutateCameraStateCallback(
        CinemachineVirtualCameraBase vcam,
        ref CameraState curState,
        float deltaTime)
    {
        if (!Application.isPlaying) return;

        if (deltaTime < 0f)
        {
            ResetFeedback();
            return;
        }

        if (_orbitalFollow == null && vcam != null)
        {
            _orbitalFollow = vcam.GetComponent<CinemachineOrbitalFollow>();
        }

        if (_orbitalFollow != null)
        {
            float currentBaseVertical = _orbitalFollow.VerticalAxis.Value;
            if (_hasLastBaseVerticalAxis)
            {
                float userDownDelta = currentBaseVertical - _lastBaseVerticalAxisValue;
                if (userDownDelta > 0f && _currentRecoilOffset.y > 0f)
                {
                    float absorbed = Mathf.Min(userDownDelta, _currentRecoilOffset.y);
                    _currentRecoilOffset.y = Mathf.Max(0f, _currentRecoilOffset.y - absorbed);
                    _targetRecoilOffset.y = Mathf.Max(0f, _targetRecoilOffset.y - absorbed);
                    currentBaseVertical = _orbitalFollow.VerticalAxis.ClampValue(currentBaseVertical - absorbed);
                    _orbitalFollow.VerticalAxis.Value = currentBaseVertical;
                }
            }
            _lastBaseVerticalAxisValue = currentBaseVertical;
            _hasLastBaseVerticalAxis = true;
        }

        if (deltaTime > 0f)
        {
            if (_targetRecoilOffset.sqrMagnitude > 0f)
            {
                float returnLerp = 1f - Mathf.Exp(-_activeReturnSpeed * deltaTime);
                _targetRecoilOffset = Vector2.Lerp(_targetRecoilOffset, Vector2.zero, returnLerp);
                if (_targetRecoilOffset.sqrMagnitude < RecoilEpsilonSqr)
                {
                    _targetRecoilOffset = Vector2.zero;
                }
            }

            if (_currentRecoilOffset.sqrMagnitude > 0f || _targetRecoilOffset.sqrMagnitude > 0f)
            {
                float snapLerp = 1f - Mathf.Exp(-_activeSnappiness * deltaTime);
                _currentRecoilOffset = Vector2.Lerp(_currentRecoilOffset, _targetRecoilOffset, snapLerp);
                if (_targetRecoilOffset == Vector2.zero && _currentRecoilOffset.sqrMagnitude < RecoilEpsilonSqr)
                {
                    _currentRecoilOffset = Vector2.zero;
                }
            }
        }

        if (_currentRecoilOffset.sqrMagnitude <= 0f || _orbitalFollow == null) return;

        _savedHorizontalAxisValue = _orbitalFollow.HorizontalAxis.Value;
        _savedVerticalAxisValue = _orbitalFollow.VerticalAxis.Value;
        _orbitalFollow.HorizontalAxis.Value = _orbitalFollow.HorizontalAxis.ClampValue(_savedHorizontalAxisValue + _currentRecoilOffset.x);
        _orbitalFollow.VerticalAxis.Value = _orbitalFollow.VerticalAxis.ClampValue(_savedVerticalAxisValue - _currentRecoilOffset.y);
        _isAxisInjected = true;
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage,
        ref CameraState state,
        float deltaTime)
    {
        if (!Application.isPlaying) return;

        if (stage == CinemachineCore.Stage.Finalize)
        {
            RestoreInjectedAxes();
            return;
        }

        if (stage != CinemachineCore.Stage.Noise || deltaTime < 0f) return;

        bool hasFallbackRecoil = !_isAxisInjected && _orbitalFollow == null && _currentRecoilOffset.sqrMagnitude > 0f;
        bool hasShake = false;
        Vector3 shakeLocalPos = Vector3.zero;
        Vector3 shakeEuler = Vector3.zero;

        if (_shakeTimeRemaining > 0f && _shakeDuration > 0f && _activeShakeIntensity > 0f)
        {
            if (deltaTime > 0f)
            {
                _shakeTimeRemaining = Mathf.Max(0f, _shakeTimeRemaining - deltaTime);
            }

            float normalizedTime = 1f - (_shakeTimeRemaining / _shakeDuration);
            float decay = 1f - normalizedTime;
            float pulse = decay * decay * Mathf.Cos(normalizedTime * Mathf.PI * 1.5f);
            float scaledPulse = _activeShakeIntensity * pulse;

            Vector3 rawPos = Vector3.Scale(_shakePositionDir, shakePositionAmplitude) * scaledPulse;
            shakeLocalPos = new Vector3(
                Mathf.Clamp(rawPos.x, -maxShakePositionAmplitude.x, maxShakePositionAmplitude.x),
                Mathf.Clamp(rawPos.y, -maxShakePositionAmplitude.y, maxShakePositionAmplitude.y),
                Mathf.Clamp(rawPos.z, -maxShakePositionAmplitude.z, maxShakePositionAmplitude.z));

            Vector3 rawRot = Vector3.Scale(_shakeRotationDir, shakeRotationAmplitude) * scaledPulse;
            shakeEuler = new Vector3(
                Mathf.Clamp(rawRot.x, -maxShakeRotationAmplitude.x, maxShakeRotationAmplitude.x),
                Mathf.Clamp(rawRot.y, -maxShakeRotationAmplitude.y, maxShakeRotationAmplitude.y),
                Mathf.Clamp(rawRot.z, -maxShakeRotationAmplitude.z, maxShakeRotationAmplitude.z));

            hasShake = true;

            if (_shakeTimeRemaining <= 0f)
            {
                _activeShakeIntensity = 0f;
            }
        }

        if (!hasFallbackRecoil && !hasShake) return;

        Quaternion recoilRotation = hasFallbackRecoil
            ? Quaternion.Euler(-_currentRecoilOffset.y, _currentRecoilOffset.x, 0f)
            : Quaternion.identity;
        Quaternion shakeRotation = hasShake
            ? Quaternion.Euler(shakeEuler)
            : Quaternion.identity;

        state.OrientationCorrection = state.OrientationCorrection * recoilRotation * shakeRotation;

        if (hasShake)
        {
            state.PositionCorrection += state.RawOrientation * shakeLocalPos;
        }
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>파이프라인 전처리에서 임시 주입한 <see cref="_orbitalFollow"/> 축 값을 순수 기준값으로 복원한다.</summary>
    private void RestoreInjectedAxes()
    {
        if (!_isAxisInjected) return;

        if (_orbitalFollow != null)
        {
            _orbitalFollow.HorizontalAxis.Value = _savedHorizontalAxisValue;
            _orbitalFollow.VerticalAxis.Value = _savedVerticalAxisValue;
        }
        _isAxisInjected = false;
    }
}
