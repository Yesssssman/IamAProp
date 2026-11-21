using System.Collections;
using UnityEngine;

namespace Audio
{
    /// <summary>같은 GameObject의 <see cref="AudioSource"/> 클립을 끝까지 재생하고 <see cref="loopDelaySeconds"/>만큼 쉰 뒤 다시 재생함.</summary>
    [RequireComponent(typeof(AudioSource))]
    public class BackgroundMusic : MonoBehaviour
    {
        /// <summary>한 번 재생이 끝난 뒤 다음 재생까지 쉬는 시간, 초 단위.</summary>
        [SerializeField] private float loopDelaySeconds = 5f;

        /// <summary>재생 반복 코루틴. 재생 전이면 null.</summary>
        private Coroutine playing;

        /// <summary>반복 재생을 시작함. 이미 재생 중이면 아무것도 하지 않음.</summary>
        public void Play()
        {
            playing ??= StartCoroutine(PlayLoop());
        }

        private IEnumerator PlayLoop()
        {
            AudioSource source = GetComponent<AudioSource>();

            while (true)
            {
                source.Play();
                yield return new WaitForSeconds(source.clip.length + loopDelaySeconds);
            }
        }
    }
}
