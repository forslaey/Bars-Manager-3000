using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
namespace ServiceTool
{
    internal static class LibraryCancellation
    {
        [ThreadStatic] internal static Func<bool> Current;
        internal static bool Requested { get { return Current != null && Current(); } }
        internal static void Check() { if (Requested) throw new OperationCanceledException("Операция отменена. Процессы этой очереди остановлены; проверь состояние компонента перед повторной установкой. Принудительная остановка не гарантирует откат изменений."); }
        internal static void Check(Func<bool> requested)
        { Check(); if (requested != null && requested()) throw new OperationCanceledException("Подготовка файлов отменена; установщик не запускался"); }
        internal static string Hash(Stream input, Func<bool> requested = null)
        {
            using (var sha = SHA256.Create())
            {
                byte[] buffer = new byte[65536]; int count;
                while (true) { Check(requested); count = input.Read(buffer, 0, buffer.Length); if (count == 0) break; sha.TransformBlock(buffer, 0, count, buffer, 0); }
                Check(requested); sha.TransformFinalBlock(new byte[0], 0, 0); return BitConverter.ToString(sha.Hash).Replace("-", "");
            }
        }
        internal static void Copy(Stream input, Stream output)
        {
            byte[] buffer = new byte[65536]; int count;
            while (true) { Check(); count = input.Read(buffer, 0, buffer.Length); if (count == 0) break; Check(); output.Write(buffer, 0, count); }
        }
        internal static string EventName(string pipe) { return @"Local\" + pipe + ".Cancel"; }
        internal static EventWaitHandle Create(string pipe)
        {
            var security = new EventWaitHandleSecurity(); security.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WindowsIdentity.GetCurrent().User, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                security.AddAccessRule(new EventWaitHandleAccessRule(sid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
            bool created; var signal = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(pipe), out created, security);
            if (!created) { signal.Dispose(); throw new InvalidOperationException("Сигнал отмены уже существует; новая очередь не запускалась"); }
            return signal;
        }
    }
}
