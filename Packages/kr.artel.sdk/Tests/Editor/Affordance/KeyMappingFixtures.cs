using System.Collections.Generic;
using UnityEngine;

namespace Artel.Affordances.CodeGen.Tests
{
    /// <summary>key 를 dictionary 에서 꺼내 읽는 behaviour. gyemong 의 <c>InputManager</c> 와 같은 모양이다(ARTEL-1230).</summary>
    internal sealed class KeyMappingFixtures : MonoBehaviour
    {
        internal enum Move
        {
            Up,
            Down,
            Use
        }

        private static readonly Dictionary<string, KeyCode> Named = new Dictionary<string, KeyCode>
        {
            { "pause", KeyCode.Escape }
        };

        private Dictionary<Move, KeyCode> mappings = new Dictionary<Move, KeyCode>();

        private void Awake()
        {
            mappings = new Dictionary<Move, KeyCode>
            {
                { Move.Up, KeyCode.W },
                { Move.Down, KeyCode.S }
            };

            mappings.Add(Move.Use, KeyCode.Return);
        }

        internal bool Up()
        {
            return Input.GetKeyDown(mappings[Move.Up]);
        }

        internal bool Held(Move move)
        {
            return Input.GetKey(mappings[move]);
        }

        internal bool Pause()
        {
            return Input.GetKeyUp(Named["pause"]);
        }
    }
}
