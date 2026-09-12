using System;
using System.Collections.Generic;
using Firebase.Analytics;

namespace FunRabbit
{
    public sealed class AnalyticsEventQueue
    {
        sealed class Entry { public string name, onceKey; public Parameter[] parameters; public bool once; }
        readonly Queue<Entry> _pending = new Queue<Entry>();
        readonly HashSet<string> _oncePending = new HashSet<string>();
        readonly Func<string, bool> _wasSent;
        readonly Action<string> _markSent;
        readonly Action<string, Parameter[]> _send;
        readonly int _capacity;
        bool _flushing;
        public int Count => _pending.Count;

        public AnalyticsEventQueue(Func<string, bool> wasSent, Action<string> markSent,
            Action<string, Parameter[]> send, int capacity = 512)
        {
            _wasSent = wasSent; _markSent = markSent; _send = send; _capacity = capacity;
        }

        public bool Enqueue(string name, Parameter[] parameters, bool once = false, string onceKey = null)
        {
            if (string.IsNullOrEmpty(name) || _pending.Count >= _capacity) return false;
            onceKey = onceKey ?? name;
            if (once && (_wasSent(onceKey) || !_oncePending.Add(onceKey))) return false;
            _pending.Enqueue(new Entry { name = name, onceKey = onceKey, parameters = parameters ?? Array.Empty<Parameter>(), once = once });
            return true;
        }

        public void Flush()
        {
            if (_flushing) return;
            _flushing = true;
            try
            {
                while (_pending.Count > 0)
                {
                    var entry = _pending.Peek();
                    _send(entry.name, entry.parameters);
                    if (entry.once) _markSent(entry.onceKey);
                    _pending.Dequeue();
                    if (entry.once) _oncePending.Remove(entry.onceKey);
                }
            }
            finally { _flushing = false; }
        }
    }
}
