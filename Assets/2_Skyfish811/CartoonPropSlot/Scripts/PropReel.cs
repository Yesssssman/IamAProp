using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Skyfish811.PropSlots
{
    public sealed class PropReel : MonoBehaviour
    {
        [SerializeField] private Image[] rows;
        [SerializeField] private float spacing = 180f;
        [SerializeField] private Button selectButton;
        [SerializeField] private DOTweenAnimation landing;
        [SerializeField] private DOTweenAnimation press;

        public IEnumerator Spin(PropOption[] strip, PropOption winner, float duration, int turns)
        {
            SetSelectable(false);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = elapsed / duration;
                Draw(strip, winner, turns * (1f - (1f - t) * (1f - t)), turns);
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            Draw(strip, winner, turns, turns);
            landing.DORestart();
        }

        private void Draw(PropOption[] strip, PropOption winner, float position, int target)
        {
            int center = Mathf.FloorToInt(position);
            for (int i = 0; i < rows.Length; i++)
            {
                int offset = i - 1;
                int index = center + offset;
                PropOption option = index == target ? winner : strip[(index + strip.Length) % strip.Length];
                rows[i].sprite = index == target || position >= target ? option.icon : option.blur;
                rows[i].rectTransform.anchoredPosition = Vector2.up * ((index - position) * spacing);
            }
        }

        public void SetSelectable(bool value) => selectButton.interactable = value;
        public void PlayPress() => press.DORestart();
    }

    [Serializable]
    public sealed class PropOption
    {
        public string id;
        public string family;
        public Sprite icon;
        public Sprite blur;
    }
}
