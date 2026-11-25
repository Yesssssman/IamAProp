using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Skyfish811.UI
{
    public sealed class LoadingScreenPreview : MonoBehaviour
    {
        [SerializeField] private LoadingScreenView loadingScreen;
        [SerializeField, Min(0f)] private float simulatedWait = 1.6f;

        private Coroutine preview;
        private bool useChest;

        private void Start() => Replay();

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.bKey.wasPressedThisFrame || keyboard.cKey.wasPressedThisFrame)
            {
                useChest = keyboard.cKey.wasPressedThisFrame;
                Replay();
            }
            else if (keyboard.rKey.wasPressedThisFrame)
                Replay();
        }

        public void Replay()
        {
            if (preview != null)
                StopCoroutine(preview);

            preview = StartCoroutine(PlayPreview());
        }

        private IEnumerator PlayPreview()
        {
            loadingScreen.BeginLoading(useChest);
            loadingScreen.ReportProgress(0.89f);
            yield return new WaitForSecondsRealtime(simulatedWait);
            loadingScreen.ReportProgress(LoadingScreenView.TransformProgress);
            yield return new WaitUntil(() => loadingScreen.HasTransformed);
            yield return new WaitForSecondsRealtime(0.15f);
            loadingScreen.NotifyDestinationReady();
            preview = null;
        }
    }
}
