using System.Collections.Generic;
using Artel.Affordances.Scan;
using Artel.Tests.Tracking;
using NUnit.Framework;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Artel.Tests
{
    /// <summary>
    /// evidence scan 이 <see cref="Instrument"/> 로 표시된 객체와 그 아래를 보고에서 빼는지 검증한다(ARTEL-906).
    /// </summary>
    /// <remarks>
    /// <see cref="PulseTextTests"/> 의 계기 관련 테스트들과 짝이다 — 저쪽은 <see cref="Live.Worth.Writing"/> 이 같은
    /// 규칙을 지키는지를 보고, 이쪽은 <see cref="SceneEvidenceScan.Capture"/> 와
    /// <see cref="SceneEvidenceScan.CapturePersistent"/> 가 지키는지를 본다.
    ///
    /// 근거를 굽지 않은 테스트 어셈블리에서는 <c>AffordanceCatalog</c> 가 늘 비어 있으므로, 컴포넌트가 보고에 실릴
    /// 자격을 얻으려면 <c>UnityEventTools.AddPersistentListener</c> 로 인스펙터 배선을 흉내 낸다 —
    /// <c>SceneScannerTests</c> 가 이미 쓰는 방법이다.
    /// </remarks>
    public sealed class SceneEvidenceScanInstrumentTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

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
            AffordanceReport.Forget();
        }

        private GameObject Object_(string name)
        {
            var made = new GameObject(name);
            _made.Add(made);
            return made;
        }

        /// <summary>인스펙터 배선이 있어 보고에 실릴 자격을 얻는 버튼. 대상은 별도 객체에 둔다.</summary>
        private Button Wired(string name)
        {
            var button = Object_(name).AddComponent<Button>();
            var listener = Object_(name + " Listener").AddComponent<TrackedFixtureBehaviour>();
            UnityEventTools.AddPersistentListener(button.onClick, listener.Ping);
            return button;
        }

        private static string ComposedAfterSceneWalk()
        {
            SceneEvidenceScan.Capture(SceneManager.GetActiveScene());
            return AffordanceReport.Compose();
        }

        [Test]
        public void 계기로_표시된_객체는_씬_순회에서_뺀다()
        {
            var marked = Wired("Instrument Button");
            marked.gameObject.AddComponent<Instrument>();
            Wired("Plain Button");

            var document = ComposedAfterSceneWalk();

            Assert.That(document, Does.Not.Contain("Instrument Button"));
            Assert.That(document, Does.Contain("Plain Button"));
        }

        [Test]
        public void 계기_아래에_있으면_표시가_없어도_안_넣는다()
        {
            var canvas = Object_("Artel Overlay Canvas");
            canvas.AddComponent<Instrument>();

            var marked = Wired("Overlay Button");
            marked.transform.SetParent(canvas.transform, false);
            Wired("Plain Button");

            var document = ComposedAfterSceneWalk();

            Assert.That(document, Does.Not.Contain("Overlay Button"));
            Assert.That(document, Does.Contain("Plain Button"));
        }

        [Test]
        public void 꺼진_계기도_씬_순회에서_뺀다()
        {
            // Live.Worth 와 같은 이유다 - 꺼진 오버레이도 계기이고, 켜질 때 갑자기 게임으로 보고되면 더 나쁘다.
            var canvas = Object_("Artel Overlay Canvas");
            canvas.AddComponent<Instrument>();
            canvas.SetActive(false);

            var marked = Wired("Hidden Overlay Button");
            marked.transform.SetParent(canvas.transform, false);

            var document = ComposedAfterSceneWalk();

            Assert.That(document, Does.Not.Contain("Hidden Overlay Button"));
        }

        [Test]
        public void 계기가_붙은_게임_오브젝트는_그대로_넣는다()
        {
            // ArtelManager 가 정확히 이 모양이다: 게임이 놓은 오브젝트에 컴포넌트로 붙고, 계기는 그 자식 캔버스에
            // 붙는다. 부모까지 빠지면 게임 오브젝트가 보고에서 사라진다.
            var host = Wired("Game Host");
            var canvas = Object_("Artel Overlay Canvas");
            canvas.AddComponent<Instrument>();
            canvas.transform.SetParent(host.transform, false);

            var document = ComposedAfterSceneWalk();

            Assert.That(document, Does.Contain("Game Host"));
        }

        [Test]
        public void 영속_오브젝트_순회에서도_계기_아래를_뺀다()
        {
            // CapturePersistent 가 DontDestroyOnLoad 씬을 걷는 자리이고, ArtelManager 의 오버레이 트리가 사는
            // 자리와 같다. 씬 순회와 같은 규칙이 여기도 적용돼야 한다.
            var canvas = Object_("Artel Overlay Canvas");
            canvas.AddComponent<Instrument>();

            var marked = Wired("Persistent Overlay Button");
            marked.transform.SetParent(canvas.transform, false);
            Wired("Persistent Plain Button");

            SceneEvidenceScan.CapturePersistent(SceneManager.GetActiveScene());
            var document = AffordanceReport.Compose();

            Assert.That(document, Does.Not.Contain("Persistent Overlay Button"));
            Assert.That(document, Does.Contain("Persistent Plain Button"));
        }
    }
}
