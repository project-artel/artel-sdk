using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Artel
{
    internal sealed class OrderedWebSocketWrites
    {
        private const int MaxQueuedMessages = 64;
        // Pulse가 밀릴 때도 같은 큐에 액션 응답을 넣을 자리를 남긴다.
        private const int PulseQueueLimit = 16;

        private readonly object gate = new object();
        private readonly Queue<IEnumerable<string>> pending = new Queue<IEnumerable<string>>();
        private readonly Func<string, Task<bool>> write;
        private readonly Action onFailure;
        private bool draining;
        private bool closed;

        public OrderedWebSocketWrites(Func<string, Task<bool>> write, Action onFailure)
        {
            this.write = write;
            this.onFailure = onFailure;
        }

        public bool TryEnqueue(IEnumerable<string> frames, bool isPulse)
        {
            lock (gate)
            {
                if (closed || pending.Count >= (isPulse ? PulseQueueLimit : MaxQueuedMessages))
                    return false;

                pending.Enqueue(frames);
                if (!draining)
                {
                    draining = true;
                    _ = Task.Run(Drain);
                }
                return true;
            }
        }

        public void Close()
        {
            lock (gate)
            {
                closed = true;
                pending.Clear();
            }
        }

        private async Task Drain()
        {
            try
            {
                while (true)
                {
                    IEnumerable<string> frames;
                    lock (gate)
                    {
                        if (pending.Count == 0)
                        {
                            draining = false;
                            return;
                        }
                        frames = pending.Dequeue();
                    }

                    foreach (var frame in frames)
                    {
                        lock (gate)
                        {
                            if (closed) return;
                        }
                        if (!await write(frame).ConfigureAwait(false))
                            throw new InvalidOperationException("The Artel WebSocket write did not complete.");
                    }
                }
            }
            catch (Exception)
            {
                lock (gate)
                {
                    if (closed) return;
                    closed = true;
                    pending.Clear();
                }
                onFailure();
            }
        }
    }
}
