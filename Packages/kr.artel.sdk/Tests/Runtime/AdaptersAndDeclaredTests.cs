using System.Collections.Generic;
using Artel.Affordances;
using Artel.Affordances.Live;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Artel.Tests
{
    /// <summary>
    /// 멤버가 아닌 상태 — uGUI 와 엔진 컴포넌트 — 를 adapter 가 읽는지, 그리고 <see cref="ArtelObserveAttribute"/> 로
    /// 표시한 멤버가 watch list 에 드는지 본다(ARTEL-1282).
    /// </summary>
    public sealed class AdaptersAndDeclaredTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        private GameObject Object(string name)
        {
            var made = new GameObject(name);
            _made.Add(made);
            return made;
        }

        [SetUp]
        public void Up()
        {
            Lookup.Forget();
            WatchList.Forget();
            Worth.Forget();
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
            Lookup.Forget();
            WatchList.Forget();
            Worth.Forget();
        }

        [Test]
        public void 토글은_눌리는지와_켜졌는지를_싣는다()
        {
            var toggle = Object("Toggle");
            toggle.AddComponent<Toggle>().isOn = true;

            var said = UiState.Of(toggle);

            Assert.That(said, Does.StartWith(",\"ui\":{"));
            Assert.That(said, Does.Contain("\"interactable\":true"));
            Assert.That(said, Does.Contain("\"isOn\":true"));
        }

        [Test]
        public void 슬라이더와_게이지와_CanvasGroup_은_값을_셋째_자리에서_끊어_싣는다()
        {
            var slider = Object("Slider");
            slider.AddComponent<Slider>().value = 0.33333f;
            var gauge = Object("Gauge");
            gauge.AddComponent<Image>().fillAmount = 0.5f;
            var group = Object("Group");
            var canvasGroup = group.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0.25f;
            canvasGroup.interactable = false;

            Assert.That(UiState.Of(slider), Does.Contain("\"value\":0.333"));
            Assert.That(UiState.Of(gauge), Does.Contain("\"fill\":0.5"));
            Assert.That(UiState.Of(group), Does.Contain("\"alpha\":0.25").And.Contain("\"groupInteractable\":false"));
        }

        [Test]
        public void 입력을_받는_객체는_순회에_들고_빈_객체는_들지_않는다()
        {
            var button = Object("Button");
            button.AddComponent<Button>();
            var bare = Object("Bare");

            Assert.That(UiState.CanCarry(button), Is.True);
            Assert.That(Worth.Writing(button, new Dictionary<System.Type, List<Watched>>()), Is.True);
            Assert.That(UiState.Of(bare), Is.Null);
            Assert.That(UiState.CanCarry(bare), Is.False);
        }

        [Test]
        public void 오디오와_스프라이트와_애니메이터의_상태를_싣는다()
        {
            var speaker = Object("Speaker");
            speaker.AddComponent<AudioSource>();
            var sprite = Object("Sprite");
            var renderer = sprite.AddComponent<SpriteRenderer>();
            renderer.color = new Color(1f, 1f, 1f, 0.5f);
            var puppet = Object("Puppet");
            puppet.AddComponent<Animator>();

            Assert.That(EngineState.Of(speaker), Is.EqualTo(",\"engine\":{\"audio\":{\"playing\":false,\"clip\":null}}"));
            Assert.That(EngineState.Of(sprite), Does.Contain("\"sprite\":{\"name\":null,\"alpha\":0.5,\"enabled\":true}"));
            Assert.That(EngineState.Of(puppet), Does.Contain("\"anim\":{"));
            Assert.That(EngineState.CanCarry(speaker), Is.True);
            Assert.That(EngineState.CanCarry(sprite), Is.False, "Renderer 만으로는 순회에 안 든다. 배경 전부가 그것이다.");
        }

        [Test]
        public void 표시한_멤버는_watch_list_에_든다()
        {
            var all = WatchList.All();
            var keys = new List<string>();

            foreach (var member in all)
            {
                keys.Add(member.Key);
            }

            Assert.That(keys, Does.Contain("Artel.Tests.ObserveFixture::Hp"));
            Assert.That(keys, Does.Contain("Artel.Tests.ObserveFixture::Lives"));
            Assert.That(keys, Does.Contain("Artel.Tests.ObserveStatics::Build"));
            Assert.That(keys, Does.Contain("Artel.Tests.ObserveProgress::stage"));
            Assert.That(keys, Does.Not.Contain("Artel.Tests.ObserveFixture::Hidden"), "표시 안 한 멤버는 안 든다.");

            var lives = all[keys.IndexOf("Artel.Tests.ObserveFixture::Lives")];
            Assert.That(lives.Getter, Is.Not.Null);
            Assert.That(lives.Asked, Is.True);

            var build = all[keys.IndexOf("Artel.Tests.ObserveStatics::Build")];
            Assert.That(build.Static, Is.True);
        }

        [Test]
        public void 컴포넌트가_아닌_타입에_표시한_멤버는_첫_판독에_인스턴스를_찾는다()
        {
            var all = WatchList.All();
            Watched stage = null;

            foreach (var member in all)
            {
                if (member.Key == "Artel.Tests.ObserveProgress::stage")
                {
                    stage = member;
                }
            }

            Assert.That(stage, Is.Not.Null);
            Assert.That(stage.Holders, Is.Not.Null.And.Empty, "훑는 순간에는 찾지 않는다.");

            var holders = Lookup.HoldersOf(stage);

            Assert.That(holders, Has.Count.EqualTo(1));
            Assert.That(LiveState.Peek(stage, holders), Does.Contain("\"value\":2"));
        }

        [Test]
        public void 판독은_표시한_static_을_statics_자리에_쓴다()
        {
            var document = LiveState.Compose(
                1, default, new Restless(), new Restless(1f), new Dictionary<string, string>(), false, out _);

            Assert.That(document, Does.Contain("\"declaring\":\"Artel.Tests.ObserveStatics\",\"member\":\"Build\""));
            Assert.That(document, Does.Contain("\"value\":\"1.0\""));
        }
    }

    internal sealed class ObserveFixture : MonoBehaviour
    {
        [ArtelObserve] public int Hp = 10;

        [ArtelObserve] public int Lives => 3;

        public int Hidden = 1;
    }

    internal static class ObserveStatics
    {
        [ArtelObserve] public static string Build = "1.0";

        public static ObserveProgress Progress = new ObserveProgress();
    }

    internal sealed class ObserveProgress
    {
        [ArtelObserve] private int stage = 2;

        public int Stage => stage;
    }
}
