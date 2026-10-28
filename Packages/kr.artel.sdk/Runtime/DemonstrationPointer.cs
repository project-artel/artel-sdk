using System;
using System.Collections.Generic;

namespace Artel
{
    internal static class DemonstrationPointer
    {
        internal static bool TryResolve(List<object> normalized, int width, int height, out List<object> pixels)
        {
            pixels = null;
            if (normalized == null || normalized.Count != 2 || width <= 0 || height <= 0) return false;
            if (!TryCoordinate(normalized[0], out var x) || !TryCoordinate(normalized[1], out var y)) return false;
            // WebRTC는 화면을 축소할 수 있으므로 입력을 적용하는 시점의 게임 해상도로 변환한다.
            pixels = new List<object> { x * width, y * height };
            return true;
        }

        private static bool TryCoordinate(object value, out double coordinate)
        {
            coordinate = 0;
            if (!(value is double || value is float || value is long || value is int || value is decimal)) return false;
            coordinate = Convert.ToDouble(value);
            return !double.IsNaN(coordinate) && !double.IsInfinity(coordinate) && coordinate >= 0 && coordinate <= 1;
        }
    }
}
