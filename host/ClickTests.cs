#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdReader {
  static class ClickTests {
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int dx, dy; public uint data, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint type; public MouseInput mouse; }
    [DllImport("user32", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr window);
    static readonly List<string> Checks = new List<string>();
    static void Assert(bool value, string label) { if (!value) throw new Exception(label); Checks.Add(label); }
    static async Task Click(Control control) {
      var point = control.PointToScreen(new Point(control.Width / 2, control.Height / 2));
      Assert(WindowFromPoint(point) == control.Handle, "Pointer target is " + control.AccessibleName);
      SetCursorPos(point.X, point.Y);
      var events = new[] { new Input { mouse = new MouseInput { flags = 2 } }, new Input { mouse = new MouseInput { flags = 4 } } };
      Assert(SendInput(2, events, Marshal.SizeOf(typeof(Input))) == 2, "Windows accepted mouse down/up");
      await Task.Delay(300);
    }
    static async Task Commands(ReaderForm form, string state, bool taskbar) {
      var buttons = form.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>()).OfType<CaptionButton>().ToArray();
      var bounds = form.Bounds;
      for (int cycle = 0; cycle < (taskbar ? 3 : 2); cycle++) {
        await Click(buttons[1]);
        Assert(form.WindowState == FormWindowState.Maximized && form.Bounds == Screen.FromControl(form).WorkingArea, state + ": maximize by pointer #" + cycle);
        await Click(buttons[1]);
        Assert(form.WindowState == FormWindowState.Normal && form.Bounds == bounds, state + ": restore original bounds by pointer #" + cycle);
        await Click(buttons[0]);
        Assert(form.WindowState == FormWindowState.Minimized, state + ": minimize by pointer #" + cycle);
        if (taskbar) await TaskbarFocusTests.RestoreFromTaskbar(form);
        else { form.RestoreWindow(); SetForegroundWindow(form.Handle); await Task.Delay(300); }
        Assert(form.Bounds == bounds, state + ": bounds after resuming minimized test");
      }
    }
    internal static async Task Run(ReaderForm form, bool taskbar = false) {
      var cursor = Cursor.Position;
      var result = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "click-result.json");
      try {
        form.Show(); form.Activate(); SetForegroundWindow(form.Handle); await Task.Delay(400);
        await Commands(form, "empty", taskbar);
        var file = Path.Combine(Program.DataRoot, "click.md"); File.WriteAllText(file, "# Pointer test\n\nDocument text.");
        form.ReceiveArguments(new[] { file });
        for (int i = 0; i < 400 && !form.UiReady; i++) await Task.Delay(50);
        Assert(form.UiReady && await form.Core.ExecuteScriptAsync("!!document.querySelector('#document h1')") == "true", "File is visible");
        SetForegroundWindow(form.Handle); await Task.Delay(300);
        await Commands(form, "document", taskbar);
        await form.Core.ExecuteScriptAsync("document.getElementById('view-split').click()"); await Task.Delay(300);
        await Commands(form, "editor", taskbar);
        if (taskbar) {
          await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
          for (int i = 0; i < 200 && form.Core != null; i++) await Task.Delay(50);
          Assert(form.Core == null, "Last tab returns to native empty window");
          await Commands(form, "returned empty", true);
        }
        File.WriteAllText(result, Program.Json.Serialize(new { ok = true, method = "Win32 SendInput caption clicks; taskbar restore clicks=" + taskbar, checks = Checks }));
      } catch (Exception error) { File.WriteAllText(result, Program.Json.Serialize(new { ok = false, error = error.ToString(), checks = Checks })); }
      finally { Cursor.Position = cursor; form.ReleaseBrowser(); form.Close(); }
    }
  }
}
#endif
