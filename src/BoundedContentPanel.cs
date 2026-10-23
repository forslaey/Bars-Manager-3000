using System;
using System.Drawing;
using System.Windows.Forms;

namespace ServiceTool
{
    // Limit the working area, while allowing the containing window to be maximized.
    // ScaleControl follows both WinForms DPI scaling and explicit Scale calls.
    internal sealed class BoundedContentPanel : Panel
    {
        private readonly Control content;
        private SizeF limit;
        private bool arranging;

        internal BoundedContentPanel(Control content, Size maximum)
        {
            this.content = content;
            limit = maximum;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            AutoScroll = false;
            DoubleBuffered = true;
            content.Dock = DockStyle.None;
            content.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Controls.Add(content);
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            limit = new SizeF(limit.Width * factor.Width, limit.Height * factor.Height);
            base.ScaleControl(factor, specified);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (content == null || arranging) return;
            arranging = true;
            try
            {
                int width = Math.Max(content.MinimumSize.Width, Math.Min(ClientSize.Width, (int)Math.Round(limit.Width)));
                int height = Math.Max(content.MinimumSize.Height, Math.Min(ClientSize.Height, (int)Math.Round(limit.Height)));
                var bounds = new Rectangle(
                    Math.Max(0, (ClientSize.Width - width) / 2),
                    Math.Max(0, (ClientSize.Height - height) / 2), width, height);
                if (content.Bounds != bounds) content.Bounds = bounds;
            }
            finally { arranging = false; }
        }
    }
}
