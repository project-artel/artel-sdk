using System;
using System.Collections;
using Artel.Protocol.Dto;
using UnityEngine;

namespace Artel
{
    internal interface IQaInputActions
    {
        // 위치는 스캔과 같은 좌상단 원점 게임 화면 좌표다.
        IEnumerator MoveMouse(int actionId, Vector2 position, bool glide, Action<ActionResultDto> completed);
        IEnumerator ChangeMouseButton(int actionId, int button, bool down, Action<ActionResultDto> completed);
        IEnumerator ChangeKey(int actionId, KeyCode key, bool down, Action<ActionResultDto> completed);
        IEnumerator ClickKey(int actionId, KeyCode key, float durationSeconds, Action<ActionResultDto> completed);
        string ReleaseAll();
    }
}
