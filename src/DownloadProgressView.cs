using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ServiceTool
{
    // Transport callbacks only replace a snapshot. Painting and animation stay on the UI thread.
    internal sealed class TransferProgress
    {
        internal string Stage, File, Key;
        internal long NetworkBytes = -1;
        internal long Bytes, Total;
        internal double Overall=-1;
        internal string WorkText;
        internal bool Complete, Paused, Failed, Cancelled, CleanupWarning;
        internal bool Active = true;
    }

    internal class DownloadProgressView : Control
    {
        readonly object sync = new object();
        readonly Timer animation = new Timer { Interval = 16 };
        readonly ToolTip tip = new ToolTip();
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        TransferProgress snapshot = new TransferProgress { Stage = "Ожидание действия", Active=false };
        long operationId; bool operationRunning, operationAnimated;
        int workTotal,workDone;double workFraction;
        internal long OperationRevision {get{lock(sync)return operationId;}}
        internal void BeginOperation(string text,bool animated=true,int tasks=1)
        {
            lock(sync){operationId++;operationRunning=true;operationAnimated=animated;workTotal=animated?Math.Max(0,tasks):0;workDone=0;workFraction=0;snapshot=WorkSnapshot(text);}
        }
        internal Action<string> OperationReporter {
            get { long id;lock(sync)id=operationId;return text=>{lock(sync){if(!operationRunning||id!=operationId)return;UpdateWorkMessage(text);}}; }
        }
        internal void FinishOperation(string text,bool success=false,bool failed=false,bool cancelled=false)
        {
            lock(sync){if(success)workDone=workTotal;var final=WorkSnapshot(OperationMessage.FinishSentence(text));if(success&&workTotal==0)final.Overall=1;operationRunning=false;operationId++;final.Complete=success;final.Failed=failed;final.Cancelled=cancelled;final.Active=false;snapshot=final;}
        }
        TransferProgress WorkSnapshot(string text){return new TransferProgress{Stage=text,Active=operationAnimated,Key="operation-"+operationId,Overall=workTotal>0?Math.Min(.999,(workDone+workFraction)/workTotal):-1,WorkText=workTotal>0?"Выполнено "+workDone+" из "+workTotal+" задач":""};}
        void UpdateWorkMessage(string text){
            if(text.StartsWith("BARS_FRACTION|")){var parts=text.Split(new[]{'|'},4);long done,total;if(parts.Length==4&&Int64.TryParse(parts[1],out done)&&Int64.TryParse(parts[2],out total)&&total>0){workFraction=Math.Max(workFraction,Math.Min(.999,done/(double)total));snapshot=WorkSnapshot(parts[3]);return;}}
            snapshot=WorkSnapshot(text);
        }
        internal void SetWork(int total){lock(sync){if(!operationRunning)return;workTotal=Math.Max(1,total);snapshot=WorkSnapshot(snapshot.Stage);}}
        internal void CompleteStep(){lock(sync){if(!operationRunning)return;workDone=Math.Min(workTotal,workDone+1);workFraction=0;snapshot=WorkSnapshot(snapshot.Stage);}}
        internal bool WorkFinished {get{lock(sync)return workTotal>0&&workDone==workTotal;}}
        internal Control WithJournal(Control journal)
        {
            var area=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Margin=Padding.Empty};
            area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));area.RowStyles.Add(new RowStyle(SizeType.Absolute,112));area.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            Dock=DockStyle.Fill;Margin=new Padding(0,8,0,8);area.Controls.Add(this,0,0);area.Controls.Add(journal,0,1);return area;
        }
        double displayed, speed, sampleTime, lastTime;
        long sampleBytes;
        string sampleFile; bool wasPaused;
        TransferProgress lastPaint;
        internal ThemePalette Palette;
        internal bool InlineDetails { get; set; }
        internal double DisplayedFraction { get { return displayed; } }
        internal TransferProgress Snapshot { get { lock(sync) return snapshot; } }
        internal DownloadProgressView()
        {
            DoubleBuffered = true; Height = 96; Margin = Padding.Empty;
            AccessibleName = "Ход загрузки";
            animation.Tick += delegate { Advance(); };
        }
        internal void Report(TransferProgress progress)
        {
            if (progress == null) return;
            lock(sync){
                if(operationRunning&&workTotal>0){if(progress.Total>0)workFraction=Math.Max(workFraction,Math.Min(.999,progress.Bytes/(double)progress.Total));var overall=WorkSnapshot(progress.Stage);overall.File=progress.File;overall.Bytes=progress.Bytes;overall.Total=progress.Total;snapshot=overall;}
                else snapshot=progress;
            }
        }
        internal void Stage(string stage, string file = null, bool complete = false, bool failed = false, bool cancelled = false)
        {
            if(operationRunning){if(failed)FinishOperation(stage,failed:true);else if(cancelled)FinishOperation(stage,cancelled:true);else if(complete)FinishOperation(stage,success:true);else OperationReporter(stage);return;}
            var previous = Snapshot;
            Report(new TransferProgress { Stage = complete || failed || cancelled ? OperationMessage.FinishSentence(stage) : stage, File = file ?? previous.File, Bytes = 0, Total = 0, Complete = complete, Failed = failed, Cancelled = cancelled, Active=!complete&&!failed&&!cancelled&&stage.IndexOf("заверш",StringComparison.OrdinalIgnoreCase)<0 });
        }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); animation.Enabled = Visible && IsHandleCreated; }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); animation.Enabled = Visible; }
        void Advance()
        {
            var p = Snapshot; double now = clock.Elapsed.TotalSeconds;
            long measured=p.NetworkBytes>=0?p.NetworkBytes:p.Bytes;
            string key=p.Key??p.File;bool reset = sampleFile != key || (p.Overall<0&&measured < sampleBytes);
            if (reset) { sampleFile = key; sampleBytes = measured; sampleTime = now; speed = 0; displayed = 0; }
            if(p.Paused||wasPaused!=p.Paused){speed=0;sampleBytes=measured;sampleTime=now;}wasPaused=p.Paused;
            if (now - sampleTime >= 1) { speed = Math.Max(0, measured - sampleBytes) / (now - sampleTime); sampleBytes = measured; sampleTime = now; }
            double target = p.Complete ? 1 : p.Overall>=0?p.Overall:p.Total > 0 ? Math.Min(.999, Math.Max(0, p.Bytes / (double)p.Total)) : 0;
            double elapsed = Math.Max(0, now - lastTime); lastTime = now;
            // Completion must be atomic: the same snapshot supplies 100% AND a full bar,
            // even when painting precedes the next timer tick or the UI thread is busy.
            if (p.Complete) displayed = 1;
            else if (!p.Paused) displayed = !SystemInformation.IsMenuAnimationEnabled ? target : displayed + (target - displayed) * (1-Math.Exp(-elapsed/.45));
            AccessibleDescription = p.Stage + (p.Overall>=0||p.Total > 0 ? " " + Math.Floor(target * 100) + "%" : "");
            bool interpolating=Math.Abs(target-displayed)>.0005;if(!interpolating&&!p.Paused)displayed=target;
            if(lastPaint==null||lastPaint.File!=p.File)tip.SetToolTip(this,p.File??"");animation.Interval=interpolating?16:100;
            if(interpolating||lastPaint!=p){lastPaint=p;Invalidate();}
        }
        internal static string SizeText(long value) { return (value / 1048576D).ToString("0.0") + " МБ"; }
        static GraphicsPath Capsule(RectangleF rect)
        {
            var path=new GraphicsPath();float diameter=Math.Min(rect.Width,rect.Height);
            if(diameter<=0)return path;
            path.AddArc(rect.Left,rect.Top,diameter,rect.Height,90,180);
            path.AddArc(rect.Right-diameter,rect.Top,diameter,rect.Height,270,180);
            path.CloseFigure();return path;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); var p = Snapshot; var colors = Palette ?? ThemePalette.Create(false);
            float scale = e.Graphics.DpiX / 96F;
            Func<int,int> px = n => (int)Math.Round(n * scale);
            e.Graphics.Clear(Parent == null ? colors.Canvas : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int line = Math.Max(Font.Height, px(18)), gap = px(10), barTop = line + px(10);
            var bar = new Rectangle(1, barTop, Math.Max(1, Width - 2), px(22));
            string percent = p.Complete ? "100%" : p.Overall>=0?Math.Floor(p.Overall*100)+"%":p.Total > 0 ? Math.Floor(Math.Min(.999, p.Bytes / (double)p.Total) * 100) + "%" : "";
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
            int percentWidth = percent.Length == 0 ? 0 : TextRenderer.MeasureText(e.Graphics, "100%", Font, Size.Empty, flags).Width + px(12);
            string stage = p.Stage ?? "";
            if (InlineDetails && !String.IsNullOrEmpty(p.WorkText))
                stage = stage.TrimEnd('.','…') + " · " + p.WorkText + (p.Active ? "…" : ".");
            TextRenderer.DrawText(e.Graphics, stage, Font, new Rectangle(1,0,Math.Max(1,Width-percentWidth-2),line), colors.Text, flags | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, percent, Font, new Rectangle(Width-percentWidth,0,percentWidth,line), colors.Text, flags | TextFormatFlags.Right);
            bool interrupted = p.Failed || p.Cancelled;
            Color interruptionColor = colors.Dark ? Color.FromArgb(255,116,109) : Color.FromArgb(179,41,54);
            using(var shape = Capsule(bar))
            using(var track = new SolidBrush(colors.Dark ? colors.SurfaceAlt : Color.FromArgb(211,223,216)))
            {
                // Do not clip to a rounded region: GDI+ region clipping has aliased edges.
                e.Graphics.FillPath(track, shape);
                Color accent = interrupted ? interruptionColor : p.Paused ? colors.Muted : colors.Accent;
                using(Brush fill = interrupted ? (Brush)new SolidBrush(accent) : new LinearGradientBrush(bar, accent, ControlPaint.Light(accent), 0F))
                {
                    if(p.Overall>=0 || p.Total > 0 || p.Complete)
                    {
                        float width=bar.Width*(float)(p.Complete?1:displayed);
                        if(width>0)using(var filled=Capsule(new RectangleF(bar.Left,bar.Top,width,bar.Height)))e.Graphics.FillPath(fill,filled);
                    }
                }
                using(var outline=new Pen(interrupted ? interruptionColor : colors.Border,Math.Max(1F,scale)))e.Graphics.DrawPath(outline,shape);
            }
            if (InlineDetails) return;
            string details = p.Total > 0 ? SizeText(p.Bytes) + " из " + SizeText(p.Total) : p.Bytes > 0 ? SizeText(p.Bytes) : "";
            if(!String.IsNullOrEmpty(p.WorkText))details=p.WorkText+(details.Length>0?" · "+details:"");
            bool receiving = p.Stage != null && p.Stage.StartsWith("Скачивание") && !p.Paused && !interrupted && !p.Complete;
            if(receiving && speed > 0) { details += " · " + SizeText((long)speed) + "/с"; if(p.Total > p.Bytes) details += " · осталось около " + Math.Ceiling((p.Total-p.Bytes)/speed).ToString("0") + " с"; }
            TextRenderer.DrawText(e.Graphics, details, Font, new Rectangle(1,bar.Bottom+gap,Math.Max(1,Width-2),line), colors.Text, flags | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, p.File ?? "", Font, new Rectangle(1,bar.Bottom+gap+line,Math.Max(1,Width-2),line), colors.Muted, flags | TextFormatFlags.PathEllipsis);
        }
        protected override void Dispose(bool disposing) { if(disposing) {animation.Dispose();tip.Dispose();} base.Dispose(disposing); }
    }
}
