using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ServiceTool
{
    internal sealed class PerformanceTrace : IDisposable
    {
        private static PerformanceTrace active;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
        private readonly System.Windows.Forms.Timer ui = new System.Windows.Forms.Timer { Interval = 100 };
        private readonly System.Threading.Timer writer;
        private readonly StreamWriter output;
        private readonly object sync = new object();
        private long previousTick;
        private bool closed;

        internal static bool Enabled { get { return active != null; } }

        internal static PerformanceTrace Start(ref string[] args)
        {
            if (!args.Contains("--profile-ui")) return null;
            args = args.Where(arg => arg != "--profile-ui").ToArray();
            try
            {
                var trace = new PerformanceTrace();
                active = trace;
                Mark("app.managed-entry");
                return trace;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private PerformanceTrace()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BARS", "Diagnostics");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "profile-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") +
                "-" + Process.GetCurrentProcess().Id + ".txt");
            output = new StreamWriter(file, false, new UTF8Encoding(true));
            output.WriteLine("BARS Manager 3000 | UTC " + DateTime.UtcNow.ToString("O") +
                " | version " + typeof(PerformanceTrace).Assembly.GetName().Version +
                " | " + Environment.OSVersion.Version + " | " + (IntPtr.Size * 8) + " bit");
            output.WriteLine("elapsed_ms | thread | event | optional_duration_ms");
            ui.Tick += delegate
            {
                long now = clock.ElapsedMilliseconds;
                long gap = now - previousTick;
                previousTick = now;
                if (gap >= 250) Record("ui.timer-gap", gap);
            };

            writer = new System.Threading.Timer(_ => Flush(), null, 1000, 1000);
        }

        internal static void StartUi() { if (active != null) active.ui.Start(); }

        internal static void Mark(string name)
        {
            var trace = active;
            if (trace != null) trace.Record(name, null);
        }

        internal static void Duration(string name, long milliseconds)
        {
            var trace = active;
            if (trace != null) trace.Record(name, milliseconds);
        }

        internal static IDisposable Begin(string name)
        {
            var trace = active;
            return trace == null ? null : new Span(trace, name);
        }

        internal static T Measure<T>(string name, Func<T> work)
        {
            using (Begin(name)) return work();
        }

        private void Record(string name, long? duration)
        {
            lines.Enqueue(String.Format(CultureInfo.InvariantCulture, "{0} | {1} | {2}{3}",
                clock.ElapsedMilliseconds, System.Threading.Thread.CurrentThread.ManagedThreadId, name,
                duration.HasValue ? " | " + duration.Value.ToString(CultureInfo.InvariantCulture) : ""));
        }

        private void Flush()
        {
            lock (sync)
            {
                if (closed) return;
                try
                {
                    string line;
                    while (lines.TryDequeue(out line)) output.WriteLine(line);
                    output.Flush();
                }
                catch (IOException) { }
            }
        }

        public void Dispose()
        {
            Mark("app.exit");
            active = null;
            ui.Dispose();
            writer.Dispose();
            lock (sync)
            {
                Flush();
                closed = true;
                try { output.Dispose(); } catch (IOException) { }
            }
        }

        private sealed class Span : IDisposable
        {
            private readonly PerformanceTrace trace;
            private readonly string name;
            private readonly long start;

            internal Span(PerformanceTrace trace, string name)
            {
                this.trace = trace;
                this.name = name;
                start = trace.clock.ElapsedMilliseconds;
                trace.Record(name + ".begin", null);
            }

            public void Dispose() { trace.Record(name, trace.clock.ElapsedMilliseconds - start); }
        }
    }
}
