using System;
using System.Threading;
using System.Threading.Tasks;

namespace ServiceTool
{
    internal static class WindowsOperationQueue
    {
        private static string detachedOwner;
        internal static string WaitingMessage
        {
            get
            {
                string owner = Volatile.Read(ref detachedOwner);
                return owner == null ? "Ожидаем завершения другой системной операции…" : "Очередь удерживает процедура службы " + owner + "…";
            }
        }
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);

        internal static bool TryEnter(out IDisposable lease)
        {
            lease = Gate.Wait(0) ? new Lease() : null;
            return lease != null;
        }

        internal static async Task<IDisposable> EnterAsync(Action waiting, Func<bool> cancelled = null)
        {
            CheckCancellation(cancelled);
            if (!Gate.Wait(0))
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                string message = WaitingMessage;
                waiting();
                while (!await Gate.WaitAsync(100))
                {
                    CheckCancellation(cancelled);
                    string current = WaitingMessage;
                    if (current != message) { message = current; waiting(); }
                    if (clock.ElapsedMilliseconds >= 120000)
                        throw new TimeoutException(message + " Новая операция не запущена. Проверь текущую процедуру и повтори.");
                }
            }

            if (cancelled != null && cancelled())
            {
                Gate.Release();
                throw new OperationCanceledException();
            }
            return new Lease();
        }

        internal static void ReleaseAfter(IDisposable lease, Task completion, string owner = null)
        {
            if (completion == null) lease.Dispose();
            else
            {
                Volatile.Write(ref detachedOwner, owner);
                completion.ContinueWith(task =>
                {
                    if (task.IsFaulted) System.Diagnostics.Trace.WriteLine(task.Exception);
                    Volatile.Write(ref detachedOwner, null);
                    lease.Dispose();
                }, TaskScheduler.Default);
            }
        }

        private static void CheckCancellation(Func<bool> cancelled)
        {
            if (cancelled != null && cancelled()) throw new OperationCanceledException();
        }

        private sealed class Lease : IDisposable
        {
            private int released;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref released, 1) == 0) Gate.Release();
            }
        }
    }
}
