using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ScrapSiege.AR
{
    /// <summary>
    /// Week 1 spike helper: shows live plane-detection counts on-device so we can
    /// visually confirm ARCore plane tracking works on the Honor test phone
    /// without needing a debugger attached.
    /// </summary>
    [RequireComponent(typeof(ARPlaneManager))]
    public class PlaneDetectionDebugHud : MonoBehaviour
    {
        [SerializeField] private TMP_Text statusText;

        private ARPlaneManager planeManager;
        private readonly StringBuilder sb = new StringBuilder();

        private void Awake()
        {
            planeManager = GetComponent<ARPlaneManager>();
        }

        private void OnEnable()
        {
            planeManager.trackablesChanged.AddListener(OnPlanesChanged);
        }

        private void OnDisable()
        {
            planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
        }

        private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            if (statusText == null) return;

            sb.Clear();
            sb.Append("Planes tracked: ").Append(planeManager.trackables.count);
            statusText.text = sb.ToString();
        }
    }
}
