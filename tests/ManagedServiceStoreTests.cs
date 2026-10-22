using System;
using System.IO;
using System.Text;
using System.Xml;
using ServiceTool;

internal static class ManagedServiceStoreTests
{
    private static int checks;

    private static void Assert(bool value, string message)
    {
        ++checks;
        if (!value) throw new Exception("FAIL: " + message);
    }

    private static bool Has(ManagedServiceStore store, string path)
    {
        string normalized = ExecutablePaths.Normalize(path);
        foreach (string value in store.GetExecutablePaths())
            if (String.Equals(value, normalized, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void WriteCatalog(string file, params string[] paths)
    {
        using (var writer = XmlWriter.Create(file, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true }))
        {
            writer.WriteStartElement("managedServices");
            writer.WriteAttributeString("version", "1");
            foreach (string path in paths)
            {
                writer.WriteStartElement("executable");
                writer.WriteAttributeString("path", path);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
    }

    private static bool SameBytes(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (int i = 0; i < left.Length; ++i) if (left[i] != right[i]) return false;
        return true;
    }

    private static void RejectFixture(string file, string contents, string newExe, string message)
    {
        File.WriteAllText(file, contents, new UTF8Encoding(false));
        byte[] before = File.ReadAllBytes(file);
        var store = new ManagedServiceStore(file);
        Assert(store.GetExecutablePaths().Length == 0 && store.Warning.Length > 0, message + " is reported without partial records");
        Assert(!store.Remember(newExe), message + " disables persistence");
        Assert(Has(store, newExe), message + " still retains observed .exe for this session");
        Assert(SameBytes(before, File.ReadAllBytes(file)), message + " leaves source bytes unchanged");
    }

    private static int Main()
    {
        string parent = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "managed-store-fixtures");
        string fixture = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try
        {
            string first = Path.Combine(fixture, "Отсутствует & первая.exe");
            string second = Path.Combine(fixture, "Second.exe");
            string third = Path.Combine(fixture, "Third.EXE");
            string fourth = Path.Combine(fixture, "Fourth.exe");
            string fifth = Path.Combine(fixture, "Fifth.exe");
            int initialFiles = Directory.GetFiles(fixture).Length;
            var memory = ManagedServiceStore.CreateMemory();
            Assert(memory.Warning.Length == 0 && memory.GetExecutablePaths().Length == 0, "memory store starts empty");
            Assert(memory.Remember(first), "memory remembers a missing binary");
            Assert(!memory.Remember(first.ToUpperInvariant()), "memory deduplicates case-insensitively");
            Assert(memory.Warning.Length == 0 && memory.GetExecutablePaths().Length == 1, "duplicate is not an error");
            string[] copy = memory.GetExecutablePaths();
            copy[0] = "tampered";
            Assert(Has(memory, first), "callers cannot mutate store via returned array");
            Assert(Directory.GetFiles(fixture).Length == initialFiles && !File.Exists(first), "memory store creates no storage files or executables");
            foreach (string invalid in new[] { null, "", "   ", Path.Combine(fixture, "not-exe.dll"), "bad\0file.exe" })
                Assert(!memory.Remember(invalid) && memory.Warning.Length > 0, "invalid Remember input reports a warning");
            Assert(memory.GetExecutablePaths().Length == 1, "invalid inputs cannot contaminate catalog");
            Assert(memory.Remember(second) && memory.Warning.Length == 0, "successful memory add clears a transient warning");

            string file = Path.Combine(fixture, "nested", "services.xml");
            var disk = new ManagedServiceStore(file);
            Assert(disk.Warning.Length == 0 && disk.GetExecutablePaths().Length == 0, "missing catalog is a valid empty store");
            Assert(!Directory.Exists(Path.GetDirectoryName(file)), "constructor does not create missing storage directories");
            Assert(disk.Remember(first), "first disk add commits a new catalog: " + disk.Warning);
            Assert(File.Exists(file) && !File.Exists(first), "only the catalog is created, not a missing binary");
            byte[] original = File.ReadAllBytes(file);
            Assert(!disk.Remember(first.ToLowerInvariant()) && disk.Warning.Length == 0, "duplicate disk add is a no-op");
            Assert(SameBytes(original, File.ReadAllBytes(file)), "duplicate does not rewrite XML");
            var reopened = new ManagedServiceStore(file);
            Assert(reopened.Warning.Length == 0 && Has(reopened, first), "catalog restores across instances");
            Assert(reopened.Remember(second), "existing XML is replaced atomically");
            Assert(new ManagedServiceStore(file).GetExecutablePaths().Length == 2, "replacement preserves previous record");
            Assert(Directory.GetFiles(Path.GetDirectoryName(file), "*.tmp").Length == 0, "successful save leaves no temporary files");

            // Two stale instances must merge under the sibling lock, rather than replace each other.
            var staleA = new ManagedServiceStore(file);
            var staleB = new ManagedServiceStore(file);
            Assert(staleA.Remember(third), "first stale instance appends");
            Assert(staleB.Remember(fourth), "second stale instance merges current XML: " + staleB.Warning);
            Assert(new ManagedServiceStore(file).GetExecutablePaths().Length == 4, "concurrent-instance additions are preserved");
            Assert(staleA.Remember(fifth), "first instance merges additions it did not originally load");
            Assert(new ManagedServiceStore(file).GetExecutablePaths().Length == 5, "all independent observations survive");

            // External removals of unrelated persisted entries are not undone by a stale instance.
            WriteCatalog(file, second);
            Assert(staleA.Remember(third), "currently observed .exe can be remembered after external removal");
            var afterEdit = new ManagedServiceStore(file);
            Assert(afterEdit.GetExecutablePaths().Length == 2 && Has(afterEdit, second) && Has(afterEdit, third), "external edits are re-read without resurrecting unrelated stale entries");

            string lockedFile = Path.Combine(fixture, "locked.xml");
            WriteCatalog(lockedFile, first);
            var blockedSave = new ManagedServiceStore(lockedFile);
            byte[] beforeLockedSave = File.ReadAllBytes(lockedFile);
            using (var blockReplace = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Assert(!blockedSave.Remember(second) && blockedSave.Warning.Length > 0, "failed atomic replacement reports warning");
                Assert(Has(blockedSave, first) && Has(blockedSave, second), "failed save retains session .exe observations");
                Assert(SameBytes(beforeLockedSave, File.ReadAllBytes(lockedFile)), "replacement failure leaves original XML unchanged");
            }
            Assert(Directory.GetFiles(fixture, "locked.xml.*.tmp").Length == 0, "failed replacement cleans its own temporary file");
            Assert(blockedSave.Remember(third) && blockedSave.Warning.Length == 0, "later successful save recovers pending session paths");
            var recovered = new ManagedServiceStore(lockedFile);
            Assert(recovered.GetExecutablePaths().Length == 3 && Has(recovered, second) && Has(recovered, third), "pending additions are committed with the next successful add");

            string lockBusyFile = Path.Combine(fixture, "busy.xml");
            WriteCatalog(lockBusyFile, first);
            var busy = new ManagedServiceStore(lockBusyFile);
            byte[] beforeBusy = File.ReadAllBytes(lockBusyFile);
            using (var heldLock = new FileStream(lockBusyFile + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert(!busy.Remember(second) && busy.Warning.Length > 0, "another writer's lock prevents a conflicting save");
                Assert(Has(busy, second), "lock contention preserves session observation");
                Assert(SameBytes(beforeBusy, File.ReadAllBytes(lockBusyFile)), "lock contention never changes original file");
            }
            Assert(busy.Remember(second) && busy.Warning.Length == 0, "duplicate session observation can retry pending persistence");
            Assert(new ManagedServiceStore(lockBusyFile).GetExecutablePaths().Length == 2, "retry persisted the pending observation");

            string deniedFile = Path.Combine(fixture, "denied-read.xml");
            WriteCatalog(deniedFile, first);
            ManagedServiceStore denied;
            byte[] beforeDenied = File.ReadAllBytes(deniedFile);
            using (var denyRead = new FileStream(deniedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                denied = new ManagedServiceStore(deniedFile);
                Assert(denied.GetExecutablePaths().Length == 0 && denied.Warning.Length > 0, "read failure is warning plus empty catalog, not constructor exception");
            }
            Assert(!denied.Remember(second) && Has(denied, second), "a previously unreadable catalog cannot be overwritten but tracks the session");
            Assert(SameBytes(beforeDenied, File.ReadAllBytes(deniedFile)), "unreadable-source protection survives release of file lock");

            string corrupt = Path.Combine(fixture, "corrupt.xml");
            RejectFixture(corrupt, "<managedServices version='1'><broken>", second, "truncated XML");
            RejectFixture(corrupt, "<managedServices version='2'/>", second, "unknown schema version");
            RejectFixture(corrupt, "<managedServices xmlns='urn:unexpected' version='1'/>", second, "unexpected namespace");
            RejectFixture(corrupt, "<managedServices version='1'><nativeService name='EventLog'/></managedServices>", second, "service-name record");
            RejectFixture(corrupt, "<managedServices version='1'><executable path='relative.exe'/></managedServices>", second, "relative executable record");
            RejectFixture(corrupt, "<managedServices version='1'><executable path='C:\\example.dll'/></managedServices>", second, "non-.exe record");
            RejectFixture(corrupt, "<managedServices version='1'><executable path='C:\\example.exe'><nested/></executable></managedServices>", second, "nested record content");
            RejectFixture(corrupt, "<!DOCTYPE managedServices [<!ENTITY x SYSTEM 'secret.txt'>]><managedServices version='1'>&x;</managedServices>", second, "DTD/external entity");

            byte[] tooBig = new byte[1024 * 1024 + 1];
            File.WriteAllBytes(corrupt, tooBig);
            var huge = new ManagedServiceStore(corrupt);
            Assert(huge.Warning.Length > 0 && huge.GetExecutablePaths().Length == 0, "catalog above 1MB is rejected before XML parsing");
            Assert(!huge.Remember(second) && Has(huge, second) && SameBytes(tooBig, File.ReadAllBytes(corrupt)), "oversized catalog remains untouched and session still works");

            string[] excess = new string[1001];
            for (int i = 0; i < excess.Length; ++i) excess[i] = Path.Combine(fixture, "Limit" + i + ".exe");
            WriteCatalog(corrupt, excess);
            byte[] beforeLimit = File.ReadAllBytes(corrupt);
            var excessive = new ManagedServiceStore(corrupt);
            Assert(excessive.Warning.Length > 0 && excessive.GetExecutablePaths().Length == 0, "more than 1000 records rejects the entire untrusted catalog");
            Assert(!excessive.Remember(second) && SameBytes(beforeLimit, File.ReadAllBytes(corrupt)), "record limit never causes rewriting a truncated subset");
            WriteCatalog(corrupt, first, first.ToUpperInvariant());
            var duplicates = new ManagedServiceStore(corrupt);
            Assert(duplicates.Warning.Length == 0 && duplicates.GetExecutablePaths().Length == 1, "XML duplicates are deduplicated case-insensitively");

            var limitedMemory = ManagedServiceStore.CreateMemory();
            bool allAdded = true;
            for (int i = 0; i < 1000; ++i) allAdded &= limitedMemory.Remember(excess[i]);
            Assert(allAdded && limitedMemory.GetExecutablePaths().Length == 1000, "memory permits the documented 1000 records");
            Assert(!limitedMemory.Remember(excess[1000]) && limitedMemory.Warning.Length > 0 && limitedMemory.GetExecutablePaths().Length == 1000, "memory entry limit is enforced without growing the list");

            string changedFile = Path.Combine(fixture, "changed.xml");
            WriteCatalog(changedFile, first);
            var changed = new ManagedServiceStore(changedFile);
            File.WriteAllText(changedFile, "externally corrupted", new UTF8Encoding(false));
            byte[] changedBytes = File.ReadAllBytes(changedFile);
            Assert(!changed.Remember(second) && changed.Warning.Length > 0 && Has(changed, first) && Has(changed, second), "external corruption disables persistence while retaining safe session records");
            Assert(SameBytes(changedBytes, File.ReadAllBytes(changedFile)), "external corruption is not overwritten");

            var invalidPath = new ManagedServiceStore("bad\0storage.xml");
            Assert(invalidPath.Warning.Length > 0 && invalidPath.GetExecutablePaths().Length == 0, "invalid storage path does not crash constructor");
            Assert(!invalidPath.Remember(second) && Has(invalidPath, second), "invalid storage path retains session-only .exe observations");
            Assert(new ManagedServiceStore(null).Warning.Length > 0, "null storage path reports warning");

            Console.WriteLine("PASS: " + checks + " managed-store checks. Only isolated fixture files under work were used; no default storage or SCM operations.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            string allowed = Path.GetFullPath(parent) + Path.DirectorySeparatorChar;
            string actual = Path.GetFullPath(fixture);
            if (actual.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Directory.Exists(actual))
                Directory.Delete(actual, true);
        }
    }
}
