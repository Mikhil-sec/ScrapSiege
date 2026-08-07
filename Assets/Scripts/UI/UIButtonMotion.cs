using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ScrapSiege.UI
{
    /// <summary>
    /// Scale-punch press feedback for HUD buttons. A colour tint alone is easy to miss on a
    /// phone held at arm's length over a table, especially with a busy camera feed behind the
    /// UI - a physical squash reads instantly and costs nothing.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UIButtonMotion : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField] private float pressedScale = 0.94f;
        [SerializeField] private float responseSpeed = 18f;

        private RectTransform rect;
        private Selectable selectable;
        private Vector3 baseScale;
        private float target = 1f;

        private void Awake()
        {
            rect = GetComponent<RectTransform>();
            selectable = GetComponent<Selectable>();
            baseScale = rect.localScale;
        }

        private void OnDisable()
        {
            // Re-enabling a panel must never restore a button still frozen mid-press.
            target = 1f;
            if (rect != null) rect.localScale = baseScale;
        }

        private void Update()
        {
            float current = Mathf.Lerp(rect.localScale.x / baseScale.x, target, Time.unscaledDeltaTime * responseSpeed);
            rect.localScale = baseScale * current;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (selectable != null && !selectable.IsInteractable()) return;
            target = pressedScale;
        }

        public void OnPointerUp(PointerEventData eventData) => target = 1f;

        public void OnPointerExit(PointerEventData eventData) => target = 1f;
    }
}
