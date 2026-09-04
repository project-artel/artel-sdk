#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Runtime.InteropServices;
#endif

namespace Artel
{
    /// <summary>
    /// <c>-artel-window-label</c> 을 작업 표시줄과 Alt+Tab 에서 보이는 창 제목에도 남긴다
    /// (ARTEL-826).
    /// </summary>
    /// <remarks>
    /// Unity 에는 실행 중인 창 제목을 바꾸는 API 가 없다. 그래서 user32 의
    /// <c>SetWindowText</c> 를 <c>DllImport</c> 로 직접 부른다.
    ///
    /// <c>UNITY_STANDALONE_WIN &amp;&amp; !UNITY_EDITOR</c> 로 막는 이유는 두 가지다.
    /// <c>UNITY_EDITOR</c> 는 EditMode·PlayMode 테스트를 포함해 에디터 안에서는 항상 서고, 이
    /// 조합이면 <c>user32.dll</c> 을 부르는 코드가 전처리기에서 아예 걷힌다 — 에디터를 macOS 나
    /// Linux 에서 돌리는 테스트가 이 파일 때문에 실패할 일이 없다. 그리고 Windows 독립 실행
    /// 빌드가 아닌 플랫폼에는 애초에 <c>user32.dll</c> 이 없으므로, 그 밖에서는 <see cref="Apply"/>
    /// 를 불러도 아무 일도 하지 않는다.
    /// </remarks>
    internal static class ArtelWindowTitle
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool SetWindowText(IntPtr windowHandle, string text);
#endif

        /// <summary>
        /// <paramref name="label"/> 을 창 제목으로 삼는다. 라벨이 없으면 제목을 건드리지 않는다.
        /// </summary>
        public static void Apply(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return;
            }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            SetWindowText(GetActiveWindow(), label);
#endif
        }
    }
}
