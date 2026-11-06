using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class UiPolishTests
    {
        private static int count;
        private static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
        private static IEnumerable<Control> Children(Control parent) { foreach (Control c in parent.Controls) { yield return c; foreach (Control child in Children(c)) yield return child; } }
        private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
        private static Button Button(Form form, string text) { foreach (Control c in Children(form)) if (c is Button && c.Text == text) return (Button)c; throw new Exception("Button absent: " + text); }
        private static void Show(Form form) { form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-22000, -22000); form.ShowInTaskbar = false; form.Show(); Application.DoEvents(); }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr handle);
        private static void Click(DataGridView grid, Point point)
        {
            IntPtr xy = new IntPtr((point.Y << 16) | (point.X & 65535));
            SendMessage(grid.Handle, 0x201, new IntPtr(1), xy); SendMessage(grid.Handle, 0x202, IntPtr.Zero, xy); Application.DoEvents();
        }
        private static void VerifyAbout(Form owner, string title, string description)
        {
            Exception failure = null;
            owner.BeginInvoke((MethodInvoker)delegate
            {
                AboutDialog dialog = Application.OpenForms.OfType<AboutDialog>().Last();
                try
                {
                    var labels = Children(dialog).OfType<Label>().ToArray();
                    Check(labels.Any(l => l.Text == title) && labels.Any(l => l.Text == description), "About identifies only the requested module, or both modules for launcher");
                    Label author = labels.Single(l => l.Text.StartsWith("Создано мной"));
                    string[] lines = author.Text.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    Check(lines.Length == 2 && lines[0] == "Создано мной с использованием вражеской нейросети," &&
                        lines[1] == "C# и Windows Powershell. 2024-2026.", "Author text has exactly the two requested lines");
                    Check(lines.All(line => TextRenderer.MeasureText(line, author.Font).Width <= author.Width) &&
                        author.Font.Height * 2 <= author.Height, "Both author lines fit without wrapping or clipping beside close button");
                }
                catch (Exception ex) { failure = ex; }
                finally { dialog.Close(); }
            });
            Button(owner, "О программе").PerformClick();
            if (failure != null) throw failure;
        }
        [STAThread] public static int Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                foreach (bool dark in new[] { false, true })
                {
                    using (var form = new LibraryManagerForm(true))
                    {
                        form.SetTheme(dark, false); Show(form);
                        var grid = Field<ServiceGrid>(form, "grid");
                        Check(form.Text == "Bars Component Manager 3000", "Library module uses English product name");
                        Check(Field<RefreshToggle>(form, "monitor").Text == "Проверять каждый 30с.", "Library toggle caption");
                        using (var dialog = new AboutDialog(ThemePalette.Create(dark), form.Icon, "Bars Component Manager 3000"))
                        {
                            Show(dialog); var label = System.Linq.Enumerable.First(System.Linq.Enumerable.OfType<Label>(Children(dialog)), l => l.Text == "Bars Component Manager 3000");
                            Check(TextRenderer.MeasureText(label.Text, label.Font).Width <= label.Width, "English component product name fits About dialog"); dialog.Close();
                        }
                        Button back = Button(form, "В меню"), report = Button(form, "Открыть журнал"), restart = Button(form,"Перезагрузка");
                        Check(!Children(form).OfType<Button>().Any(b=>b.Text=="О программе"),"About exists only in launcher");
                        Check(restart.Left<back.Left && back.Bottom==report.Bottom && back.Height>=38,"Restart before menu on aligned footer");
                        Check(Children(form).OfType<Label>().Any(l=>l.Text=="Менеджер системных библиотек Windows"),"Localized content heading preserves product window title");
                        var all = Field<BrandButton>(form, "selectAll");
                        var allChecks = Field<Dictionary<string, LibraryCheck>>(form, "checks");
                        var notApplicable = ((LibraryPackage)grid.Rows[grid.Rows.Count - 1].Tag).Id;
                        LibraryState originalState = allChecks[notApplicable].State; allChecks[notApplicable].State = LibraryState.NotApplicable;
                        all.PerformClick();
                        Check(Field<HashSet<string>>(form, "selection").Count == allChecks.Count - 1 && all.Text == "Снять выбор", "Select all marks every applicable row, including manually chosen previews");
                        Check(!Field<HashSet<string>>(form, "selection").Contains(notApplicable), "Select all excludes incompatible OS package");
                        all.PerformClick(); Check(Field<HashSet<string>>(form, "selection").Count == 0 && all.Text == "Выбрать всё", "Second select-all click clears all marks");
                        var busyFlag = typeof(LibraryManagerForm).GetField("installing", BindingFlags.Instance | BindingFlags.NonPublic); busyFlag.SetValue(form, true);
                        all.PerformClick(); busyFlag.SetValue(form, false); Check(Field<HashSet<string>>(form, "selection").Count == 0, "Select-all ignores clicks during installation");
                        allChecks[notApplicable].State = originalState;
                        Check(grid.CellBorderStyle == DataGridViewCellBorderStyle.Single, "Library cell borders on both axes");
                        grid.FirstDisplayedScrollingRowIndex = 0; grid.CurrentCell = grid.Rows[0].Cells[1];
                        Rectangle cell = grid.GetCellDisplayRectangle(0, 0, false);
                        Point corner = new Point(cell.Left + 4, cell.Top + 4);
                        bool before = Convert.ToBoolean(grid.Rows[0].Cells[0].Value);
                        Click(grid, corner);
                        Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value) != before, "First click at corner outside checkbox glyph toggles once");
                        string id = ((LibraryPackage)grid.Rows[0].Tag).Id;
                        Check(Field<HashSet<string>>(form, "selection").Contains(id) == !before, "Click updates installation selection");
                        Click(grid, new Point(cell.Right - 4, cell.Bottom - 4));
                        Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value) == before, "Opposite corner toggles once");
                        var key = new KeyEventArgs(Keys.Space);
                        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(grid, new object[] { key });
                        Check(key.Handled && key.SuppressKeyPress && Convert.ToBoolean(grid.Rows[0].Cells[0].Value) != before, "Space toggles without starting native edit or double toggling");
                        var installing = typeof(LibraryManagerForm).GetField("installing", BindingFlags.Instance | BindingFlags.NonPublic);
                        installing.SetValue(form, true); Click(grid, corner); installing.SetValue(form, false);
                        Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value) != before, "Selection locked during installation");
                        var checks = Field<Dictionary<string, LibraryCheck>>(form, "checks");
                        LibraryState old = checks[id].State; checks[id].State = LibraryState.NotApplicable;
                        Click(grid, corner); checks[id].State = old;
                        Check(Convert.ToBoolean(grid.Rows[0].Cells[0].Value) != before, "Unavailable package cannot be toggled");
                        using (var bitmap = new Bitmap(grid.Width, grid.Height))
                        {
                            grid.DrawToBitmap(bitmap, grid.ClientRectangle);
                            Check(bitmap.GetPixel(cell.Left + 10, cell.Bottom - 1).ToArgb() == grid.GridColor.ToArgb(), "Custom checkbox paints bottom border");
                            Rectangle textCell = grid.GetCellDisplayRectangle(1, 0, false);
                            Check(bitmap.GetPixel(textCell.Right - 1, textCell.Top + 5).ToArgb() == grid.GridColor.ToArgb(), "Text cells retain vertical border after selection");
                        }
                        var flag = typeof(LibraryManagerForm).GetField("installing", BindingFlags.Instance | BindingFlags.NonPublic);
                        var confirm = typeof(LibraryManagerForm).GetField("confirmCancellation", BindingFlags.Instance | BindingFlags.NonPublic);
                        var cancelFlag = typeof(LibraryManagerForm).GetField("cancelRequested", BindingFlags.Instance | BindingFlags.NonPublic);
                        flag.SetValue(form, true); confirm.SetValue(form, new Func<bool, DialogResult>(closing => DialogResult.No));
                        form.Close(); Check(form.Visible && !(bool)cancelFlag.GetValue(form), "Declining exit keeps installation and window active");
                        confirm.SetValue(form, new Func<bool, DialogResult>(closing => DialogResult.Yes));
                        form.Close(); Check(form.Visible && (bool)cancelFlag.GetValue(form) && Field<bool>(form,"closeAfterCancel"), "Confirming exit requests cancellation then automatic close after cleanup");
                        flag.SetValue(form, false);
                        back.PerformClick(); Check(!form.Visible && form.MenuRequested, "Library return button requests menu after cancellation cleanup");
                    }
                    using (var form = new MainForm(null, true))
                    {
                        form.SetTheme(dark, false); Show(form);
                        Check(Field<RefreshToggle>(form, "autoRefreshToggle").Text == "Проверять каждый 10с.", "Service toggle caption");
                        Check(Field<DataGridView>(form, "servicesGrid").CellBorderStyle == DataGridViewCellBorderStyle.Single, "Service cell borders on both axes");
                        Button back = Button(form, "В меню");
                        Check(!Children(form).OfType<Button>().Any(b=>b.Text=="О программе"),"Service About removed");
                        Check(back.Height>=38,"Service menu remains usable");
                        back.PerformClick(); Check(!form.Visible && form.MenuRequested, "Standalone service module requests launcher on return");
                    }
                    using (var hub = new ManagerHub())
                    {
                        hub.SetTheme(dark, false); Show(hub);
                        VerifyAbout(hub, "Bars Manager 3000", "Управление Барсом. Я главная тут");
                        foreach (string title in new[] { "Менеджер служб", "Менеджер библиотек" })
                        {
                            Button open = Button(hub, title); Control ancestor = open.Parent; while (!(ancestor is Card)) ancestor = ancestor.Parent;
                            Point position = ancestor.PointToClient(open.PointToScreen(Point.Empty));
                            Check(ancestor.ClientSize.Height - position.Y - open.Height >= 13, "Launcher button has bottom inset");
                            Check(ancestor.ClientSize.Width - position.X - open.Width >= 22, "Launcher button shifted left from right frame");
                            Check(position.Y > ancestor.Height / 2 && open.Height >= 50, "Launcher button sits below description: y=" + position.Y + " card=" + ancestor.Height + " button=" + open.Height);
                            Check(open.Width >= TextRenderer.MeasureText(open.Text, open.Font).Width + 16, "Launcher action text fits");
                        }
                        hub.Close();
                    }
                    using (var parent = new Panel { BackColor = Color.White })
                    using (var card = new Card { Size = new Size(180, 90) })
                    using (var bitmap = new Bitmap(180, 90))
                    using (var graphics = Graphics.FromImage(bitmap))
                    {
                        parent.Controls.Add(card); graphics.Clear(Color.Magenta);
                        Rectangle clip = new Rectangle(50, 30, 20, 20); graphics.SetClip(clip);
                        typeof(Card).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(card, new object[] { new PaintEventArgs(graphics, clip) });
                        Check(bitmap.GetPixel(5, 5).ToArgb() == Color.Magenta.ToArgb(), "Partial card paint leaves unrelated outline untouched");
                    }
                }
                Console.WriteLine("PASS: " + count + " UI rendering/input checks in both themes; no installation or SCM changes."); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
