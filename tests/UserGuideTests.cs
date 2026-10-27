using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class UserGuideTests
    {
        private static int count;
        private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); count++; }
        private static T Field<T>(object form, string name) { return (T)form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
        private static void Call(object form, string method, params object[] args) { form.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args); }
        private static void Set(object form, string name, object value) { form.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, value); }
        private static IEnumerable<Control> All(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (Control nested in All(child)) yield return nested; } }
        private static void Show(Form form, float scale)
        {
            form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
            form.Show(); if (scale != 1F) form.Scale(new SizeF(scale, scale)); Application.DoEvents();
        }
        private static void Capture(Form form, string name)
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "previews"); Directory.CreateDirectory(folder);
            using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(folder, name + ".png")); }
        }
        [STAThread] public static int Main()
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Check(UserGuide.Sections.Length >= 12, "All modules have guide sections");
                Check(UserGuide.Find("Отмена ОТМЕНА", "отмена").SequenceEqual(new[] { 0, 7 }), "Case-insensitive search");
                Check(UserGuide.Find("text", " ").Count == 0, "Whitespace query is empty");
                using (var banner = new PictureBox()) using (var tips = new ToolTip())
                {
                    System.Diagnostics.ProcessStartInfo opened = null;
                    BrandAssets.BindWebsite(banner, tips, null, info => opened = info);
                    typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(banner, new object[] { EventArgs.Empty });
                    Check(opened != null && opened.FileName == "https://www.datakrat.ru/" && opened.UseShellExecute, "Banner click opens official site with system browser");
                    Check(tips.GetToolTip(banner) == "Официальный сайт", "Shared banner tooltip");
                }
                foreach (bool dark in new[] { false, true })
                {
                    var forms = new ManagerModuleForm[] { new ManagerHub(), new LibraryManagerForm(true), new SoftwareManagerForm(true), new WindowsFeaturesForm(true, true), new ToolsDeploymentForm(true), new UserGuideForm() };
                    foreach (var module in forms) using (module)
                    {
                        module.SetTheme(dark, false); Show(module, 1F);
                        var banner = (PictureBox)typeof(ManagerModuleForm).GetField("logo", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
                        var tips = (ToolTip)typeof(ManagerModuleForm).GetField("tips", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(module);
                        Check(banner.Image != null && banner.Cursor == Cursors.Hand, module.Text + ": clickable banner in active theme");
                        Check(banner.AccessibleName == "Открыть сайт ДАТАКРАТ" && banner.AccessibleDescription.Contains("https://www.datakrat.ru/"), module.Text + ": banner link and description");
                        Check(tips.GetToolTip(banner) == "Официальный сайт", module.Text + ": tooltip matches service manager");
                        module.Close();
                    }
                }
                foreach (bool dark in new[] { false, true }) foreach (float scale in new[] { 1F, 1.25F, 1.5F, 2F })
                using (var guide = new UserGuideForm())
                {
                    guide.SetTheme(dark, false); Show(guide, scale);
                    var search = Field<BrandTextBox>(guide, "search"); var list = Field<ListBox>(guide, "sections"); var reader = Field<RichTextBox>(guide, "reader");
                    Check(list.BorderStyle == BorderStyle.None, "Section menu has no native hover border");
                    list.Focus(); Application.DoEvents();
                    Check(list.BorderStyle == BorderStyle.None && list.TabStop, "Focus keeps border absent without disabling keyboard access");
                    Check(list.Items.Count == UserGuide.Sections.Length, "Empty search lists all sections");
                    Check(reader.ReadOnly && reader.WordWrap, "Guide is read-only and wraps text");
                    foreach (var button in All(guide).OfType<BrandButton>()) Check(button.Parent.ClientRectangle.Contains(button.Bounds), "Guide button fits at scale " + scale);
                    search.Text = "служб"; Application.DoEvents();
                    int total = UserGuide.Sections.Sum(section => UserGuide.Find(section.Document, "служб").Count);
                    Check(total > list.Items.Count && list.Items.Count > 1, "Search covers section bodies");
                    Check(Field<List<Tuple<GuideSection, int>>>(guide, "hits").Count == total, "All matches indexed");
                    for (int i = 0; i < total; i++)
                    {
                        Check(reader.SelectionLength==0 && String.Equals(reader.Text.Substring(reader.SelectionStart,5), "служб", StringComparison.OrdinalIgnoreCase), "Match caret remains at exact offset without blue selection");
                        Call(guide, "MoveHit", 1);
                    }
                    Check(Field<int>(guide, "activeHit") == 0, "Next match wraps across sections");
                    Call(guide, "MoveHit", -1); Check(Field<int>(guide, "activeHit") == total - 1, "Previous match wraps");
                    guide.SetTheme(!dark, false); Check(reader.SelectionLength==0 && reader.Text.Substring(reader.SelectionStart,5).ToLowerInvariant() == "служб", "Changing theme preserves current match"); guide.SetTheme(dark, false);
                    search.Text = "Swagger"; Check(list.Items.Count == 1 && reader.Text.Contains("Try it out"), "Swagger instructions found");
                    if (scale == 1F) Capture(guide, "guide-search-" + (dark ? "dark" : "light"));
                    search.Text = "zzzz-no-match-123"; Check(list.Items.Count == 0 && !Field<BrandButton>(guide, "next").Enabled && reader.Text.Contains("Ничего не найдено"), "No-result state");
                    search.Clear(); Check(list.Items.Count == UserGuide.Sections.Length, "Reset restores contents");
                    guide.ClientSize = new Size((int)(940 * scale), (int)(660 * scale)); Application.DoEvents();
                    Check(reader.Width > 300 * scale && reader.Height >= Math.Min(250 * scale,Screen.FromControl(guide).WorkingArea.Height*0.4), "Reading area usable at minimum size scale="+scale+" reader="+reader.Size+" window="+guide.ClientSize);
                    if (scale == 1F) Capture(guide, "guide-min-" + (dark ? "dark" : "light"));
                    guide.Close();
                }
                foreach (bool dark in new[] { false, true }) using (var form = new MainForm(null, true))
                {
                    form.SetTheme(dark, false); Show(form, 1F);
                    var banner = Field<PictureBox>(form, "brandImage");
                    Check(banner.Image != null && banner.Cursor == Cursors.Hand && banner.AccessibleDescription.Contains("https://www.datakrat.ru/"), "Service banner follows shared link behavior");
                    Check(Field<ToolTip>(form, "toolTips").GetToolTip(banner) == "Официальный сайт", "Service banner tooltip");
                    Set(form, "selectedPath", @"C:\Example\service.exe"); Set(form, "verified", true);
                    var matches = Field<List<ServiceEntry>>(form, "matches"); matches.Clear();
                    foreach (string state in new[] { "Running", "Stopped", "Start Pending", "Stop Pending", "Paused", "Pause Pending", "Continue Pending", "Unknown" })
                    {
                        matches.Clear(); matches.Add(new ServiceEntry { Name = "Example", DisplayName = "Example service", State = state }); Call(form, "ShowRegistrationStatus");
                        string title = Field<Label>(form, "statusTitle").Text;
                        Check(title == MainForm.RegistrationText(state), "Header reflects the single service state");
                        Check(!Field<Label>(form, "statusDetail").Visible && !Field<Label>(form, "hint").Visible, "Both lower lines stay hidden for all service states");
                    }
                    matches[0].State = "Running"; Call(form, "ShowRegistrationStatus");
                    Capture(form, "services-clean-" + (dark ? "dark" : "light"));
                    Call(form, "SetStatus", "Ошибка проверки", "Диагностическая подробность", Color.Firebrick);
                    Check(Field<TextBox>(form, "log").Text.Contains("Диагностическая подробность"), "Error detail remains in journal");
                    Check(!Field<Label>(form, "statusDetail").Visible, "Errors do not restore removed line"); form.Close();
                }
                using (var hub = new ManagerHub())
                {
                    Show(hub, 1F); var buttons = All(hub).OfType<BrandButton>();
                    Check(buttons.Count(button => button.Text == "Инструкция") == 1, "Dedicated guide launcher button");
                    Check(buttons.Count(button => button.Primary) == 5, "Five module cards retained"); Capture(hub, "hub-guide"); hub.Close();
                }
                Console.WriteLine("PASS: " + count + " guide/search/service UI checks. Both themes; simulated 100/125/150/200%. No service operations or installers."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
