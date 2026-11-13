using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class SnowBanner : PictureBox
    {
        private readonly Timer timer = new Timer { Interval = 40 };
        private readonly PointF[] seeds = new PointF[28];
        private float elapsed;
        private bool disposingTimer;

        internal SnowBanner()
        {
            DoubleBuffered = true;
            var random = new Random(712);
            for (int i = 0; i < seeds.Length; i++)
                seeds[i] = new PointF((float)random.NextDouble(), (float)random.NextDouble());
            timer.Tick += delegate
            {
                var window = FindForm();
                if (!Visible || window == null || window.WindowState == FormWindowState.Minimized) return;
                elapsed = (elapsed + .04f) % 3600;
                Invalidate();
            };
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateTimer();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateTimer();
        }

        private void UpdateTimer()
        {
            if (timer == null || disposingTimer) return;
            timer.Enabled = Visible && IsHandleCreated && SystemInformation.IsMenuAnimationEnabled;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Image == null || !timer.Enabled) return;
            float height = ClientSize.Height;
            float imageWidth = Math.Min(ClientSize.Width, height * Image.Width / Image.Height);
            float span = Math.Min(ClientSize.Width, imageWidth + height * 3);
            float left = (ClientSize.Width - span) / 2;
            var saved = e.Graphics.Save();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(BackColor.GetBrightness() < .5f
                ? Color.FromArgb(178, 235, 248, 255) : Color.FromArgb(155, 76, 124, 153)))
            {
                for (int i = 0; i < seeds.Length; i++)
                {
                    var seed = seeds[i];
                    float x = left + seed.X * span + (float)Math.Sin(elapsed + i) * height * .09f;
                    if (Math.Abs(x - ClientSize.Width / 2f) < imageWidth / 2f + 4) continue;
                    float y = (seed.Y + elapsed * (.14f + .027f * (i % 5))) % 1f * height;
                    float size = (1.7f + (i % 4) * .55f) * e.Graphics.DpiX / 96f;
                    e.Graphics.FillEllipse(brush, x, y, size, size);
                }
            }
            e.Graphics.Restore(saved);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposingTimer) { disposingTimer = true; timer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}