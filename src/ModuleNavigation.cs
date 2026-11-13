using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class ModuleHeadingStyle
    {
        internal const int Left=12, Top=8;
        internal static Label Create(string text,bool alignToEdge=false)
        { return new FlushLabel {InkLeft=alignToEdge?(System.Func<Graphics,int>)(g=>0):null,Text=text,Font=new Font("Segoe UI",15F,FontStyle.Bold),Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true}; }
    }
    internal interface IModuleOperation
    {
        string ActiveOperation { get; }
    }
    internal static class ModuleNavigation
    {
        internal const int FooterButtonWidth = 164, FooterButtonHeight = 40;
        internal static bool ReturnToHub()
        {
            var context = ManagerApplicationContext.ActiveContext;
            return context != null && context.ShowHub();
        }
        internal static T ShowDialog<T>(Form owner, System.Func<T> factory) where T : Form
        {
            var context=ManagerApplicationContext.ActiveContext;
            T form=context==null?factory():context.Dialog(factory);
            if(form.IsHandleCreated)form.StartPosition=FormStartPosition.Manual;
            var module=form as ManagerModuleForm; if(module!=null)module.SetTheme(ThemePreference.LoadDark(),false);
            try { ScopedDialogs.Show(owner, form); return form; }
            finally { if(context==null)form.Dispose(); }
        }
        internal static Task<DialogResult> ShowOwnedAsync(Form owner, Form dialog, bool lockOwner = true)
        {
            if (owner.IsDisposed || ExitConfirmed) return Task.FromResult(DialogResult.Cancel);
            var completion = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialog.DialogResult = DialogResult.None;
            CloseOnDialogResult(dialog, dialog);
            bool wasEnabled = owner.Enabled;
            dialog.FormClosed += delegate
            {
                if (!owner.IsDisposed)
                {
                    if (lockOwner) owner.Enabled = wasEnabled;
                    if (!ExitConfirmed && owner.Visible && wasEnabled) owner.Activate();
                }
                completion.TrySetResult(dialog.DialogResult);
            };
            dialog.Disposed += delegate { completion.TrySetResult(DialogResult.Cancel); };
            if (lockOwner) owner.Enabled = false;
            CenterOwned(owner, dialog);
            try { dialog.Show(owner); }
            catch { if (lockOwner && !owner.IsDisposed) owner.Enabled = wasEnabled; throw; }
            return completion.Task;
        }

        internal static void CenterOwned(Form owner, Form dialog)
        {
            var area = Screen.FromControl(owner).WorkingArea;
            var bounds = owner.WindowState == FormWindowState.Minimized ? owner.RestoreBounds : owner.Bounds;
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Load += delegate
            {
                var centered = new Rectangle(bounds.Left + (bounds.Width - dialog.Width) / 2,
                    bounds.Top + (bounds.Height - dialog.Height) / 2, dialog.Width, dialog.Height);
                dialog.Bounds = WindowPlacement.Fit(centered, area);
            };
        }

        private static void CloseOnDialogResult(Control container, Form dialog)
        {
            foreach (Control child in container.Controls)
            {
                if (child is IButtonControl)
                    child.Click += delegate
                    {
                        if (!dialog.IsDisposed && dialog.DialogResult != DialogResult.None) dialog.Close();
                    };
                CloseOnDialogResult(child, dialog);
            }
        }

        internal static void EqualButtons(TableLayoutPanel row,Control scroll)
        {
            bool arranging=false;
            row.Layout+=delegate{if(arranging||row.Width<1)return;arranging=true;try{
                int n=row.ColumnCount,gap=System.Math.Max(1,(int)System.Math.Round(10F*row.Font.Height/16F)),inset=scroll.Visible?scroll.Width:0;
                int available=System.Math.Max(n,row.ClientSize.Width-inset),width=System.Math.Max(1,(available-gap*(n-1))/n),extra=available-width*n-gap*(n-1);
                row.Padding=new Padding(0,0,inset,0);
                for(int i=0;i<n;i++){int spacing=i==n-1?0:gap+extra/(n-1)+(i<extra%(n-1)?1:0);var control=row.GetControlFromPosition(i,0);if(control!=null)control.Margin=new Padding(0,0,spacing,0);float wanted=width+spacing;if(row.ColumnStyles[i].SizeType!=SizeType.Absolute||System.Math.Abs(row.ColumnStyles[i].Width-wanted)>.1){row.ColumnStyles[i].SizeType=SizeType.Absolute;row.ColumnStyles[i].Width=wanted;}}
}finally{arranging=false;}};
        }
        internal static bool ExitConfirmed
        { get { var context = ManagerApplicationContext.ActiveContext; return context != null && context.ExitConfirmed; } }

        internal static void AddCloseButton(TableLayoutPanel footer, Button back, Button close, int row)
        {
            int column = footer.ColumnCount++;
            footer.ColumnStyles[column - 1].Width += 10;
            back.Margin = new Padding(0, 0, 10, 0);
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            Configure(close);
            close.Click += delegate { close.FindForm().Close(); };
            footer.Controls.Add(close, column, row);
            StandardFooter(footer, footer.Controls.OfType<Button>().OrderBy(button => footer.GetColumn(button)).ToArray());
        }
        internal static void StandardFooter(TableLayoutPanel row, params Button[] buttons)
        { StandardFooterWithGap(row,10,buttons); }
        internal static void RightAlignedFooter(TableLayoutPanel row, params Button[] buttons)
        { LayoutFooter(row,10,true,buttons); }
        internal static void StandardFooterWithGap(TableLayoutPanel row,int gap, params Button[] buttons)
        { LayoutFooter(row,gap,false,buttons); }
        private static void LayoutFooter(TableLayoutPanel row,int gap,bool rightAligned,Button[] buttons)
        {
            var labels = row.Controls.Cast<Control>().Where(control => !(control is Button)).ToArray();
            row.SuspendLayout();
            row.Controls.Clear();
            row.ColumnStyles.Clear();
            row.RowStyles.Clear();
            row.ColumnCount = row.RowCount = 1;
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var footer = new FooterPanel(labels, buttons, gap, rightAligned) { Padding = row.Padding, Dock = DockStyle.Fill, Margin = Padding.Empty };
            row.Padding = Padding.Empty;
            row.Controls.Add(footer, 0, 0);
            row.ResumeLayout(true);
        }

        private sealed class FooterPanel : Panel
        {
            private readonly Control[] labels;
            private readonly Button[] buttons;
            private bool arranging;
            private readonly bool rightAligned;

            private float gapBase;
            internal FooterPanel(Control[] labels, Button[] buttons,int gap,bool rightAligned)
            {
                gapBase=gap;
                this.rightAligned=rightAligned;
                this.labels = labels;
                this.buttons = buttons;
                foreach (var control in labels.Cast<Control>().Concat(buttons))
                {
                    control.Dock = DockStyle.None;
                    control.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                    control.Margin = Padding.Empty;
                    Controls.Add(control);
                }
            }

            protected override void ScaleControl(SizeF factor,BoundsSpecified specified)
            {
                gapBase *= factor.Width;
                base.ScaleControl(factor,specified);
            }
            protected override void OnLayout(LayoutEventArgs e)
            {
                if (arranging) return;
                arranging = true;
                try
                {
                    base.OnLayout(e);
                    if (buttons == null || buttons.Length == 0) return;
                    var area = DisplayRectangle;
                    if (area.Width <= 0 || area.Height <= 0) return;
                    float scale = 1F;
                    if (IsHandleCreated) using (var graphics = CreateGraphics()) scale = graphics.DpiX / 96F;
                    int gap = Math.Max(1, (int)Math.Round(gapBase));
                    bool fill = !rightAligned && labels.Length == 0;
                    int width = fill
                        ? Math.Max(1, (area.Width - gap * (buttons.Length - 1)) / buttons.Length)
                        : Math.Min((int)Math.Round(FooterButtonWidth * scale),
                            Math.Max(1, (area.Width - gap * (buttons.Length - 1)) / buttons.Length));
                    int height = Math.Min(area.Height, (int)Math.Round(FooterButtonHeight * scale));
                    int x = fill ? area.Left : area.Right - width * buttons.Length - gap * (buttons.Length - 1);
                    int y = area.Top + (area.Height - height) / 2;
                    var labelBounds = new Rectangle(area.Left, area.Top, Math.Max(0, x - area.Left - gap), area.Height);
                    foreach (var label in labels)
                        if (label.Bounds != labelBounds) label.Bounds = labelBounds;
                    int extra = fill ? area.Width - width * buttons.Length - gap * (buttons.Length - 1) : 0;
                    int buttonIndex = 0;
                    foreach (var button in buttons)
                    {
                        var bounds = new Rectangle(x, y, width, height);
                        if (button.Bounds != bounds) button.Bounds = bounds;
                        x += width + gap + (buttonIndex < extra ? 1 : 0);
                        buttonIndex++;
                    }
                }
                finally { arranging = false; }
            }
        }

        internal static void Configure(Button button)
        {button.Dock=DockStyle.None;button.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;button.Margin=Padding.Empty;button.Size=new Size(140,40);}
    }
}
