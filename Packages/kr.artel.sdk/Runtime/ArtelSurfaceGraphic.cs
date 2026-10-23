using UnityEngine;
using UnityEngine.UI;

namespace Artel
{
    internal sealed class ArtelSurfaceGraphic : MaskableGraphic
    {
        public float Radius = 6f;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var bounds = rectTransform.rect;
            var radius = Mathf.Min(Radius, Mathf.Min(bounds.width, bounds.height) * 0.5f);
            mesh.AddVert(bounds.center, color, Vector2.zero);
            const int segments = 6;
            for (var corner = 0; corner < 4; corner++)
            {
                var center = new Vector2(
                    corner < 2 ? bounds.xMax - radius : bounds.xMin + radius,
                    corner == 0 || corner == 3 ? bounds.yMax - radius : bounds.yMin + radius);
                for (var step = 0; step <= segments; step++)
                {
                    var angle = (90f - corner * 90f - step * 90f / segments) * Mathf.Deg2Rad;
                    mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
                }
            }
            var perimeterCount = 4 * (segments + 1);
            for (var index = 1; index <= perimeterCount; index++)
                mesh.AddTriangle(0, index, index == perimeterCount ? 1 : index + 1);
        }
    }
}
