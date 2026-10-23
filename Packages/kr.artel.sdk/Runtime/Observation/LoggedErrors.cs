using System;
using System.Collections.Generic;
using Artel.Protocol.Dto;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// 게임이 찍은 예외와 Error 로그를 묶어 세어 둔다(ARTEL-1218).
    /// </summary>
    /// <remarks>
    /// <c>Application.logMessageReceived</c> 를 건다. <c>Exception</c>, <c>Error</c>, <c>Assert</c> 만 모으고
    /// <c>Warning</c> 과 <c>Log</c> 는 버린다. 게임이 <c>try</c> 로 잡고 <c>Debug.LogException</c> 으로 찍은 예외도
    /// <c>LogType.Exception</c> 으로 같은 자리에 들어온다.
    ///
    /// 같은 예외가 매 frame 찍히는 게임이 흔하다. 그래서 type, message, stack 첫 줄이 같으면 한 묶음으로 세고 frame 범위만
    /// 늘린다. 묶음은 <see cref="MaxGroups"/> 개까지 두고, 넘치면 <c>pulse</c> 로 이미 보낸 것 중 가장 오래전에 찍힌
    /// 것부터 버린다. 버려진 묶음이 다시 찍히면 새 묶음으로 처음부터 센다.
    ///
    /// 메인 스레드 전용 이벤트를 쓴다. 다른 스레드의 로그는 들어오지 않지만, 대신 <c>Time.frameCount</c> 를 그 자리에서
    /// 읽을 수 있고 잠금이 필요 없다.
    /// </remarks>
    internal sealed class LoggedErrors : IDisposable
    {
        internal const int MaxGroups = 100;
        internal const int MaxMessageLength = 200;

        private readonly Dictionary<(string type, string message, string stackHead), Group> groups =
            new Dictionary<(string, string, string), Group>();

        private readonly Func<int> currentFrame;
        private long nextOrder;
        private bool listening;

        public LoggedErrors(Func<int> currentFrame = null)
        {
            this.currentFrame = currentFrame ?? (() => Time.frameCount);
        }

        internal int GroupCount => groups.Count;

        public void Listen()
        {
            if (listening)
            {
                return;
            }

            Application.logMessageReceived += Record;
            listening = true;
        }

        public void Dispose()
        {
            if (!listening)
            {
                return;
            }

            Application.logMessageReceived -= Record;
            listening = false;
        }

        internal void Record(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
            {
                return;
            }

            var message = condition ?? string.Empty;
            var truncated = message.Length > MaxMessageLength;
            if (truncated)
            {
                message = message.Substring(0, MaxMessageLength);
            }

            var key = (type.ToString(), message, StackHead(stackTrace));
            var frame = currentFrame();

            if (groups.TryGetValue(key, out var group))
            {
                group.Count++;
                group.LastFrame = frame;
                return;
            }

            if (groups.Count >= MaxGroups)
            {
                EvictOne();
            }

            groups[key] = new Group
            {
                Type = key.Item1,
                Message = message,
                StackHead = key.Item3,
                Truncated = truncated,
                FirstFrame = frame,
                LastFrame = frame,
                Count = 1,
                Order = nextOrder++
            };
        }

        /// <summary>
        /// 직전 호출 이후 새로 생기거나 count 가 늘어난 묶음을 <paramref name="limit"/> 개까지 꺼내고, 꺼낸 것을 보냈다고 적는다.
        /// </summary>
        /// <remarks>
        /// 한도를 넘은 묶음은 보냈다고 적지 않으므로 다음 호출에 나간다. 묶음 수가 <see cref="MaxGroups"/> 로 묶여 있어 밀린
        /// 것이 끝없이 쌓이지 않는다.
        /// </remarks>
        internal List<LoggedErrorDto> TakeUnsent(int limit)
        {
            var unsent = new List<Group>();
            foreach (var group in groups.Values)
            {
                if (group.SentCount != group.Count)
                {
                    unsent.Add(group);
                }
            }

            unsent.Sort((left, right) => left.Order.CompareTo(right.Order));
            var taken = new List<LoggedErrorDto>(Math.Min(limit, unsent.Count));
            for (var index = 0; index < unsent.Count && taken.Count < limit; index++)
            {
                unsent[index].SentCount = unsent[index].Count;
                taken.Add(unsent[index].ToDto());
            }

            return taken;
        }

        /// <summary><paramref name="startFrame"/> 이후에 한 번이라도 찍힌 묶음을 <paramref name="limit"/> 개까지. 보냄 표시는 바꾸지 않는다.</summary>
        internal List<LoggedErrorDto> SeenSince(int startFrame, int limit)
        {
            var seen = new List<Group>();
            foreach (var group in groups.Values)
            {
                if (group.LastFrame >= startFrame)
                {
                    seen.Add(group);
                }
            }

            seen.Sort((left, right) => left.Order.CompareTo(right.Order));
            var result = new List<LoggedErrorDto>(Math.Min(limit, seen.Count));
            for (var index = 0; index < seen.Count && result.Count < limit; index++)
            {
                result.Add(seen[index].ToDto());
            }

            return result;
        }

        private void EvictOne()
        {
            (string, string, string)? oldestSent = null;
            (string, string, string)? oldestAny = null;
            var oldestSentFrame = int.MaxValue;
            var oldestAnyFrame = int.MaxValue;

            foreach (var pair in groups)
            {
                if (pair.Value.LastFrame < oldestAnyFrame)
                {
                    oldestAnyFrame = pair.Value.LastFrame;
                    oldestAny = pair.Key;
                }

                if (pair.Value.SentCount == pair.Value.Count && pair.Value.LastFrame < oldestSentFrame)
                {
                    oldestSentFrame = pair.Value.LastFrame;
                    oldestSent = pair.Key;
                }
            }

            var victim = oldestSent ?? oldestAny;
            if (victim.HasValue)
            {
                groups.Remove(victim.Value);
            }
        }

        private static string StackHead(string stackTrace)
        {
            if (string.IsNullOrEmpty(stackTrace))
            {
                return null;
            }

            foreach (var line in stackTrace.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0)
                {
                    return trimmed;
                }
            }

            return null;
        }

        private sealed class Group
        {
            public string Type;
            public string Message;
            public string StackHead;
            public bool Truncated;
            public int FirstFrame;
            public int LastFrame;
            public int Count;
            public int SentCount;
            public long Order;

            public LoggedErrorDto ToDto()
            {
                return new LoggedErrorDto
                {
                    Type = Type,
                    Message = Message,
                    StackHead = StackHead,
                    FirstFrame = FirstFrame,
                    LastFrame = LastFrame,
                    Count = Count,
                    Truncated = Truncated
                };
            }
        }
    }
}
