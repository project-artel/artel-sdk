using System.Collections.Generic;
using Artel.Protocol;

namespace Artel.Protocol.Dto
{
    internal sealed class ConfigureInputRequest
    {
        internal string Mode { get; private set; }

        internal static bool TryParse(List<object> parameters, out ConfigureInputRequest request)
        {
            request = null;
            if (parameters == null || parameters.Count != 1 ||
                !ActionParamsObject.TryRead(parameters[0], out var fields) ||
                !fields.TryGetValue("mode", out var value) || !(value is string mode) ||
                (mode != "virtual" && mode != "os")) return false;
            request = new ConfigureInputRequest { Mode = mode };
            return true;
        }
    }
}
