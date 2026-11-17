using UnityEngine;

namespace LaunchGiftPortraits.Content;

/// <summary>Mirror the text area's centre with its background, while keeping letters upright.</summary>
sealed class BubbleTextFlip
{
    RectTransform text, frame;
    Vector3 baseline, last;
    bool applied;

    public void Bind(RectTransform nextText, RectTransform nextFrame)
    {
        if (text == nextText && frame == nextFrame) return;
        Restore();
        text = nextText;
        frame = nextFrame;
    }

    public void Apply(bool flip)
    {
        if (text == null || frame == null || text == frame) return;
        if (!flip) { Restore(); return; }
        var current = text.localPosition;
        if (!applied || current != last) baseline = current;
        // Use the native position as the baseline; never reflect last frame's reflected result.
        var parent = text.parent;
        var centre = text.TransformPoint(text.rect.center);
        var baseCentre = centre - (parent != null ? parent.TransformVector(current - baseline) : current - baseline);
        var local = frame.InverseTransformPoint(baseCentre);
        local.y = frame.rect.yMin + frame.rect.yMax - local.y;
        var delta = frame.TransformPoint(local) - baseCentre;
        var target = baseline + (parent != null ? parent.InverseTransformVector(delta) : delta);
        if (current != target) text.localPosition = target;
        last = target;
        applied = true;
    }

    public void Restore()
    {
        // If native layout changed after our last update, its new position already wins.
        if (applied && text != null && text.localPosition == last) text.localPosition = baseline;
        applied = false;
    }
}
