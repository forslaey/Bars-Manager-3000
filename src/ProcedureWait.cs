using System;
using System.Diagnostics;
using System.Threading;

namespace ServiceTool
{
    internal enum ProcedureWaitReason { Exited, RegistrationObserved, UserStopped, TimedOut }

    internal sealed class ProcedureWaitResult
    {
        internal ProcedureWaitReason Reason;
        internal int ExitCode;
        internal bool Exited { get { return Reason == ProcedureWaitReason.Exited; } }
    }

    internal static class ProcedureWait
    {
        internal const int DefaultTimeoutMilliseconds = 120000;

        // An install/uninstall EXE can keep its console/host alive after changing SCM.
        // Observe registration as well as process exit, without killing the external process.
        internal static ProcedureWaitResult Run(Func<int, bool> waitForExit, Func<int> exitCode,
            Func<ServiceSnapshot> readCatalog, string executable, bool install, CancellationToken cancellation,
            Action<int> waiting, int timeoutMilliseconds = DefaultTimeoutMilliseconds, int pollMilliseconds = 1000)
        {
            if (timeoutMilliseconds < 1 || pollMilliseconds < 1) throw new ArgumentOutOfRangeException();
            var clock = Stopwatch.StartNew();
            int confirmations = 0, nextNotice = 15;
            while (true)
            {
                if (cancellation.IsCancellationRequested) return PendingOrExited(waitForExit, exitCode, ProcedureWaitReason.UserStopped);
                int remaining = Math.Max(1, timeoutMilliseconds - (int)Math.Min(Int32.MaxValue, clock.ElapsedMilliseconds));
                if (waitForExit(Math.Min(pollMilliseconds, remaining)))
                    return new ProcedureWaitResult { Reason = ProcedureWaitReason.Exited, ExitCode = exitCode() };
                if (cancellation.IsCancellationRequested) return PendingOrExited(waitForExit, exitCode, ProcedureWaitReason.UserStopped);
                try
                {
                    ServiceSnapshot snapshot = readCatalog();
                    bool observed = false;
                    if (snapshot != null && snapshot.Entries != null)
                    {
                        int matches = ServiceCatalog.FindMatches(snapshot.Entries, executable).Count;
                        observed = install ? matches > 0 : snapshot.Complete && matches == 0;
                    }
                    confirmations = observed ? confirmations + 1 : 0;
                }
                catch (Exception) { confirmations = 0; } // A read failure is never evidence of removal.
                if (confirmations >= 2) return PendingOrExited(waitForExit, exitCode, ProcedureWaitReason.RegistrationObserved);
                if (clock.ElapsedMilliseconds >= timeoutMilliseconds) return PendingOrExited(waitForExit, exitCode, ProcedureWaitReason.TimedOut);
                int seconds = (int)(clock.ElapsedMilliseconds / 1000);
                if (seconds >= nextNotice)
                {
                    if (waiting != null) waiting(seconds);
                    nextNotice = seconds + 15;
                }
            }
        }

        private static ProcedureWaitResult Result(ProcedureWaitReason reason)
        { return new ProcedureWaitResult { Reason = reason, ExitCode = -1 }; }

        private static ProcedureWaitResult PendingOrExited(Func<int, bool> wait, Func<int> code, ProcedureWaitReason reason)
        { return wait(0) ? new ProcedureWaitResult { Reason = ProcedureWaitReason.Exited, ExitCode = code() } : Result(reason); }
    }
}
