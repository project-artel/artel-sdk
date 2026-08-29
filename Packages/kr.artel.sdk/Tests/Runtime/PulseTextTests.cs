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
