using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using ServiceTool;

// Pure profiles/DPAPI/metadata/protocol and inert Installer-tree tests only.
// Never call Execute, a valid RunHelper, Install, Commit, Rollback, Uninstall, UAC or SCM.
internal static class ServiceInstallProfileTests
{
    private static int checks;
    private static string root;
    private static string exe;
    private const string Secret = "Test-only-ø-Ж-Password-892";

    private static int Main()
    {
        root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profile-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        exe = Path.Combine(root, "BARS Worker.exe");
        File.WriteAllText(exe, "Inert fixture, never executed.");
        try
        {
            VerifyProfiles(); VerifyStore(); VerifyProtocol(); VerifyMetadata(); VerifyInstallerTree();
            Console.WriteLine("PASS: " + checks + " install-profile assertions. No installer/helper, UAC or service operation executed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            string allowed = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(root);
            if (target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(target).StartsWith("profile-data-", StringComparison.Ordinal))
                Directory.Delete(target, true);
        }
    }

    private static ServiceInstallProfile UserProfile()
    {
        return new ServiceInstallProfile { ExecutablePath = exe, Mode = ServiceInstallMode.ManagedDotNet,
            Account = InstallAccountKind.User, UserName = WindowsAccountInfo.CurrentUserName() };
    }

    private static void VerifyProfiles()
    {
        ServiceInstallProfile profile = ServiceInstallProfile.Default("\"" + exe + "\"");
        Check(profile.ExecutablePath == exe && profile.Mode == ServiceInstallMode.Ordinary, "Ordinary is the generic default; quoted path is normalized");
        Check(profile.Account == InstallAccountKind.LocalSystem, "LocalSystem is the default account for new profiles");
        Throws(delegate { ServiceInstallProfile.Default(Path.ChangeExtension(exe, ".txt")); }, "Non-.exe rejected");
        profile = UserProfile(); profile.Validate();
        Check(!profile.HasSavedPassword, "Password storage starts opt-out");
        string password, warning;
        Check(!profile.TryGetSavedPassword(out password, out warning) && password == null && warning.Length == 0, "No invented empty password");
        profile.RememberPassword(Secret);
        Check(profile.HasSavedPassword && !profile.ProtectedPassword.Contains(Secret), "Only protected ciphertext is stored");
        Check(profile.TryGetSavedPassword(out password, out warning) && password == Secret, "DPAPI opt-in roundtrip"); password = null;
        ServiceInstallProfile clone = profile.Clone(); clone.ExecutablePath = Path.Combine(root, "Other.exe");
        Check(!clone.TryGetSavedPassword(out password, out warning) && password == null && warning.Length > 0, "Ciphertext is bound to exact .exe");
        clone = profile.Clone(); clone.UserName += "_other";
        Check(!clone.TryGetSavedPassword(out password, out warning) && password == null, "Ciphertext is bound to account identity");
        clone = profile.Clone(); clone.ProtectedPassword = "not-base64!";
        Check(!clone.TryGetSavedPassword(out password, out warning), "Invalid ciphertext has explicit recovery");
        clone = profile.Clone(); clone.Mode = ServiceInstallMode.Ordinary; clone.Validate();
        Check(!clone.HasSavedPassword && clone.UserName.Length == 0, "Ordinary mode clears account credentials");
        clone = profile.Clone(); clone.Account = InstallAccountKind.LocalService; clone.Validate();
        Check(!clone.HasSavedPassword && clone.UserName.Length == 0, "Built-in account clears user credentials");
        clone = profile.Clone(); clone.UserName = ""; Throws(clone.Validate, "User requires account name");
        clone = profile.Clone(); clone.Account = (InstallAccountKind)99; Throws(clone.Validate, "Unknown account enum rejected");
        Check(WindowsAccountInfo.ValidateUserName(WindowsAccountInfo.CurrentUserName()) != null, "Current Windows identity resolves to SID");
        Throws(delegate { WindowsAccountInfo.ValidateUserName("BARS-DefinitelyMissingDomain\\BARS-MissingUser-" + Guid.NewGuid().ToString("N")); }, "Unknown Windows account rejected");
    }

    private static void VerifyStore()
    {
        var memory = ServiceInstallProfileStore.CreateMemory();
        string http = Path.Combine(root, "HttpService.exe");
        ServiceInstallProfile automatic = memory.Get(http);
        Check(automatic.Mode == ServiceInstallMode.ManagedDotNet && automatic.Account == InstallAccountKind.LocalSystem && automatic.UserName.Length == 0, "HttpService defaults to LocalSystem without user credentials");
        Check(!memory.HasStoredProfile(http), "Automatic default is not falsely marked configured");
        ServiceInstallProfile ordinary = ServiceInstallProfile.Default(http); memory.Save(ordinary);
        Check(memory.HasStoredProfile(http) && memory.Get(http).Mode == ServiceInstallMode.Ordinary, "Explicit per-.exe ordinary override is retained");
        string path = Path.Combine(root, "profiles.xml");
        var store = new ServiceInstallProfileStore(path);
        ServiceInstallProfile profile = UserProfile(); profile.RememberPassword(Secret);
        Check(store.Save(profile), "Profile save succeeds: " + store.Warning);
        string xml = File.ReadAllText(path);
        Check(!xml.Contains(Secret) && !xml.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(Secret))), "XML contains neither plaintext nor simply base64-encoded password");
        var reopened = new ServiceInstallProfileStore(path);
        ServiceInstallProfile loaded = reopened.Get(exe.ToUpperInvariant());
        string password, warning;
        Check(loaded.UserName == profile.UserName && loaded.TryGetSavedPassword(out password, out warning) && password == Secret, "Disk reload, case-insensitive identity and DPAPI recovery"); password = null;
        loaded.UserName = "modified-in-caller";
        Check(reopened.Get(exe).UserName == profile.UserName, "Caller mutation cannot alter store");
        var second = new ServiceInstallProfileStore(path);
        ServiceInstallProfile other = ServiceInstallProfile.Default(Path.Combine(root, "Other.exe"));
        Check(second.Save(other), "Second instance saves its distinct .exe");
        Check(store.Save(profile), "First instance re-reads under lock");
        Check(new ServiceInstallProfileStore(path).HasStoredProfile(other.ExecutablePath), "Concurrent instance addition is not overwritten");
        string lockPath = path + ".lock";
        using (var held = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var pending = ServiceInstallProfile.Default(Path.Combine(root, "Pending.exe"));
            Check(!store.Save(pending) && store.Warning.Length > 0, "Locked disk produces session-only warning");
            Check(store.HasStoredProfile(pending.ExecutablePath), "Failed write keeps session profile");
        }
        Check(store.Save(other), "Later save recovers after contention");
        Check(new ServiceInstallProfileStore(path).HasStoredProfile(Path.Combine(root, "Pending.exe")), "Later success flushes prior pending profiles");
        string corrupt = Path.Combine(root, "corrupt.xml");
        const string malformed = "<!DOCTYPE installProfiles [<!ENTITY x SYSTEM 'file:///not-read'>]><installProfiles version='1'>&x;</installProfiles>";
        File.WriteAllText(corrupt, malformed);
        var bad = new ServiceInstallProfileStore(corrupt);
        Check(bad.Warning.Length > 0 && !bad.Save(profile) && File.ReadAllText(corrupt) == malformed, "DTD/corrupt source never overwritten");
    }

    private static void VerifyProtocol()
    {
        string pipe = "BARS.SM3.Install." + new string('a', 32);
        ProcessStartInfo info = ServiceProfileInstaller.BuildHelperStartInfo(exe, pipe, 123);
        Check(info.Verb == "runas" && info.UseShellExecute && info.WindowStyle == ProcessWindowStyle.Hidden, "Explicit elevated helper settings");
        Check(info.Arguments == "--install-profile-helper " + pipe + " 123" && !info.Arguments.Contains(Secret) && !info.Arguments.Contains(exe), "CLI has only pipe and PID; no credentials or target");
        string name; int pid;
        Check(ServiceProfileInstaller.TryParseHelperArguments(new string[] { "--install-profile-helper", pipe, "123" }, out name, out pid) && pid == 123, "Strict helper parsing");
        foreach (string[] bad in new string[][] { null, new string[0], new string[] { "--install-profile-helper", "../pipe", "1" },
            new string[] { "--install-profile-helper", pipe, "-1" }, new string[] { "--install-profile-helper", pipe, "1", Secret } })
        {
            Check(!ServiceProfileInstaller.TryParseHelperArguments(bad, out name, out pid), "Malformed helper input rejected");
            Check(ServiceProfileInstaller.RunHelper(bad) == 87, "Invalid helper returns before pipe/assembly/SCM");
        }
        byte[] bytes = ServiceProfileInstaller.SerializeRequest(UserProfile(), Secret, false);
        try
        {
            ServiceProfileInstaller.InstallRequest request = ServiceProfileInstaller.DeserializeRequest(bytes);
            Check(request.Profile.ExecutablePath == exe && request.Password == Secret && !request.Uninstall, "Bounded pipe payload roundtrip");
            var extra = new byte[bytes.Length + 1]; Buffer.BlockCopy(bytes, 0, extra, 0, bytes.Length);
            Throws(delegate { ServiceProfileInstaller.DeserializeRequest(extra); }, "Trailing wire bytes rejected");
            bytes[0] = 99; Throws(delegate { ServiceProfileInstaller.DeserializeRequest(bytes); }, "Unknown protocol rejected");
        }
        finally { Array.Clear(bytes, 0, bytes.Length); }
        Throws(delegate { ServiceProfileInstaller.DeserializeRequest(new byte[65537]); }, "Oversized wire payload rejected");
        bytes = ServiceProfileInstaller.SerializeRequest(UserProfile(), null, true);
        Check(ServiceProfileInstaller.DeserializeRequest(bytes).Uninstall, "Uninstall needs no password"); Array.Clear(bytes, 0, bytes.Length);
        Check(!ServiceProfileInstaller.SafeError(new Exception("outer " + Secret, new Exception("inner " + Secret)), Secret).Contains(Secret), "Nested helper errors redact exact password");
        AuthorizationRuleCollection rules = ServiceProfileInstaller.CreatePipeSecurity().GetAccessRules(true, true, typeof(SecurityIdentifier));
        Check(rules.Count == 3, "Pipe ACL has two local grants and a network-logon denial");
        int grants = 0; bool deniesNetwork = false;
        foreach (AuthorizationRule rule in rules)
        {
            Check(!rule.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.WorldSid, null)), "No Everyone grant");
            PipeAccessRule access = (PipeAccessRule)rule;
            if (access.AccessControlType == AccessControlType.Allow) grants++;
            if (access.AccessControlType == AccessControlType.Deny && access.IdentityReference.Equals(new SecurityIdentifier(WellKnownSidType.NetworkSid, null))) deniesNetwork = true;
        }
        Check(grants == 2 && deniesNetwork, "Only local current user/Admin grants; remote network tokens rejected");
    }

    private static void VerifyMetadata()
    {
        Check(ServiceProfileInstaller.ReadManagedArchitecture(Assembly.GetExecutingAssembly().Location) == "anycpu", "Reads this inert test executable metadata without loading target");
        Throws(delegate { ServiceProfileInstaller.ReadManagedArchitecture(exe); }, "Native/text .exe rejected before assembly load");
        foreach (string architecture in new string[] { "x86", "x64", "anycpu" })
        {
            string file = Path.Combine(root, architecture + ".exe"); WritePeFixture(file, architecture);
            Check(ServiceProfileInstaller.ReadManagedArchitecture(file) == architecture, "PE architecture fixture " + architecture);
        }
    }

    private static void WritePeFixture(string path, string architecture)
    {
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
        using (var writer = new BinaryWriter(stream))
        {
            stream.SetLength(1024); writer.Write((ushort)0x5A4D); stream.Position = 60; writer.Write(128);
            stream.Position = 128; writer.Write(0x00004550); writer.Write((ushort)(architecture == "x64" ? 0x8664 : 0x14C)); writer.Write((ushort)1);
            stream.Position = 148; writer.Write((ushort)240);
            stream.Position = 152; writer.Write((ushort)(architecture == "x64" ? 0x20B : 0x10B));
            stream.Position = 152 + (architecture == "x64" ? 112 : 96) + 14 * 8; writer.Write(0x2000u); writer.Write(72u);
            stream.Position = 152 + 240 + 8; writer.Write(512u); writer.Write(0x2000u); writer.Write(512u); writer.Write(512u);
            stream.Position = 512 + 16; writer.Write(architecture == "x86" ? 3u : 1u);
        }
    }

    private static void VerifyInstallerTree()
    {
        using (var rootInstaller = new Installer())
        using (var project = new Installer())
        using (var process1 = new ServiceProcessInstaller())
        using (var process2 = new ServiceProcessInstaller())
        {
            rootInstaller.Installers.Add(project); project.Installers.Add(process1); project.Installers.Add(process2);
            bool hookCalled = false;
            var context = new InstallContext(null, new string[] { "/logfile=", "/logtoconsole=false", "/unattended=true" });
            context.Parameters["assemblypath"] = "fixture";
            project.BeforeInstall += delegate { hookCalled = true; context.Parameters["assemblypath"] += " -s"; process1.Account = ServiceAccount.LocalSystem; };
            ServiceInstallProfile profile = UserProfile();
            Check(ServiceProfileInstaller.ApplyAccountTree(rootInstaller, profile, Secret, context, true) == 2, "Every nested ServiceProcessInstaller gets explicit account");
            Check(process1.Account == ServiceAccount.User && process2.Account == ServiceAccount.User && process1.Username == profile.UserName && process2.Password == Secret, "In-memory account and credentials supplied before installer use");
            MethodInfo before = typeof(Installer).GetMethod("OnBeforeInstall", BindingFlags.Instance | BindingFlags.NonPublic);
            before.Invoke(project, new object[] { new System.Collections.Hashtable() });
            Check(hookCalled && context.Parameters["assemblypath"].EndsWith(" -s", StringComparison.Ordinal), "Existing custom BeforeInstall hook preserved");
            Check(process1.Account == ServiceAccount.User, "Explicit account reapplied after parent custom hook");
            Check(context.Parameters["logfile"] == "" && context.Parameters["logtoconsole"] == "false" && context.Parameters["unattended"] == "true", "No framework logs or fallback account prompt");
            foreach (InstallAccountKind kind in new InstallAccountKind[] { InstallAccountKind.LocalService, InstallAccountKind.NetworkService, InstallAccountKind.LocalSystem })
            {
                profile.Account = kind;
                ServiceProfileInstaller.ApplyAccountTree(rootInstaller, profile, null, context, false);
                Check(process1.Account == ServiceProfileInstaller.AccountValue(kind), "Explicit built-in mapping " + kind);
            }
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); checks++; }
    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch (Exception ex)
        {
            if (ex is ArgumentException || ex is FormatException || ex is IOException || ex is InvalidDataException || ex is BadImageFormatException) { checks++; return; }
            throw;
        }
        throw new Exception("FAIL: expected rejection: " + message);
    }
}
