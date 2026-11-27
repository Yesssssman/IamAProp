using UnityEngine;

/// <summary>애니메이션 이벤트 및 착지 시 이동·점프·착지 사운드를 재생하는 컴포넌트.</summary>
[RequireComponent(typeof(AudioSource))]
public class StepSoundTest : MonoBehaviour
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>발소리 이벤트 시 재생할 오디오 클립.</summary>
    [SerializeField] private AudioClip stepClip;

    /// <summary>점프 애니메이션 이벤트 시 재생할 오디오 클립.</summary>
    [SerializeField] private AudioClip jumpClip;

    /// <summary>바닥 착지 시 재생할 프롭별 오디오 클립.</summary>
    [SerializeField] private AudioClip landingClip;

    /// <summary>발소리 재생 볼륨 배율.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float stepVolume = 1f;

    /// <summary>점프 사운드 재생 볼륨 배율.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float jumpVolume = 1f;

    /// <summary>착지 사운드 재생 볼륨 배율.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float landingVolume = 1f;

    /// <summary>발소리 중복 재생을 막기 위한 최소 간격 (초 단위).</summary>
    [Min(0f)]
    [SerializeField] private float minStepInterval = 0.12f;

    /// <summary>발소리를 허용할 애니메이션 클립의 최소 블렌드 가중치.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float minStepClipWeight = 0.2f;

    /// <summary>점프 사운드 중복 재생을 막기 위한 최소 간격 (초 단위).</summary>
    [Min(0f)]
    [SerializeField] private float minJumpInterval = 0.15f;

    /// <summary>착지 사운드 중복 재생을 막기 위한 최소 간격 (초 단위).</summary>
    [Min(0f)]
    [SerializeField] private float minLandingInterval = 0.2f;

    /// <summary>이동 사운드 재생에 사용할 <see cref="AudioSource"/> 컴포넌트.</summary>
    private AudioSource _audioSource;

    /// <summary>생존 여부를 조회하는 상위 체력 컴포넌트.</summary>
    private PlayerHealth _playerHealth;

    /// <summary>이동 속력, 접지 및 래그돌 상태를 조회하는 상위 프롭 컴포넌트.</summary>
    private IPropEntity _propEntity;

    /// <summary>마지막으로 발소리를 재생한 시각 (<see cref="Time.time"/> 기준).</summary>
    private float _lastStepSoundTime = float.NegativeInfinity;

    /// <summary>마지막으로 점프 사운드를 재생한 시각 (<see cref="Time.time"/> 기준).</summary>
    private float _lastJumpSoundTime = float.NegativeInfinity;

    /// <summary>마지막으로 착지 사운드를 재생한 시각 (<see cref="Time.time"/> 기준).</summary>
    private float _lastLandingSoundTime = float.NegativeInfinity;

    /// <summary>점프 사운드 이벤트 재생을 일시적으로 억제할 만료 시각 (<see cref="Time.time"/> 기준).</summary>
    private float _suppressJumpSoundUntil = float.NegativeInfinity;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _playerHealth = GetComponentInParent<PlayerHealth>();
        _propEntity = GetComponentInParent<IPropEntity>();
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary><paramref name="duration"/>초 동안 점프 애니메이션 사운드 이벤트를 무시한다.</summary>
    public void SuppressJumpSound(float duration)
    {
        if (duration <= 0f) return;
        _suppressJumpSoundUntil = Mathf.Max(_suppressJumpSoundUntil, Time.time + duration);
    }

    /// <summary>바닥 착지 시 <see cref="landingClip"/>을 1회 재생한다.</summary>
    public void PlayLandingSound()
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_propEntity != null && _propEntity.IsDead) return;
        if (Time.time - _lastLandingSoundTime < minLandingInterval) return;

        if (_audioSource != null && landingClip != null)
        {
            _lastLandingSoundTime = Time.time;
            _audioSource.PlayOneShot(landingClip, landingVolume);
        }
    }

    // ***************************************************************
    // Animation Events
    // ***************************************************************

    /// <summary>애니메이션 이벤트에서 호출되어 <see cref="stepClip"/>을 1회 재생한다.</summary>
    public void PlayStepSound(AnimationEvent evt)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_propEntity != null
            && (_propEntity.IsDead || _propEntity.IsRagdollIdle || !_propEntity.IsGrounded || _propEntity.PlanarSpeedRatio < 0.1f))
        {
            return;
        }

        if (evt != null && evt.animatorClipInfo.weight < minStepClipWeight) return;
        if (Time.time - _lastStepSoundTime < minStepInterval) return;

        if (_audioSource != null && stepClip != null)
        {
            _lastStepSoundTime = Time.time;
            _audioSource.PlayOneShot(stepClip, stepVolume);
        }
    }

    /// <summary>점프 애니메이션 이벤트에서 호출되어 <see cref="jumpClip"/>을 1회 재생한다.</summary>
    public void PlayJumpSound()
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_propEntity != null && (_propEntity.IsDead || _propEntity.IsRagdollIdle)) return;
        if (Time.time < _suppressJumpSoundUntil) return;
        if (Time.time - _lastJumpSoundTime < minJumpInterval) return;

        if (_audioSource != null && jumpClip != null)
        {
            _lastJumpSoundTime = Time.time;
            _audioSource.PlayOneShot(jumpClip, jumpVolume);
        }
    }
}
