using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServiceTool
{
    // WinForms disables every top-level window on the UI thread during native
    // dialogs. Restore unrelated windows while leaving the immediate owner locked.
    internal static class ScopedDialogs
    {
        private const uint CloseMessage = 0x0010, OwnerWindow = 4;
        [ThreadStatic] private static Stack<Scope> scopes;

        internal static DialogResult Show(Form owner, CommonDialog dialog)
        {
            return Run(owner, () => dialog.ShowDialog(owner));
        }

        internal static DialogResult Show(Form owner, Form dialog)
        {
            if (owner == null) return dialog.ShowDialog();
            ModuleNavigation.CenterOwned(owner, dialog);
            return Run(owner, () => dialog.ShowDialog(owner));
        }

        internal static bool DismissForExit(Action retry)
        {
            if (scopes == null || scopes.Count == 0) return false;
            var scope = scopes.Peek();
            scope.ResumeExit = retry;
            scope.Dismiss();
            return true;
        }

        private static DialogResult Run(Form owner, Func<DialogResult> show)
        {
            if (owner.IsDisposed || ModuleNavigation.ExitConfirmed) return DialogResult.Cancel;
            if (scopes == null) scopes = new Stack<Scope>();
            var scope = new Scope(owner);
            scopes.Push(scope);
            using (var timer = new Timer { Interval = 30 })
            {
                EventHandler restore = delegate { if (scopes.Peek() == scope) scope.RestoreOtherWindows(); };
                Application.EnterThreadModal += restore;
                timer.Tick += restore;
                timer.Start();
                try { return show(); }
                finally
                {
                    timer.Stop();
                    Application.EnterThreadModal -= restore;
                    scopes.Pop();
                    scope.ReapplyManagedLocks();
                    if (scopes.Count > 0) scopes.Peek().RestoreOtherWindows();
                    if (scope.ResumeExit != null) scope.ResumeExit();
                }
            }
        }

        private sealed class Scope
        {
            private readonly IntPtr owner;
            private readonly HashSet<IntPtr> existing;
            private readonly List<IntPtr> others;
            internal Action ResumeExit;

            internal Scope(Form form)
            {
                owner = form.Handle;
                existing = new HashSet<IntPtr>(ThreadWindows(handle => GetWindow(handle, OwnerWindow) == owner));
                others = ThreadWindows(handle => handle != owner && IsWindowVisible(handle) && IsWindowEnabled(handle));
            }

            internal void RestoreOtherWindows()
            {
                foreach (var handle in others)
                {
                    if (!IsWindowVisible(handle) || IsWindowEnabled(handle)) continue;
                    var control = Control.FromHandle(handle);
                    if (control == null || control.Enabled) EnableWindow(handle, true);
                }
                if (ResumeExit != null) Dismiss();
            }

            internal void ReapplyManagedLocks()
            {
                foreach (var handle in others)
                {
                    var control = Control.FromHandle(handle);
                    if (control != null && !control.Enabled && IsWindowEnabled(handle)) EnableWindow(handle, false);
                }
            }

            internal void Dismiss()
            {
                foreach (IntPtr handle in ThreadWindows(window => GetWindow(window, OwnerWindow) == owner))
                    if (!existing.Contains(handle)) PostMessage(handle, CloseMessage, IntPtr.Zero, IntPtr.Zero);
            }

            private static List<IntPtr> ThreadWindows(Predicate<IntPtr> include)
            {
                var windows = new List<IntPtr>();
                EnumThreadWindows(GetCurrentThreadId(), (handle, unused) =>
                {
                    if (include(handle)) windows.Add(handle);
                    return true;
                }, IntPtr.Zero);
                return windows;
            }
        }

        private delegate bool WindowCallback(IntPtr window, IntPtr data);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr window, bool enable);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    }
}