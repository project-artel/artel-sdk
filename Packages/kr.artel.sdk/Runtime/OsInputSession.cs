using System;
using System.Collections;
using System.Collections.Generic;
using Artel.NativeInput;
using Artel.Protocol.Dto;
using UnityEngine;

namespace Artel
{
    internal sealed class OsInputSession : IQaInputActions
    {
        private readonly Func<IOsInputBackend> createBackend;
        private readonly HashSet<KeyCode> heldKeys = new HashSet<KeyCode>();
        private readonly HashSet<int> heldButtons = new HashSet<int>();
        private IOsInputBackend backend;
        internal int Generation { get; private set; }

        internal OsInputSession(Func<IOsInputBackend> createBackend = null)
        {
            this.createBackend = createBackend ?? OsInputBackendFactory.Create;
        }

        internal string Validate() => Attempt(() => EnsureBackend().Validate(requireFocus: true));

        public IEnumerator MoveMouse(int actionId, Vector2 position, bool glide, Action<ActionResultDto> completed)
        {
            yield return Complete(actionId, MoveMouse(position), completed);
        }

        public IEnumerator ChangeMouseButton(int actionId, int button, bool down, Action<ActionResultDto> completed)
        {
            yield return Complete(actionId, SetButton(button, down), completed);
        }

        public IEnumerator ChangeKey(int actionId, KeyCode key, bool down, Action<ActionResultDto> completed)
        {
            yield return Complete(actionId, ChangeKey(key, down), completed);
        }

        public IEnumerator ClickKey(int actionId, KeyCode key, float durationSeconds, Action<ActionResultDto> completed)
        {
            var error = ChangeKey(key, true);
            if (error == null)
            {
                var generation = Generation;
                yield return new WaitForSecondsRealtime(durationSeconds);
                if (Generation != generation)
                {
                    completed(ActionResultDto.Failure(actionId, "OS input key_click was interrupted."));
                    yield break;
                }
                error = ChangeKey(key, false);
            }
            yield return Complete(actionId, error, completed);
        }

        private string ChangeKey(KeyCode key, bool down) =>
            MouseButtonKeyCode.TryGetButton(key, out var button) ? SetButton(button, down) : SetKey(key, down);

        private IEnumerator Complete(int actionId, string error, Action<ActionResultDto> completed)
        {
            if (error != null)
            {
                var releaseError = ReleaseAll();
                completed(ActionResultDto.Failure(actionId, releaseError == null ? error : error + "; release failed: " + releaseError));
                yield break;
            }

            // OS에 이벤트를 제출한 뒤 한 프레임을 기다린다.
            yield return null;
            completed(ActionResultDto.Success(actionId));
        }

        internal string SetKey(KeyCode key, bool down) => Attempt(() =>
        {
            if (!down && !heldKeys.Contains(key)) return;
            if (down) EnsureBackend().Validate();
            EnsureBackend().SetKey(key, down);
            if (down) heldKeys.Add(key);
            else heldKeys.Remove(key);
        });

        internal string SetButton(int button, bool down) => Attempt(() =>
        {
            if (!down && !heldButtons.Contains(button)) return;
            if (down) EnsureBackend().Validate();
            EnsureBackend().SetMouseButton(button, down);
            if (down) heldButtons.Add(button);
            else heldButtons.Remove(button);
        });

        internal string MoveMouse(Vector2 gameScreenPosition) => Attempt(() =>
        {
            EnsureBackend().Validate();
            backend.MoveMouse(gameScreenPosition);
        });

        public string ReleaseAll()
        {
            if (backend == null) return null;
            Generation++;
            var failures = new List<string>();
            foreach (var key in new List<KeyCode>(heldKeys))
            {
                var error = SetKey(key, false);
                if (error != null) failures.Add(error);
            }
            foreach (var button in new List<int>(heldButtons))
            {
                var error = SetButton(button, false);
                if (error != null) failures.Add(error);
            }
            if (failures.Count > 0) return string.Join("; ", failures);
            var closingBackend = backend;
            backend = null;
            return Attempt(closingBackend.Dispose);
        }

        private IOsInputBackend EnsureBackend() => backend ?? (backend = createBackend());

        private static string Attempt(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return "OS input: " + exception.Message;
            }
        }
    }
}
