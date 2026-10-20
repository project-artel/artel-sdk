using UnityEngine;
using UnityEngine.UI;

namespace Artel
{
    /// <summary>Draws the Artel mark without requiring an SVG runtime dependency.</summary>
    public sealed class ArtelLogoGraphic : Graphic
    {
        internal static readonly Color32 Charcoal = new Color32(0x20, 0x23, 0x2B, 0xFF);
        internal static readonly Color32 Ink = new Color32(0xF2, 0xEF, 0xE9, 0xFF);
        internal static readonly Color32 Coral = new Color32(0xF0, 0x4B, 0x3A, 0xFF);

        // #F04B3A는 다크 배경(#14161C) 위에서 대비 4.5:1을 넘지 못한다. 라이트에서는
        // 원본을 그대로 쓴다. artel-home과 마케팅 사이트가 쓰는 규칙과 같다.
        internal static readonly Color32 CoralDark = new Color32(0xFF, 0x5C, 0x48, 0xFF);

        internal static Color32 Accent(bool darkTheme) => darkTheme ? CoralDark : Coral;

        internal static Color32 Body(bool darkTheme) => darkTheme ? Ink : Charcoal;

        private Color32 bodyColor = Charcoal;
        private Color32 accentColor = Coral;

        public Color32 BodyColor
        {
            get => bodyColor;
            set
            {
                bodyColor = value;
                SetVerticesDirty();
            }
        }

        public Color32 AccentColor
        {
            get => accentColor;
            set
            {
                accentColor = value;
                SetVerticesDirty();
            }
        }

        // 64x64 상자 안의 SVG 좌표(y는 아래로 증가)다. 데스크톱 앱과 웹사이트가 쓰는 값과 같다.
        // 본체는 앞 6개가 바깥 윤곽, 뒤 6개가 안쪽 윤곽을 거꾸로 이은 오목 다각형이다.
        // 바깥 k번째 점은 안쪽 (OutlineCount - 1 - k)번째 점과 짝이 된다.
        private const int OutlineCount = 6;
        private static readonly Vector2[] BodyPolygon =
        {
            new Vector2(57.28f, 45.10f),
            new Vector2(57.28f, 17.40f),
            new Vector2(32.00f, 2.80f),
            new Vector2(6.72f, 17.40f),
            new Vector2(6.72f, 46.60f),
            new Vector2(30.70f, 60.45f),
            new Vector2(30.70f, 50.05f),
            new Vector2(15.72f, 41.40f),
            new Vector2(15.72f, 22.60f),
            new Vector2(32.00f, 13.20f),
            new Vector2(48.28f, 22.60f),
            new Vector2(48.28f, 39.90f)
        };

        // 본체에서 잘려 나간 오른쪽 아래 변이다. 볼록한 사다리꼴이다.
        private static readonly Vector2[] AccentPolygon =
        {
            new Vector2(33.30f, 50.05f),
            new Vector2(33.30f, 60.45f),
            new Vector2(55.99f, 47.35f),
            new Vector2(46.99f, 42.15f)
        };

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();

            AddBand(vertexHelper, BodyPolygon, bodyColor);
            AddConvex(vertexHelper, AccentPolygon, accentColor);
        }

        // 바깥 윤곽과 안쪽 윤곽의 짝이 맞는 점끼리 quad를 이어 오목 다각형을 채운다.
        private void AddBand(VertexHelper vertexHelper, Vector2[] points, Color32 fillColor)
        {
            var first = vertexHelper.currentVertCount;
            foreach (var point in points)
            {
                AddVertex(vertexHelper, ToLocal(point), fillColor);
            }

            var last = points.Length - 1;
            for (var index = 0; index < OutlineCount - 1; index++)
            {
                var outerStart = first + index;
                var outerEnd = first + index + 1;
                var innerEnd = first + last - (index + 1);
                var innerStart = first + last - index;

                // 화면에서 시계 방향이 앞면이다.
                vertexHelper.AddTriangle(outerStart, innerEnd, outerEnd);
                vertexHelper.AddTriangle(outerStart, innerStart, innerEnd);
            }
        }

        private void AddConvex(VertexHelper vertexHelper, Vector2[] points, Color32 fillColor)
        {
            var first = vertexHelper.currentVertCount;
            foreach (var point in points)
            {
                AddVertex(vertexHelper, ToLocal(point), fillColor);
            }

            for (var index = 1; index < points.Length - 1; index++)
            {
                vertexHelper.AddTriangle(first, first + index + 1, first + index);
            }
        }

        private float Scale => Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) / 64f;

        private Vector2 ToLocal(Vector2 point)
        {
            var scale = Scale;
            var origin = new Vector2(
                rectTransform.rect.xMin + ((rectTransform.rect.width - (64f * scale)) * 0.5f),
                rectTransform.rect.yMin + ((rectTransform.rect.height - (64f * scale)) * 0.5f));
            return origin + new Vector2(point.x, 64f - point.y) * scale;
        }

        private static void AddVertex(VertexHelper vertexHelper, Vector2 position, Color32 fillColor)
        {
            vertexHelper.AddVert(position, fillColor, Vector2.zero);
        }
    }
}
