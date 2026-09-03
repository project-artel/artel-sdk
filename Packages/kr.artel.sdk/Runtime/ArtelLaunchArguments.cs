using System;
using System.Collections.Generic;
using System.Globalization;
using Artel.Auth;
using Artel.Domain;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// 실행 인자와 <c>ARTEL_SDK_TOKEN</c> 환경 변수가 실어 온 세션 (ARTEL-787).
    /// </summary>
    /// <remarks>
    /// 무인 실행에는 오버레이를 누를 사람이 없다. 그래서 로그인·프로젝트 선택·로그아웃을
    /// 프로세스가 뜰 때 인자로 받는다. <c>ArtelManager.InstallLaunchSession</c> 이 첫 씬이
    /// 열리기 전에, 그러니까 어떤 매니저의 <c>Awake</c> 보다도 먼저 이 값을 세션에 넣는다.
    /// 서버 주소만 <c>ArtelManager.SpawnInDevelopmentBuilds</c> 가 자기가 띄우는 매니저에
    /// 적용한다 — 씬이 들고 온 매니저는 자기 <c>Server</c> 설정이 이긴다.
    ///
    /// 토큰만 환경 변수로 받는다. 실행 인자는 같은 기계의 다른 사용자가 프로세스 목록에서
    /// 그대로 읽으므로, 명령행에 실은 토큰은 곧 새어 나간 토큰이다. 같은 이유로 잘못된 값을
    /// 적는 오류 문구에도 토큰 원문은 넣지 않는다.
    ///
    /// <see cref="Parse"/> 는 Unity API 를 하나도 부르지 않는다. 읽는 것과 저장소에 쓰는 것을
    /// 갈라 두어야 파싱이 player 없이 EditMode 에서 돈다.
    /// </remarks>
    internal sealed class ArtelLaunchArguments
    {
        public const string TokenEnvironmentVariable = "ARTEL_SDK_TOKEN";

        public const string ServerArgument = "-artel-server";
        public const string SecureArgument = "-artel-secure";
        public const string FrontendArgument = "-artel-frontend";
        public const string ProjectArgument = "-artel-project";
        public const string LogoutArgument = "-artel-logout";

        /// <summary>
        /// 실행 인자로 받은 토큰의 만료 시각으로 저장하는 값. 빈 문자열이다.
        /// </summary>
        /// <remarks>
        /// 인자를 주는 쪽은 만료 시각을 모른다 — 토큰 하나만 건네받아 환경 변수에 실을 뿐이다.
        /// <see cref="ArtelSdkSession.TryLoadToken"/> 은 저장된 문자열을 읽지 못하면 만료로
        /// 치지 않으므로, 빈 문자열은 "만료 시각을 모른다"로 남고 세션은 지워지지 않는다.
        ///
        /// 날짜를 지어내는 쪽은 둘 다 나쁘다. 가까운 날짜를 적으면 아직 살아 있는 토큰을
        /// SDK 가 스스로 버려서 실행 중인 QA 런이 끊기고, 먼 미래를 적으면 PlayerPrefs 에
        /// 거짓이 남는다. 이미 만료된 토큰은 서버가 401 로 돌려주고 오버레이가 그때 로그인을
        /// 묻는다. <c>WebSocketTransportTests</c> 도 같은 자리에 빈 문자열을 넣는다.
        /// </remarks>
        private const string UnknownExpiresAt = "";

        /// <summary>
        /// 실행 인자로 받은 세션의 표시 이름. 환경 변수 이름을 그대로 쓴다.
        /// </summary>
        /// <remarks>
        /// 인자를 주는 쪽은 토큰이 누구 것인지 모르고, SDK 는 알아내려고 토큰을 뜯어보지
        /// 않는다. 빈 문자열로 두면 오버레이가 <c>AccountLabel</c> 에서 "완료" 를 그려
        /// 브라우저로 로그인한 세션과 구별되지 않는다. 환경 변수 이름을 적어 두면 화면과
        /// 로그를 보는 사람이 이 세션이 어디서 왔는지 바로 안다.
        /// </remarks>
        private const string TokenSourceDisplayName = TokenEnvironmentVariable;

        private readonly List<string> errors = new List<string>();

        private ArtelLaunchArguments()
        {
        }

        /// <summary><c>-artel-logout</c> 이 있었는가.</summary>
        public bool ClearsSession { get; private set; }

        /// <summary><c>ARTEL_SDK_TOKEN</c> 이 실어 온 SDK 토큰. 없으면 null.</summary>
        public string Token { get; private set; }

        /// <summary><c>-artel-project</c> 가 고른 프로젝트 id. 없으면 null.</summary>
        public string ProjectId { get; private set; }

        /// <summary><c>-artel-secure</c> 가 정한 값. 없으면 null 이고 <see cref="Server"/> 기본값이 남는다.</summary>
        public bool? Secure { get; private set; }

        /// <summary><c>-artel-server</c> 의 host. 없거나 값이 잘못됐으면 null.</summary>
        public string Host { get; private set; }

        /// <summary><c>-artel-server</c> 의 port. 없거나 값이 잘못됐으면 null.</summary>
        public int? Port { get; private set; }

        /// <summary><c>-artel-frontend</c> 가 정한 로그인 중계 페이지 주소. 없으면 null.</summary>
        public string FrontendOrigin { get; private set; }

        /// <summary>잘못된 값에 대해 남길 문구. 인자가 모두 성했으면 비어 있다.</summary>
        public IReadOnlyList<string> Errors
        {
            get { return errors; }
        }

        public static ArtelLaunchArguments ReadFromProcess()
        {
            return Parse(
                Environment.GetCommandLineArgs(),
                Environment.GetEnvironmentVariable(TokenEnvironmentVariable));
        }

        /// <summary>
        /// 인자 목록과 환경 변수 값을 읽는다. 잘못된 값은 <see cref="Errors"/> 에 쌓고 그 항목만
        /// 비워 둔다 — 하나가 잘못됐다고 나머지까지 버리면 무엇이 적용됐는지 더 알기 어렵다.
        /// </summary>
        /// <param name="commandLineArguments">
        /// <c>Environment.GetCommandLineArgs()</c> 가 주는 그대로. 첫 항목인 실행 파일 경로는
        /// 어느 인자와도 맞지 않으므로 따로 떼어내지 않는다.
        /// </param>
        /// <param name="token">환경 변수 값. 변수가 없으면 null.</param>
        public static ArtelLaunchArguments Parse(IReadOnlyList<string> commandLineArguments, string token)
        {
            var parsed = new ArtelLaunchArguments();
            parsed.ReadToken(token);

            if (commandLineArguments == null)
            {
                return parsed;
            }

            for (var index = 0; index < commandLineArguments.Count; index++)
            {
                var argument = commandLineArguments[index];
                if (string.IsNullOrEmpty(argument))
                {
                    continue;
                }

                if (Matches(argument, LogoutArgument))
                {
                    parsed.ClearsSession = true;
                    continue;
                }

                if (Matches(argument, ServerArgument))
                {
                    if (parsed.TryTakeValue(commandLineArguments, ServerArgument, ref index, out var value))
                    {
                        parsed.ReadServer(value);
                    }

                    continue;
                }

                if (Matches(argument, SecureArgument))
                {
                    if (parsed.TryTakeValue(commandLineArguments, SecureArgument, ref index, out var value))
                    {
                        parsed.ReadSecure(value);
                    }

                    continue;
                }

                if (Matches(argument, FrontendArgument))
                {
                    if (parsed.TryTakeValue(commandLineArguments, FrontendArgument, ref index, out var value))
                    {
                        parsed.ReadFrontend(value);
                    }

                    continue;
                }

                if (Matches(argument, ProjectArgument))
                {
                    if (parsed.TryTakeValue(commandLineArguments, ProjectArgument, ref index, out var value))
                    {
                        parsed.ProjectId = value;
                    }
                }
            }

            return parsed;
        }

        /// <summary>
        /// 잘못된 값을 <c>Debug.LogError</c> 로 남긴다. 무인 실행에서는 이 로그가 유일한 단서다.
        /// </summary>
        public void LogErrors()
        {
            for (var index = 0; index < errors.Count; index++)
            {
                Debug.LogError("[Artel] " + errors[index]);
            }
        }

        /// <summary>
        /// 받은 값을 세션에 넣는다. 인자가 하나도 없으면 아무것도 쓰지 않는다.
        /// </summary>
        /// <remarks>
        /// <c>-artel-logout</c> 이 가장 먼저다. 지우고 나서 채우는 순서라야 한 번의 실행으로
        /// 계정을 바꿀 수 있다. 뒤로 밀면 <see cref="ArtelSdkSession.Clear"/> 가 방금 넣은
        /// 토큰과 프로젝트를 도로 지운다.
        ///
        /// 저장은 <see cref="ArtelSdkSession"/> 만 거친다. PlayerPrefs 나 보안 저장소에 직접
        /// 쓰면 오버레이가 쓰는 길과 두 갈래가 되고, 키 하나가 어긋나도 드러나지 않는다.
        /// </remarks>
        public void InstallSession()
        {
            if (ClearsSession)
            {
                ArtelSdkSession.Clear();
            }

            if (!string.IsNullOrWhiteSpace(Token))
            {
                ArtelSdkSession.SaveToken(Token, UnknownExpiresAt, TokenSourceDisplayName);
            }

            if (!string.IsNullOrWhiteSpace(ProjectId))
            {
                ArtelSdkSession.SaveProjectId(ProjectId);
            }
        }

        /// <summary>
        /// 이 실행이 세션을 지우러 온 것뿐인가. 지우기만 하고 새 세션을 넣지 않을 때 참이다.
        /// </summary>
        /// <remarks>
        /// 참이면 게임은 지운 뒤 스스로 끝나야 한다. 그것이 <c>-artel-logout</c> 하나만 준
        /// 실행이 요구하는 전부이고, 끝나지 않으면 부르는 쪽은 게임이 언제 다 지웠는지 알 수 없다 —
        /// 로그아웃이 지워졌는지 아닌지 모르는 채로 시간이 다 가기를 기다리게 된다.
        ///
        /// 토큰이나 프로젝트가 함께 온 실행은 계정을 바꾸러 온 것이므로 끝내지 않는다.
        /// </remarks>
        public bool ClearsSessionOnly
        {
            get
            {
                return ClearsSession
                    && string.IsNullOrWhiteSpace(Token)
                    && string.IsNullOrWhiteSpace(ProjectId);
            }
        }

        /// <summary>받은 값만 <paramref name="server"/> 에 덮어쓴다. 나머지는 기본값이 남는다.</summary>
        public void ConfigureServer(Server server)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            server.OverrideEndpoints(Secure, Host, Port, FrontendOrigin);
        }

        // 대소문자를 가리지 않는다. 인자 이름이 조금 달라 조용히 무시되는 것이 이 기능의 가장
        // 나쁜 실패다 — 무인 실행은 아무 로그도 없이 오버레이 앞에서 멈춘다.
        private static bool Matches(string argument, string name)
        {
            return string.Equals(argument.Trim(), name, StringComparison.OrdinalIgnoreCase);
        }

        // 값이 `-` 로 시작하면 다음 인자를 삼키지 않는다. `-artel-project -batchmode` 는
        // 프로젝트 id 를 빠뜨린 것이지 `-batchmode` 라는 프로젝트를 고른 것이 아니다.
        private bool TryTakeValue(
            IReadOnlyList<string> arguments,
            string name,
            ref int index,
            out string value)
        {
            var valueIndex = index + 1;
            if (valueIndex >= arguments.Count
                || string.IsNullOrWhiteSpace(arguments[valueIndex])
                || arguments[valueIndex].TrimStart().StartsWith("-", StringComparison.Ordinal))
            {
                errors.Add(name + " 에 값이 없습니다.");
                value = null;
                return false;
            }

            index = valueIndex;
            value = arguments[valueIndex].Trim();
            return true;
        }

        // host 와 port 를 함께 세운다. port 가 잘못된 채로 host 만 덮어쓰면 어느 포트로 붙는지가
        // 인자와 기본값에 반씩 걸린다.
        private void ReadServer(string value)
        {
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || separator == value.Length - 1)
            {
                errors.Add(ServerArgument + " 는 host:port 형식이어야 합니다: " + value);
                return;
            }

            var portText = value.Substring(separator + 1);
            if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
            {
                errors.Add(ServerArgument + " 의 port 가 숫자가 아닙니다: " + portText);
                return;
            }

            if (parsedPort < 1 || parsedPort > 65535)
            {
                errors.Add(ServerArgument + " 의 port 는 1 과 65535 사이여야 합니다: " + parsedPort);
                return;
            }

            Host = value.Substring(0, separator);
            Port = parsedPort;
        }

        private void ReadSecure(string value)
        {
            if (!bool.TryParse(value, out var parsedSecure))
            {
                errors.Add(SecureArgument + " 는 true 나 false 여야 합니다: " + value);
                return;
            }

            Secure = parsedSecure;
        }

        // scheme 까지 본다. `home.stage.artel.kr` 처럼 scheme 없이 적은 값은 Uri 가 절대 주소로
        // 받아 주지 않고, 받아 준다 해도 로그인 중계 페이지를 열 수 없다.
        private void ReadFrontend(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsedOrigin)
                || (parsedOrigin.Scheme != Uri.UriSchemeHttp && parsedOrigin.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add(FrontendArgument + " 는 http 나 https 로 시작하는 절대 주소여야 합니다: " + value);
                return;
            }

            FrontendOrigin = value;
        }

        // 변수가 없는 것과 비어 있는 것은 다르다. 없는 것은 인자를 주지 않은 실행이고, 비어 있는
        // 것은 토큰을 실으려다 실패한 실행이다. 뒤쪽은 말해 주지 않으면 알 길이 없다.
        private void ReadToken(string token)
        {
            if (token == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                errors.Add(TokenEnvironmentVariable + " 이 비어 있습니다.");
                return;
            }

            Token = token.Trim();
        }
    }
}
