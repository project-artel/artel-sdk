using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Artel.Tests.Input
{
    public sealed class KeyboardStatusControllerTests
    {
        [Test]
        public void FormatPressedKeys_UsesReadableKeyLabels()
        {
            var result = KeyboardStatusController.FormatPressedKeys(
                new List<KeyCode> { KeyCode.W, KeyCode.LeftShift, KeyCode.Space });

            Assert.That(result, Is.EqualTo("W  +  LEFT SHIFT  +  SPACE"));
        }

        [Test]
        public void FormatPressedKeys_UsesPlaceholderWhenNoKeyIsPressed()
        {
            Assert.That(
                KeyboardStatusController.FormatPressedKeys(new List<KeyCode>()),
                Is.EqualTo("—"));
        }

        [Test]
        public void KeyboardOverlay_UsesDarkBrandPaletteByDefault()
        {
            var hadTheme = PlayerPrefs.HasKey("Artel.DarkTheme");
            var previousTheme = PlayerPrefs.GetInt("Artel.DarkTheme");
            var host = new GameObject("keyboard status");
            try
            {
                PlayerPrefs.SetInt("Artel.DarkTheme", 1);
                var controller = host.AddComponent<KeyboardStatusController>();
                typeof(KeyboardStatusController)
                    .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, null);
                var panel = host.transform
                    .Find("Artel Keyboard Status Canvas/Keyboard Status Panel");

                Assert.That(panel.GetComponent<Image>().color, Is.EqualTo((Color)KeyboardStatusController.DarkPanelColor));
                // 다크에서는 밝힌 coral을 써야 한다. 원본 #F04B3A는 다크 패널 위에서
                // 대비 4.5:1을 넘지 못한다.
                Assert.That(
                    panel.Find("Brand Accent").GetComponent<Image>().color,
                    Is.EqualTo((Color)ArtelLogoGraphic.CoralDark));
                Assert.That(panel.Find("Separator"), Is.Not.Null);

                PlayerPrefs.SetInt("Artel.DarkTheme", 0);
                typeof(KeyboardStatusController)
                    .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(controller, null);

                Assert.That(panel.GetComponent<Image>().color, Is.EqualTo((Color)KeyboardStatusController.LightPanelColor));
                Assert.That(
                    panel.Find("Brand Accent").GetComponent<Image>().color,
                    Is.EqualTo((Color)ArtelLogoGraphic.Coral));
            }
            finally
            {
                Object.DestroyImmediate(host);
                if (hadTheme)
                {
                    PlayerPrefs.SetInt("Artel.DarkTheme", previousTheme);
                }
                else
                {
                    PlayerPrefs.DeleteKey("Artel.DarkTheme");
                }
            }
        }

        [Test]
        public void HideForCapture_TurnsThePanelOff()
        {
            var host = new GameObject("keyboard status");
            try
            {
                var controller = Awaken(host);

                controller.HideForCapture();

                Assert.That(PanelCanvas(host).enabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void ShowAfterCapture_TurnsThePanelBackOn()
        {
            var host = new GameObject("keyboard status");
            try
            {
                var controller = Awaken(host);

                controller.HideForCapture();
                controller.ShowAfterCapture();

                Assert.That(PanelCanvas(host).enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 캡처는 자기가 끈 패널만 켠다.
        /// </summary>
        /// <remarks>
        /// 패널을 끄고 싶어 끈 사람이 있을 수 있고, 그 패널이 다음 캡처 뒤에 혼자 켜지면 안 된다.
        /// </remarks>
        [Test]
        public void ShowAfterCapture_LeavesAPanelThatWasAlreadyOffAlone()
        {
            var host = new GameObject("keyboard status");
            try
            {
                var controller = Awaken(host);
                var canvas = PanelCanvas(host);
                canvas.enabled = false;

                controller.HideForCapture();
                controller.ShowAfterCapture();

                Assert.That(canvas.enabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 캡처 두 개가 겹쳐도 패널은 꺼진 채 남지 않는다.
        /// </summary>
        /// <remarks>
        /// `capture_screen` 과 evidence scan 의 thumbnail 이 같은 컨트롤러를 부른다. 둘이 겹치면 뒤쪽 이미지에
        /// 패널이 한 번 찍힐 수는 있어도, 끄고 켜는 것이 어긋나 패널이 사라진 채 남는 일은 없어야 한다.
        /// </remarks>
        [Test]
        public void ShowAfterCapture_TurnsThePanelOnAfterOverlappingCaptures()
        {
            var host = new GameObject("keyboard status");
            try
            {
                var controller = Awaken(host);

                controller.HideForCapture();
                controller.HideForCapture();
                controller.ShowAfterCapture();
                controller.ShowAfterCapture();

                Assert.That(PanelCanvas(host).enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// EditMode 는 <c>Awake</c> 를 부르지 않는다. 부르지 않으면 패널이 없어 어떤 단언도 무의미해진다.
        /// </summary>
        private static KeyboardStatusController Awaken(GameObject host)
        {
            var controller = host.AddComponent<KeyboardStatusController>();
            typeof(KeyboardStatusController)
                .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
            return controller;
        }

        private static Canvas PanelCanvas(GameObject host)
        {
            return host.transform.Find("Artel Keyboard Status Canvas").GetComponent<Canvas>();
        }
    }
}
