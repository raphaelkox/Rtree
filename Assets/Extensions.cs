using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Extensions
{
    public static Rect ExpandedToContain(this Rect rect, Rect otherRect) {
        Rect newRect = new(rect);
        newRect.xMin = Mathf.Min(rect.xMin, otherRect.xMin);
        newRect.yMin = Mathf.Min(rect.yMin, otherRect.yMin);
        newRect.xMax = Mathf.Max(rect.xMax, otherRect.xMax);
        newRect.yMax = Mathf.Max(rect.yMax, otherRect.yMax);
        return newRect;
    }
}
