using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Input
{
    public sealed class VirtualMouseStateTests
    {
        [Test]
        public void Press_ExposesDownHeldAndUpAsFrameSnapshots()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(0, 10);

            Assert.That(mouse.GetButtonDown(0, 10), Is.False, "the press starts on the next frame");
            Assert.That(mouse.GetButtonDown(0, 11), Is.True);
            Assert.That(mouse.GetButtonDown(0, 11), Is.True, "reading it must not consume it");
            Assert.That(mouse.GetButton(0, 11), Is.True);
            Assert.That(mouse.GetButtonDown(0, 12), Is.False);
            Assert.That(mouse.GetButton(0, 12), Is.True);

            mouse.Release(0, 12);

            Assert.That(mouse.GetButton(0, 12), Is.True, "still down on the frame that asked");
            Assert.That(mouse.GetButtonUp(0, 13), Is.True);
            Assert.That(mouse.GetButton(0, 13), Is.False);
            Assert.That(mouse.GetButtonUp(0, 14), Is.False);
        }

        [Test]
        public void Press_LeavesTheOtherButtonsAlone()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(1, 3);

            Assert.That(mouse.GetButton(1, 4), Is.True);
            Assert.That(mouse.GetButton(0, 4), Is.False);
            Assert.That(mouse.GetButton(2, 4), Is.False);
            Assert.That(mouse.IsAnyButtonHeld(4), Is.True);
        }

        /// <summary>
        /// <c>mouse_down</c> 과 <c>KeyCode.Mouse0</c> 을 실은 <c>key_down</c> 이 같은 버튼을 가리키므로
        /// 둘이 겹쳐 들어올 수 있다. 그때 폴링하는 게임이 클릭을 두 번으로 세면 안 된다.
        /// </summary>
        [Test]
        public void Press_OnAButtonAlreadyHeldChangesNothing()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(0, 10);

            Assert.That(mouse.GetButtonDown(0, 11), Is.True);

            mouse.Press(0, 11);

            Assert.That(mouse.GetButton(0, 12), Is.True, "the button stays held");
            Assert.That(
                mouse.GetButtonDown(0, 12), Is.False,
                "the second press must not start the hold over");
        }

        [Test]
        public void Press_AfterAReleaseStartsANewHold()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(0, 10);
            mouse.Release(0, 11);

            // 놓기가 예약된 버튼은 이미 끝난 누름이다. 다음 누름을 삼키면 연타가 한 번이 된다.
            mouse.Press(0, 11);

            Assert.That(mouse.GetButtonDown(0, 12), Is.True);
            Assert.That(mouse.GetButton(0, 12), Is.True);
        }

        [Test]
        public void IsAnyButtonDown_OnlyReportsTheFrameThePressStartsOn()
        {
            var mouse = new VirtualMouseState();

            Assert.That(mouse.IsAnyButtonDown(10), Is.False);

            mouse.Press(2, 10);

            Assert.That(mouse.IsAnyButtonDown(10), Is.False, "the press starts on the next frame");
            Assert.That(mouse.IsAnyButtonDown(11), Is.True);
            Assert.That(mouse.IsAnyButtonDown(12), Is.False, "held is not down");
            Assert.That(mouse.IsAnyButtonHeld(12), Is.True);
        }

        [Test]
        public void Press_IgnoresAButtonThatDoesNotExist()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(7, 3);
            mouse.Release(-1, 3);

            Assert.That(mouse.GetButton(7, 4), Is.False);
            Assert.That(mouse.IsAnyButtonHeld(4), Is.False);
        }

        [Test]
        public void ReleaseAll_LetsGoOfEveryHeldButton()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(0, 1);
            mouse.Press(2, 1);

            mouse.ReleaseAll(5);

            Assert.That(mouse.GetButtonUp(0, 6), Is.True);
            Assert.That(mouse.GetButtonUp(2, 6), Is.True);
            Assert.That(mouse.IsAnyButtonHeld(6), Is.False);
        }

        [Test]
        public void Position_IsUnclaimedUntilTheAgentMovesIt()
        {
            var mouse = new VirtualMouseState();
            var restingHand = new Vector2(10f, 10f);

            Assert.That(mouse.OwnsPointer(restingHand), Is.False);

            mouse.MoveTo(new Vector2(120f, 240f), restingHand);

            Assert.That(mouse.OwnsPointer(restingHand), Is.True);
            Assert.That(mouse.Position, Is.EqualTo(new Vector2(120f, 240f)));

            mouse.Clear();

            Assert.That(mouse.OwnsPointer(restingHand), Is.False);
        }

        [Test]
        public void OwnsPointer_GivesTheMouseBackAsSoonAsAPersonMovesIt()
        {
            var mouse = new VirtualMouseState();
            mouse.MoveTo(new Vector2(120f, 240f), new Vector2(10f, 10f));

            // Jitter from a mouse sitting still is not somebody reaching for it.
            Assert.That(mouse.OwnsPointer(new Vector2(12f, 11f)), Is.True);

            Assert.That(mouse.OwnsPointer(new Vector2(400f, 300f)), Is.False);

            // And it stays given back: the claim is not re-taken by the hand coming to rest again.
            Assert.That(mouse.OwnsPointer(new Vector2(10f, 10f)), Is.False);
        }

        [Test]
        public void ReleasePointer_LeavesAHeldButtonAlone()
        {
            // The connection ending hands the pointer back, but a button still has to report its
            // release on the right frame rather than vanishing mid-press.
            var mouse = new VirtualMouseState();
            mouse.MoveTo(new Vector2(120f, 240f), Vector2.zero);
            mouse.Press(0, 1);

            mouse.ReleasePointer();

            Assert.That(mouse.OwnsPointer(Vector2.zero), Is.False);
            Assert.That(mouse.GetButton(0, 2), Is.True);
        }

        [Test]
        public void Refresh_ForgetsAButtonOnceItsReleaseFrameHasPassed()
        {
            var mouse = new VirtualMouseState();
            mouse.Press(0, 1);
            mouse.Release(0, 1);

            // 누름은 2 에서 시작하고 놓기는 그보다 뒤여야 하므로 3 이다(ARTEL-766).
            // The release frame itself still has to report the up, so only later frames may drop it.
            mouse.Refresh(3);
            Assert.That(mouse.GetButtonUp(0, 3), Is.True);

            mouse.Refresh(4);
            Assert.That(mouse.GetButtonUp(0, 4), Is.False);
            Assert.That(mouse.GetButton(0, 4), Is.False);
        }

        [Test]
        public void Release_OnThePressFrameStillLeavesOneHeldFrame()
        {
            // `click_at` 이 `mouse_down` 과 `mouse_up` 을 프레임 양보 없이 보내 둘이 같은
            // 프레임에 처리된다. 놓기가 누름과 같은 프레임을 가리키면 눌린 프레임이 0개가 되고,
            // 프레임마다 폴링하는 쪽 — `VirtualMouseMessenger` 와 `Input.GetMouseButtonDown` —
            // 은 그 누름을 통째로 못 본다. 게임에 `OnMouseDown` 이 아예 안 갔다(ARTEL-766).
            var mouse = new VirtualMouseState();
            mouse.Press(0, 5);
            mouse.Release(0, 5);

            Assert.That(mouse.GetButtonDown(0, 6), Is.True, "the press has to be visible somewhere");
            Assert.That(mouse.GetButton(0, 6), Is.True);
            Assert.That(mouse.GetButtonUp(0, 6), Is.False, "up cannot land on the same frame as down");

            Assert.That(mouse.GetButtonUp(0, 7), Is.True);
            Assert.That(mouse.GetButton(0, 7), Is.False);
        }

        [Test]
        public void Release_AfterHeldFramesKeepsTheFrameItAsked()
        {
            // 드래그다. 누름과 놓기 사이에 프레임이 지나갔으면 하한에 걸릴 일이 없고, 놓기는
            // 요청한 프레임의 다음에 그대로 선다.
            var mouse = new VirtualMouseState();
            mouse.Press(0, 5);
            mouse.Release(0, 20);

            Assert.That(mouse.GetButton(0, 20), Is.True, "still down on the frame that asked");
            Assert.That(mouse.GetButtonUp(0, 21), Is.True);
            Assert.That(mouse.GetButton(0, 21), Is.False);
        }
    }
}
