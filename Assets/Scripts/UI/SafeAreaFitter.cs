using UnityEngine;

namespace ScrapSiege.UI
{
    /// <summary>
    /// Insets a full-screen RectTransform to Screen.safeArea so HUD controls don't sit under a
    /// notch, punch-hole camera or gesture bar. Matters here specifically: this is a phone-only
    /// AR game whose top bar and bottom action bar are both pinned to screen edges, and the
    /// target devices (Honor, Samsung, iPhone) all have different cutouts.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform rect;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();
            Apply();
        }

        // Cheap enough to poll: two struct compares per frame, and it has to be polled because
        // Unity raises no event when the safe area changes (rotation, split screen, gesture bar).
        private void Update()
        {
            if (Screen.safeArea == lastSafeArea &&
                Screen.width == lastScreenSize.x &&
                Screen.height == lastScreenSize.y)
                return;

            Apply();
        }

        private void Apply()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;

            lastSafeArea = Screen.safeArea;
            lastScreenSize = new Vector2Int(Screen.width, Screen.height);

            Vector2 min = lastSafeArea.position;
            Vector2 max = lastSafeArea.position + lastSafeArea.size;
            min.x /= Screen.width;
            min.y /= Screen.height;
            max.x /= Screen.width;
            max.y /= Screen.height;

            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
