using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class ResponsiveLayoutTests
    {
        private static int count;
        private static void Check(bool value, string message)
        { if (!value) throw new Exception(message); count++; }
        private static IEnumerable<Control> Children(Control parent)
        {
            foreach (Control child in parent.Controls)
            { yield return child; foreach (var nested in Children(child)) yield return nested; }
        }
        [STAThread] private static int Main()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                foreach (bool dark in new[] { false, true })
                foreach (float scale in new[] { 1F, 1.25F, 1.5F, 2F })
                {
                    Form[] forms = { new ManagerHub(), new MainForm(null, true), new LibraryManagerForm(true),
                        new SoftwareManagerForm(true), new ToolsDeploymentForm(true),
                        new WindowsFeaturesForm(true, true), new InitialSystemSettingsForm(true) };
                    Size[] limits = { new Size(1160,860), new Size(1440,1040), new Size(1520,1040),
                        new Size(1280,1040), new Size(640,500), new Size(1000,760), new Size(1280,960) };
                    for (int i = 0; i < forms.Length; i++) using (var form = forms[i])
                    {
                        var module = form as ManagerModuleForm;
                        if (module != null) module.SetTheme(dark, false);
                        var services = form as MainForm;
                        if (services != null) services.SetTheme(dark, false);
                        form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000,-20000);
                        form.ShowInTaskbar = false; form.Show(); Application.DoEvents();
                        Rectangle working=Screen.FromControl(form).WorkingArea;
                        Check(form.Width<=working.Width&&form.Height<=working.Height,"Initial window fits actual desktop: "+form.GetType().Name);
                        if(i==1||i==3)
                        {
                            var viewport=Children(form).OfType<TableLayoutPanel>().First().Parent as ScrollableControl;
                            var menu=Children(form).OfType<BrandButton>().Single(b=>b.Text=="В меню");
                            viewport.ScrollControlIntoView(menu);Application.DoEvents();
                            Rectangle menuBounds=viewport.RectangleToClient(menu.RectangleToScreen(menu.ClientRectangle));
                            Check(viewport.ClientRectangle.Contains(menuBounds),"Bottom menu reachable at native DPI: "+form.GetType().Name);
                            viewport.AutoScrollPosition=Point.Empty;Application.DoEvents();
                        }
                        // Normalize the real desktop DPI first; otherwise 125% Windows
                        // multiplied by the test's 125% silently tests 156%, and limits
                        // expressed in 96-DPI units are compared with physical pixels.
                        float nativeScale=form.CurrentAutoScaleDimensions.Width/96F;
                        if(Math.Abs(nativeScale-1F)>0.001F)form.Scale(new SizeF(1F/nativeScale,1F/nativeScale));
                        Application.DoEvents();
                        Size original = form.ClientSize;
                        if (scale != 1F) form.Scale(new SizeF(scale,scale));
                        // Scale may clamp a top-level window to the physical monitor.
                        // Establish the same explicit client size used on restore.
                        form.ClientSize=new Size((int)(original.Width*scale),(int)(original.Height*scale));
                        Application.DoEvents();form.PerformLayout();Application.DoEvents();
                        var content = Children(form).OfType<TableLayoutPanel>().First();
                        var frame = (ScrollableControl)content.Parent;
                        bool compact = i==0 || i==4;
                        Check(Children(form).OfType<BoundedContentPanel>().Any()==compact, "Only launcher and utilities use compact content");
                        int initialWidth = content.Width;
                        foreach (Size size in new[] { new Size(1920,1080), new Size(3840,2160), new Size(2560,1440), new Size(1080,1920), new Size(1200,1920), original, new Size(3840,2160), original })
                        {
                            form.ClientSize = new Size((int)(size.Width*scale), (int)(size.Height*scale));
                            Application.DoEvents(); form.PerformLayout();Application.DoEvents();
                            string context = form.GetType().Name + " / " + scale + " / " + size;
                            if(compact)Check(content.Width <= limits[i].Width*scale+1 && content.Height <= limits[i].Height*scale+1, "Unbounded content: " + context);
                            else Check(content.Width>=frame.ClientSize.Width-1 && content.Height>=frame.ClientSize.Height-1, "Module no longer fills window: " + context + " content="+content.Bounds+" parent="+frame.ClientRectangle);
                            if (!frame.HorizontalScroll.Visible)
                                Check(Math.Abs(content.Left*2+content.Width-frame.ClientSize.Width)<=2, "Horizontal center: " + context);
                            if (!frame.VerticalScroll.Visible)
                                Check(Math.Abs(content.Top*2+content.Height-frame.ClientSize.Height)<=2, "Vertical center: " + context);
                            foreach (var toggle in Children(form).OfType<RefreshToggle>())
                            {
                                Check(toggle.Right == toggle.Parent.ClientSize.Width, "Refresh toggle right edge: " + context);
                                Check(toggle.Parent.ClientRectangle.Contains(toggle.Bounds), "Refresh toggle clipped: " + context);
                            }
                            foreach (var theme in Children(form).OfType<ThemeToggleButton>())
                                Check(theme.Parent.ClientRectangle.Contains(theme.Bounds), "Theme switch clipped: " + context);
                            foreach (var button in Children(form).OfType<BrandButton>().Where(b=>b.Visible))
                                Check(button.Parent.ClientRectangle.Contains(button.Bounds), "Button outside parent: " + context + " / " + button.Text);
                            if (scale==1F && (size.Width==2560 || size.Width==1080))
                            {
                                string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"responsive-previews");Directory.CreateDirectory(directory);
                                // Render only the working area, keeping preview files manageable.
                                using(var bitmap=new Bitmap(content.Width,content.Height))
                                { content.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(directory,form.GetType().Name+"-"+(dark?"dark":"light")+"-"+size.Width+"x"+size.Height+".png")); }
                            }
                        }
                        Check(Math.Abs(content.Width-initialWidth)<=1, "Restore changes working width: " + form.GetType().Name + " / " + scale + " initial="+initialWidth+" final="+content.Width+" original="+original+" client="+form.ClientSize);
                    }
                }
                Console.WriteLine("ResponsiveLayoutTests: "+count+" passed. Both themes; resize/restore; simulated 100/125/150/200%. No system operations.");
                return 0;
            }
            catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
