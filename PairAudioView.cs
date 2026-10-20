using System.Buffers.Binary;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace EngineSoundTool;

/// <summary>Audio and markers share an exact time axis; RPM retains its original index axis.</summary>
public sealed class PairAudioView : FrameworkElement
{
    private sealed record Trace(int Index,short[] Samples,float[] Rpm,double[] Positions,bool Replaced, short[] Minima, short[] Maxima, int LeafCount);
    private Trace[] traces=[];
    private double start, end=1, duration=1, cursor;
    private int playing=-1;
    private BdlDocument? displayed;
    private int displayedFirst=-1;
    private long preparedChangeRevision = -1;
    private long revision;
    private readonly SemaphoreSlim preparationGate = new(1);
    private bool active = true;
    private readonly Brush[] colors=[new SolidColorBrush(Color.FromRgb(184,255,22)),new SolidColorBrush(Color.FromRgb(86,205,220))];
    public event Action<int,double>? SeekRequested;
    public PairAudioView(){MinHeight=360;ClipToBounds=true;ToolTip="Click the waveform to preview from that position. Mouse wheel: zoom the audio timeline. Double-click: show the full duration."; Unloaded += (_,_) => { active=false; revision++; }; Loaded += (_,_) => { active=true; SetPair(displayed,displayedFirst); };}
    public void SetPair(BdlDocument? doc,int first) => _ = SetPairAsync(doc, first);
    private async Task SetPairAsync(BdlDocument? doc,int first)
    {
        long request = ++revision;
        bool same=doc!=null && displayed==doc && displayedFirst==first;
        if(!same)preparedChangeRevision=-1;
        bool preserveRange=same && preparedChangeRevision>=0 && traces.Length>0;
        displayed=doc;displayedFirst=first;
        if (doc is null) { traces=[]; preparedChangeRevision=-1; InvalidateVisual(); return; }
        if (!active) return;
        long change = doc.ChangeRevision;
        if (same && preparedChangeRevision == change && traces.Length > 0) return;
        await preparationGate.WaitAsync();
        try
        {
        if (request != revision) return;
        var prepared = await Task.Run(() =>
        {
        return doc.Sections.Skip(first).Take(2).Select(s =>
        {
            byte[] pcm=doc.GetPcm(s.Index);short[] samples=new short[s.SampleCount];
            for(int i=0;i<samples.Length;i++)samples[i]=BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i*2,2));
            int leaves=1; while(leaves < (samples.Length+255)/256) leaves*=2;
            var minima=Enumerable.Repeat(short.MaxValue,leaves*2).ToArray();
            var maxima=Enumerable.Repeat(short.MinValue,leaves*2).ToArray();
            for(int i=0;i<samples.Length;i++){int leaf=leaves+i/256;minima[leaf]=Math.Min(minima[leaf],samples[i]);maxima[leaf]=Math.Max(maxima[leaf],samples[i]);}
            for(int i=leaves-1;i>0;i--){minima[i]=Math.Min(minima[i*2],minima[i*2+1]);maxima[i]=Math.Max(maxima[i*2],maxima[i*2+1]);}
            return new Trace(s.Index,samples,doc.GetRpmValues(s.Index),doc.GetPositions(s.Index),doc.IsReplaced(s.Index),minima,maxima,leaves);
        }).ToArray();
        });
        if (request != revision || !active || change != doc.ChangeRevision) return;
        traces=prepared;
        preparedChangeRevision=change;
        duration=traces.Length==0 ? 1 : traces.Max(t=>t.Samples.Length/48000.0);
        if(!preserveRange){start=0;end=duration;playing=-1;}InvalidateVisual();
        }
        catch(Exception error){ if(request==revision && active) ToolTip="Unable to prepare the waveform: " + error.Message; }
        finally { preparationGate.Release(); }
    }
    private static (short Min,short Max) Range(Trace trace,int from,int to)
    {
        short min=short.MaxValue,max=short.MinValue;
        while(from<to && from%256!=0){min=Math.Min(min,trace.Samples[from]);max=Math.Max(max,trace.Samples[from++]);}
        while(from<to && to%256!=0){--to;min=Math.Min(min,trace.Samples[to]);max=Math.Max(max,trace.Samples[to]);}
        int left=trace.LeafCount+from/256,right=trace.LeafCount+to/256;
        while(left<right)
        {
            if((left&1)!=0){min=Math.Min(min,trace.Minima[left]);max=Math.Max(max,trace.Maxima[left++]);}
            if((right&1)!=0){--right;min=Math.Min(min,trace.Minima[right]);max=Math.Max(max,trace.Maxima[right]);}
            left/=2;right/=2;
        }
        return(min,max);
    }
    public void SetCursor(int index,double seconds){playing=index;cursor=seconds;InvalidateVisual();}
    private void Text(DrawingContext dc,string text,double x,double y,double size=11,Brush? brush=null)
        =>dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush??Brushes.LightGray,VisualTreeHelper.GetDpi(this).PixelsPerDip),new Point(x,y));
    private Rect WaveRect(int row)
    {
        double h=ActualHeight/Math.Max(1,traces.Length);
        return new Rect(52,row*h+26,Math.Max(1,ActualWidth-66),Math.Max(30,h*.38));
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(26,26,26)),null,new Rect(RenderSize));
        if(traces.Length==0){Text(dc,"Open a bundle to view audio and tables.",16,20,14);return;}
        double rowHeight=ActualHeight/traces.Length;
        for(int row=0;row<traces.Length;row++)
        {
            var t=traces[row];Brush color=colors[row];Rect wave=WaveRect(row);
            Text(dc,$"Section {t.Index:00} · {(t.Replaced ? "Replacement Audio" : "Original Audio")} · {t.Samples.Length/48000.0:F3} s · {t.Positions.Length} position markers",52,row*rowHeight+4,12,color);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(31,31,31)),new Pen(new SolidColorBrush(Color.FromRgb(55,55,55)),.5),wave);
            dc.PushClip(new RectangleGeometry(wave));
            // Min/max bins keep short peaks visible at every zoom level.
            var wavePen=new Pen(color,1);
            int width=Math.Max(1,(int)wave.Width);
            for(int x=0;x<width;x++)
            {
                int a=Math.Clamp((int)((start+(end-start)*x/width)*48000),0,t.Samples.Length);
                int b=Math.Clamp((int)Math.Ceiling((start+(end-start)*(x+1)/width)*48000),0,t.Samples.Length);
                if(a>=b)continue;
                var (min,max)=Range(t,a,b);
                double mid=wave.Top+wave.Height/2;
                dc.DrawLine(wavePen,new Point(wave.Left+x,mid-max/32768.0*wave.Height/2),new Point(wave.Left+x,mid-min/32768.0*wave.Height/2));
            }
            var markerPen=new Pen(new SolidColorBrush(Color.FromArgb(80,184,255,22)),.7);
            foreach(double sample in t.Positions)
            {
                double seconds=sample/48000;
                if(seconds<start||seconds>end)continue;
                double x=wave.Left+(seconds-start)/(end-start)*wave.Width;
                dc.DrawLine(markerPen,new Point(x,wave.Top),new Point(x,wave.Bottom));
            }
            if(playing==t.Index && cursor>=start && cursor<=end)
            {
                double x=wave.Left+(cursor-start)/(end-start)*wave.Width;
                dc.DrawLine(new Pen(Brushes.Crimson,2),new Point(x,wave.Top),new Point(x,wave.Bottom));
            }
            dc.Pop();
            Text(dc,"+1",20,wave.Top);Text(dc,"−1",20,wave.Bottom-14);
            for(int i=0;i<=4;i++)Text(dc,$"{start+(end-start)*i/4:F2} s",wave.Left+wave.Width*i/4-12,wave.Bottom+2);
            double rpmTop=wave.Bottom+33,rpmHeight=Math.Max(24,rowHeight-(rpmTop-row*rowHeight)-24);
            var rpmRect=new Rect(wave.Left,rpmTop,wave.Width,rpmHeight);
            Text(dc,"RPM values · separate table axis; mapping to audio time is unconfirmed",wave.Left,rpmTop-16,10);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(31,31,31)),new Pen(new SolidColorBrush(Color.FromRgb(55,55,55)),.5),rpmRect);
            float low=t.Rpm.Min(),high=t.Rpm.Max();double range=Math.Max(1,high-low);
            var geometry=new StreamGeometry();
            using(var g=geometry.Open())
                for(int i=0;i<t.Rpm.Length;i++)
                {
                    var point=new Point(rpmRect.Left+i/(double)(t.Rpm.Length-1)*rpmRect.Width,rpmRect.Bottom-(t.Rpm[i]-low)/range*rpmRect.Height);
                    if(i==0)g.BeginFigure(point,false,false);else g.LineTo(point,true,false);
                }
            geometry.Freeze();dc.DrawGeometry(null,new Pen(color,1.5),geometry);
            Text(dc,$"{high:F0}",5,rpmRect.Top);Text(dc,$"{low:F0}",5,rpmRect.Bottom-12);
            Text(dc,"Index 0",rpmRect.Left,rpmRect.Bottom+2,10);Text(dc,"Index 1023",rpmRect.Right-60,rpmRect.Bottom+2,10);
        }
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.ChangedButton!=MouseButton.Left)return;
        Point p=e.GetPosition(this);
        for(int row=0;row<traces.Length;row++)if(WaveRect(row).Contains(p))
        {
            if(e.ClickCount==2){start=0;end=duration;InvalidateVisual();return;}
            Rect r=WaveRect(row);double seconds=start+(p.X-r.Left)/r.Width*(end-start);
            SeekRequested?.Invoke(traces[row].Index,Math.Min(seconds,(traces[row].Samples.Length-1)/48000.0));
            e.Handled=true;return;
        }
    }
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if(traces.Length==0)return;
        Point p=e.GetPosition(this);Rect r=WaveRect(0);
        double relative=Math.Clamp((p.X-r.Left)/r.Width,0,1),anchor=start+relative*(end-start);
        double span=Math.Clamp((end-start)*(e.Delta>0 ? .6 : 1/.6),Math.Min(.02,duration),duration);
        start=Math.Clamp(anchor-relative*span,0,duration-span);end=start+span;
        InvalidateVisual();e.Handled=true;
    }
}
