using System;
using System.Collections.Generic;
using Artel.Protocol.Dto;

namespace Artel
{
    internal interface IArtelActionDeliverySource
    {
        ActionDeliveryJournal ActionDelivery { get; }
    }

    /// <summary>같은 서버·instance의 재접속 동안 ACTION 실행과 완료 결과를 기억한다.</summary>
    internal sealed class ActionDeliveryJournal
    {
        internal const int MaximumEntries = 1024;
        private static readonly object scopeGate = new object();
        private static string currentTarget;
        private static ActionDeliveryJournal currentJournal;
        private readonly Dictionary<long, Entry> entries = new Dictionary<long, Entry>();
        private readonly int maximumEntries;
        internal string ProcessId { get; } = Guid.NewGuid().ToString();

        internal ActionDeliveryJournal(int maximumEntries = MaximumEntries)
        {
            this.maximumEntries = maximumEntries;
        }

        internal static ActionDeliveryJournal ForTarget(Uri server, string instanceId)
        {
            var target = server.GetLeftPart(UriPartial.Authority) + "/" + instanceId;
            lock (scopeGate)
            {
                if (currentJournal == null || currentTarget != target)
                {
                    currentTarget = target;
                    currentJournal = new ActionDeliveryJournal();
                }
                return currentJournal;
            }
        }

        internal Entry Register(ArtelRequestDto request, string text, long nowUnixMilliseconds,
            out bool added, out string error)
        {
            added = false;
            error = null;
            if (request.Delivery.ProcessId != ProcessId)
            {
                error = "ACTION delivery belongs to a different SDK process.";
                return null;
            }
            if (entries.TryGetValue(request.Id, out var existing))
            {
                if (existing.Text == text) return existing;
                error = "ACTION requestId was reused with a different request body.";
                return null;
            }
            if (request.Delivery.ExpiresAtUnixMs <= nowUnixMilliseconds)
            {
                error = "ACTION delivery expired before execution.";
                return null;
            }
            var expired = new List<long>();
            foreach (var pair in entries)
                if (pair.Value.Result != null && pair.Value.Request.Delivery.ExpiresAtUnixMs <= nowUnixMilliseconds)
                    expired.Add(pair.Key);
            foreach (var id in expired) entries.Remove(id);
            if (entries.Count >= maximumEntries)
            {
                error = "ACTION delivery journal is full; the request was not executed.";
                return null;
            }
            var entry = new Entry(request, text);
            entries.Add(request.Id, entry);
            added = true;
            return entry;
        }

        internal sealed class Entry
        {
            internal readonly ArtelRequestDto Request;
            internal readonly string Text;
            internal string Result { get; private set; }
            internal Entry(ArtelRequestDto request, string text)
            {
                Request = request;
                Text = text;
            }
            internal void Complete(string result)
            {
                // 전송 실패나 뒤늦은 취소가 이미 완료한 결과를 덮어쓰지 않는다.
                if (Result == null) Result = result;
            }
        }
    }
}
