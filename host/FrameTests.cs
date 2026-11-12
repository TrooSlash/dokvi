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
  static class FrameTests {
    [DllImport("user32")] static extern IntPtr SendMessage(IntPtr h, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")] static extern bool DrawMenuBar(IntPtr h);
    [DllImport("uxtheme", CharSet=CharSet.Unicode)] static extern int SetWindowTheme(IntPtr h, string app, string id);
    static readonly List<string> Checks = new List<string>();
    static string Folder;
    static void Assert(bool value, string label) { if(!value) throw new Exception(label); Checks.Add(label); }
    static async Task CheckChrome(ReaderForm form,string label) {
      await Task.Delay(200);
      using(var bitmap=new Bitmap(form.Width,form.Height)) {
        using(var graphics=Graphics.FromImage(bitmap)) graphics.CopyFromScreen(form.Location,Point.Empty,form.Size);
        bitmap.Save(Path.Combine(Folder,label+".png"));
        var expected = form.IsDark ? Color.FromArgb(41,43,46) : Color.FromArgb(240,241,243);
        var pixel = bitmap.GetPixel(250,14);
        Assert(pixel.ToArgb()==expected.ToArgb(),label+" title is custom chrome, actual="+pixel.ToString());
        Assert(bitmap.GetPixel(3,150).ToArgb()==form.BackColor.ToArgb(),label+" has no classic border");
      }
    }
    internal static async Task Run(ReaderForm form) {
      Folder=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"frame-checks"); Directory.CreateDirectory(Folder);
      try {
        form.Activate(); await Task.Delay(400);
        Assert(form.Core==null,"Empty window stays native");
        await CheckChrome(form,"initial");
        // Reproduce the native paint paths used after Alt+Tab and dialogs.
        for(int i=0;i<3;i++) {
          SendMessage(form.Handle,0x86,IntPtr.Zero,IntPtr.Zero);
          SendMessage(form.Handle,0x86,new IntPtr(1),IntPtr.Zero);
          SendMessage(form.Handle,0x85,new IntPtr(1),IntPtr.Zero);
          DrawMenuBar(form.Handle);
        }
        await CheckChrome(form,"after-activation");
        form.Text="Проверка заголовка — Dokvi";
        await CheckChrome(form,"after-title-change");
        // Classic theme fallback must not be able to add another title bar.
        SetWindowTheme(form.Handle,"","");
        SendMessage(form.Handle,0x86,new IntPtr(1),IntPtr.Zero);
        SendMessage(form.Handle,0x85,new IntPtr(1),IntPtr.Zero);
        await CheckChrome(form,"classic-theme");
        var maximize=form.Controls.Cast<Control>().SelectMany(c=>c.Controls.Cast<Control>()).OfType<Button>().Single(b=>b.AccessibleName=="Развернуть");
        var originalBounds=form.Bounds;
        maximize.PerformClick(); await Task.Delay(250);
        Assert(form.WindowState==FormWindowState.Maximized,"Maximize button works");
        Assert(form.Bounds==Screen.FromControl(form).WorkingArea,"Maximized window respects taskbar");
        await CheckChrome(form,"maximized");
        maximize.PerformClick(); await Task.Delay(200);
        Assert(form.Bounds==originalBounds,"Restore from maximize keeps original bounds");
        var minimize=form.Controls.Cast<Control>().SelectMany(c=>c.Controls.Cast<Control>()).OfType<Button>().Single(b=>b.AccessibleName=="Свернуть");
        minimize.PerformClick(); await Task.Delay(150);
        Assert(form.WindowState==FormWindowState.Minimized,"Minimize button works");
        form.RestoreWindow(); form.Activate(); await CheckChrome(form,"restored");
        Assert(form.Bounds==originalBounds,"Restore from minimize keeps original bounds");
        form.Preferences["theme"]="dark"; await form.Call("theme",new object[]{"dark"}); await CheckChrome(form,"dark");
        form.Preferences["theme"]="light"; await form.Call("theme",new object[]{"light"});
        string file=Path.Combine(Program.DataRoot,"Frame.md"); File.WriteAllText(file,"# Проверка\n\nТекст документа.");
        form.ReceiveArguments(new[]{file});
        for(int i=0;i<200;i++) { if(form.Core!=null && form.UiReady && await form.Core.ExecuteScriptAsync("!!document.querySelector('#document h1')")=="true") break; await Task.Delay(50); }
        Assert(form.Core!=null && form.UiReady,"Document opens in native window");
        form.Activate();
        SendMessage(form.Handle,0x86,new IntPtr(1),IntPtr.Zero); DrawMenuBar(form.Handle);
        await CheckChrome(form,"document");
        await form.Core.ExecuteScriptAsync("document.getElementById('view-split').click()"); await Task.Delay(350);
        await CheckChrome(form,"editor");
        await form.Core.ExecuteScriptAsync("document.getElementById('help').click();document.querySelector('.help-content').scrollTop=10000");
        await Task.Delay(150);
        Assert(await form.Core.ExecuteScriptAsync("document.getElementById('help-close').getBoundingClientRect().top>0")=="true","Help close stays visible");
        await form.Core.ExecuteScriptAsync("document.getElementById('help-close').click();document.querySelector('.tab-item.active .tab-close').click()");
        for(int i=0;i<100 && form.Core!=null;i++) await Task.Delay(50);
        Assert(form.Core==null,"Last tab releases browser");
        await CheckChrome(form,"returned-empty");
        File.WriteAllText(Path.Combine(Folder,"result.json"),Program.Json.Serialize(new {ok=true,checks=Checks}));
      } catch(Exception error) { File.WriteAllText(Path.Combine(Folder,"result.json"),Program.Json.Serialize(new{ok=false,error=error.ToString(),checks=Checks})); }
      form.ReleaseBrowser(); form.Close();
    }
  }
}
#endif
