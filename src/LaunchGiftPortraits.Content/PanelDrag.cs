using UnityEngine;
using UnityEngine.EventSystems;

namespace LaunchGiftPortraits.Content;

/// <summary>Drags the native window from its header, matching the game's own panels.</summary>
sealed class PanelDrag : MonoBehaviour, IDragHandler, IPointerDownHandler
{
    internal RectTransform target;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (target != null) target.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (target == null) return;
        var canvas = target.GetComponentInParent<Canvas>();
        var scale = canvas != null ? canvas.scaleFactor : 1f;
        if (scale <= 0f) scale = 1f;
        target.anchoredPosition += eventData.delta / scale;
    }
}
