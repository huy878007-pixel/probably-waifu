using System.Collections;
using System.Reflection;
using UnityEngine;

namespace LaunchGiftPortraits.Content;

/// <summary>Use the native scaler's immutable registration, never its already-followed output.</summary>
sealed class BubblePlacement
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    RectTransform bubble, name;
    Vector2 nativeBubbleAnchor, nativeNameAnchor;
    Vector3 nativeNameScale;
    Vector2 bubbleBase, lastBubble, lastName;
    bool bubbleApplied, nameApplied;

    public void Bind(DialogUIManager ui)
    {
        var nextBubble = ui?.clientBubble != null ? ui.clientBubble.transform as RectTransform : null;
        var nextName = ui?.clientNameBubble != null ? ui.clientNameBubble.transform as RectTransform : null;
        if (bubble == nextBubble && name == nextName) return;
        Restore(); bubble = nextBubble; name = nextName;
        if (bubble == null || name == null) return;
        bubbleBase = bubble.anchoredPosition;
        nativeBubbleAnchor = bubble.anchoredPosition; nativeNameAnchor = name.anchoredPosition;
        nativeNameScale = name.localScale;
        var scaler = typeof(DialogUIManager).GetField("bubbleScaler", Fields)?.GetValue(ui);
        var entries = scaler?.GetType().GetField("entries", Fields)?.GetValue(scaler) as IEnumerable;
        if (entries == null) return;
        foreach (var entry in entries)
        {
            var type = entry.GetType();
            var rect = type.GetField("rect", Fields)?.GetValue(entry) as RectTransform;
            if (rect == bubble) nativeBubbleAnchor = (Vector2)type.GetField("basePosition", Fields).GetValue(entry);
            if (rect != name) continue;
            nativeNameAnchor = (Vector2)type.GetField("basePosition", Fields).GetValue(entry);
            nativeNameScale = (Vector3)type.GetField("baseScale", Fields).GetValue(entry);
        }
    }

    Vector2 NativeNamePosition()
    {
        // DialogBubbleScaler positions a follower from its leader. Our leader already carries the
        // previous frame's user offset when native scaling runs; subtract that output explicitly.
        float factor = Mathf.Abs(nativeNameScale.y) > .00001f ? name.localScale.y / nativeNameScale.y : 1f;
        return bubbleBase + (nativeNameAnchor - nativeBubbleAnchor) * factor;
    }

    static bool SameAxis(float a, float b) => Mathf.Abs(a - b) < .001f;

    void ReadNativeBubble()
    {
        var current = bubble.anchoredPosition;
        // Native mode switches can write only Y while retaining the mod's X. A resize can
        // also change localPosition without changing the anchor. Track each anchor axis.
        if (!bubbleApplied || !SameAxis(current.x, lastBubble.x)) bubbleBase.x = current.x;
        if (!bubbleApplied || !SameAxis(current.y, lastBubble.y)) bubbleBase.y = current.y;
    }

    public void Apply(float bubbleX, float bubbleY, bool upper, float nameX, float nameY)
    {
        if (bubble == null || name == null) return;
        ReadNativeBubble();
        var nativeName = NativeNamePosition();
        var targetBubble = bubbleBase + new Vector2(bubbleX, bubbleY);
        if (bubble.anchoredPosition != targetBubble) bubble.anchoredPosition = targetBubble;
        lastBubble = targetBubble; bubbleApplied = bubbleX != 0 || bubbleY != 0;
        Vector2 targetName;
        if (upper)
        {
            // The name's TOP LEFT attaches to the bubble's BOTTOM LEFT. It follows the actual
            // fitted rectangle, including wrapping, native scaling, canvas transforms and pivots.
            var target = bubble.TransformPoint(new Vector3(bubble.rect.xMin, bubble.rect.yMin + 2f, 0));
            var corner = name.TransformPoint(new Vector3(name.rect.xMin, name.rect.yMax, 0));
            var delta = target - corner;
            targetName = name.anchoredPosition + (Vector2)(name.parent != null ? name.parent.InverseTransformVector(delta) : delta);
        }
        else targetName = nativeName + new Vector2(nameX, nameY);
        if (name.anchoredPosition != targetName) name.anchoredPosition = targetName;
        lastName = targetName; nameApplied = true;
    }

    public void Restore()
    {
        if (bubble != null && name != null)
        {
            ReadNativeBubble();
            var nativeName = NativeNamePosition();
            if (nameApplied)
            {
                var current = name.anchoredPosition;
                if (SameAxis(current.x, lastName.x)) current.x = nativeName.x;
                if (SameAxis(current.y, lastName.y)) current.y = nativeName.y;
                name.anchoredPosition = current;
            }
            if (bubbleApplied) bubble.anchoredPosition = bubbleBase;
        }
        bubbleApplied = nameApplied = false;
    }
}
