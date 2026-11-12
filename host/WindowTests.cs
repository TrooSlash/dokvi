#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdReader {
  static class WindowTests {
    [DllImport("user32")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32")] static extern bool IsZoomed(IntPtr window);
    [DllImport("user32")] static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
    [DllImport("user32")] static extern bool GetWindowPlacement(IntPtr window,ref Placement placement);
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int left,top,right,bottom; public override string ToString() { return left+","+top+","+right+","+bottom; } }
    [StructLayout(LayoutKind.Sequential)] struct Placement { public int length,flags,show; public Point min,max; public NativeRect normal; }
    static string Describe(ReaderForm form) {
      NativeRect rect; GetWindowRect(form.Handle,out rect);
      var placement=new Placement {length=Marshal.SizeOf(typeof(Placement))}; GetWindowPlacement(form.Handle,ref placement);
      return " nativeZoom="+IsZoomed(form.Handle)+" nativeRect="+rect+" placement="+placement.flags+"/"+placement.show+"/"+placement.normal;
    }
    static int Hit(Control control, Point screen) {
      return (int)SendMessage(control.Handle,0x84,IntPtr.Zero,new IntPtr(unchecked((screen.Y << 16) | (screen.X & 65535))));
    }
    static Control Leaf(Control control, Point screen) {
      while(true) { var child=control.GetChildAtPoint(control.PointToClient(screen)); if(child==null) return control; control=child; }
    }
    static readonly List<string> Checks=new List<string>();
    static void Assert(bool value,string label) { if(!value) throw new Exception(label); Checks.Add(label); }
    static CaptionButton[] Buttons(ReaderForm form) {
      return form.Controls.Cast<Control>().SelectMany(c=>c.Controls.Cast<Control>()).OfType<CaptionButton>().ToArray();
    }
    static void ClickWhileCaptured(CaptionButton button) {
      // Reproduce the state inside a real WinForms Button.Click handler.
      // PerformClick alone misses the bug because it doesn't capture the mouse.
      button.Capture=true;
      if(!button.Capture) throw new Exception("Could not establish button mouse capture");
      int clicks=0; EventHandler count=delegate { clicks++; }; button.Click+=count;
      try { button.PerformClick(); }
      finally { button.Capture=false; button.Click-=count; }
      if(clicks!=1) throw new Exception("Expected one click: "+button.AccessibleName+", actual="+clicks+", canSelect="+button.CanSelect);
    }
    static async Task CheckWindowCommands(ReaderForm form,string label) {
      var buttons=Buttons(form);
      var original=form.Bounds;
      foreach(var button in buttons) {
        var point=button.PointToScreen(new Point(button.Width/2,button.Height/2));
        Assert(Hit(form,point)==1 && Hit(button,point)==1 && Leaf(form,point)==button && WindowFromPoint(point)==button.Handle,label+": pointer targets "+button.AccessibleName);
      }
      // Repeat to catch stale capture and restore bounds accumulating frame insets.
      for(int cycle=0;cycle<2;cycle++) {
        ClickWhileCaptured(buttons[1]); await Task.Delay(250);
        Assert(form.WindowState==FormWindowState.Maximized,label+": maximize while captured #"+cycle);
        Assert(buttons[1].Maximized && IsZoomed(form.Handle),label+": maximize icon and Windows state agree #"+cycle);
        Assert(form.Bounds==Screen.FromControl(form).WorkingArea,label+": maximize respects taskbar #"+cycle);
        ClickWhileCaptured(buttons[1]); await Task.Delay(250);
        Assert(form.WindowState==FormWindowState.Normal && form.Bounds==original,label+": restore original bounds #"+cycle+", actual="+form.WindowState+" "+form.Bounds+", expected="+original+Describe(form));
        Assert(!buttons[1].Maximized && !IsZoomed(form.Handle),label+": restore icon and Windows state agree #"+cycle);
        ClickWhileCaptured(buttons[0]); await Task.Delay(250);
        Assert(form.WindowState==FormWindowState.Minimized,label+": minimize while captured #"+cycle);
        SendMessage(form.Handle,0x112,new IntPtr(0xF120),IntPtr.Zero); await Task.Delay(250);
        Assert(form.WindowState==FormWindowState.Normal && form.Bounds==original,label+": restore minimized window #"+cycle);
      }
      SendMessage(form.Handle,0x112,new IntPtr(0xF030),IntPtr.Zero); await Task.Delay(250); ClickWhileCaptured(buttons[0]); await Task.Delay(250);
      Assert(form.WindowState==FormWindowState.Minimized,label+": minimize maximized window");
      form.RestoreWindow(); await Task.Delay(250);
      Assert(form.WindowState==FormWindowState.Maximized,label+": restore remembers maximized state");
      ClickWhileCaptured(buttons[1]); await Task.Delay(250);
      Assert(form.Bounds==original,label+": original bounds after minimize from maximized");
    }
    internal static async Task Run(ReaderForm form) {
      string result=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"window-result.json");
      try {
        await Task.Delay(200);
        Assert(form.Core==null,"Empty window stays native");
        await CheckWindowCommands(form,"empty");
        string file=Path.Combine(Program.DataRoot,"Window.md");
        File.WriteAllText(file,"# Проверка окна\n\nТекст документа.");
        form.ReceiveArguments(new[]{file});
        bool ready=false;
        for(int i=0;i<300;i++) {
          if(form.Core!=null && form.UiReady && await form.Core.ExecuteScriptAsync("!!document.querySelector('#document h1')")=="true") { ready=true; break; }
          await Task.Delay(50);
        }
        Assert(ready,"Document is rendered before testing window commands");
        await CheckWindowCommands(form,"document");
        await form.Core.ExecuteScriptAsync("document.getElementById('view-split').click()");
        await Task.Delay(150);
        await CheckWindowCommands(form,"editor");
        await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
        for(int i=0;i<100 && form.Core!=null;i++) await Task.Delay(50);
        Assert(form.Core==null,"Closing last tab releases browser");
        await CheckWindowCommands(form,"returned empty");
        File.WriteAllText(result,Program.Json.Serialize(new {ok=true,checks=Checks}));
      } catch(Exception error) { File.WriteAllText(result,Program.Json.Serialize(new {ok=false,error=error.ToString(),checks=Checks})); }
      finally { form.ReleaseBrowser(); form.Close(); }
    }
  }
}
#endif
