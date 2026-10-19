using System;
using System.Collections.Generic;
using System.Drawing;
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
        private static void Click(DataGridView grid, Point point)
        {
            IntPtr xy = new IntPtr((point.Y << 16) | (point.X & 65535));
            SendMessage(grid.Handle, 0x201, new IntPtr(1), xy); SendMessage(grid.Handle, 0x202, IntPtr.Zero, xy); Application.DoEvents();
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
                        Button about = Button(form, "О программе"), back = Button(form, "В меню"), report = Button(form, "Сохранить отчёт");
                        Check(about.Top == back.Top && back.Top == report.Top && about.Height >= 38, "Library footer buttons share baseline and useful height");
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
                        Button back = Button(form, "В меню"), about = Button(form, "О программе");
                        Check(back.Top == about.Top && back.Height == about.Height && back.Height >= 38, "Service footer buttons aligned");
                        back.PerformClick(); Check(!form.Visible && form.MenuRequested, "Standalone service module requests launcher on return");
                    }
                    using (var hub = new ManagerHub())
                    {
                        hub.SetTheme(dark, false); Show(hub);
                        foreach (string title in new[] { "Менеджер Служб", "Bars Component Manager 3000" })
                        {
                            Button open = Button(hub, title); Control ancestor = open.Parent; while (!(ancestor is Card)) ancestor = ancestor.Parent;
                            Point position = ancestor.PointToClient(open.PointToScreen(Point.Empty));
                            Check(ancestor.ClientSize.Height - position.Y - open.Height >= 13, "Launcher button has bottom inset");
                            Check(ancestor.ClientSize.Width - position.X - open.Width >= 22, "Launcher button shifted left from right frame");
                            Check(position.Y > ancestor.Height / 2 && open.Height >= 50, "Launcher button sits below description");
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
                foreach(bool menu in new[] {true,false})
                using(var hub=new ManagerHub())
                using(var module=new LibraryManagerForm(true))
                {
                    Show(hub);
                    module.StartPosition=FormStartPosition.Manual;module.Location=new Point(-22000,-22000);module.ShowInTaskbar=false;
                    module.Shown+=delegate { module.BeginInvoke((MethodInvoker)delegate { if(menu)Button(module,"В меню").PerformClick();else module.Close(); }); };
                    typeof(ManagerHub).GetMethod("OpenModule",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hub,new object[]{module});
                    Check(menu ? hub.Visible && !hub.IsDisposed : hub.IsDisposed,"Back returns launcher; window close exits whole manager");
                    if(!hub.IsDisposed)hub.Close();
                }
                Console.WriteLine("PASS: " + count + " UI rendering/input/navigation checks in both themes; no installation or SCM changes."); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
