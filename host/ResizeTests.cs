#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdReader {
  static class ResizeTests {
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int dx, dy; public uint data, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint type; public MouseInput mouse; }
    [DllImport("user32")] static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    static readonly List<object> Results = new List<object>();
    static bool Passed = true;
    static void Mouse(uint flags) {
      if (SendInput(1, new[] { new Input { mouse = new MouseInput { flags = flags } } }, Marshal.SizeOf(typeof(Input))) != 1)
        throw new Exception("Windows did not accept mouse input");
    }
    static async Task Drag(ReaderForm form, Point point, Point delta, int hold = 120) {
      var start = form.PointToScreen(point);
      if (GetAncestor(WindowFromPoint(start), 2) != form.Handle) throw new Exception("Resize target is covered by another window");
      // Windows sizing runs a modal loop; release must arrive independently of the UI thread.
      await Task.Run(() => {
        SetCursorPos(start.X, start.Y); Thread.Sleep(100); Mouse(2);
        try {
          Thread.Sleep(hold);
          for (int step = 1; step <= 8; step++) {
            SetCursorPos(start.X + delta.X * step / 8, start.Y + delta.Y * step / 8); Thread.Sleep(30);
          }
        } finally { Mouse(4); }
      });
      await Task.Delay(180);
    }
    static async Task Edges(ReaderForm form, string state) {
      var original = form.Bounds;
      foreach (string edge in new[] { "right", "bottom-right", "bottom", "bottom-left", "left", "top-left", "top", "top-right" }) {
        form.Bounds = original; SetForegroundWindow(form.Handle); await Task.Delay(100);
        bool left = edge.Contains("left"), right = edge.Contains("right"), top = edge.Contains("top"), bottom = edge.Contains("bottom");
        var point = new Point(left ? 1 : right ? form.ClientSize.Width - 2 : form.ClientSize.Width / 2,
          top ? 1 : bottom ? form.ClientSize.Height - 2 : form.ClientSize.Height / 2);
        var delta = new Point(left ? 80 : right ? -80 : 0, top ? 60 : bottom ? -60 : 0);
        await Drag(form, point, delta);
        var expected = new Rectangle(original.X + (left ? 80 : 0), original.Y + (top ? 60 : 0),
          original.Width - (left || right ? 80 : 0), original.Height - (top || bottom ? 60 : 0));
        bool ok = Math.Abs(form.Left - expected.Left) <= 2 && Math.Abs(form.Top - expected.Top) <= 2 &&
          Math.Abs(form.Width - expected.Width) <= 2 && Math.Abs(form.Height - expected.Height) <= 2;
        Passed &= ok; Results.Add(new { state, edge, ok, before = original.ToString(), expected = expected.ToString(), actual = form.Bounds.ToString() });
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resize-result.json"), Program.Json.Serialize(new { ok = false, running = true, checks = Results }));
      }
      form.Bounds = original;
    }
    internal static async Task Run(ReaderForm form) {
      var cursor = Cursor.Position;
      string result = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resize-result.json");
      try {
        form.Activate(); SetForegroundWindow(form.Handle); await Task.Delay(400);
        await Edges(form, "empty");
        string path = Path.Combine(Program.DataRoot, "resize.md");
        string text = "# Resize test\n\nUnchanged text.\n\n| A | B |\n|---|---|\n| One | Two |\n";
        File.WriteAllText(path, text);
        form.ReceiveArguments(new[] { path });
        for (int i = 0; i < 400 && !form.UiReady; i++) await Task.Delay(50);
        if (!form.UiReady) throw new Exception("Document did not open");
        await Task.Delay(400);
        await Edges(form, "document");
        await form.Core.ExecuteScriptAsync("document.getElementById('view-split').click()"); await Task.Delay(300);
        await Edges(form, "editor");
        var normal = form.Bounds;
        for (int i = 0; i < 20; i++) {
          form.Size = new Size(880, 540); await Task.Delay(120);
          await Drag(form, new Point(form.ClientSize.Width / 2, form.ClientSize.Height - 1), new Point(0, -20), i % 2 == 0 ? 0 : 120);
          bool bottom = form.Width == 880 && form.Height == 520;
          Passed &= bottom; Results.Add(new { state = "editor-bottom-repeat", attempt = i + 1, holdMs = i % 2 == 0 ? 0 : 120, ok = bottom, actual = form.Size.ToString() });
        }
        form.Bounds = normal;
        await Drag(form, new Point(form.ClientSize.Width - 2, form.ClientSize.Height / 2), new Point(-80, 0), 0);
        bool quickDrag = form.Width == normal.Width - 80 && form.Height == normal.Height;
        Passed &= quickDrag; Results.Add(new { state = "quick-drag", ok = quickDrag });
        form.Bounds = normal;
        form.Size = new Size(form.MinimumSize.Width + 40, form.MinimumSize.Height + 40); await Task.Delay(200);
        await Drag(form, new Point(form.ClientSize.Width - 2, form.ClientSize.Height - 2), new Point(-80, -80));
        bool minimum = form.Size == form.MinimumSize;
        Passed &= minimum; Results.Add(new { state = "minimum-size", ok = minimum, actual = form.Size.ToString() });
        form.Bounds = normal;
        form.ToggleMaximize(); await Task.Delay(300);
        var maximized = form.Bounds;
        await Drag(form, new Point(form.ClientSize.Width - 2, form.ClientSize.Height / 2), new Point(-80, 0));
        bool staysMaximized = form.WindowState == FormWindowState.Maximized && form.Bounds == maximized;
        Passed &= staysMaximized; Results.Add(new { state = "maximized", ok = staysMaximized });
        form.RestoreWindow(); await Task.Delay(300);
        bool unchanged = await form.Core.ExecuteScriptAsync("document.querySelector('.source-editor').value") == Program.Json.Serialize(text);
        Passed &= unchanged; Results.Add(new { state = "editor-text-unchanged", ok = unchanged });
        form.ReleaseBrowser();
        await Edges(form, "returned-empty");
        File.WriteAllText(result, Program.Json.Serialize(new { ok = Passed, method = "Win32 SendInput mouse down/up and physical cursor drags", checks = Results }));
      } catch (Exception error) { File.WriteAllText(result, Program.Json.Serialize(new { ok = false, error = error.ToString(), checks = Results })); }
      finally { Cursor.Position = cursor; form.ReleaseBrowser(); form.Close(); }
    }
  }
}
#endif
