using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MdReader {
  // Software Direct2D draws into the existing WinForms DC; no browser or GPU
  // device is needed for the welcome screen. COM slots follow d2d1.h/dwrite.h.
  static class SurfaceText {
    [StructLayout(LayoutKind.Sequential)] struct Rect { internal int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Box { internal float left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Rgba { internal float r, g, b, a; internal Rgba(Color c) { r=c.R/255f; g=c.G/255f; b=c.B/255f; a=1; } }
    [StructLayout(LayoutKind.Sequential)] struct TargetProperties { internal int type, format, alpha; internal float dpiX, dpiY; internal int usage, level; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateTarget(IntPtr self, ref TargetProperties properties, out IntPtr target);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet=CharSet.Unicode)] delegate int CreateFormat(IntPtr self, string family, IntPtr collection, int weight, int style, int stretch, float size, string locale, out IntPtr format);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int SetFormat(IntPtr self, int value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Bind(IntPtr self, IntPtr dc, ref Rect rect);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateBrush(IntPtr self, ref Rgba color, IntPtr properties, out IntPtr brush);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Begin(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int End(IntPtr self, out ulong tag1, out ulong tag2);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void SetMode(IntPtr self, int mode);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Clear(IntPtr self, ref Rgba color);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet=CharSet.Unicode)] delegate void Draw(IntPtr self, string text, uint length, IntPtr format, ref Box bounds, IntPtr brush, int options, int measuring);
    [DllImport("d2d1.dll", PreserveSig=true)] static extern int D2D1CreateFactory(int type, ref Guid iid, IntPtr options, out IntPtr factory);
    [DllImport("dwrite.dll", PreserveSig=true)] static extern int DWriteCreateFactory(int type, ref Guid iid, out IntPtr factory);
    static IntPtr drawing, writing, target;
    internal static int FallbackCount { get; private set; }
    static readonly Dictionary<string,IntPtr> formats = new Dictionary<string,IntPtr>();
    static T Method<T>(IntPtr instance, int slot) where T : class {
      return (T)(object)Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size), typeof(T));
    }
    static void Check(int result) { Marshal.ThrowExceptionForHR(result); }
    static void Release(ref IntPtr value) { if (value!=IntPtr.Zero) { Marshal.Release(value); value=IntPtr.Zero; } }
    static void Initialize() {
      if (drawing==IntPtr.Zero) {
        var iid=new Guid("06152247-6f50-465a-9245-118bfd3b6007");
        Check(D2D1CreateFactory(0, ref iid, IntPtr.Zero, out drawing));
        Application.ApplicationExit += delegate {
          foreach(var format in formats.Values) Marshal.Release(format);
          formats.Clear(); Release(ref target); Release(ref writing); Release(ref drawing);
        };
      }
      if (writing==IntPtr.Zero) {
        var iid=new Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
        Check(DWriteCreateFactory(0, ref iid, out writing));
      }
      if (target==IntPtr.Zero) {
        var properties=new TargetProperties {type=1,format=87,alpha=3,dpiX=96,dpiY=96};
        Check(Method<CreateTarget>(drawing,16)(drawing,ref properties,out target));
        Method<SetMode>(target,34)(target,2);
      }
    }
    internal static bool Paint(Graphics graphics, string text, Font font, Rectangle bounds, Color foreground, Color background, bool centered) {
      if (bounds.Width<=0 || bounds.Height<=0) return true;
      IntPtr dc=IntPtr.Zero, brush=IntPtr.Zero;
      try {
        Initialize();
        float size=font.SizeInPoints*graphics.DpiY/72;
        string key=font.Name+":"+size.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+centered;
        IntPtr format;
        if (!formats.TryGetValue(key,out format)) {
          Check(Method<CreateFormat>(writing,15)(writing,font.Name,IntPtr.Zero,400,0,5,size,"",out format));
          try {
            Check(Method<SetFormat>(format,3)(format,centered?2:0));
            Check(Method<SetFormat>(format,4)(format,2));
            Check(Method<SetFormat>(format,5)(format,1));
            formats.Add(key,format);
          } catch { Marshal.Release(format); throw; }
        }
        dc=graphics.GetHdc();
        var rect=new Rect {left=bounds.Left,top=bounds.Top,right=bounds.Right,bottom=bounds.Bottom};
        Check(Method<Bind>(target,57)(target,dc,ref rect));
        var ink=new Rgba(foreground); var paper=new Rgba(background);
        Check(Method<CreateBrush>(target,8)(target,ref ink,IntPtr.Zero,out brush));
        var box=new Box {right=bounds.Width,bottom=bounds.Height};
        Method<Begin>(target,48)(target);
        Method<Clear>(target,47)(target,ref paper);
        Method<Draw>(target,27)(target,text,(uint)text.Length,format,ref box,brush,2,0);
        ulong tag1,tag2;
        Check(Method<End>(target,49)(target,out tag1,out tag2));
        return true;
      } catch (COMException) { FallbackCount++; Release(ref target); return false; }
      finally { Release(ref brush); if (dc!=IntPtr.Zero) graphics.ReleaseHdc(dc); }
    }
  }
}
