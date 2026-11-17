using UnityEngine;
using UnityEngine.UI;

namespace LaunchGiftPortraits.Content;

/// <summary>Mirror only the background mesh; text, layout and native animation stay untouched.</summary>
public sealed class BubbleFrameFlip : BaseMeshEffect
{
    readonly List<UIVertex> vertices = new();

    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || mesh.currentVertCount == 0) return;
        vertices.Clear();
        mesh.GetUIVertexStream(vertices);
        float sumY = graphic.rectTransform.rect.yMin + graphic.rectTransform.rect.yMax;
        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            vertex.position.y = sumY - vertex.position.y;
            vertices[i] = vertex;
        }
        // Reflection reverses winding. Keep the original facing even for a culling material.
        for (int i = 0; i + 2 < vertices.Count; i += 3)
            (vertices[i + 1], vertices[i + 2]) = (vertices[i + 2], vertices[i + 1]);
        mesh.Clear();
        mesh.AddUIVertexTriangleStream(vertices);
        vertices.Clear();
    }
}
