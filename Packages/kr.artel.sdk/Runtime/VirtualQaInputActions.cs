using System;
using System.Collections;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using UnityEngine;

namespace Artel
{
    internal sealed class VirtualQaInputActions : IQaInputActions
    {
        private readonly CursorController cursorController;
        private readonly PointerEventDispatcher pointerEvents;

        internal VirtualQaInputActions(CursorController cursorController, PointerEventDispatcher pointerEvents)
        {
            this.cursorController = cursorController;
            this.pointerEvents = pointerEvents;
        }

        public string ReleaseAll()
        {
            pointerEvents?.ReleaseAll();
            ArtelInput.ReleaseAllVirtualInput();
            return null;
        }

        public IEnumerator MoveMouse(int actionId, Vector2 position, bool glide, Action<ActionResultDto> completed) =>
            WithMouseCompletion(actionId, done => MovePointer(actionId, position, glide, done), completed,
                waitForUiFrame: glide);

        private IEnumerator MovePointer(int actionId, Vector2 position, bool glide, Action<ActionResultDto> completed)
        {
            yield return cursorController.MoveTo(new Vector2(position.x, Screen.height - position.y), moved =>
            {
                ArtelInput.MoveMouse(moved);
                if (!NewInputMouse.UsesNativeUi) pointerEvents.MoveTo(moved);
            }, glide: glide, immediate: !glide);
            completed(ActionResultDto.Success(actionId));
        }

        public IEnumerator ChangeMouseButton(int actionId, int button, bool down, Action<ActionResultDto> completed) =>
            WithMouseCompletion(actionId, done => ChangeButton(actionId, button, down, done, reportPress: down),
                completed, releaseOnValidationFailure: !down, reportPress: down);

        private IEnumerator ChangeButton(int actionId, int button, bool down, Action<ActionResultDto> completed, bool reportPress)
        {
            if (!reportPress)
            {
                SetButton(button, down);
                completed(ActionResultDto.Success(actionId));
                yield break;
            }

            ArtelInput.ForgetLastPress();
            pointerEvents.ForgetLastPress();
            SetButton(button, true);
            var eventReceiver = pointerEvents.LastPressReceiver;
            yield return null;
            // OnGUI는 Update 뒤에 실행되므로 수신 결과를 읽기 전에 Repaint를 기다린다.
            if (button == 0 && string.IsNullOrEmpty(eventReceiver) && ArtelInput.HasRecentGuiButtons)
                yield return null;
            completed(ActionResultDto.Success(actionId, PressOutcome(eventReceiver)));
        }

        public IEnumerator ChangeKey(int actionId, KeyCode key, bool down, Action<ActionResultDto> completed) =>
            MouseButtonKeyCode.TryGetButton(key, out var button)
                ? WithMouseCompletion(actionId, done => ChangeButton(actionId, button, down, done, reportPress: false),
                    completed, releaseOnValidationFailure: !down)
                : ChangeKeyboardKey(actionId, key, down, completed);

        private static IEnumerator ChangeKeyboardKey(int actionId, KeyCode key, bool down, Action<ActionResultDto> completed)
        {
            if (!down)
            {
                // 가상 장치가 멈췄어도 기존 hold를 해제할 수 있어야 한다.
                ArtelInput.ReleaseKey(key);
                completed(ActionResultDto.Success(actionId));
                yield break;
            }

            var error = ValidateKeyboard(key);
            if (error != null)
            {
                completed(ActionResultDto.Failure(actionId, error));
                yield break;
            }

            ArtelInput.PressKey(key);
            yield return CompleteKeyboardAction(actionId, key, completed);
        }

        public IEnumerator ClickKey(int actionId, KeyCode key, float durationSeconds, Action<ActionResultDto> completed) =>
            MouseButtonKeyCode.TryGetButton(key, out var button)
                ? WithMouseCompletion(actionId, done => ClickButton(actionId, button, durationSeconds, done), completed)
                : ClickKeyboardKey(actionId, key, durationSeconds, completed);

        private static IEnumerator ClickKeyboardKey(int actionId, KeyCode key, float durationSeconds, Action<ActionResultDto> completed)
        {
            var error = ValidateKeyboard(key);
            if (error != null)
            {
                completed(ActionResultDto.Failure(actionId, error));
                yield break;
            }

            if (!NewInputKeyboard.DeliversThroughInputSystem(key))
            {
                ArtelInput.ClickKey(key, durationSeconds);
                completed(ActionResultDto.Success(actionId));
                yield break;
            }

            // 짧은 클릭이 FixedUpdate 사이에서 끝나지 않도록 누름 처리 이후부터 시간을 잰다.
            ArtelInput.PressKey(key);
            var pressed = false;
            yield return WaitForInputSystem(NewInputKeyboard.HasProcessed, () => NewInputKeyboard.IsActive,
                value => pressed = value);
            if (!pressed)
            {
                completed(ActionResultDto.Failure(actionId, "Input System did not process the QA key press."));
                yield break;
            }

            yield return new WaitForSecondsRealtime(durationSeconds);
            ArtelInput.ReleaseKey(key);
            yield return CompleteKeyboardAction(actionId, key, completed);
        }

        private static string ValidateKeyboard(KeyCode key) =>
            NewInputKeyboard.ValidationError(key) ?? InputReadManifest.KeyCommandError(key, NewInputKeyboard.IsRegistered);

        private static IEnumerator CompleteKeyboardAction(int actionId, KeyCode key, Action<ActionResultDto> completed)
        {
            if (NewInputKeyboard.DeliversThroughInputSystem(key) && NewInputKeyboard.IsActive)
            {
                var processed = false;
                yield return WaitForInputSystem(NewInputKeyboard.HasProcessed, () => NewInputKeyboard.IsActive,
                    value => processed = value);
                if (!processed)
                {
                    completed(ActionResultDto.Failure(actionId, "Input System did not process the QA key action."));
                    yield break;
                }
            }
            completed(ActionResultDto.Success(actionId));
        }

        private IEnumerator ClickButton(int actionId, int button, float durationSeconds, Action<ActionResultDto> completed)
        {
            SetButton(button, true);
            if (NewInputMouse.IsActive)
            {
                // 짧은 click도 FixedUpdate가 press를 처리하고 UI가 읽은 뒤에 놓는다.
                var pressed = false;
                yield return WaitForInputSystem(NewInputMouse.HasProcessed, () => NewInputMouse.IsActive,
                    value => pressed = value);
                if (!pressed)
                {
                    completed(ActionResultDto.Failure(actionId, "Input System did not process the QA mouse press."));
                    yield break;
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(durationSeconds);
            SetButton(button, false);
            completed(ActionResultDto.Success(actionId));
        }

        private void SetButton(int button, bool press)
        {
            if (press)
            {
                ArtelInput.PressMouseButton(button);
                if (!NewInputMouse.UsesNativeUi)
                {
                    pointerEvents.Press(button);
                    if (button == 0 && !string.IsNullOrEmpty(pointerEvents.LastPressReceiver))
                        ArtelInput.GuiPressHandledByEventSystem = true;
                }
            }
            else
            {
                ArtelInput.ReleaseMouseButton(button);
                if (!NewInputMouse.UsesNativeUi) pointerEvents.Release(button);
            }
        }

        internal string ValidateMouse(bool releaseOnFailure)
        {
            var error = NewInputMouse.ValidationError;
            if (error != null && releaseOnFailure) ArtelInput.ReleaseAllVirtualInput();
            return error;
        }

        private IEnumerator WithMouseCompletion(int actionId, Func<Action<ActionResultDto>, IEnumerator> execute,
            Action<ActionResultDto> completed, bool releaseOnValidationFailure = false, bool waitForUiFrame = true,
            bool reportPress = false)
        {
            var error = ValidateMouse(releaseOnValidationFailure);
            if (error != null)
            {
                completed(ActionResultDto.Failure(actionId, error));
                yield break;
            }

            ActionResultDto result = null;
            yield return execute(value => result = value);
            if (result != null && result.IsSuccess && NewInputMouse.IsActive)
            {
                var processed = false;
                yield return WaitForInputSystem(NewInputMouse.HasProcessed, () => NewInputMouse.IsActive,
                    value => processed = value);
                if (!processed)
                {
                    completed(ActionResultDto.Failure(actionId, "Input System did not process the QA mouse action."));
                    yield break;
                }
                // 클릭 결과와 일반 QA는 EventSystem.Update까지 기다린다.
                if (waitForUiFrame) yield return null;
                if (reportPress) result.ReturnValue = PressOutcome(pointerEvents.LastPressReceiver);
            }
            completed(result);
        }

        private static IEnumerator WaitForInputSystem(Func<int, bool> processed, Func<bool> active, Action<bool> finished)
        {
            var targetFrame = Time.frameCount + 1;
            var deadline = Time.realtimeSinceStartup + 2f;
            while (!processed(targetFrame))
            {
                if (Time.realtimeSinceStartup >= deadline || !active())
                {
                    ArtelInput.ReleaseAllVirtualInput();
                    finished(false);
                    yield break;
                }
                yield return null;
            }
            finished(true);
        }

        private static Dictionary<string, object> PressOutcome(string eventReceiver)
        {
            // native UI의 후속 raycast는 실제 수신 증거가 아니다.
            var nativeUi = NewInputMouse.UsesNativeUi;
            var reached = nativeUi ? ArtelInput.LastGuiPressReceiver :
                (string.IsNullOrEmpty(eventReceiver)
                    ? ArtelInput.LastGuiPressReceiver ?? ArtelInput.LastPressReceiver
                    : eventReceiver);
            return new Dictionary<string, object>
            {
                { "reached", string.IsNullOrEmpty(reached) ? null : reached },
                { "pointerHeldByPerson", string.IsNullOrEmpty(reached) &&
                    (nativeUi ? !ArtelInput.OwnsVirtualPointer : !ArtelInput.SawPress) }
            };
        }

    }
}
