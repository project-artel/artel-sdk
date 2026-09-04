namespace Artel
{
    /// <summary>
    /// <c>-artel-window-label</c> 로 받은 값을 프로세스가 사는 동안만 들고 있는다 (ARTEL-826).
    /// </summary>
    /// <remarks>
    /// <c>ArtelManager.InstallLaunchSession</c> 이 첫 씬이 열리기 전에 이 값을 채우고,
    /// <see cref="ArtelOverlayController"/> 는 그보다 한참 뒤, 매니저가 붙을 때 <c>CreateGui</c>
    /// 에서 읽는다. 둘 사이를 잇는 자리가 필요해서 정적 필드에 둔다 —
    /// <c>InstallLaunchSession</c> 과 마찬가지로 한 프로세스에서 한 번만, 어떤 <c>Awake</c>
    /// 보다도 먼저 도는 훅이 쓰므로 두 번째로 쓰는 사람도 경쟁하는 사람도 없다.
    ///
    /// <c>ArtelSdkSession</c> 이나 <c>PlayerPrefs</c> 에 넣지 않는 이유는 그 둘이 프로세스가
    /// 끝난 뒤에도 남는 저장소이기 때문이다. 이 라벨은 이번 한 번의 실행이 어떤 테스트로
    /// 떴는지를 말할 뿐이다. 저장소에 넣으면 라벨 인자 없이 뜬 다음 실행에도 화면에 남아,
    /// 실제로는 다른 테스트로 뜬 창이 지난 실행의 라벨을 계속 보여 준다.
    /// <see cref="ArtelSdkIdentity"/> 와 <see cref="ArtelOwnedPlayerPrefs"/> 가 지키는 값은
    /// 설치본의 정체나 로그인 세션이라 프로세스를 넘어 남아야 하는 값이고, 이 라벨과는
    /// 수명이 다르다.
    /// </remarks>
    internal static class ArtelWindowLabel
    {
        /// <summary>이번 실행이 받은 라벨. 인자가 없었으면 null.</summary>
        public static string Value { get; set; }
    }
}
