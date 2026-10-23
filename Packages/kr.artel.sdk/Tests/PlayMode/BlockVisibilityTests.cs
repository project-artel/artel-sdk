using System.Collections;
using System.Collections.Generic;
using Artel.Domain;
using Artel.Protocol.Mapping;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Artel.Tests
{
    /// <summary>
    /// scan 블록의 <c>visible</c> 이 <c>onScreen</c> 과 갈리는 경우들(ARTEL-1225).
    /// </summary>
    public sealed class BlockVisibilityTests
    {
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var created in spawned)
            {
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }

            spawned.Clear();
        }

        /// <summary>track B <c>defects-v1-unreached.md</c> 의 sprite 결함: 적의 <c>SpriteRenderer</c> 를 끈 빌드.</summary>
        [UnityTest]
        public IEnumerator ASpriteWhoseRendererIsOff_IsOnScreenButNotVisible()
        {
            MainCamera();
            var enemy = Sprite("artel visibility enemy");
            yield return null;

            var shown = TransformOf(enemy.name);
            Assert.That(shown.OnScreen, Is.True);
            Assert.That(shown.Visible, Is.True);

            enemy.GetComponent<SpriteRenderer>().enabled = false;
            var hidden = TransformOf(enemy.name);

            Assert.That(hidden.OnScreen, Is.True, "onScreen keeps its meaning");
            Assert.That(hidden.Visible, Is.False);
        }

        [UnityTest]
        public IEnumerator ATransparentSprite_IsNotVisible()
        {
            MainCamera();
            var enemy = Sprite("artel visibility ghost");
            enemy.GetComponent<SpriteRenderer>().color = new Color(1f, 1f, 1f, 0f);
            yield return null;

            Assert.That(TransformOf(enemy.name).Visible, Is.False);
        }

        [UnityTest]
        public IEnumerator APanelUnderACanvasGroupWithAlphaZero_IsNotVisible()
        {
            var canvas = OverlayCanvas("artel visibility canvas");
            var group = Panel("artel visibility group", canvas.transform, new Vector2(400f, 300f), Vector2.zero);
            group.AddComponent<CanvasGroup>().alpha = 0f;
            var panel = Panel("artel visibility faded", group.transform, new Vector2(100f, 100f), Vector2.zero);
            yield return null;

            var faded = TransformOf(panel.name);
            Assert.That(faded.OnScreen, Is.True);
            Assert.That(faded.Visible, Is.False);

            group.GetComponent<CanvasGroup>().alpha = 0.5f;
            Assert.That(TransformOf(panel.name).Visible, Is.True);
        }

        [UnityTest]
        public IEnumerator AGraphicWithAlphaZero_IsNotVisible()
        {
            var canvas = OverlayCanvas("artel visibility canvas");
            var panel = Panel("artel visibility clear", canvas.transform, new Vector2(100f, 100f), Vector2.zero);
            panel.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            yield return null;

            Assert.That(TransformOf(panel.name).Visible, Is.False);
        }

        [UnityTest]
        public IEnumerator AChildScrolledOutsideItsRectMask2D_IsNotVisible()
        {
            var canvas = OverlayCanvas("artel visibility canvas");
            var viewport = Panel("artel visibility viewport", canvas.transform, new Vector2(200f, 200f), Vector2.zero);
            viewport.AddComponent<RectMask2D>();
            var inside = Panel("artel visibility inside", viewport.transform, new Vector2(50f, 50f), Vector2.zero);
            var outside = Panel("artel visibility outside", viewport.transform, new Vector2(50f, 50f), new Vector2(150f, 0f));
            yield return null;

            Assert.That(TransformOf(inside.name).Visible, Is.True);

            var clipped = TransformOf(outside.name);
            Assert.That(clipped.OnScreen, Is.True, "still inside the frame");
            Assert.That(clipped.Visible, Is.False);
        }

        [UnityTest]
        public IEnumerator AButtonCoveredByAnotherPanel_IsNotVisible()
        {
            EventSystemObject();
            var canvas = OverlayCanvas("artel visibility canvas");
            // 다른 테스트가 남긴 UI 보다 위에 그려야 이 테스트의 버튼과 덮개만 raycast 에 걸린다. SDK overlay 는 판정이 건너뛴다.
            canvas.sortingOrder = 30000;
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            var button = Panel("artel visibility button", canvas.transform, new Vector2(100f, 100f), Vector2.zero);
            button.AddComponent<Button>();
            yield return null;

            Assert.That(TransformOf(button.name).Visible, Is.True, "nothing on top yet");

            // 나중에 붙인 형제가 위에 그려지고 raycast 도 먼저 맞는다.
            Panel("artel visibility cover", canvas.transform, new Vector2(300f, 300f), Vector2.zero);
            yield return null;

            var covered = TransformOf(button.name);
            Assert.That(covered.OnScreen, Is.True);
            Assert.That(covered.Visible, Is.False);
        }

        [UnityTest]
        public IEnumerator VisibleIsWrittenBesideOnScreen()
        {
            var canvas = OverlayCanvas("artel visibility canvas");
            Panel("artel visibility wire", canvas.transform, new Vector2(100f, 100f), Vector2.zero);
            yield return null;

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(
                SceneSnapshotMapper.ToDto(new SceneScanner().Scan().Scene));

            Assert.That(json, Does.Contain("\"onScreen\":true,\"visible\":true"));
        }

        private static BlockTransform TransformOf(string name)
        {
            var block = Find(new SceneScanner().Scan().Scene.Children, name);
            Assert.That(block, Is.Not.Null, name + " was not scanned");
            return block.Transform;
        }

        private static SceneBlock Find(IReadOnlyList<SceneBlock> blocks, string name)
        {
            foreach (var block in blocks)
            {
                if (block.Name == name)
                {
                    return block;
                }

                var match = Find(block.Children, name);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private void MainCamera()
        {
            if (Camera.main != null)
            {
                return;
            }

            var camera = Track(new GameObject("artel visibility camera", typeof(Camera))).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);
        }

        private GameObject Sprite(string name)
        {
            var texture = new Texture2D(4, 4);
            spawned.Add(texture);
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f), 4f);
            spawned.Add(sprite);
            var enemy = Track(new GameObject(name, typeof(SpriteRenderer)));
            enemy.GetComponent<SpriteRenderer>().sprite = sprite;
            return enemy;
        }

        private void EventSystemObject()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            Track(new GameObject("artel visibility event system", typeof(EventSystem), typeof(StandaloneInputModule)));
        }

        private Canvas OverlayCanvas(string name)
        {
            var canvas = Track(new GameObject(name, typeof(Canvas))).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }

        /// <summary>부모의 가운데에 놓인 사각형. <paramref name="offset"/> 만큼 비켜 놓는다.</summary>
        private GameObject Panel(string name, Transform parent, Vector2 size, Vector2 offset)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rectTransform = panel.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            rectTransform.sizeDelta = size;
            rectTransform.anchoredPosition = offset;
            return panel;
        }

        private GameObject Track(GameObject created)
        {
            spawned.Add(created);
            return created;
        }
    }
}
