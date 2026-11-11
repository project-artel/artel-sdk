using NUnit.Framework;
using UnityEngine;

namespace Artel.Tests.Input
{
    /// <summary>
    /// 키 명령을 "게임에 닿지 않는다"로 실패시키는 조건(ARTEL-1412). 확실할 때만 실패하고, 모르면 지금처럼 보낸다.
    /// </summary>
    public sealed class InputReadManifestTests
    {
        [SetUp]
        public void SetUp() => InputReadManifest.ClearForTests();

        [TearDown]
        public void TearDown() => InputReadManifest.ClearForTests();

        /// <summary>RedRunner 의 모양이다. 키는 읽지 않고 축만 읽는다.</summary>
        [Test]
        public void AKeyTheGameNeverReads_FailsWithTheAxesItDoesRead()
        {
            InputReadManifest.Declare("Assembly-CSharp", "", "", false, "Roll\nCancel", false);
            InputReadManifest.SawAxis("Jump");

            var error = InputReadManifest.KeyCommandError(KeyCode.Space, keyboardAdapterRegistered: false);

            Assert.That(error, Does.Contain("Space"));
            Assert.That(error, Does.Contain("(Cancel, Jump, Roll)"));
            Assert.That(error, Does.Contain("set_button"));
            Assert.That(error, Does.Contain("set_axis"));
        }

        [Test]
        public void AKeyReadAsALiteralSomewhere_IsSent()
        {
            InputReadManifest.Declare("Assembly-CSharp", "", "", false, "Horizontal", false);
            InputReadManifest.Declare("Assembly-CSharp-firstpass", ((int)KeyCode.Space).ToString(), "", false, "", false);

            Assert.That(InputReadManifest.KeyCommandError(KeyCode.Space, false), Is.Null);
            Assert.That(InputReadManifest.KeyCommandError(KeyCode.Return, false), Is.Not.Null);
        }

        [Test]
        public void AKeyNameThatParsesAsAKeyCode_CountsAsThatKey()
        {
            InputReadManifest.Declare("Assembly-CSharp", "", "space", false, "Horizontal", false);

            Assert.That(InputReadManifest.KeyCommandError(KeyCode.Space, false), Is.Null);
        }

        /// <summary>legacy 는 "left shift" 같은 이름도 받는다. 어느 키인지 모르면 판단하지 않는다.</summary>
        [Test]
        public void AKeyNameThatDoesNotParse_WithdrawsTheJudgement()
        {
            InputReadManifest.Declare("Assembly-CSharp", "", "left shift", false, "Horizontal", false);

            Assert.That(InputReadManifest.KeyCommandError(KeyCode.Space, false), Is.Null);
        }

        /// <summary>"1" 은 Enum.TryParse 가 KeyCode 1 로 받는다. Alpha1 로 착각해 "never reads" 를 말하면 안 된다.</summary>
        [Test]
        public void ANumericKeyName_IsNotTakenAsAKeyCodeValue()
        {
            InputReadManifest.Declare("Assembly-CSharp", "", "1", false, "Horizontal", false);

            Assert.That(InputReadManifest.KeyCommandError(KeyCode.Alpha1, false), Is.Null);
        }

        [Test]
        public void KeysReadByValueOrAnyKey_WithdrawTheJudgement()
        {
            InputReadManifest.Declare("Assembly-CSharp", "", "", true, "Horizontal", false);

            Assert.That(InputReadManifest.KeyCommandError(KeyCode.Space, false), Is.Null);
        }

        /// <summary>축을 읽지 않는 게임은 키를 못 읽는 다른 이유가 있을 수 있다. 축을 알려 줄 것도 없다.</summary>
        [Test]
        public void AGameThatReadsNoAxes_IsLeftAlone()
        {
            InputReadManifest.Declare("Assembly-CSharp", "", "", false, "", false);

            Assert.That(InputReadManifest.KeyCommandError(KeyCode.Space, false), Is.Null);
        }

        [Test]
        public void AxisNamesOnlyKnownAtRunTime_AreStillNamedAsSuch()
        {
            InputReadManifest.Declare("Assembly-CSharp-firstpass", "", "", false, "", true);

            Assert.That(
                InputReadManifest.KeyCommandError(KeyCode.Space, false),
                Does.Contain("whose names the game computes at run time"));
        }

        /// <summary>위빙이 안 됐으면 아무것도 모른다. 지금처럼 보낸다.</summary>
        [Test]
        public void NoManifestAtAll_IsLeftAlone()
        {
            InputReadManifest.SawAxis("Jump");

            Assert.That(InputReadManifest.KeyCommandError(KeyCode.Space, false), Is.Null);
        }

        /// <summary>Input System 키보드 adapter 가 있으면 키가 그쪽으로도 간다.</summary>
        [Test]
        public void TheKeyboardAdapter_TakesTheKeyAnyway()
        {
            InputReadManifest.Declare("Assembly-CSharp", "", "", false, "Horizontal", false);

            Assert.That(InputReadManifest.KeyCommandError(KeyCode.Space, keyboardAdapterRegistered: true), Is.Null);
        }

        [Test]
        public void MouseKeyCodes_GoThroughTheMouse()
        {
            InputReadManifest.Declare("Assembly-CSharp", "", "", false, "Horizontal", false);

            Assert.That(InputReadManifest.KeyCommandError(KeyCode.Mouse0, false), Is.Null);
        }

        /// <summary>`list_inputs` 의 키와 축 부분. 키는 빌드 기록, 축은 빌드 기록에 런 중 본 이름을 더한다.</summary>
        [Test]
        public void Describe_ListsDeclaredKeysAndAxesWithTheOnesSeenAtRunTime()
        {
            InputReadManifest.Declare("Assembly-CSharp", ((int)KeyCode.UpArrow) + "," + (int)KeyCode.Space, "escape", false, "Roll", false);
            InputReadManifest.Declare("Assembly-CSharp-firstpass", "", "", false, "", true);
            InputReadManifest.SawAxis("Jump");

            var inputs = new Artel.Protocol.Dto.InputListDto();
            InputReadManifest.Describe(inputs);

            Assert.That(inputs.Keys, Is.EqualTo(new[] { "Escape", "Space", "UpArrow" }));
            Assert.That(inputs.Axes, Is.EqualTo(new[] { "Jump", "Roll" }));
            Assert.That(inputs.OtherKeys, Is.False);
        }

        [Test]
        public void Describe_SaysOtherKeysMayBeRead_WhenAKeyIsReadByValue()
        {
            InputReadManifest.Declare("Assembly-CSharp", "", "", true, "", false);

            var inputs = new Artel.Protocol.Dto.InputListDto();
            InputReadManifest.Describe(inputs);

            Assert.That(inputs.OtherKeys, Is.True);
            Assert.That(inputs.Keys, Is.Empty);
        }

        /// <summary>Domain Reload 를 끈 재진입은 같은 assembly 를 다시 알린다. 앞의 기록을 덮어야 한다.</summary>
        [Test]
        public void DeclaringTheSameAssemblyAgain_ReplacesIt()
        {
            InputReadManifest.Declare("Assembly-CSharp", ((int)KeyCode.Space).ToString(), "", false, "Horizontal", false);
            InputReadManifest.Declare("Assembly-CSharp", "", "", false, "Horizontal", false);

            Assert.That(InputReadManifest.KeyCommandError(KeyCode.Space, false), Is.Not.Null);
        }
    }
}
