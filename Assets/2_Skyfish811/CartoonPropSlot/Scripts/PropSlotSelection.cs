using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace Skyfish811.PropSlots
{
    public sealed class PropSlotSelection : MonoBehaviour
    {
        [SerializeField] private PropOption[] options;
        [SerializeField] private PropReel[] reels;
        [SerializeField] private UnityEvent<string> onSelected = new UnityEvent<string>();
        private PropOption[] candidates;
        private bool accepting;
        private int stopped;
        public string SelectedId { get; private set; }

        private void OnEnable() => Roll();
        private void OnDisable() => StopAllCoroutines();

        public void Roll()
        {
            StopAllCoroutines();
            accepting = false;
            stopped = 0;
            SelectedId = null;
            var families = options.GroupBy(option => option.family).Select(group => group.ToArray()).ToArray();
            Shuffle(families);
            candidates = new PropOption[reels.Length];
            for (int i = 0; i < reels.Length; i++)
            {
                var family = families[i];
                candidates[i] = family[Random.Range(0, family.Length)];
                var strip = (PropOption[])options.Clone();
                Shuffle(strip);
                StartCoroutine(Spin(i, strip));
            }
        }

        private IEnumerator Spin(int i, PropOption[] strip)
        {
            yield return reels[i].Spin(strip, candidates[i], 1.0f + i * 0.22f, 9 + i * 2);
            if (++stopped == reels.Length)
            {
                accepting = true;
                foreach (var reel in reels) reel.SetSelectable(true);
            }
        }

        public void Select(int index)
        {
            if (!accepting || index < 0 || index >= reels.Length) return;
            accepting = false;
            SelectedId = candidates[index].id;
            foreach (var reel in reels) reel.SetSelectable(false);
            reels[index].PlayPress();
            onSelected.Invoke(SelectedId);
        }

        private static void Shuffle<T>(IList<T> items)
        {
            for (int i = items.Count - 1; i > 0; i--)
            {
                int other = Random.Range(0, i + 1);
                T temp = items[i];
                items[i] = items[other];
                items[other] = temp;
            }
        }
    }
}
