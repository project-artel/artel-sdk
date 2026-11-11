#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using Artel.Affordances.Live;
using Artel.Tracking;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Artel.Tests
{
    /// <summary>
    /// <c>pulse</c> 의 객체 항목이 <c>visible</c> 을 나르는지(ARTEL-1435).
    /// </summary>
    /// <remarks>
    /// macro 의 <c>actionable</c> 이 이 값으로 덮인 버튼을 거른다. 덮개는 <see cref="BlockVisibilityTests"/> 와 같은 방식으로
    /// 만든다 — 나중에 붙인 형제가 위에 그려지고 raycast 도 먼저 맞는다. 클릭 배선은
    /// <c>UnityEventTools.AddPersistentListener</c> 로 인스펙터 배선을 흉내 낸다. <c>offers.clicks</c> 는 persistent call 만 읽는다.
    /// </remarks>
    public sealed class PulseVisibleTests
    {
        private readonly List<Object> spawned = new List<Object>();
        private readonly Dictionary<string, string> since = new Dictionary<string, string>();
        private readonly Restless restless = new Restless();
        private readonly Restless pixels = new Restless(1f);
        private long reading;

        [SetUp]
        public void SetUp()
        {
            Lookup.Forget();
            WatchList.Forget();
            Worth.Forget();
            LiveState.Visibility = new PulseVisibility();
        }

        [TearDown]
        public void TearDown()
        {
            LiveState.Visibility = null;

            foreach (var created in spawned)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            spawned.Clear();
            Lookup.Forget();
            WatchList.Forget();
            Worth.Forget();
        }

        [UnityTest]
        public IEnumerator ACoveredButton_SaysVisibleFalse_AndTrueAgainWhenUncovered()
        {
            var canvas = UiCanvas();
            var button = WiredButton(canvas.transform, "artel pulse visible button");
            yield return null;

            Assert.That(VisibleOf(Compose(), button.name), Is.EqualTo(true), "whole reading, nothing on top");

            var cover = Panel("artel pulse visible cover", canvas.transform, new Vector2(300f, 300f));
            yield return null;

            var covered = Compose();
            Assert.That(VisibleOf(covered, button.name), Is.EqualTo(false));
            Assert.That(covered["whole"]?.Value<bool>(), Is.False, "a delta carries the change");

            Object.DestroyImmediate(cover);
            yield return null;

            Assert.That(VisibleOf(Compose(), button.name), Is.EqualTo(true));
        }

        [UnityTest]
        public IEnumerator AnUnchangedVisible_IsNotRepeatedInADelta()
        {
            var canvas = UiCanvas();
            var button = WiredButton(canvas.transform, "artel pulse visible steady");
            yield return null;

            Compose();
            yield return null;

            Assert.That(VisibleOf(Compose(), button.name), Is.Null);
        }

        [UnityTest]
        public IEnumerator AnObjectThatOffersNoClick_CarriesNoVisible()
        {
            var canvas = UiCanvas();
            var button = Panel("artel pulse visible unwired", canvas.transform, new Vector2(100f, 100f));
            button.AddComponent<Button>();
            yield return null;

            var document = Compose();
            Assert.That(Entry(document, button.name), Is.Not.Null, "a Selectable is walked");
            Assert.That(VisibleOf(document, button.name), Is.Null);
        }

        [UnityTest]
        public IEnumerator WithoutAVisibilityInstalled_NoObjectCarriesVisible()
        {
            LiveState.Visibility = null;
            var canvas = UiCanvas();
            var button = WiredButton(canvas.transform, "artel pulse visible uninstalled");
            yield return null;

            var document = Compose();
            Assert.That(Entry(document, button.name), Is.Not.Null);
            Assert.That(VisibleOf(document, button.name), Is.Null);
        }

        private JObject Compose()
        {
            return JObject.Parse(LiveState.Compose(
                ++reading, default, restless, pixels, since, false, out _));
        }

        private static JObject Entry(JObject document, string name)
        {
            foreach (var entry in (JArray)document["active"])
            {
                var path = entry["path"]?.Value<string>();
                if (path != null && (path == name || path.EndsWith("/" + name)))
                {
                    return (JObject)entry;
                }
            }

            return null;
        }

        /// <summary>그 객체 항목의 <c>visible</c>. 항목이 없거나 키가 없으면 null.</summary>
        private static bool? VisibleOf(JObject document, string name)
        {
            return Entry(document, name)?["visible"]?.Value<bool>();
        }

        private Canvas UiCanvas()
        {
            if (EventSystem.current == null)
            {
                Track(new GameObject("artel pulse visible event system", typeof(EventSystem), typeof(StandaloneInputModule)));
            }

            var canvas = Track(new GameObject("artel pulse visible canvas", typeof(Canvas))).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 다른 테스트가 남긴 UI 보다 위에 그려야 이 테스트의 버튼과 덮개만 raycast 에 걸린다.
            canvas.sortingOrder = 30000;
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private GameObject WiredButton(Transform parent, string name)
        {
            var button = Panel(name, parent, new Vector2(100f, 100f));
            var listener = button.AddComponent<PulseVisibleListener>();
            UnityEventTools.AddPersistentListener(button.AddComponent<Button>().onClick, listener.Ping);
            return button;
        }

        private GameObject Panel(string name, Transform parent, Vector2 size)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rectTransform = panel.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            rectTransform.sizeDelta = size;
            return panel;
        }

        private GameObject Track(GameObject created)
        {
            spawned.Add(created);
            return created;
        }
    }

    internal sealed class PulseVisibleListener : MonoBehaviour
    {
        public void Ping()
        {
        }
    }
}
#endif
