using System;
using System.Drawing;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace ServiceTool
{
    // Exercises only isolated UI controls; no service, saved preference, or clipboard access.
    internal static class JournalScrollTests
    {
        private static int checks;

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                using (Form host = new Form())
                using (JournalTextBox journal = new JournalTextBox())
                using (JournalScrollBar bar = new JournalScrollBar())
                {
                    host.ShowInTaskbar = false;
                    host.StartPosition = FormStartPosition.Manual;
                    host.Location = new Point(-30000, -30000);
                    host.ClientSize = new Size(600, 400);
                    host.Opacity = 0;
                    journal.Bounds = new Rectangle(16, 16, 420, 170);
                    journal.Multiline = true;
                    journal.ReadOnly = true;
                    journal.WordWrap = true;
                    journal.BorderStyle = BorderStyle.None;
                    journal.Font = new Font("Segoe UI", 10F);
                    bar.Bounds = new Rectangle(442, 16, 18, 170);
                    host.Controls.Add(journal);
                    host.Controls.Add(bar);
                    bar.Bind(journal);
                    host.Show();
                    Pump();

                    VerifyVisibility(journal, bar);
                    VerifyScrollingAndSelection(journal, bar);
                    VerifyInput(journal, bar);
                    VerifyWrappingAndResize(journal, bar);
                    VerifyTheme(journal, bar);
                    VerifyRecreatedHandle(journal, bar);
                    host.Close();
                }
                Console.WriteLine("PASS: " + checks + " journal scrolling assertions. Isolated controls only; no service, storage, or clipboard operation.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void VerifyVisibility(JournalTextBox journal, JournalScrollBar bar)
        {
            Assert(journal.ScrollBars == ScrollBars.None, "Native white scrollbar is disabled");
            Assert(journal.ReadOnly && journal.Multiline && journal.WordWrap, "Journal retains selectable multiline text");
            journal.Text = String.Empty;
            Pump();
            Assert(bar.Visible && !bar.ThumbBounds.IsEmpty, "Empty journal keeps its fixed thumb"); int fixedHeight=bar.ThumbBounds.Height;
            journal.Text = ".exe службы BARS выбран.\r\nСлужба установлена.\r\nГотово.";
            Pump();
            Assert(bar.Visible, "Fitting journal keeps its scrollbar");
            journal.Text = LongLog();
            journal.ScrollToLine(0);
            Pump();
            Assert(bar.Visible, "Long session history exposes the custom scrollbar"); Assert(bar.ThumbBounds.Height==fixedHeight,"Thumb length is independent of log volume");
            Assert(journal.TotalLineCount >= 150, "All long-log entries are available");
            Assert(journal.VisibleLineCount > 0 && journal.VisibleLineCount < journal.TotalLineCount, "Visible capacity reflects a real text viewport");
            Assert(bar.ThumbBounds.Height > 0 && bar.TrackBounds.Contains(bar.ThumbBounds), "Thumb remains inside its track");
        }

        private static void VerifyScrollingAndSelection(JournalTextBox journal, JournalScrollBar bar)
        {
            int changes = 0;
            journal.ViewportChanged += delegate { changes++; };
            int start = journal.Text.IndexOf("0075", StringComparison.Ordinal);
            journal.Select(start, 4);
            journal.ScrollToLine(30);
            Pump();
            Assert(journal.FirstVisibleLine == 30, "Direct scrolling reaches the requested history entry");
            Assert(journal.SelectionStart == start && journal.SelectionLength == 4 && journal.SelectedText == "0075", "Scrolling preserves the selected text for copying");
            journal.ScrollByLines(7);
            Assert(journal.FirstVisibleLine == 37, "Relative scrolling moves through history");
            journal.ScrollToLine(Int32.MaxValue);
            Pump();
            Assert(journal.FirstVisibleLine == journal.MaximumFirstLine, "Direct scrolling clamps at the final viewport");
            Assert(journal.FirstVisibleLine + journal.VisibleLineCount >= journal.TotalLineCount, "Final viewport reaches the end of the journal");
            journal.ScrollToLine(Int32.MinValue);
            Pump();
            Assert(journal.FirstVisibleLine == 0, "Direct scrolling clamps at the first entry");
            Assert(changes > 0, "Viewport changes notify the bound scrollbar");
            Assert(bar.ThumbBounds.Top == bar.TrackBounds.Top, "Thumb returns to the top with the text");
        }

        private static void VerifyInput(JournalTextBox journal, JournalScrollBar bar)
        {
            journal.ScrollToLine(0);
            Raise(bar, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 9, 30, -120));
            Pump();
            Assert(journal.FirstVisibleLine > 0, "Wheel over the scrollbar moves the journal");
            int afterBarWheel = journal.FirstVisibleLine;
            Raise(journal, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 30, 30, -120));
            Pump();
            Assert(journal.FirstVisibleLine > afterBarWheel, "Wheel over the text moves the same journal viewport");

            journal.ScrollToLine(0);
            Pump();
            Rectangle thumb = bar.ThumbBounds;
            int x = thumb.Left + thumb.Width / 2;
            int y = thumb.Top + thumb.Height / 2;
            Raise(bar, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            Raise(bar, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, x, bar.TrackBounds.Bottom + 100, 0));
            Raise(bar, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, x, bar.TrackBounds.Bottom + 100, 0));
            Pump();
            Assert(journal.FirstVisibleLine == journal.MaximumFirstLine, "Dragging beyond the lower track reaches and clamps at the end");
            Assert(!bar.Capture, "Releasing the drag releases pointer capture");
            thumb = bar.ThumbBounds;
            y = thumb.Top + thumb.Height / 2;
            Raise(bar, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
            Raise(bar, "OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, x, bar.TrackBounds.Top - 100, 0));
            Raise(bar, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, x, bar.TrackBounds.Top - 100, 0));
            Pump();
            Assert(journal.FirstVisibleLine == 0, "Dragging beyond the upper track reaches and clamps at the beginning");

            int pagePoint = Math.Min(bar.TrackBounds.Bottom - 1, bar.ThumbBounds.Bottom + 15);
            Raise(bar, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, x, pagePoint, 0));
            Raise(bar, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, x, pagePoint, 0));
            Pump();
            Assert(journal.FirstVisibleLine > 0, "Clicking the track pages through the history");

            Raise(bar, "OnKeyDown", new KeyEventArgs(Keys.End));
            Assert(journal.FirstVisibleLine == journal.MaximumFirstLine, "End key reaches the latest log entries");
            Raise(bar, "OnKeyDown", new KeyEventArgs(Keys.Home));
            Assert(journal.FirstVisibleLine == 0, "Home key reaches the first log entries");
            Raise(bar, "OnKeyDown", new KeyEventArgs(Keys.PageDown));
            Assert(journal.FirstVisibleLine > 0, "PageDown key pages through the journal");
        }

        private static void VerifyWrappingAndResize(JournalTextBox journal, JournalScrollBar bar)
        {
            journal.Text = ".exe службы BARS: " + new string('Ж', 2100);
            journal.Width = 420;
            journal.Height = 90;
            bar.Height = 90;
            journal.ScrollToLine(0);
            Pump();
            int wideLines = journal.TotalLineCount;
            journal.Width = 190;
            Pump();
            Assert(journal.TotalLineCount > wideLines, "Narrowing wraps long journal messages into more visible lines");
            Assert(bar.Visible, "Wrapped overflow retains scrolling");
            journal.ScrollToLine(Int32.MaxValue);
            Assert(journal.FirstVisibleLine == journal.MaximumFirstLine, "Wrapped messages can be scrolled to their end (first=" + journal.FirstVisibleLine + ", maximum=" + journal.MaximumFirstLine + ", total=" + journal.TotalLineCount + ", visible=" + journal.VisibleLineCount + ")");
            journal.Text = ".exe службы BARS выбран.\r\nГотово.";
            journal.Height = 250;
            bar.Height = 250;
            Pump();
            Assert(bar.Visible && journal.FirstVisibleLine == 0, "Shorter content and a larger viewport remove stale scroll state");
            journal.Width = 420;
            journal.Height = 170;
            bar.Height = 170;
            journal.Text = LongLog();
            journal.ScrollToLine(25);
            Pump();
            Assert(bar.Visible && journal.FirstVisibleLine == 25, "Overflow scrolling works again after resizing");
        }

        private static void VerifyTheme(JournalTextBox journal, JournalScrollBar bar)
        {
            int start = journal.Text.IndexOf("0060", StringComparison.Ordinal);
            journal.Select(start, 4);
            journal.ScrollToLine(25);
            int top = journal.FirstVisibleLine;
            foreach (bool dark in new bool[] { true, false, true })
            {
                ThemePalette palette = ThemePalette.Create(dark);
                journal.BackColor = palette.Surface;
                journal.ForeColor = palette.Text;
                bar.Palette = palette;
                Pump();
                Assert(journal.FirstVisibleLine == top && journal.SelectionStart == start && journal.SelectedText == "0060", "Theme changes preserve scroll position and text selection");
                using (Bitmap image = new Bitmap(bar.Width, bar.Height))
                {
                    bar.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                    if (dark)
                    {
                        int white = 0;
                        for (int y = 0; y < image.Height; y++)
                            for (int x = 0; x < image.Width; x++)
                            {
                                Color pixel = image.GetPixel(x, y);
                                if (pixel.R > 245 && pixel.G > 245 && pixel.B > 245) white++;
                            }
                        Assert(white == 0, "Dark scrollbar renders without the native white strip");
                    }
                    Rectangle thumb = bar.ThumbBounds;
                    Color thumbPixel = image.GetPixel(thumb.Left + thumb.Width / 2, thumb.Top + thumb.Height / 2);
                    Color trackPixel = image.GetPixel(Math.Min(image.Width - 1, bar.TrackBounds.Left), Math.Max(0, bar.TrackBounds.Top));
                    Assert(thumbPixel.ToArgb() != trackPixel.ToArgb(), "Thumb is visibly distinct from its themed track");
                }
            }
        }

        private static void VerifyRecreatedHandle(JournalTextBox journal, JournalScrollBar bar)
        {
            int total = journal.TotalLineCount;
            typeof(Control).GetMethod("RecreateHandle", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(journal, null);
            Pump();
            Assert(journal.TotalLineCount == total && bar.Visible, "Recreated native text handle retains the history and bound scrollbar");
            journal.ScrollToLine(40);
            Assert(journal.FirstVisibleLine == 40, "Scrolling still works after native handle recreation");
            journal.ScrollToLine(Int32.MaxValue);
            Assert(journal.FirstVisibleLine == journal.MaximumFirstLine, "Recreated handle can still reach the final viewport");
        }

        private static string LongLog()
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < 150; i++)
                text.AppendFormat("{0:0000}: .exe службы BARS — запись журнала.\r\n", i);
            return text.ToString().TrimEnd('\r', '\n');
        }

        private static void Pump() { Application.DoEvents(); }

        private static void Raise(Control control, string method, EventArgs args)
        {
            MethodInfo handler = control.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (handler == null) throw new InvalidOperationException("Missing input handler: " + method);
            handler.Invoke(control, new object[] { args });
        }

        private static void Assert(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Assertion failed: " + message);
        }
    }
}
