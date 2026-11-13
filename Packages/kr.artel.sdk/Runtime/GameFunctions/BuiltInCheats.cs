using System;
using System.Collections.Generic;

namespace Artel
{
    internal static class BuiltInCheats
    {
        public const string ResetGame = "reset_game";

        private static readonly Dictionary<string, GameFunctionParam[]> Parameters =
            new Dictionary<string, GameFunctionParam[]>(StringComparer.Ordinal)
            {
                { ResetGame, Array.Empty<GameFunctionParam>() }
            };

        public static bool TryGetParameters(string name, out GameFunctionParam[] parameters)
        {
            parameters = null;
            return name != null && Parameters.TryGetValue(name, out parameters);
        }
    }
}
