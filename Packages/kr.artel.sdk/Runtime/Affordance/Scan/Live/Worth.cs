using System;
using System.Collections.Generic;
using Artel.Affordances.Scan;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// 테스트가 작용할 수 있는 객체인지 — 리포트의 규칙을, 초당 열 번 묻는다.
    /// </summary>
    /// <remarks>
    /// 규칙은 스캔의 것이고 새로 만드는 대신 여기 복사한다: 객체는 그 컴포넌트 중 하나가 구워진 근거를 나르거나 인스펙터로
    /// 연결된 호출을 가질 때 센다. 그것이 리포트의 목록을 천이 아니라 마흔셋으로 만드는 것이고, 거기에
    /// <c>Canvas/ExitButton</c> 을 넣는 것이다 — <c>onClick</c> 이 메서드를 가리키는 Button.
    ///
    /// 같은 규칙이어야 한다. 명세는 리포트 자신의 순회에서 쓰였으므로, 선을 다른 데 긋는 판독은 패키지가 답할 수 있는 줄을
    /// 확인 불가로 보고하거나 판독을 배경으로 가득 채운다. 둘이 달라도 되는 자리가 하나 있는데, 그것도 한 방향으로만이다:
    /// 감시 대상 멤버를 쥔 객체는 다른 무엇도 자격이 없더라도 쓴다. 아무도 찾을 수 없는 값이 배경 한 줄보다 나쁘기
    /// 때문이다.
    ///
    /// 객체마다 한 번 답하고 기억한다. 컴포넌트의 UnityEvent 필드를 읽는 일은 리플렉션이고, 스캔은 씬마다 한 번 치르는
    /// 값을 이쪽은 매 박자마다 치르게 된다. 타입이 아니라 객체에 대고 기억한다: 한 타입의 Button 둘은 서로 다르게
    /// 연결돼 있고, 그중 하나는 아무것도 가리키지 않을 수 있다.
    ///
    /// <b>기억하므로 조건은 전부 객체에 붙박이여야 한다.</b> 구워진 근거도, 인스펙터 연결도, 감시 대상 멤버도, 무슨
    /// 컴포넌트를 달았는지도 실행 중에 변하지 않는다. 지금 무슨 값을 쥐고 있는지를 여기서 물으면 그 순간의 답이 영영
    /// 굳는다 — 대사창이 뜨는 프레임에 비어 있다는 이유로 그 실행 내내 판독에 못 들어왔다(ARTEL-840). 값이 변하는
    /// 것을 보는 일은 매 박자 도는 판독의 몫이다. 조건을 더할 사람은 그것이 어느 쪽인지 먼저 물어야 한다.
    /// </remarks>
    internal static class Worth
    {
        /// <summary>
        /// 전부 버리고 다시 알아내기 전까지 답을 몇 개나 쥐고 있는지.
        /// </summary>
        /// <remarks>
        /// 한 시간 동안 만들고 부수는 게임은 그러지 않으면 여태 만든 객체마다 여기에 줄 하나씩을 늘린다. 전부 버리는 값은
        /// 비싼 순회 한 번이고 틀린 답을 줄 수는 없는데, 대안이 누수일 때 택할 거래가 그것이다.
        /// </remarks>
        private const int MaxRemembered = 4096;

        /// <summary>
        /// "아니오" 를 몇 초 만에 다시 묻는지.
        /// </summary>
        /// <remarks>
        /// 컴포넌트는 런타임에 붙는다. 붙기 전에 물어 받은 "아니오" 를 영원히 쥐면 그 객체는 무엇을
        /// 달든 판독에 못 들어온다 — 대사창이 그것으로 빠졌다(ARTEL-840). 그래서 부정만 만료시킨다.
        ///
        /// 박자가 0.1 초이니 열 박자에 한 번 다시 묻는다. 그 사이에는 최대 이만큼 늦게 들어온다.
        /// </remarks>
        private const float StaleDenial = 1f;

        /// <summary>
        /// 언제 답했는지와 함께 기억한다. 긍정에는 시각이 필요 없다.
        /// </summary>
        private static readonly Dictionary<int, bool> Answered = new Dictionary<int, bool>();

        private static readonly Dictionary<int, float> Denied = new Dictionary<int, float>();

        internal static bool Writing(GameObject subject, Dictionary<Type, List<Watched>> byOwner)
        {
            if (subject == null)
            {
                return false;
            }

            var id = subject.GetInstanceID();

            if (Answered.TryGetValue(id, out var already))
            {
                // 긍정은 그대로 쥔다. 뒤집어서 객체를 판독에서 빼면 `LiveState.Gone` 이 그것을
                // 사라졌다고 보고한다 — 읽는 쪽은 살아 있는 객체를 지운다. 볼 것이 없어진 객체를
                // 계속 보는 값은 그것보다 싸다.
                if (already)
                {
                    return true;
                }

                if (Denied.TryGetValue(id, out var when) && Time.unscaledTime - when < StaleDenial)
                {
                    return false;
                }
            }

            if (Answered.Count >= MaxRemembered)
            {
                Answered.Clear();
                Denied.Clear();
            }

            var answer = Ask(subject, byOwner);
            Answered[id] = answer;

            if (answer)
            {
                Denied.Remove(id);
            }
            else
            {
                Denied[id] = Time.unscaledTime;
            }

            return answer;
        }

        private static bool Ask(GameObject subject, Dictionary<Type, List<Watched>> byOwner)
        {
            Component[] components;

            try
            {
                components = subject.GetComponents<Component>();
            }
            catch (Exception)
            {
                // 스캔은 이것을 씬에 대한 공백으로 보고한다. 여기서는 그저 아무 말도 할 수 없는 객체일 뿐이다.
                return false;
            }

            // 계기는 게임이 아니다. 무엇을 들었든 보고하지 않는다 (ARTEL-698).
            //
            // 제일 먼저 묻는 것은 이 답이 나머지를 전부 무의미하게 만들기 때문이다. SDK 의 오버레이에도
            // 버튼이 있고 글자가 있어서, 순서가 뒤면 아래 조건들이 그것을 게임으로 들여보낸다.
            if (Instrument.Marks(subject))
            {
                return false;
            }

            // 글자를 띄우는 객체는 다른 무엇도 자격이 없어도 쓴다. 스캔의 규칙에서 한 방향으로 벗어나는 두 번째
            // 자리이고, 첫 번째(감시 대상 멤버를 쥔 객체)와 같은 이유다 — 아무도 찾을 수 없는 값이 배경 한 줄보다
            // 나쁘다. 라벨은 근거를 굽지도 인스펙터로 무엇을 부르지도 않아 셋 중 어느 조건에도 걸리지 않는데,
            // 명세가 묻는 것은 그 라벨에 적힌 글자인 경우가 흔하다.
            //
            // 지금 띄우고 있는지가 아니라 띄울 수 있는지를 묻는다. 이 답은 기억되므로, 그 순간의 값을 보고 답하면
            // 그때 비어 있던 라벨이 영영 들어오지 못한다 — 대사창이 그랬다(ARTEL-840). 빈 값을 싣지 않는 일은
            // 매 박자 도는 `LiveState.Said` 가 이미 한다.
            //
            // 컴포넌트 순회보다 먼저 묻는 것은 이 답이 그 순회를 통째로 건너뛰게 하기 때문이다.
            if (Legible.CanCarry(subject))
            {
                return true;
            }

            var calls = new List<PersistentCall>();

            foreach (var component in components)
            {
                if (component == null)
                {
                    continue;
                }

                var type = component.GetType();

                if (byOwner.ContainsKey(type) || AffordanceCatalog.For(type) != null)
                {
                    return true;
                }

                calls.Clear();

                try
                {
                    PersistentCallReader.Read(component, calls);
                }
                catch (Exception)
                {
                    continue;
                }

                if (calls.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void Forget()
        {
            Answered.Clear();
            Denied.Clear();
            Legible.Forget();
        }
    }
}
