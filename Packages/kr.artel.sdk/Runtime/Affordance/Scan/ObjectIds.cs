using System.Collections.Generic;
using UnityEngine;

namespace Artel
{
    /// <summary>
    /// 객체를 가리키는 정수 id 를 얻고, 그 id 로 객체를 되찾는다.
    /// </summary>
    /// <remarks>
    /// Unity 6000.4 에서 <c>GetInstanceID</c> 와 <c>Resources.InstanceIDToObject</c> 가 deprecated 됐다.
    /// 6000.4 는 경고고 6000.5 는 <c>CS0619</c> 하드 오류다 — 한 곳만 남아도 프로젝트 전체가 컴파일되지 않으므로
    /// 게임이 6000.5 로 올라가는 순간 SDK 를 넣은 것만으로 빌드가 죽는다. 그래서 분기는 경고가 시작되는 6000.4 에
    /// 둔다. <c>GetEntityId</c> 와 <c>Resources.EntityIdToObject</c> 둘 다 6000.4 에 이미 있다.
    ///
    /// 되찾는 쪽이 어렵다. id 는 판독에 실려 나가고 <c>button_click</c> 의 <c>targetId</c> 로 돌아오므로 와이어에서는
    /// <c>int</c> 여야 하는데, Unity 는 <c>int</c> 를 <c>EntityId</c> 로 되돌리는 공인된 경로를 주지 않는다. 남은
    /// 변환 연산자는 그 자체가 deprecated 고, 곧 없앤다고 적혀 있다.
    ///
    /// 그래서 내보낸 id 를 여기서 기억한다. 기억하는 것은 <c>EntityId</c> — 값 형식이라 객체를 붙잡지 않는다.
    /// 살아 있는지는 여전히 <c>Resources.EntityIdToObject</c> 가 답하므로, 파괴된 객체는 예전처럼 <c>null</c> 로
    /// 돌아온다. <c>SceneScanner.TryGetTarget</c> 이 사전을 버린 이유(ARTEL-397)는 그 사전이 스캔마다 비워져서
    /// 스캔이 멈추면 조작 대상을 잃는 것이었다. 이 표는 비우지 않는다. 그리고 id 를 만든 적이 없으면 그 id 가
    /// 돌아올 일도 없다 — 내보낸 것만이 되돌아온다.
    ///
    /// 열쇠는 <c>EntityId</c> 의 하위 32비트다. 이것이 유일한 것은 GameObject 끼리일 때뿐이다 — 실측으로
    /// 살아있는 20만 개를 봤고 상위 워드는 하나였다. 그러나 ECS <c>Entity</c> 는 같은 <c>EntityId</c> 공간에
    /// 살면서 상위 워드가 다르고(Version), 하위 워드는 그저 Index 다. 우리는 Transform 을 훑어 GameObject 만
    /// 보고하므로 지금은 겹치지 않지만, 이것은 Unity 가 보장한 적 없는 관찰이다. 문서는 비트 배치가 버전마다
    /// 바뀔 수 있다고 적는다. 와이어가 id 를 64비트로 실으면 이 전제가 통째로 없어진다 — 그것이 진짜 고침이고
    /// 세 레포가 함께 움직여야 해서 여기서 하지 않는다.
    ///
    /// 이 어셈블리에 사는 것은 <c>Artel.Runtime</c> 이 이것을 참조하고 그 반대가 아니기 때문이다. 스캔과
    /// 판독과 <c>SceneScanner</c> 가 모두 같은 id 를 써야 하므로, 엔진을 볼 수 있는 어셈블리 중 가장 아래에
    /// 둔다. 네임스페이스가 <c>Artel</c> 인 것도 같은 이유다 — 부르는 쪽이 <c>using</c> 을 더하지 않는다.
    /// </remarks>
    internal static class ObjectIds
    {
#if UNITY_6000_4_OR_NEWER
        /// <summary>표가 이만큼 차면 죽은 것을 한 번 훑어 버린다.</summary>
        private const int Crowded = 4096;

        private static readonly Dictionary<int, EntityId> Minted = new Dictionary<int, EntityId>();
        private static readonly List<int> Dead = new List<int>();

        /// <summary>다음 청소를 시작할 크기. 청소할 때마다 뒤로 민다.</summary>
        /// <remarks>
        /// 고정 문턱이면 표가 그 크기에 닿은 뒤로는 <b>새 id 마다</b> 전체를 훑는다. 살아있는 것만 담긴 표는
        /// 아무것도 못 버리므로 다음 id 에서 또 훑는다 — 객체 하나에 표 크기만큼 네이티브 호출이 붙는다.
        /// 청소 뒤 문턱을 현재 크기의 두 배로 밀면, 훑는 값이 그 사이에 들어온 객체 수에 나뉜다.
        /// </remarks>
        private static int sweepAt = Crowded;

        public static int Of(Object subject)
        {
            var entityId = subject.GetEntityId();
            var id = entityId.GetHashCode();

            if (Minted.Count >= sweepAt && !Minted.ContainsKey(id))
            {
                Sweep();
                var doubled = Minted.Count * 2;
                sweepAt = doubled < Crowded ? Crowded : doubled;
            }

            Minted[id] = entityId;
            return id;
        }

        public static int KeyOf(Object subject)
        {
            return subject.GetEntityId().GetHashCode();
        }

        public static Object Find(int id)
        {
            return Minted.TryGetValue(id, out var entityId)
                ? Resources.EntityIdToObject(entityId)
                : null;
        }

        private static void Sweep()
        {
            Dead.Clear();

            foreach (var pair in Minted)
            {
                if (Resources.EntityIdToObject(pair.Value) == null)
                {
                    Dead.Add(pair.Key);
                }
            }

            foreach (var id in Dead)
            {
                Minted.Remove(id);
            }

            Dead.Clear();
        }
#else
        public static int Of(Object subject)
        {
            return subject.GetInstanceID();
        }

        public static int KeyOf(Object subject)
        {
            return subject.GetInstanceID();
        }

        public static Object Find(int id)
        {
            return Resources.InstanceIDToObject(id);
        }
#endif
    }
}
