using Artel.Pointer;
using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests
{
    public sealed class PointerAimTargetsTests
    {
        [Test]
        public void AimPoint_IsTheRectCenterWhenTheRectIsWhollyOnScreen()
        {
            var point = PointerAimTargets.AimPoint(new Rect(100f, 200f, 50f, 40f), 1620f, 1054f);

            Assert.That(point, Is.EqualTo(new Vector2(125f, 220f)));
        }

        [Test]
        public void AimPoint_StaysInsideTheVisiblePartOfARectThatHangsOffTheBottom()
        {
            // WordVenture 의 쉬는 손패 카드: rect 는 화면 아래 끝(1054)을 넘어 1153 까지 내려간다. 전체 중심은 1055.5 로 화면 밖이다.
            var card = new Rect(901.8f, 957.9f, 132.7f, 195.2f);

            var point = PointerAimTargets.AimPoint(card, 1620f, 1054f);

            Assert.That(point.x, Is.EqualTo(card.center.x).Within(0.01f));
            Assert.That(point.y, Is.EqualTo((957.9f + 1054f) / 2f).Within(0.01f));
            Assert.That(point.y, Is.LessThan(1054f));
        }

        [Test]
        public void AimPoint_ClipsEverySideOfTheScreen()
        {
            var point = PointerAimTargets.AimPoint(new Rect(-100f, -40f, 300f, 200f), 150f, 120f);

            Assert.That(point, Is.EqualTo(new Vector2(75f, 60f)));
        }
    }
}
