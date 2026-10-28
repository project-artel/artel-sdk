using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Artel.Affordances.Scan;
using UnityEngine;

namespace Artel.Affordances.Live
{
    /// <summary>
    /// 엔진 컴포넌트가 지금 어떤 상태인지. 어느 애니메이션인지, 소리가 나는지, 무엇을 그리는지.
    /// </summary>
    /// <remarks>
    /// 게임 코드의 어느 필드도 쥐지 않는 상태다. 애니메이터의 현재 state 와 오디오의 재생 여부는 엔진 안에 있고, 게임은
    /// 그것을 묻지 않으므로 분석이 뽑을 멤버가 없다. 화면 녹화가 보여 주는 절반이고, 녹화가 줄 수 없는 절반 — 이름 — 을
    /// 여기서 싣는다.
    ///
    /// 연속값은 싣지 않는다. <c>normalizedTime</c> 은 매 프레임 바뀌어 모든 판독을 변화로 만든다. 상태의 이름과 켜짐만.
    /// 객체 항목에 <c>engine</c> 하나로 싣고 장부가 통째로 비교한다.
    /// </remarks>
    internal static class EngineState
    {
        /// <summary>엔진 모듈 참조 없이 이름으로 읽는 것. ParticleSystem 모듈은 모든 프로젝트에 있지 않다.</summary>
        private const string Particles = "UnityEngine.ParticleSystem";

        private static readonly Dictionary<Type, PropertyInfo> ParticlesPlaying = new Dictionary<Type, PropertyInfo>();

        private static readonly List<Component> Holding = new List<Component>();

        /// <summary>이것을 가진 객체는 상한 안에서 <see cref="Worth"/> 가 넣는다. Renderer 는 아니다 — 배경 전부가 그것이다.</summary>
        internal static bool CanCarry(GameObject subject)
        {
            if (subject == null)
            {
                return false;
            }

            try
            {
                subject.GetComponents(Holding);
            }
            catch (Exception)
            {
                Holding.Clear();
                return false;
            }

            var answer = false;

            foreach (var component in Holding)
            {
                if (component is Animator || component is AudioSource ||
                    (component != null && UiState.InFamily(component.GetType(), Particles)))
                {
                    answer = true;
                    break;
                }
            }

            Holding.Clear();
            return answer;
        }

        /// <summary><c>,"engine":{...}</c> 조각, 또는 이 객체에 엔진 상태가 없으면 null.</summary>
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

            StringBuilder text = null;

            foreach (var component in Holding)
            {
                if (component == null)
                {
                    continue;
                }

                switch (component)
                {
                    case Animator animator:
                        Key(ref text, "anim");
                        Playing(text, animator);
                        continue;

                    case AudioSource audio:
                        Key(ref text, "audio");
                        text.Append("{\"playing\":").Append(audio.isPlaying ? "true" : "false").Append(",\"clip\":");
                        Json.String(text, audio.clip == null ? null : audio.clip.name);
                        text.Append('}');
                        continue;

                    case SpriteRenderer sprite:
                        Key(ref text, "sprite");
                        text.Append('{');
                        Json.Property(text, "name", sprite.sprite == null ? null : sprite.sprite.name);
                        text.Append(",\"alpha\":");
                        UiState.Value(text, (float)Math.Round(sprite.color.a, 2));
                        text.Append(",\"enabled\":").Append(sprite.enabled ? "true" : "false");
                        text.Append('}');
                        continue;

                    case Renderer renderer:
                        Key(ref text, "renderer");
                        text.Append("{\"enabled\":").Append(renderer.enabled ? "true" : "false").Append('}');
                        continue;
                }

                var playing = ParticlesPlayingOf(component.GetType());

                if (playing == null)
                {
                    continue;
                }

                bool? isPlaying = null;

                try
                {
                    isPlaying = playing.GetValue(component) as bool?;
                }
                catch (Exception)
                {
                    // 던지는 속성 하나는 건너뛸 컴포넌트다.
                }

                if (isPlaying.HasValue)
                {
                    Key(ref text, "particles");
                    text.Append("{\"playing\":").Append(isPlaying.Value ? "true" : "false").Append('}');
                }
            }

            Holding.Clear();

            if (text == null)
            {
                return null;
            }

            text.Append('}');
            return text.ToString();
        }

        private static void Key(ref StringBuilder text, string key)
        {
            if (text == null)
            {
                text = new StringBuilder(96);
                text.Append(",\"engine\":{");
            }
            else
            {
                text.Append(',');
            }

            Json.String(text, key);
            text.Append(':');
        }

        private static void Playing(StringBuilder text, Animator animator)
        {
            AnimatorStateInfo state;

            try
            {
                state = animator.GetCurrentAnimatorStateInfo(0);
            }
            catch (Exception exception)
            {
                text.Append('{');
                Json.Property(text, "unread", exception.GetType().Name);
                text.Append('}');
                return;
            }

            text.Append("{\"stateHash\":").Append(state.shortNameHash.ToString(System.Globalization.CultureInfo.InvariantCulture));

            foreach (var name in WatchList.AnimatorNames)
            {
                if (!state.IsName(name))
                {
                    continue;
                }

                text.Append(',');
                Json.Property(text, "state", name);
                break;
            }

            text.Append('}');
        }

        private static PropertyInfo ParticlesPlayingOf(Type type)
        {
            if (ParticlesPlaying.TryGetValue(type, out var already))
            {
                return already;
            }

            PropertyInfo property = null;

            if (UiState.InFamily(type, Particles))
            {
                try
                {
                    property = type.GetProperty("isPlaying", BindingFlags.Public | BindingFlags.Instance);
                }
                catch (Exception)
                {
                    property = null;
                }
            }

            ParticlesPlaying[type] = property;
            return property;
        }

        internal static void Forget()
        {
            ParticlesPlaying.Clear();
        }
    }
}
