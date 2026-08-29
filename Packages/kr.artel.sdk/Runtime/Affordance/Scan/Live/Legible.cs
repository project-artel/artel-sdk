using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// 객체가 화면에 글자를 띄우고 있는가, 그리고 그 글자가 무엇인가.
    /// </summary>
    /// <remarks>
    /// 판독에 화면의 글자가 하나도 없었다. 에이전트가 글자를 얻는 경로는 화면 캡처뿐이었고, 그래서 판독보다 그림을 믿었다 —
    /// 카드를 <c>tag=Spell</c> 이 아니라 그림에 그려진 "Fire" 로 부르고, <c>tag=MagicType</c> 카드를 <c>tag=Spell</c> 칸에
    /// 넣은 뒤 게임 결함이라고 보고했다. 스토리 씬에서는 <c>Space</c> 를 네 번 연달아 누르면서 매번 "이번엔 넘어갔나" 를
    /// 추측했다. 대사가 아직 찍히는 중인지, 다음 줄로 갔는지, 씬이 끝났는지 가릴 값이 없었다.
    ///
    /// <b>리플렉션으로 찾는 이유.</b> 이 어셈블리(<c>Artel.Affordances.Scan</c>)는 <c>UnityEngine.UI</c> 도
    /// <c>Unity.TextMeshPro</c> 도 참조하지 않는다. 참조를 더하면 TextMeshPro 가 없는 프로젝트에서 패키지가 컴파일되지
    /// 않는다 — 글자를 읽자고 설치를 강요하는 거래다. <see cref="Readable"/> 와 <c>PersistentCallReader</c> 가 이미 같은
    /// 이유로 리플렉션을 쓴다.
    ///
    /// <b>이름 목록으로 고르는 이유.</b> "<c>string text</c> 속성을 가진 컴포넌트 전부" 로 잡으면 그 이름을 전혀 다른
    /// 뜻으로 쓰는 게임 자신의 컴포넌트가 딸려 온다. 여기 적힌 것은 Unity 가 글자를 그리는 데 쓰라고 준 타입들이고,
    /// 기반 타입으로 맞추므로 <c>TextMeshProUGUI</c> 와 <c>TextMeshPro</c> 는 <c>TMP_Text</c> 한 줄이 덮는다.
    ///
    /// 입력 필드도 함께 넣는다. 거기 담긴 것은 게임이 그린 글자가 아니라 플레이어가 친 글자인데, 명세가 묻는 것이
    /// 그쪽인 경우가 흔하다 — 무엇을 입력했고 그것이 남아 있는가.
    ///
    /// 타입마다 한 번 답하고 기억한다. <see cref="Worth"/> 가 객체마다 기억하는 것과 같은 거래이고, 다만 이쪽은 답이
    /// 타입에만 달려 있어 타입에 대고 기억한다.
    /// </remarks>
    internal static class Legible
    {
        /// <summary>
        /// 글자를 몇 자까지 싣는가.
        /// </summary>
        /// <remarks>
        /// 대사 한 덩이가 이보다 긴 게임이 있고, 그 뒤를 다 실으면 판독 하나가 그 씬에서 그 문단이 된다. 자른 것은
        /// 말줄임표로 보이게 남긴다 — 조용히 자르면 독자가 그것을 문장의 끝으로 읽는다.
        ///
        /// <b>자른 뒤의 것을 장부에 넣는다.</b> 온전한 값으로 비교하고 자른 값을 보내면, 앞 <see cref="MaxSaid"/> 자가
        /// 같은 두 문자열이 "변했다" 고 보고되면서 실려 가는 내용은 그대로인 판독이 된다. 독자가 대고 비교할 것과 독자가
        /// 받는 것은 같아야 한다. 대신 그 경계를 넘어간 뒤의 변화는 보이지 않는다.
        /// </remarks>
        private const int MaxSaid = 200;

        private const string Cut = "…";

        /// <summary>
        /// 전부 버리고 다시 알아내기 전까지 타입을 몇 개나 기억하는지.
        /// </summary>
        private const int MaxRemembered = 2048;

        /// <summary>
        /// Unity 가 글자를 그리라고 준 타입들. 기반 타입으로 맞춘다.
        /// </summary>
        private static readonly string[] Families =
        {
            "UnityEngine.UI.Text",
            "UnityEngine.UI.InputField",
            "UnityEngine.TextMesh",
            "TMPro.TMP_Text",
            "TMPro.TMP_InputField"
        };

        /// <summary>
        /// 타입에서 글자를 꺼내는 속성. 그런 타입이 아니면 <c>null</c> 이고, 그 답도 기억한다.
        /// </summary>
        private static readonly Dictionary<Type, PropertyInfo> Answered =
            new Dictionary<Type, PropertyInfo>();

        /// <summary>
        /// 컴포넌트를 담아 볼 자리. 다시 쓴다.
        /// </summary>
        /// <remarks>
        /// <see cref="Worth"/> 는 객체마다 한 번 묻고 답을 기억하므로 배열을 새로 받아도 그 값을 한 번만 치른다.
        /// 이쪽은 판독마다 모든 객체에 대해 불리므로 초당 열 번씩 걷는 객체 수만큼 배열이 생긴다. 리스트를 받는
        /// 오버로드는 그것을 채워 넣기만 한다.
        ///
        /// 판독은 메인 스레드에서만 돈다. 하나를 돌려쓰는 것이 그래서 안전하다.
        /// </remarks>
        private static readonly List<Component> Holding = new List<Component>();

        /// <summary>
        /// 이 객체가 글자를 띄우고 있는가. <see cref="Worth"/> 가 순회에 넣을지 정할 때 묻는다.
        /// </summary>
        internal static bool Carries(GameObject subject)
        {
            return Of(subject) != null;
        }

        /// <summary>
        /// 이 객체가 띄우고 있는 글자. 없으면 <c>null</c>.
        /// </summary>
        /// <remarks>
        /// 빈 문자열도 <c>null</c> 로 답한다. 글자를 띄우라고 놓였지만 지금은 아무것도 안 띄운 라벨이 흔하고, 그것들을
        /// 전부 싣는 것은 판독을 빈칸으로 채우는 일이다. 대신 <b>있던 글자가 비는 것</b>은 변화로 보고되어야 하므로,
        /// 그 처리는 부르는 쪽의 장부에 맡긴다.
        ///
        /// 한 객체에 글자 컴포넌트가 둘 이상이면 처음 것만 읽는다. 그런 배치는 드물고, 둘을 이어 붙이면 어느 쪽이
        /// 어느 것인지 말할 수 없는 한 줄이 된다.
        /// </remarks>
        internal static string Of(GameObject subject)
        {
            if (subject == null)
            {
                return null;
            }

            try
            {
                subject.GetComponents(Holding);
            }
            catch (Exception)
            {
                Holding.Clear();
                return null;
            }

            foreach (var component in Holding)
            {
                if (component == null)
                {
                    continue;
                }

                var reader = ReaderFor(component.GetType());

                if (reader == null)
                {
                    continue;
                }

                string said;

                try
                {
                    said = reader.GetValue(component) as string;
                }
                catch (Exception)
                {
                    // 던지는 속성 하나는 건너뛸 컴포넌트이지 이 객체를 포기할 이유가 아니다.
                    continue;
                }

                if (string.IsNullOrEmpty(said))
                {
                    continue;
                }

                Holding.Clear();
                return said.Length <= MaxSaid ? said : said.Substring(0, MaxSaid) + Cut;
            }

            Holding.Clear();
            return null;
        }

        internal static void Forget()
        {
            Answered.Clear();
        }

        private static PropertyInfo ReaderFor(Type type)
        {
            if (Answered.TryGetValue(type, out var already))
            {
                return already;
            }

            if (Answered.Count >= MaxRemembered)
            {
                Answered.Clear();
            }

            var reader = Ask(type);
            Answered[type] = reader;
            return reader;
        }

        private static PropertyInfo Ask(Type type)
        {
            if (!InAFamily(type))
            {
                return null;
            }

            PropertyInfo reader;

            try
            {
                reader = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception)
            {
                return null;
            }

            return reader != null && reader.CanRead && reader.PropertyType == typeof(string)
                ? reader
                : null;
        }

        private static bool InAFamily(Type type)
        {
            for (var walking = type; walking != null; walking = walking.BaseType)
            {
                var name = walking.FullName;

                if (name == null)
                {
                    continue;
                }

                for (var at = 0; at < Families.Length; at++)
                {
                    if (name == Families[at])
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
