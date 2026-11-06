using System;
using System.Collections.Generic;
using System.Globalization;
using Artel.Affordances.Live;
using Artel.Pointer;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// <c>wait_until</c> 의 조건. 선언형 소수만 — <c>exists(selector)</c>, <c>active(selector)</c>,
    /// <c>Type::member op literal</c>, <c>and</c>·<c>or</c>, 괄호.
    /// </summary>
    /// <remarks>
    /// 식을 받지 않는다. 게임 안에서 매 프레임 평가되는 것은 agent 가 읽을 수 있어야 하고, 되돌릴 수 있어야 하고, 게임을
    /// 바꾸지 않아야 한다. 선택지 셋에 비교 하나면 "이 값이 이렇게 될 때까지" 를 다 쓴다.
    ///
    /// 멤버 이름은 <see cref="Lookup"/> 이 한 번 풀고, 값은 매 프레임 그 자리에서 읽는다. <c>and</c> 가 <c>or</c> 보다 먼저 묶인다.
    /// </remarks>
    internal sealed class WaitCondition
    {
        private abstract class Node
        {
            internal abstract bool Holds(out string error);
        }

        private sealed class Either : Node
        {
            internal Node Left;
            internal Node Right;
            internal bool And;

            internal override bool Holds(out string error)
            {
                var left = Left.Holds(out error);

                if (error != null)
                {
                    return false;
                }

                if (And ? !left : left)
                {
                    return left;
                }

                return Right.Holds(out error);
            }
        }

        private sealed class Selected : Node
        {
            internal string Selector;
            internal bool Active;

            internal override bool Holds(out string error)
            {
                error = null;

                if (!ScenePathSelectorResolver.TryResolve(Selector, out var transform, out _) || transform == null)
                {
                    return false;
                }

                return !Active || transform.gameObject.activeInHierarchy;
            }
        }

        private sealed class Compared : Node
        {
            internal string Name;
            internal string Op;
            internal object Literal;
            internal Lookup.Answer Answer;

            internal override bool Holds(out string error)
            {
                error = null;

                if (Answer == null)
                {
                    var answers = Lookup.Resolve(new[] { Name }, new Lookup.Walk());
                    Answer = answers[0];

                    if (!Answer.Resolved)
                    {
                        error = Name + ": " + Answer.Reason;
                        return false;
                    }
                }

                var member = Answer.Member;
                object on = null;

                if (!member.Static)
                {
                    on = Holder(member);

                    if (on == null)
                    {
                        // 쥔 것이 지금 없다. 거짓이지 오류는 아니다 — 아직 안 생긴 객체를 기다리는 조건일 수 있다.
                        return false;
                    }
                }

                if (!LiveState.TryValue(member, on, out var actual, out var unread))
                {
                    error = Name + ": " + unread;
                    return false;
                }

                return Compare(actual, Op, Literal);
            }

            private static object Holder(Watched member)
            {
                if (member.Holders != null)
                {
                    var holders = Lookup.HoldersOf(member);
                    return holders.Count > 0 ? holders[0] : null;
                }

                foreach (var one in Resources.FindObjectsOfTypeAll(member.Owner))
                {
                    if (one is Component component && component != null && component.gameObject.scene.IsValid())
                    {
                        return component;
                    }
                }

                return null;
            }
        }

        private readonly Node root;

        internal string Text { get; }

        private WaitCondition(string text, Node root)
        {
            Text = text;
            this.root = root;
        }

        internal bool Holds(out string error)
        {
            return root.Holds(out error);
        }

        internal static bool TryParse(string text, out WaitCondition condition, out string error)
        {
            condition = null;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "wait_until condition is empty.";
                return false;
            }

            try
            {
                var tokens = Tokenize(text);
                var at = 0;
                var node = Or(tokens, ref at);

                if (at != tokens.Count)
                {
                    throw new FormatException("unexpected `" + tokens[at] + "`");
                }

                condition = new WaitCondition(text, node);
                return true;
            }
            catch (FormatException failure)
            {
                error = "wait_until condition: " + failure.Message + " — use exists(selector), active(selector), " +
                        "Type::member op literal, and/or, parentheses.";
                return false;
            }
        }

        // --- parser --------------------------------------------------------------------------

        private static Node Or(List<string> tokens, ref int at)
        {
            var left = And(tokens, ref at);

            while (at < tokens.Count && tokens[at] == "or")
            {
                at++;
                left = new Either { Left = left, Right = And(tokens, ref at), And = false };
            }

            return left;
        }

        private static Node And(List<string> tokens, ref int at)
        {
            var left = Term(tokens, ref at);

            while (at < tokens.Count && tokens[at] == "and")
            {
                at++;
                left = new Either { Left = left, Right = Term(tokens, ref at), And = true };
            }

            return left;
        }

        private static Node Term(List<string> tokens, ref int at)
        {
            if (at >= tokens.Count)
            {
                throw new FormatException("condition ends early");
            }

            var token = tokens[at++];

            if (token == "(")
            {
                var inner = Or(tokens, ref at);

                if (at >= tokens.Count || tokens[at] != ")")
                {
                    throw new FormatException("missing `)`");
                }

                at++;
                return inner;
            }

            if (token.StartsWith("exists(", StringComparison.Ordinal) || token.StartsWith("active(", StringComparison.Ordinal))
            {
                var open = token.IndexOf('(');
                var selector = token.Substring(open + 1, token.Length - open - 2).Trim();

                if (selector.Length == 0)
                {
                    throw new FormatException("empty selector in `" + token + "`");
                }

                return new Selected { Selector = selector, Active = token.StartsWith("active(", StringComparison.Ordinal) };
            }

            if (!token.Contains("::"))
            {
                throw new FormatException("`" + token + "` is not exists(...), active(...) or Type::member");
            }

            if (at + 1 >= tokens.Count || Array.IndexOf(Operators, tokens[at]) < 0)
            {
                throw new FormatException("`" + token + "` needs an operator and a literal");
            }

            var op = tokens[at++];
            var literal = Literal(tokens[at++]);

            return new Compared { Name = token, Op = op, Literal = literal };
        }

        private static readonly string[] Operators = { "==", "!=", "<", "<=", ">", ">=" };

        private static object Literal(string token)
        {
            if (token == "null")
            {
                return null;
            }

            if (token == "true" || token == "false")
            {
                return token == "true";
            }

            if (token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"')
            {
                return token.Substring(1, token.Length - 2);
            }

            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return number;
            }

            // 따옴표 없는 글자는 그대로 문자열이다. enum 값 이름이 흔하다.
            return token;
        }

        /// <summary>
        /// 토큰: 괄호, <c>and</c>/<c>or</c>, 연산자, 따옴표 글자, <c>exists(…)</c>/<c>active(…)</c> 는 selector 까지 한 토큰, 나머지는 공백까지.
        /// </summary>
        private static List<string> Tokenize(string text)
        {
            var tokens = new List<string>();
            var at = 0;

            while (at < text.Length)
            {
                var character = text[at];

                if (char.IsWhiteSpace(character))
                {
                    at++;
                    continue;
                }

                if (character == '(' || character == ')')
                {
                    tokens.Add(character.ToString());
                    at++;
                    continue;
                }

                if (character == '"')
                {
                    var close = text.IndexOf('"', at + 1);

                    if (close < 0)
                    {
                        throw new FormatException("unterminated string");
                    }

                    tokens.Add(text.Substring(at, close - at + 1));
                    at = close + 1;
                    continue;
                }

                if (character == '=' || character == '!' || character == '<' || character == '>')
                {
                    var two = at + 1 < text.Length && text[at + 1] == '=' ? 2 : 1;
                    var op = text.Substring(at, two);

                    if (Array.IndexOf(Operators, op) < 0)
                    {
                        throw new FormatException("unknown operator `" + op + "`");
                    }

                    tokens.Add(op);
                    at += two;
                    continue;
                }

                // selector 는 괄호·대괄호·공백을 품을 수 있어 `exists(` 부터 짝 맞는 `)` 까지를 한 토큰으로 자른다.
                if (string.CompareOrdinal(text, at, "exists(", 0, 7) == 0 || string.CompareOrdinal(text, at, "active(", 0, 7) == 0)
                {
                    var depth = 0;
                    var end = at;

                    for (; end < text.Length; end++)
                    {
                        if (text[end] == '(')
                        {
                            depth++;
                        }
                        else if (text[end] == ')' && --depth == 0)
                        {
                            break;
                        }
                    }

                    if (end >= text.Length)
                    {
                        throw new FormatException("missing `)` after " + text.Substring(at, 6));
                    }

                    tokens.Add(text.Substring(at, end - at + 1));
                    at = end + 1;
                    continue;
                }

                var start = at;

                while (at < text.Length && !char.IsWhiteSpace(text[at]) && text[at] != '(' && text[at] != ')' &&
                       text[at] != '=' && text[at] != '!' && text[at] != '<' && text[at] != '>')
                {
                    at++;
                }

                tokens.Add(text.Substring(start, at - start));
            }

            return tokens;
        }

        // --- compare --------------------------------------------------------------------------

        internal static bool Compare(object actual, string op, object literal)
        {
            if (literal == null || actual == null)
            {
                var same = actual == null && literal == null;
                return op == "==" ? same : op == "!=" && !same;
            }

            if (literal is double wanted && TryNumber(actual, out var number))
            {
                switch (op)
                {
                    case "==": return Math.Abs(number - wanted) < 1e-6;
                    case "!=": return Math.Abs(number - wanted) >= 1e-6;
                    case "<": return number < wanted;
                    case "<=": return number <= wanted;
                    case ">": return number > wanted;
                    case ">=": return number >= wanted;
                }
            }

            if (literal is bool flag && actual is bool held)
            {
                return op == "==" ? held == flag : op == "!=" && held != flag;
            }

            var left = actual is UnityEngine.Object reference ? (reference == null ? null : reference.name) : Convert.ToString(actual, CultureInfo.InvariantCulture);
            var right = Convert.ToString(literal, CultureInfo.InvariantCulture);
            var equal = string.Equals(left, right, StringComparison.Ordinal);

            return op == "==" ? equal : op == "!=" && !equal;
        }

        private static bool TryNumber(object value, out double number)
        {
            switch (value)
            {
                case int i: number = i; return true;
                case long l: number = l; return true;
                case float f: number = f; return true;
                case double d: number = d; return true;
                case short s: number = s; return true;
                case byte b: number = b; return true;
                case System.Collections.ICollection collection: number = collection.Count; return true;
            }

            number = 0;
            return false;
        }
    }
}
