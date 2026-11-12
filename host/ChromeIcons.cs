using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MdReader {
  // Native buttons and the document use the same alpha mask, including antialiasing.
  static class ChromeIcons {
    static readonly Dictionary<string,Bitmap> masks = new Dictionary<string,Bitmap>();
    static readonly Dictionary<string,string> urls = new Dictionary<string,string>();
    static ChromeIcons() { Application.ApplicationExit += delegate { foreach(var mask in masks.Values) mask.Dispose(); masks.Clear(); urls.Clear(); }; }
    internal static int Size(string name) { return name == "plus" ? 16 : 17; }
    static Bitmap Mask(string name, int pixels) {
      string key=name+":"+pixels;
      Bitmap bitmap;
      if(masks.TryGetValue(key,out bitmap)) return bitmap;
      bitmap=new Bitmap(pixels,pixels,PixelFormat.Format32bppArgb);
      using(var g=Graphics.FromImage(bitmap)) {
        g.SmoothingMode=SmoothingMode.AntiAlias;
        if(name=="plus") {
          float scale=pixels/16f;
          int middle=(int)Math.Round(pixels/2f), start=(int)Math.Round(3*scale), length=(int)Math.Round(10*scale), thickness=Math.Max(1,(int)Math.Round(scale));
          g.SmoothingMode=SmoothingMode.None;
          g.FillRectangle(Brushes.White,start,middle,length,thickness);
          g.FillRectangle(Brushes.White,middle,start,thickness,length);
        } else {
          g.ScaleTransform(pixels/24f,pixels/24f);
          using(var pen=new Pen(Color.White,1.65f) {StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round}) {
            if(name=="panel") {
              using(var shape=QuietButton.RoundedRectangle(new RectangleF(3,4,18,16),4)) g.DrawPath(pen,shape);
              g.DrawLine(pen,9,4,9,20); g.DrawLine(pen,5.5f,8,6.5f,8); g.DrawLine(pen,5.5f,11,6.5f,11);
            } else if(name=="more") {
              foreach(int x in new[]{5,12,19}) g.DrawEllipse(pen,x-1,11,2,2);
            } else { bitmap.Dispose(); throw new ArgumentException("Unknown chrome icon", "name"); }
          }
        }
      }
      masks.Add(key,bitmap); return bitmap;
    }
    internal static void Paint(Graphics graphics,string name,int pixels,Point origin,Color color) {
      Paint(graphics,Mask(name,pixels),origin,color);
    }
    internal static void Paint(Graphics graphics,Bitmap mask,Point origin,Color color) {
      using(var attributes=new ImageAttributes()) {
        var tint=new ColorMatrix(); tint.Matrix00=color.R/255f; tint.Matrix11=color.G/255f; tint.Matrix22=color.B/255f;
        attributes.SetColorMatrix(tint);
        graphics.DrawImage(mask,new Rectangle(origin,mask.Size),0,0,mask.Width,mask.Height,GraphicsUnit.Pixel,attributes);
      }
    }
    internal static string DataUrl(Bitmap bitmap) {
      using(var stream=new MemoryStream()) { bitmap.Save(stream,ImageFormat.Png); return "data:image/png;base64,"+Convert.ToBase64String(stream.ToArray()); }
    }
    internal static object[] ForDocument(double scale) {
      if(Double.IsNaN(scale) || Double.IsInfinity(scale) || scale<.5 || scale>8) throw new ArgumentOutOfRangeException("scale");
      var result=new List<object>();
      foreach(string name in new[]{"panel","plus","more"}) {
        int pixels=(int)Math.Round(Size(name)*scale); string key=name+":"+pixels, url;
        if(!urls.TryGetValue(key,out url)) {
          url=DataUrl(Mask(name,pixels));
          urls.Add(key,url);
        }
        result.Add(new { name, pixels, url });
      }
      return result.ToArray();
    }
  }
}
