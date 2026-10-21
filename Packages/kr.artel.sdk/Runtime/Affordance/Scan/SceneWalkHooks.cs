using System;
using System.Collections;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// 순회가 씬 하나를 읽어 낸 자리에서 바깥이 끼어들 수 있는 유일한 지점.
    /// </summary>
    /// <remarks>
    /// 이 어셈블리는 <c>Artel.Runtime</c> 을 참조하지 않는다 — 참조는 반대 방향 하나뿐이다. 그래서 근거 문서를 올리는 쪽이
    /// 씬마다 무언가를 하려면 순회가 그 자리를 열어 주는 수밖에 없다. 순회가 저쪽을 알게 만들면 참조가 양방향이 되고,
    /// 스캔만 쓰려는 프로젝트가 업로드 코드까지 함께 들여야 한다.
    ///
    /// 코루틴을 돌려받는 이유는 캡처가 프레임을 기다려야 하기 때문이다. 값 하나를 돌려주는 콜백으로는 back buffer 가 다 그려질
    /// 때까지 기다릴 수 없고, 기다리지 않고 읽으면 이전 씬이 찍힌다.
    ///
    /// <b>예외는 구독자가 스스로 삼킨다.</b> <c>yield return</c> 을 <c>try</c> 로 감쌀 수 없어 순회는 이 코루틴 안에서 터진
    /// 예외를 잡지 못한다. 새어 나오면 순회 전체가 그 자리에서 멎고 근거 문서가 아예 나오지 않는다 — 곁다리인 캡처가 본 일을
    /// 죽이는 것이라 그렇게 두지 않는다.
    /// </remarks>
    public static class SceneWalkHooks
    {
        /// <summary>
        /// 씬 하나를 읽은 직후 순회가 부른다. 인자는 그 씬의 이름이고, null 을 돌려주면 순회는 곧바로 다음 씬으로 간다.
        /// </summary>
        public static Func<string, IEnumerator> SceneRead;

        /// <summary>
        /// 순회가 씬 하나를 열기 직전에 부른다(ARTEL-1177). 인자는 앞서 연 씬 수 · 열 씬 전부 · 이제 열 씬의 이름이다.
        /// 다 돈 뒤에 한 번 더, 두 수가 같고 이름이 null 인 채로 부른다.
        /// </summary>
        /// <remarks>
        /// 큰 게임의 순회는 수 분이 걸리고, 그동안 서버는 결과 한 번만 받는다. 사람이 얼마나 남았는지 알려면 게임이 도중에
        /// 알려야 하는데, 몇 번째인지를 아는 것은 순회뿐이다. 값만 넘기고 기다리지 않는다 — 보내는 쪽이 순회를 늦추면 안 된다.
        /// </remarks>
        public static Action<int, int, string> SceneOpening;

        internal static void OnSceneOpening(int done, int total, string sceneName)
        {
            var hook = SceneOpening;

            if (hook == null)
            {
                return;
            }

            try
            {
                hook(done, total, sceneName);
            }
            catch (Exception exception)
            {
                // 진행 알림은 곁다리다. 터져도 순회는 계속 간다.
                UnityEngine.Debug.LogWarning("[Artel] A scene-opening hook threw and was skipped: " + exception.Message);
            }
        }

        /// <summary>순회가 이 자리에서 쓰는 코루틴. 구독자가 없거나 아무것도 하지 않으면 null.</summary>
        internal static IEnumerator OnSceneRead(string sceneName)
        {
            var hook = SceneRead;

            if (hook == null)
            {
                return null;
            }

            try
            {
                return hook(sceneName);
            }
            catch (Exception exception)
            {
                // 코루틴을 만드는 도중의 실패는 여기서 잡을 수 있다. 도는 도중의 실패는 구독자 몫이다.
                UnityEngine.Debug.LogWarning(
                    "[Artel] A scene-read hook threw before it started and was skipped: " + exception.Message);
                return null;
            }
        }
    }
}
