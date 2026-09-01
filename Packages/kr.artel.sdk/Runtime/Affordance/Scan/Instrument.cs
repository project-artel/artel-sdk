using UnityEngine;

namespace Artel.Affordances.Scan
{
    /// <summary>
    /// 이 객체와 그 아래는 계기다. 게임이 아니다.
    /// </summary>
    /// <remarks>
    /// 판독은 게임을 보고해야 하는데, SDK 가 화면에 띄우는 것들이 게임인 척 섞여 들어왔다. 스테이지 런의 렌더에서 객체를
    /// 세면 <c>Artel Keyboard Status Canvas</c> 아래가 48줄로 1등이었고, 게임에서 가장 많은
    /// <c>Card(Clone)</c> 25줄의 두 배였다. 같은 창에서 게임의 글자는 <c>Word Venture</c> 하나였다(ARTEL-698).
    ///
    /// 셋 다 나쁘다. 에이전트가 <c>PRESSED KEYS</c> 를 게임 UI 로 읽을 수 있고, 포인터 좌표가 마우스를 따라 매 판독
    /// 바뀌어 그 씬은 <c>settled</c> 가 영영 뜨지 않으며, 보자고 넣은 대사가 계기 표시에 묻힌다.
    ///
    /// <b>루트에 <c>hideFlags</c> 를 다는 것으로는 안 된다.</b> <c>Artel Pulse</c> 와 <c>Artel Scene Walk</c> 는 자기
    /// 루트를 만들어 그 표시를 달고, 걷기가 루트 단위로 그것을 건너뛴다. 오버레이는 그렇게 못 산다 —
    /// <c>ArtelManager</c> 가 <b>게임이 놓은 오브젝트에 컴포넌트로 붙고</b> 캔버스를 그 자식으로 만들기 때문에, 루트가
    /// 게임 것이다. 샘플 게임에서는 <c>StageDataSingleton</c> 이 함께 사는 그 오브젝트다. 거기에 표시를 달면 게임 것까지
    /// 숨는다.
    ///
    /// 부모를 옮겨 자기 루트로 빼는 방법도 있지만, 지금 수명이 <c>ArtelManager</c> 의 오브젝트에 묶여 있어 옮기면 그
    /// 수명이 달라진다. 보고에서 빼자고 살고 죽는 규칙을 바꾸는 것은 값이 맞지 않는다.
    ///
    /// 이름으로 거르지도 않는다. <see cref="Live.Readable"/> 이 어셈블리를 이름으로 거르는 선례가 있지만 그쪽은 게임이
    /// 쓸 수 없는 이름이고, 오브젝트 이름은 게임이 자유롭게 쓴다.
    ///
    /// 그래서 표시를 하나 붙인다. 이 컴포넌트를 단 객체와 그 아래 전부가 보고에서 빠진다. 붙이는 쪽이 자기 것이라고
    /// 말하는 것이므로 우연히 겹칠 일이 없다.
    ///
    /// <b>화면에서는 그대로 보인다.</b> 이것은 보고의 문제이지 렌더링의 문제가 아니다 — 사람이 보라고 띄운 것들이다.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class Instrument : MonoBehaviour
    {
        /// <summary>
        /// 이 객체가 계기 안에 있는가. 자기 자신이 표시를 달았거나, 조상 중 하나가 달았으면 참이다.
        /// </summary>
        /// <remarks>
        /// 조상을 거슬러 오른다. <see cref="Live.Worth"/> 가 객체마다 한 번 묻고 답을 기억하므로, 이 값을 판독마다
        /// 치르지 않는다.
        ///
        /// <c>GetComponentInParent</c> 를 쓰지 않는 것은 그것이 꺼진 객체를 건너뛰기 때문이다. 오버레이는 꺼져 있을 수
        /// 있고, 꺼진 계기도 계기다 — 켜질 때 갑자기 게임으로 보고되면 그것이 더 나쁘다.
        /// </remarks>
        internal static bool Marks(GameObject subject)
        {
            if (subject == null)
            {
                return false;
            }

            for (var walking = subject.transform; walking != null; walking = walking.parent)
            {
                if (walking.GetComponent<Instrument>() != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
