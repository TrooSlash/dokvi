using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MdReader {
  static class ChromeControls {
    internal const string FontFamily="Segoe UI";
    internal const int FontSize=12, BarHeight=45, ButtonHeight=30, ButtonTop=8, ButtonRadius=4;
    internal const int CreateWidth=78, OpenWidth=30, ActionWidth=31, ToolbarGap=6, ToolbarInset=15, NarrowInset=11, NarrowWidth=850;
    sealed class LabelAsset {
      internal Bitmap Mask;
      internal string Url;
      internal int Width, FontSize;
    }
    static readonly Dictionary<string,LabelAsset> labels=new Dictionary<string,LabelAsset>();
    static readonly Dictionary<string,int> widths=new Dictionary<string,int>();
    sealed class SurfaceAsset { internal Bitmap Fill, Border; internal string FillUrl, BorderUrl; }
    static readonly Dictionary<string,SurfaceAsset> surfaces=new Dictionary<string,SurfaceAsset>();
    static ChromeControls() { Application.ApplicationExit+=delegate { foreach(var label in labels.Values) label.Mask.Dispose(); labels.Clear(); foreach(var surface in surfaces.Values) { surface.Fill.Dispose();surface.Border.Dispose(); } surfaces.Clear(); }; }
    internal static Font Font(int pixels=FontSize) { return new Font(FontFamily,pixels*72f/96); }
    internal static string Label(string id) { return id=="new"?L.T("Создать"):id=="outline-toggle"?L.T("Разделы"):"Aa"; }
    internal static int Width(string id) {
      if(id=="new") return CreateWidth;
      if(id!="outline-toggle") return ActionWidth;
      string text=Label(id); int width;
      if(widths.TryGetValue(text,out width)) return width;
      using(var bitmap=new Bitmap(1,1)) using(var g=Graphics.FromImage(bitmap)) using(var font=Font())
        width=41+(int)Math.Ceiling(g.MeasureString(text,font,PointF.Empty,StringFormat.GenericTypographic).Width);
      widths.Add(text,width); return width;
    }
    static LabelAsset Asset(string id,float scale) {
      string text=Label(id),key=id+":"+text+":"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
      LabelAsset asset;
      if(labels.TryGetValue(key,out asset)) return asset;
      int width=Width(id),size=id=="appearance"?13:FontSize;
      int w=(int)Math.Round(width*scale),h=(int)Math.Round(ButtonHeight*scale);
      var mask=new Bitmap(w,h,PixelFormat.Format32bppArgb);
      mask.SetResolution(96*scale,96*scale);
      // DirectWrite's grayscale coverage becomes an alpha mask shared with WebView.
      using(var g=Graphics.FromImage(mask)) using(var font=Font(size)) {
        g.Clear(Color.Black);
        int left=id=="outline-toggle"?(int)Math.Round(33*scale):0;
        SurfaceLabel.Draw(g,text,font,new Rectangle(left,0,w-left,h),Color.White,Color.Black,id!="outline-toggle");
      }
      for(int y=0;y<h;y++) for(int x=0;x<w;x++) { int alpha=mask.GetPixel(x,y).R; mask.SetPixel(x,y,Color.FromArgb(alpha,255,255,255)); }
      asset=new LabelAsset {Mask=mask,Width=width,FontSize=size}; labels.Add(key,asset); return asset;
    }
    internal static void PaintLabel(Graphics graphics,string id,float scale,Color color) { ChromeIcons.Paint(graphics,Asset(id,scale).Mask,Point.Empty,color); }
    static SurfaceAsset Surface(Size size,float scale) {
      string key=size.Width+":"+size.Height+":"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
      SurfaceAsset asset; if(surfaces.TryGetValue(key,out asset)) return asset;
      asset=new SurfaceAsset {Fill=new Bitmap(size.Width,size.Height),Border=new Bitmap(size.Width,size.Height)};
      using(var g=Graphics.FromImage(asset.Fill)) using(var shape=QuietButton.RoundedRectangle(new RectangleF(0,0,size.Width,size.Height),ButtonRadius*2*scale)) {
        g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.Half;g.FillPath(Brushes.White,shape);
      }
      float inset=.5f*scale;
      using(var g=Graphics.FromImage(asset.Border)) using(var pen=new Pen(Color.White,scale)) using(var shape=QuietButton.RoundedRectangle(new RectangleF(inset,inset,size.Width-2*inset,size.Height-2*inset),(ButtonRadius*2-1)*scale)) {
        g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.Half;g.DrawPath(pen,shape);
      }
      surfaces.Add(key,asset);return asset;
    }
    internal static void PaintSurface(Graphics graphics,Size size,float scale,Color fill,Color? border) {
      var asset=Surface(size,scale);ChromeIcons.Paint(graphics,asset.Fill,Point.Empty,fill);
      if(border.HasValue) ChromeIcons.Paint(graphics,asset.Border,Point.Empty,border.Value);
    }
    internal static object ForDocument(double scale) {
      var icons=ChromeIcons.ForDocument(scale);
      var result=new List<object>();
      var frames=new List<object>();
      foreach(string id in new[]{"new","open","outline-toggle","appearance","more"}) {
        int width=id=="open"?OpenWidth:Width(id);
        var surface=Surface(new Size((int)Math.Round(width*scale),(int)Math.Round(ButtonHeight*scale)),(float)scale);
        if(surface.FillUrl==null) {surface.FillUrl=ChromeIcons.DataUrl(surface.Fill);surface.BorderUrl=ChromeIcons.DataUrl(surface.Border);}
        frames.Add(new{id,fill=surface.FillUrl,border=surface.BorderUrl});
      }
      foreach(string id in new[]{"new","outline-toggle","appearance"}) {
        var asset=Asset(id,(float)scale);
        if(asset.Url==null) asset.Url=ChromeIcons.DataUrl(asset.Mask);
        result.Add(new {id,width=asset.Width,height=ButtonHeight,fontSize=asset.FontSize,url=asset.Url});
      }
      return new {icons,labels=result,frames,style=new Dictionary<string,object> {
        {"font-family",FontFamily},{"font-size",FontSize},{"bar-height",BarHeight},{"button-height",ButtonHeight},{"button-radius",ButtonRadius},
        {"create-width",CreateWidth},{"open-width",OpenWidth},{"action-width",ActionWidth},{"toolbar-gap",ToolbarGap}
      }};
    }
  }
}
