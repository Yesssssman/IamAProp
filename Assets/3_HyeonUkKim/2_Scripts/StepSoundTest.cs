using UnityEngine;

/// <summary>애니메이션 이벤트에서 발소리 오디오 클립을 재생하는 테스트 컴포넌트.</summary>
[RequireComponent(typeof(AudioSource))]
public class StepSoundTest : MonoBehaviour
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>발소리 이벤트 시 재생할 오디오 클립.</summary>
    [SerializeField] private AudioClip stepClip;

    /// <summary>발소리 재생에 사용할 <see cref="AudioSource"/> 컴포넌트.</summary>
    private AudioSource _audioSource;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
    }

    // ***************************************************************
    // Animation Events
    // ***************************************************************

    /// <summary>애니메이션 이벤트에서 호출되어 <see cref="stepClip"/>을 1회 재생한다.</summary>
    public void PlayStepSound()
    {
        if (_audioSource != null && stepClip != null)
        {
            _audioSource.PlayOneShot(stepClip);
        }
    }
}
