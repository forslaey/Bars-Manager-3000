using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class LibraryManagerTests
    {
        private static int checks;
        private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        private static void Reject(Action action, string message) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, message); }
        private static string root;
        private static void VersionPolicyTests()
        {
            Check(LibraryVersions.Compare("8.0.31", "8.0.31.0") == 0, "Missing revision normalized");
            Check(LibraryVersions.Compare("11.0.0-rc.10.1", "11.0.0-rc.2.99") > 0, "Prerelease identifiers use numeric ordering");
            Check(LibraryVersions.Compare("11.0.0", "11.0.0-rc.1.123") > 0, "Stable follows prerelease");
            Check(LibraryVersions.Compare("8.0.31+commitA", "8.0.31+commitB") == 0, "Informational commit is not runtime version");
            var runtime = new LibraryPackage { Name = "Runtime", Kind = LibraryKind.DotNet, Version = "8.0.31", Architecture = "x64", RuntimeFamily = "Microsoft.NETCore.App" };
            foreach (string version in new[] { "8.0.30", "8.0.31", "8.0.32" })
            {
                var state = LibraryVersions.Known(runtime, version, "fixture");
                Check(state.Relation == (version == "8.0.30" ? LibraryRelation.Older : version == "8.0.31" ? LibraryRelation.Equal : LibraryRelation.Newer), "Runtime patch classified: " + version);
                Check(LibraryVersions.NeedsInstall(state) == (version == "8.0.30"), "Only older requires action: " + version);
                Check(LibraryVersions.CanReinstall(state) == (version != "8.0.32"), "Newer runtime cannot be reinstalled with old source: " + version);
                Check(!String.IsNullOrEmpty(LibraryVersions.Advice(state)), "Runtime has recommendation");
            }
            Check(!LibraryVersions.Compatible(runtime, "9.0.31") && !LibraryVersions.Compatible(runtime, "8.1.31"), "Other major/minor does not substitute requested branch");
            Check(!LibraryVersions.Compatible(runtime, "8.0.31-rc.1.99"), "RC cannot satisfy stable runtime");
            runtime.Version = "11.0.0-rc.1.26425.128";
            Check(LibraryVersions.Compatible(runtime, "11.0.0-rc.1.26425.129"), "Same RC series compares complete build");
            Check(!LibraryVersions.Compatible(runtime, "11.0.0-rc.2.26425.129"), "Different RC series is kept separate");
            var original = LibraryCatalog.All();
            Check(!original.Any(p => p.Kind == LibraryKind.Composite || p.Name.Contains("RuntimePack Full")), "Standalone Full absent from built-in catalog");
            var old = new LibraryPackage { Id = "oldFull", Name = "RuntimePack Full", Kind = LibraryKind.Composite };
            LibraryCatalog.Merge(new[] { old }); Check(!LibraryCatalog.All().Any(p => p.Id == old.Id), "Old cache cannot resurrect Full");
            var modern = original.Single(p => p.Kind == LibraryKind.VcBundle); var legacy = original.Single(p => p.Kind == LibraryKind.VcLegacy);
            foreach (var profile in new[] { legacy, modern })
            {
                string tasks = LibraryVcProfiles.Tasks(profile);
                Check(tasks.Split(',').Contains("rtl"), "Embedded Lite is mandatory for every VC profile");
                Check(!tasks.Contains("legacy") && !tasks.Contains("1450"), "Forced old modern runtime is never selected");
                string command = LibraryPlan.Command(profile, "source.exe", "log").Arguments;
                Check(command.Contains("/TASKS=") && command.Contains(tasks), "Exact noninteractive task profile passed to Inno");
            }
            Check(LibraryVcProfiles.Tasks(legacy) == @"v0513,v0513\silent,rtl", "Legacy profile excludes modern VC");
            Check(LibraryVcProfiles.Tasks(modern) == @"v1522,v1522\silent,rtl", "Modern profile excludes old VC task");
            var products = new List<LibraryRegisteredProduct>();
            foreach (string arch in new[] { "x86", "x64" }) foreach (int generation in new[] { 11, 12, 14 })
                products.Add(new LibraryRegisteredProduct { Name = "Microsoft Visual C++ " + (generation == 14 ? "v14" : generation == 11 ? "2012" : "2013") + " Redistributable (" + arch + ")", Version = LibraryVcProfiles.Minimum(generation, arch, modern), Architecture = arch });
            Func<int,string,string> files = (generation,arch) => LibraryVcProfiles.Minimum(generation,arch,modern);
            Check(LibraryVcProfiles.Check(legacy, products, true, p => true, files).State == LibraryState.Installed, "2012/2013 x86+x64 satisfied without comparing against wrapper v14");
            Check(LibraryVcProfiles.Check(legacy, products, true, p => true, (g,a) => g == 11 ? "11.0.51106.1" : "12.0.40664.0").Relation == LibraryRelation.Equal, "VC2012 package and individual CRT build are distinct version domains");
            Check(LibraryVcProfiles.Check(modern, products, true, p => true, files).State == LibraryState.Installed, "Modern v14 name recognized with all digits");
            var missing = products.Where(p => !(p.Architecture == "x86" && p.Version.StartsWith("12."))).ToList();
            Check(LibraryVcProfiles.Check(legacy, missing, true, p => true, files).State == LibraryState.Missing, "Missing x86 generation not covered by x64 or v14");
            Check(LibraryVcProfiles.Check(legacy, products, true, p => false, files).State == LibraryState.Missing, "Stale VC registration excluded");
            Check(LibraryVcProfiles.Check(legacy, products, true, p => true, (g,a) => null).State == LibraryState.Unknown, "VC registration without system DLLs requires check");
            foreach (var product in products.Where(p => p.Version.StartsWith("14."))) product.Version = "14.44.35211.0";
            Check(LibraryVcProfiles.Check(modern, products, true, p => true, (g,a) => "14.44.35211.0").Relation == LibraryRelation.Older, "Older v14 offered update, not absent");
            foreach (var product in products.Where(p => p.Version.StartsWith("14."))) product.Version = "14.99.99999.0";
            Check(LibraryVcProfiles.Check(modern, products, true, p => true, (g,a) => "14.99.99999.0").Relation == LibraryRelation.Newer, "Later modern v14 is recognized");
            Reject(() => LibraryVcProfiles.ValidateNoDowngrade(modern, products), "Source blocked before downgrading any newer VC architecture");
            var msi = original.First(p => p.Kind == LibraryKind.Msi && !p.Name.Contains("MSXML"));
            Check(LibraryPlan.Command(msi, "fixture.msi", "log", true).Arguments.Contains("REINSTALL=ALL REINSTALLMODE=vomus"), "Same MSI ProductCode minor update explicitly recaches newer source");
            Check(!LibraryPlan.Command(msi, "fixture.msi", "log").Arguments.Contains("REINSTALL="), "First MSI install has no repair flags");
            Version source = LibraryVersions.Number(msi.Version);
            string earlier = new Version(source.Major, source.Minor, 0, 0).ToString();
            Check(LibraryVersions.Known(msi, earlier, "fixture").Relation == LibraryRelation.Older, "MSI subversions compared");
            var xml = original.First(p => p.Name.Contains("MSXML"));
            Check(LibraryVersions.Known(xml, "6.30.26100.1", "OS").Relation == LibraryRelation.Newer, "Integrated MSXML 6 from Windows satisfies old MSI");
            var sdk = original.First(p => p.Kind == LibraryKind.Framework);
            Check(LibraryVersions.Required(sdk) == "4.8.1" && !LibraryVersions.Compatible(sdk, "4.8"), "Targeting pack generation uses Framework version, not wrapper build");
            var protectedXml = LibraryVersions.Known(xml, "6.30.26100.1", "OS"); protectedXml.ProtectedComponent = true;
            Check(!LibraryVersions.CanRemove(protectedXml) && !LibraryVersions.CanReinstall(protectedXml), "OS MSXML has no standalone removal or reinstall action");
            Check(LibraryVersions.BatchCaption(new[] { LibraryVersions.Known(runtime, "11.0.0-rc.1.26425.127", "fixture") }) == "Обновить выбранные", "Action caption follows older actual build");
        }
        [STAThread] public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--inert-child") { Thread.Sleep(550); return 0; }
            if (args.Length == 1 && args[0] == "--inert-parent")
            { Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--inert-child") { UseShellExecute = false, CreateNoWindow = true }); return 0; }
            root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "library-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length == 2 && args[0] == "--scan")
                {
                    List<LibraryPackage> found = LibraryDiscovery.Scan(args[1], Console.WriteLine);
                    Check(found.Count == 40 && found.Count(p => p.Kind == LibraryKind.VcLegacy) == 1 && !found.Any(p => p.Kind == LibraryKind.Composite), "Thirty-nine supported sources plus second VC profile, Full excluded");
                    Check(found.Count(p => p.Kind == LibraryKind.DotNet) == 20, "Twenty .NET runtimes discovered from metadata");
                    Check(found.Single(p => p.Kind == LibraryKind.VcBundle).Version == "14.52.37025.0", "Current VC version comes from file metadata");
                    LibraryCatalog.Merge(found); Check(LibraryCatalog.All().Count == 40, "Renamed/updated sources replace existing family entries instead of duplicating"); LibraryCatalog.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "discovered-library-catalog.xml"));
                    var vc = found.Single(p => p.Kind == LibraryKind.VcBundle);
                    LibraryPackage renamed = LibraryDiscovery.Describe(LibrarySource.PackagePath(args[1], vc), "renamed-without-version.exe");
                    Check(renamed.Id == vc.Id && renamed.Version == vc.Version, "Renaming file does not change detected VC identity/version");
                    Console.WriteLine("PASS: " + checks + " real read-only inventory checks. No installer was started."); return 0;
                }
                if (args.Length == 2 && args[0] == "--live-screenshot")
                {
                    using (var form = new LibraryManagerForm(false))
                    {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.ShowInTaskbar = false; form.SetTheme(true, false);
                        form.Show(); DateTime deadline = DateTime.UtcNow.AddSeconds(45);
                        var checking = typeof(LibraryManagerForm).GetField("checking", BindingFlags.Instance | BindingFlags.NonPublic);
                        do { Application.DoEvents(); Thread.Sleep(20); } while ((bool)checking.GetValue(form) && DateTime.UtcNow < deadline);
                        Check(!(bool)checking.GetValue(form), "Live check finishes without any installer directory prompt");
                        using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(args[1]); }
                        form.Close();
                    }
                    Console.WriteLine("PASS: live monitoring UI captured; no installer, folder dialog or UAC."); return 0;
                }
                if (args.Length == 2 && args[0] == "--detect")
                {
                    List<LibraryCheck> detected = LibraryDetection.Scan();
                    File.WriteAllLines(args[1], detected.Select(c => c.Package.Name + " | в комплекте: " + LibraryVersions.Required(c.Package) + " | в системе: " + c.ActualVersion + " | " + c.Caption + " | " + LibraryVersions.Advice(c) + " | " + c.Detail), System.Text.Encoding.UTF8);
                    Check(detected.Count == 40, "All catalog entries checked without any installer directory");
                    Console.WriteLine("PASS: read-only host detection of " + detected.Count + " entries; installed=" + detected.Count(c => c.State == LibraryState.Installed)); return 0;
                }
                VersionPolicyTests();
                int pulses = 0;
                var heartbeat = new LibraryHeartbeat(text => Interlocked.Increment(ref pulses), 20);
                heartbeat.SetPhase("Slow copy"); Thread.Sleep(100); Check(Volatile.Read(ref pulses) > 1, "Heartbeat continues while worker is busy copying");
                heartbeat.Dispose(); int stopped = Volatile.Read(ref pulses); Thread.Sleep(80); Check(Volatile.Read(ref pulses) == stopped, "No timer writes after final-result barrier");
                List<LibraryPackage> original = LibraryCatalog.All();
                var mixed = new[]
                {
                    new LibraryCheck { Package = original.First(p => p.Group == "SQL Server"), State = LibraryState.Installed },
                    new LibraryCheck { Package = original.First(p => p.Group == "SQL Server"), State = LibraryState.Missing },
                    new LibraryCheck { Package = original.First(p => p.Group == "Microsoft .NET"), State = LibraryState.Missing },
                    new LibraryCheck { Package = original.First(p => p.Group == ".NET Framework"), State = LibraryState.Missing },
                    new LibraryCheck { Package = original.First(p => p.Kind == LibraryKind.VcBundle), State = LibraryState.Unknown }
                };
                List<LibraryCheck> sorted = LibraryPresentation.Sort(mixed);
                Check(sorted.Take(3).All(c => c.State == LibraryState.Missing) && sorted[3].State == LibraryState.Unknown && sorted[4].State == LibraryState.Installed, "Missing then uncertain then installed");
                Check(sorted[0].Package.Group == ".NET Framework" && sorted[1].Package.Group == "Microsoft .NET" && sorted[2].Package.Group == "SQL Server", ".NET families adjacent before SQL inside each state block");
                Check(original.Count == 40, "Built-in catalog has forty packages"); Check(original.Select(p => p.Id).Distinct().Count() == 40, "Unique stable IDs");
                Check(!original.Any(p => p.RelativePath.Contains("Исправлятор") || p.RelativePath.Contains("Удалятор") || p.RelativePath.Contains("1.0.31.exe")), "Manual-only tools excluded");
                foreach (LibraryPackage p in original)
                {
                    LibraryPackage copy = LibraryCatalog.Deserialize(LibraryCatalog.Serialize(p));
                    Check(copy.Id == p.Id && copy.RelativePath == p.RelativePath && copy.Kind == p.Kind && copy.Version == p.Version, "Catalog round-trip");
                    Check(!String.IsNullOrEmpty(LibraryPlan.Command(p, @"C:\Папка с пробелами\" + Path.GetFileName(p.RelativePath), Path.Combine(root, "vendor.log")).FileName), "Command built without execution");
                }
                var vcPackage = LibraryCatalog.Deserialize(LibraryCatalog.Serialize(original.Single(p => p.Kind == LibraryKind.VcBundle)));
                string stable = vcPackage.Id; vcPackage.Version = "14.99.99999.0"; vcPackage.RelativePath = "new name.exe";
                Check(LibraryDiscovery.Id(vcPackage) == stable, "New version/name does not require new compiled ID");
                Check(LibraryPlan.Command(vcPackage, "x.exe", "x.log").Arguments.Contains("/VERYSILENT"), "VC bundle uses confirmed Inno switches");
                LibraryCatalog.Replace(new[] { vcPackage }); string cache = Path.Combine(root, "catalog.xml"); LibraryCatalog.Save(cache); LibraryCatalog.Replace(original); LibraryCatalog.Load(cache);
                Check(LibraryCatalog.All().Single(p => p.Kind == LibraryKind.VcBundle).Version == "14.99.99999.0", "External catalog updates versions without rebuild"); LibraryCatalog.Replace(original);
                File.WriteAllText(Path.Combine(root, "bad.xml"), "<!DOCTYPE libraries [<!ENTITY x SYSTEM 'file:///c:/secret'>]><libraries>&x;</libraries>");
                Reject(() => LibraryCatalog.Load(Path.Combine(root, "bad.xml")), "DTD/external entities rejected"); Check(LibraryCatalog.All().Count == 40, "Failed import preserves live catalog");
                Directory.CreateDirectory(Path.Combine(root, "Библиотеки")); Check(LibrarySource.AutoDetect(root) == Path.Combine(root, "Библиотеки"), "Adjacent folder detected at any executable location");
                Check(LibrarySource.AutoDetect(Path.Combine(root, "missing")) == null, "Missing folder does not prompt or block");
                Check(LibrarySource.NormalizeRoot(root) == Path.Combine(root, "Библиотеки"), "Parent folder selection normalized");
                LibraryPackage escape = LibraryCatalog.Deserialize(LibraryCatalog.Serialize(original[0])); escape.RelativePath = @"..\escape.exe";
                Reject(() => LibrarySource.PackagePath(root, escape), "Traversal rejected before launch");
                byte[] payload = new byte[] { 1, 2, 3, 4 }; using (var sha = SHA256.Create()) escape.Sha256 = BitConverter.ToString(sha.ComputeHash(payload)).Replace("-", "");
                using (var input = new MemoryStream(payload)) LibrarySource.VerifyStream(input, escape);
                Reject(() => { using (var input = new MemoryStream(new byte[] { 1, 2, 3, 5 })) LibrarySource.VerifyStream(input, escape); }, "Changed payload rejected");
                Check(LibraryDetection.VersionFits("8.0.32", "8.0.31"), "Newer runtime patch fits"); Check(!LibraryDetection.VersionFits("9.0.1", "8.0.31"), "New major does not replace older runtime");
                var smo = original.First(p => p.Kind == LibraryKind.Msi && p.Name.Contains("2014 Management Objects") && p.Architecture == "x64");
                Check(LibraryPlan.Expand(new[] { smo }).Any(p => p.Name.Contains("CLR") && p.Architecture == "x64" && p.Version.StartsWith("12.")), "SMO adds matching CLR dependency");
                var asp = original.First(p => p.RuntimeFamily == "Microsoft.AspNetCore.App" && !p.Preview);
                Check(LibraryPlan.Expand(new[] { asp }).Any(p => p.RuntimeFamily == "Microsoft.NETCore.App" && p.Architecture == asp.Architecture && p.Version == asp.Version), "ASP runtime adds base runtime dependency");
                Check(LibraryPlan.Success(3010) && LibraryPlan.NeedsReboot(3010) && !LibraryPlan.Success(1603), "Exit/reboot classification");
                string pipe; int parent; Check(!LibrarySession.Parse(new[] { "--library-worker", "wrong", "1" }, out pipe, out parent), "Malformed worker args rejected");
                Check(LibrarySession.RunWorker(new[] { "--library-worker" }) == 87, "Invalid worker returns before opening UAC or a pipe");
                var time = Stopwatch.StartNew(); int heartbeats = 0;
                int exit = LibraryProcess.Run(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--inert-parent") { WorkingDirectory = root }, text => heartbeats++);
                Check(exit == 0 && time.ElapsedMilliseconds >= 450 && heartbeats > 0, "Job waits for inert grandchild after parent exits");
                foreach (bool dark in new[] { false, true })
                {
                    using (var form = new LibraryManagerForm(true))
                    {
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000); form.ShowInTaskbar = false;
                        form.SetTheme(dark, false); form.Show(); Application.DoEvents();
                        Check(form.SourceDirectory == null && !form.IsInstalling, "Monitoring window opens with no source folder or installation");
                        using (var image = new Bitmap(form.Width, form.Height)) form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                        form.Close();
                    }
                    using (var hub = new ManagerHub()) { hub.SetTheme(dark, false); Check(hub.Controls.Count > 0, "Hub built in both themes"); }
                }
                Console.WriteLine("PASS: " + checks + " library/catalog/UI/job checks. Only inert child processes, no installers, UAC or SCM actions."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
