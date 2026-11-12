#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace MdReader {
  static class TaskbarFocusTests {
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int dx, dy; public uint data, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint type; public MouseInput mouse; }
    [DllImport("user32")] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern IntPtr GetFocus();
    [DllImport("user32")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string name, string title);
    static readonly List<object> Checks = new List<object>();
    static bool Passed = true;

    internal static async Task RestoreFromTaskbar(ReaderForm form) {
      string title = form.Text;
      var taskbar = FindWindow("Shell_TrayWnd", null);
      if (taskbar == IntPtr.Zero) throw new Exception("Windows taskbar is unavailable");
      Point point = await Task.Run(() => {
        var root = AutomationElement.FromHandle(taskbar);
        var elements = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
        var matches = elements.Cast<AutomationElement>().Where(e => (e.Current.Name.Contains("Dokvi") || e.Current.Name.StartsWith("MD Reader")) && (e.Current.ControlType == ControlType.Button || e.Current.ControlType == ControlType.TabItem)).ToArray();
        if (matches.Length != 1) {
          File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "taskbar-elements.json"), Program.Json.Serialize(elements.Cast<AutomationElement>().Select(e => new { name = e.Current.Name, type = e.Current.ControlType.ProgrammaticName, className = e.Current.ClassName }).ToArray()));
          throw new Exception("Expected one taskbar button for " + title + ", found " + matches.Length);
        }
        var rectangle = matches[0].Current.BoundingRectangle;
        if (rectangle.IsEmpty || rectangle.Width < 1 || rectangle.Height < 1) throw new Exception("Taskbar button has no screen coordinates");
        return new Point((int)(rectangle.X + rectangle.Width / 2), (int)(rectangle.Y + rectangle.Height / 2));
      });
      if (GetAncestor(WindowFromPoint(point), 2) != taskbar) throw new Exception("Taskbar click is covered by another window");
      SetCursorPos(point.X, point.Y);
      var input = new[] { new Input { mouse = new MouseInput { flags = 2 } }, new Input { mouse = new MouseInput { flags = 4 } } };
      if (SendInput(2, input, Marshal.SizeOf(typeof(Input))) != 2) throw new Exception("Windows rejected taskbar click");
      for (int i = 0; i < 60 && (form.WindowState == FormWindowState.Minimized || GetForegroundWindow() != form.Handle); i++) await Task.Delay(50);
      if (form.WindowState == FormWindowState.Minimized || GetForegroundWindow() != form.Handle) throw new Exception("Taskbar click did not restore test window");
      await Task.Delay(200);
    }
    static async Task EditorClick(ReaderForm form) {
      var point = await InputFocusTests.JS<int[]>(form, "(()=>{const r=document.querySelector('.cm-line').getBoundingClientRect();return [Math.round((r.x+35)*devicePixelRatio),Math.round((r.y+r.height/2)*devicePixelRatio)]})()");
      await InputFocusTests.Click(form, form.View.PointToScreen(new Point(point[0], point[1])));
    }
    internal static async Task Run(ReaderForm form) {
      var cursor = Cursor.Position;
      string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "taskbar-focus-result.json");
      try {
        string path = Path.Combine(Program.DataRoot, "taskbar-enter.md");
        File.WriteAllText(path, "TASKBAR-ENTER\nSECOND-LINE\n");
        form.ReceiveArguments(new[] { path });
        for (int i = 0; i < 400 && !form.UiReady; i++) await Task.Delay(50);
        if (!form.UiReady) throw new Exception("Document did not open");
        form.Text = "Dokvi Enter QA";
        SetForegroundWindow(form.Handle); await Task.Delay(300);
        var buttons = form.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>()).OfType<CaptionButton>().ToArray();
        foreach (string mode in new[] { "split", "source" }) {
          await form.Core.ExecuteScriptAsync("document.getElementById('view-" + mode + "').click()");
          await Task.Delay(200);
          foreach (bool maximized in new[] { false, true }) foreach (bool clickEditor in new[] { false, true }) for (int cycle = 0; cycle < 3; cycle++) {
            if ((form.WindowState == FormWindowState.Maximized) != maximized) await InputFocusTests.Click(form, buttons[1]);
            await EditorClick(form);
            string before = await InputFocusTests.Text(form);
            int minimizeClicks = 0;
            EventHandler recordClick = delegate { minimizeClicks++; };
            buttons[0].Click += recordClick;
            var minimizePoint = buttons[0].PointToScreen(new Point(buttons[0].Width / 2, buttons[0].Height / 2));
            var pointerTarget = WindowFromPoint(minimizePoint);
            await InputFocusTests.Click(form, buttons[0]);
            buttons[0].Click -= recordClick;
            if (form.WindowState != FormWindowState.Minimized) throw new Exception("Minimize click failed: clicks=" + minimizeClicks + ", state=" + form.WindowState + ", point=" + minimizePoint + ", target=" + pointerTarget + ", expected=" + buttons[0].Handle + ", bounds=" + form.Bounds);
            await RestoreFromTaskbar(form);
            if (clickEditor) await EditorClick(form);
            var bounds = form.Bounds;
            var state = form.WindowState;
            var focus = new { activeControl = form.ActiveControl == null ? null : form.ActiveControl.GetType().Name, nativeFocus = GetFocus().ToInt64(), viewContainsFocus = form.View.ContainsFocus };
            int position = await InputFocusTests.JS<int>(form, "document.querySelector('.source-editor').selectionStart");
            for (int enter = 0; enter < 2; enter++) {
              string expected = before.Insert(position, "\n");
              await InputFocusTests.Press(form, 0x0D);
              string actual = await InputFocusTests.Text(form);
              bool ok = actual == expected && form.WindowState == state && form.Bounds == bounds;
              Passed &= ok;
              Checks.Add(new { mode, maximized, clickEditor, cycle, enter, command = "Enter after real taskbar click", ok, before, expected, actual, focus, stateBefore = state.ToString(), stateAfter = form.WindowState.ToString() });
              if (!ok) break;
              before = actual; position++;
            }
            if (form.WindowState == FormWindowState.Minimized) await RestoreFromTaskbar(form);
          }
        }
        File.WriteAllText(output, Program.Json.Serialize(new { ok = Passed, method = "SendInput caption/taskbar/editor mouse clicks and Enter; UI Automation reads taskbar bounds only", checks = Checks }));
      } catch (Exception error) { File.WriteAllText(output, Program.Json.Serialize(new { ok = false, error = error.ToString(), checks = Checks })); }
      finally { Cursor.Position = cursor; form.ReleaseBrowser(); form.Close(); }
    }
  }
}
#endif
