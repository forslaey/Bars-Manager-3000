using System;
using System.Diagnostics;
using System.Threading;
namespace ServiceTool
{
    // Keeps the connection alive during slow network/disk copy as well as installation.
    // Dispose is a write barrier: no queued timer callback can send after the final result.
    internal sealed class LibraryHeartbeat : IDisposable
    {
        private readonly object gate = new object();
        private readonly Action<string> send;
        private readonly Timer timer;
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        private string phase = "Подготавливаем установку";
        private bool completed;
        public LibraryHeartbeat(Action<string> callback, int interval)
        {
            send = callback;
            timer = new Timer(delegate { lock (gate) if (!completed) send(phase + ", прошло " + elapsed.Elapsed.ToString(@"hh\:mm\:ss")); }, null, interval, interval);
        }
        public void SetPhase(string text) { lock (gate) { phase = text; if (!completed) send(text); } }
        public void Dispose() { lock (gate) completed = true; timer.Dispose(); }
    }
}
