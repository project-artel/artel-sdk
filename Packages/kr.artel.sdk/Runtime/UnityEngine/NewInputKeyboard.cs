using System.Collections.Generic;
using Artel.Protocol.Dto;
using UnityEngine;

namespace Artel
{
    // optional assembly 가 등록되지 않으면 legacy 키 경로만 쓴다. NewInputMouse 와 같은 구조다.
    internal static class NewInputKeyboard
    {
        internal const string MissingAdapterError =
            "QA key actions require Input System 1.7.0 or newer and the Artel.InputSystem adapter.";

        private static INewInputKeyboardBackend backend;

        internal static void Register(INewInputKeyboardBackend replacement)
        {
            backend?.Dispose();
            backend = replacement;
        }

        internal static bool IsRegistered => backend != null;
        internal static bool IsActive => backend?.IsActive ?? false;

        /// <summary>
        /// 이 키를 게임에 넣을 수 없는 이유. 넣을 수 있으면 null 이다.
        /// </summary>
        /// <remarks>
        /// legacy 경로가 있으면(Both) Input System 쪽 사정으로 실패시키지 않는다. legacy proxy 가 그 키를 이미 받는다.
        /// </remarks>
        internal static string ValidationError(KeyCode key)
        {
            if (backend == null)
                return Application.isPlaying && !LegacyInput.IsAvailable ? MissingAdapterError : null;
            return LegacyInput.IsAvailable ? null : backend.ValidationError(key);
        }

        /// <summary>
        /// 이 키를 Input System 장치로도 넣는가. false 면 legacy proxy 만으로 보낸다(Both 에서 Input System 쪽이 멈춘 경우).
        /// </summary>
        /// <remarks>
        /// New 전용에서 false 인 경우는 <see cref="ValidationError"/> 가 이미 실패로 막는다.
        /// </remarks>
        internal static bool DeliversThroughInputSystem(KeyCode key) =>
            backend != null && Application.isPlaying && backend.ValidationError(key) == null;

        /// <summary>지금 켜진 Input System action 과 그 키보드 키. adapter 가 없으면 빈 목록이다.</summary>
        internal static List<InputActionKeysDto> EnabledActionKeys() =>
            backend?.EnabledActionKeys() ?? new List<InputActionKeysDto>();

        internal static void Activate() => backend?.Activate();
        internal static bool HasProcessed(int frame) => backend?.HasProcessed(frame) ?? true;
        internal static void Release() => backend?.Release();
        // reset 과 SubsystemRegistration 의 실행 순서가 달라도 등록은 유지한다.
        internal static void Dispose() => backend?.Dispose();
    }

    internal interface INewInputKeyboardBackend
    {
        bool IsActive { get; }
        string ValidationError(KeyCode key);
        List<InputActionKeysDto> EnabledActionKeys();
        void Activate();
        bool HasProcessed(int frame);
        void Release();
        void Dispose();
    }
}
