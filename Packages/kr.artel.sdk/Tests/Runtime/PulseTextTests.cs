using System;
using System.Collections.Generic;
using Artel.Affordances.Live;
using Artel.Affordances.Scan;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Artel.Tests
{
    /// <summary>
    /// 판독이 화면의 글자를 나르는지 검증한다(ARTEL-678).
    ///
    /// 두 가지가 각각 막고 있었고 둘 다 풀려야 한다. <see cref="Worth"/> 가 라벨을 순회에 넣지 않아 객체 자체가
    /// 판독에 없었고, 넣더라도 값을 읽을 자리가 없었다.
    ///
    /// 여기서 확인하는 것은 <b>읽는 쪽</b>이다. 걷기 자체는 watch list 가 어셈블리에 구워진 근거에서 오고 테스트
    /// 어셈블리에는 그것이 없어 돌릴 수 없다 — <c>PulseGoneTests</c> 가 같은 이유로 장부만 놓고 본다.
    ///
    /// 갈래는 <c>UnityEngine.UI.Text</c> 하나로만 본다. 이름으로 맞추므로 갈래마다 도는 코드가 다르지 않다.
    /// TMP 를 쓰지 않는 것은 batchmode 에서 <c>TMP_Settings.instance</c> 가 리소스 임포터 창을 열려다
    /// 화면이 없다고 에러를 남기기 때문이다 — 이 규칙과 무관한 실패다.
    /// </summary>
    public sealed class PulseTextTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        private GameObject Object(string name)
        {
            var made = new GameObject(name);
            _made.Add(made);
            return made;
        }

        private GameObject Saying(string said)
        {
            var made = Object("Label");
            made.AddComponent<Text>().text = said;
            return made;
        }

        [TearDown]
        public void Down()
        {
            foreach (var made in _made)
            {
                if (made != null)
                {
                    UnityEngine.Object.DestroyImmediate(made);
                }
            }

            _made.Clear();
            Legible.Forget();
        }

        [Test]
        public void 글자를_읽는다()
        {
            Assert.That(Legible.Of(Saying("전투를 시작하려면 Space 를 누르세요")),
                Is.EqualTo("전투를 시작하려면 Space 를 누르세요"));
        }

        [Test]
        public void 글자를_띄우는_객체는_판독에_넣는다()
        {
            // 이것이 없으면 라벨은 값을 실을 자리에 닿지도 못한다. 근거를 굽지도, 인스펙터로 무엇을 부르지도
            // 않아 `Worth` 의 세 조건 어디에도 걸리지 않는다.
            Assert.That(
                Worth.Writing(Saying("Stage 1"), new Dictionary<Type, List<Watched>>()),
                Is.True);
        }

        [Test]
        public void 글자가_없는_객체는_그대로_안_넣는다()
        {
            var bare = Object("Empty");

            Assert.That(Legible.Of(bare), Is.Null);
            Assert.That(Worth.Writing(bare, new Dictionary<Type, List<Watched>>()), Is.False);
        }

        [Test]
        public void 빈_글자는_싣지_않는다()
        {
            // 글자를 띄우라고 놓였지만 지금은 아무것도 안 띄운 라벨이 흔하다. 그것들을 전부 실으면 판독이
            // 빈칸으로 찬다.
            Assert.That(Legible.Of(Saying(string.Empty)), Is.Null);
        }

        [Test]
        public void 긴_글자는_자르고_잘렸음을_보인다()
        {
            var long_ = new string('가', 400);

            var said = Legible.Of(Saying(long_));

            Assert.That(said, Does.EndWith("…"));
            Assert.That(said.Length, Is.LessThan(long_.Length));
            Assert.That(said, Does.StartWith(new string('가', 200)));
        }

        [Test]
        public void 자른_뒤의_것을_그대로_돌려준다()
        {
            // 온전한 값으로 비교하고 자른 값을 보내면, 앞부분이 같은 두 문자열이 "변했다" 고 보고되면서
            // 실려 가는 내용은 그대로인 판독이 된다. 장부가 대고 비교할 것과 독자가 받는 것은 같아야 한다.
            var first = Legible.Of(Saying(new string('가', 300) + "앞"));
            var second = Legible.Of(Saying(new string('가', 300) + "뒤"));

            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void 글자가_아닌_text_속성은_읽지_않는다()
        {
            // "`string text` 를 가진 컴포넌트 전부" 로 잡으면 그 이름을 전혀 다른 뜻으로 쓰는 게임 자신의
            // 컴포넌트가 딸려 온다.
            var impostor = Object("Impostor");
            impostor.AddComponent<NotALabel>();

            Assert.That(Legible.Of(impostor), Is.Null);
        }

        [Test]
        public void 계기로_표시된_것은_판독에_안_넣는다()
        {
            // SDK 가 화면에 띄우는 것들이 게임인 척 섞여 들어왔다 (ARTEL-698). 스테이지 렌더에서
            // `Artel Keyboard Status Canvas` 아래가 48줄로 1등이었고, 게임에서 가장 많은
            // `Card(Clone)` 25줄의 두 배였다.
            var overlay = Saying("PRESSED KEYS");
            overlay.AddComponent<Instrument>();

            Assert.That(
                Worth.Writing(overlay, new Dictionary<Type, List<Watched>>()),
                Is.False);
        }

        [Test]
        public void 계기_아래에_있으면_표시가_없어도_안_넣는다()
        {
            // 표시는 캔버스에 하나 붙이고 그 아래 전부가 빠진다. 라벨마다 붙이게 하면 언젠가
            // 하나를 빠뜨리고, 그날 그 라벨만 게임인 척 나온다.
            var canvas = Object("Artel Keyboard Status Canvas");
            canvas.AddComponent<Instrument>();

            var label = Saying("(960, 374)");
            label.transform.SetParent(canvas.transform, false);

            Assert.That(
                Worth.Writing(label, new Dictionary<Type, List<Watched>>()),
                Is.False);
        }

        [Test]
        public void 계기가_붙은_게임_오브젝트는_그대로_넣는다()
        {
            // `ArtelManager` 는 게임이 놓은 오브젝트에 컴포넌트로 붙고 캔버스를 그 자식으로 만든다.
            // 그래서 표시를 부모가 아니라 캔버스에 단다 — 부모에 달면 게임 것까지 빠진다.
            // 샘플 게임에서는 `StageDataSingleton` 이 그 오브젝트에 산다.
            var host = Saying("게임이 놓은 것");
            var canvas = Object("Artel Keyboard Status Canvas");
            canvas.AddComponent<Instrument>();
            canvas.transform.SetParent(host.transform, false);

            Assert.That(
                Worth.Writing(host, new Dictionary<Type, List<Watched>>()),
                Is.True);
        }

        [Test]
        public void 꺼진_계기도_계기다()
        {
            // `GetComponentInParent` 를 안 쓰는 이유다 — 그것은 꺼진 객체를 건너뛴다. 오버레이는
            // 꺼져 있을 수 있고, 켜질 때 갑자기 게임으로 보고되면 그것이 더 나쁘다.
            var canvas = Object("Artel Overlay Canvas");
            canvas.AddComponent<Instrument>();
            canvas.SetActive(false);

            var label = Saying("숨어 있는 계기");
            label.transform.SetParent(canvas.transform, false);

            Assert.That(
                Worth.Writing(label, new Dictionary<Type, List<Watched>>()),
                Is.False);
        }

        /// <summary>
        /// 이름만 같은 남. 속성으로 두는 것이 요점이다 — 필드로 두면 <c>GetProperty</c> 가 어차피 못 찾아,
        /// 이름 목록이 없어도 이 테스트가 통과한다.
        /// </summary>
        private sealed class NotALabel : MonoBehaviour
        {
            public string text
            {
                get { return "이것은 화면의 글자가 아니다"; }
            }
        }
    }
}
