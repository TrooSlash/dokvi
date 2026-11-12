#if TESTING
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdReader {
  static class InputFocusTests {
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int dx, dy; public uint data, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KeyboardInput { public ushort key, scan; public uint flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MouseInput mouse; [FieldOffset(0)] public KeyboardInput keyboard; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint type; public InputUnion data; }
    [DllImport("user32")] static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32")] static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32")] static extern IntPtr GetFocus();
    static readonly List<object> Checks = new List<object>();
    static bool Passed = true;
    static void Send(params Input[] input) {
      if (SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(Input))) != input.Length) throw new Exception("Windows rejected physical input");
    }
    internal static async Task Click(ReaderForm form, Point point) {
      Move(form, point);
      Send(new Input { data = new InputUnion { mouse = new MouseInput { flags = 2 } } }, new Input { data = new InputUnion { mouse = new MouseInput { flags = 4 } } });
      await Task.Delay(250);
    }
    internal static void Move(ReaderForm form, Point point) {
      if (GetAncestor(WindowFromPoint(point), 2) != form.Handle) throw new Exception("Test pointer target is covered by another window");
      var desktop=SystemInformation.VirtualScreen;
      Send(new Input { data = new InputUnion { mouse = new MouseInput { dx=(int)((point.X-desktop.Left+.5)*65536/desktop.Width),dy=(int)((point.Y-desktop.Top+.5)*65536/desktop.Height),flags=0xC001 } } });
    }
    internal static void MouseButton(bool down) { Send(new Input { data = new InputUnion { mouse = new MouseInput { flags = down ? 2u : 4u } } }); }
    internal static Task Click(ReaderForm form, Control control) { return Click(form, control.PointToScreen(new Point(control.Width / 2, control.Height / 2))); }
    internal static bool OwnsForeground(ReaderForm form) {
      for (var window=GetForegroundWindow();window!=IntPtr.Zero;window=GetWindow(window,4)) if(window==form.Handle)return true;
      return false;
    }
    internal static async Task TypeText(ReaderForm form,string text) {
      if (!OwnsForeground(form)) throw new Exception("Test window or its dialog lost keyboard focus");
      await Task.Run(() => {
        foreach(char character in text) Send(
          new Input {type=1,data=new InputUnion {keyboard=new KeyboardInput {scan=character,flags=4}}},
          new Input {type=1,data=new InputUnion {keyboard=new KeyboardInput {scan=character,flags=6}}});
      });
      await Task.Delay(100);
    }
    internal static async Task Press(ReaderForm form, ushort key, bool control = false, bool shift = false) {
      if (!OwnsForeground(form)) throw new Exception("Test window or its dialog lost keyboard focus");
      await Task.Run(() => {
        Action<ushort, bool> sendKey = (vk, up) => {
          bool extended=vk>=0x21 && vk<=0x28 || vk==0x2D || vk==0x2E;
          Send(new Input { type = 1, data = new InputUnion { keyboard = new KeyboardInput { scan = (ushort)MapVirtualKey(vk,0), flags = 8u | (up ? 2u : 0u) | (extended ? 1u : 0u) } } });
        };
        if (control) sendKey(0x11, false);
        if (shift) sendKey(0x10, false);
        try { sendKey(key, false); Thread.Sleep(30); sendKey(key, true); }
        finally { if (shift) sendKey(0x10, true); if (control) sendKey(0x11, true); }
      });
      await Task.Delay(250);
    }
    internal static async Task<T> JS<T>(ReaderForm form, string script) { return Program.Json.Deserialize<T>(await form.Core.ExecuteScriptAsync(script)); }
    internal static Task<string> Text(ReaderForm form) { return JS<string>(form, "document.querySelector('.source-editor').value"); }
    internal static async Task Run(ReaderForm form) {
      var cursor = Cursor.Position;
      string output = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "input-focus-result.json");
      try {
        string path = Path.Combine(Program.DataRoot, "enter.md");
        File.WriteAllText(path, "ENTER-PROBE\nSECOND-LINE\n");
        form.ReceiveArguments(new[] { path });
        for (int i = 0; i < 400 && !form.UiReady; i++) await Task.Delay(50);
        if (!form.UiReady) throw new Exception("Document did not open");
        SetForegroundWindow(form.Handle); await Task.Delay(300);
        var buttons = form.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>()).OfType<CaptionButton>().ToArray();
        foreach (string mode in new[] { "split", "source" }) {
          await form.Core.ExecuteScriptAsync("document.getElementById('view-" + mode + "').click()"); await Task.Delay(300);
          foreach (string action in new[] { "maximize", "restore", "minimize" }) {
            if (form.WindowState != FormWindowState.Normal) form.RestoreWindow();
            if (action == "restore") { await Click(form, buttons[1]); await Click(form, buttons[1]); }
            else if (action == "maximize") await Click(form, buttons[1]);
            else {
              await Click(form, buttons[0]);
              if (form.WindowState != FormWindowState.Minimized) throw new Exception("Minimize click failed");
              // Resume the harness; this is not a taskbar-click assertion.
              form.RestoreWindow(); SetForegroundWindow(form.Handle); await Task.Delay(300);
            }
            var point = await JS<int[]>(form, "(()=>{const r=document.querySelector('.cm-line').getBoundingClientRect();return [Math.round((r.x+35)*devicePixelRatio),Math.round((r.y+r.height/2)*devicePixelRatio)]})()");
            await Click(form, form.View.PointToScreen(new Point(point[0], point[1])));
            string initial = await Text(form);
            await Press(form, 0x58);
            string before = await Text(form);
            if (before.Length != initial.Length + 1) throw new Exception("Typing did not reach the editor after " + action);
            string typed = before;
            bool enterPassed = true;
            int position = await JS<int>(form, "document.querySelector('.source-editor').selectionStart");
            var bounds = form.Bounds; var state = form.WindowState;
            string activeControl = form.ActiveControl == null ? null : form.ActiveControl.GetType().Name;
            long focus = GetFocus().ToInt64();
            for (int enter = 0; enter < 2; enter++) {
              string expected = before.Insert(position, "\n");
              await Press(form, 0x0D);
              string actual = await Text(form);
              bool ok = actual == expected && form.Bounds == bounds && form.WindowState == state;
              Passed &= ok;
              enterPassed &= ok;
              Checks.Add(new { mode, action, enter, ok, before, expected, actual, stateBefore = state.ToString(), stateAfter = form.WindowState.ToString(), activeControl, nativeFocus = focus });
              if (!ok) break;
              before = actual; position++;
            }
            if (enterPassed) {
              await Press(form, 0x5A, true); await Press(form, 0x5A, true);
              bool undo = await Text(form) == typed && form.WindowState == state;
              Passed &= undo; Checks.Add(new { mode, action, command = "Ctrl+Z twice", ok = undo });
              await Press(form, 0x59, true); await Press(form, 0x59, true);
              bool redo = await Text(form) == before && form.WindowState == state;
              Passed &= redo; Checks.Add(new { mode, action, command = "Ctrl+Y twice", ok = redo });
              await Press(form, 0x53, true);
              for (int i = 0; i < 80 && (File.ReadAllText(path) != before || !await JS<bool>(form,"!document.body.inert")); i++) await Task.Delay(50);
              bool saved = File.ReadAllText(path) == before && await JS<bool>(form,"!document.body.inert") && form.WindowState == state;
              Passed &= saved; Checks.Add(new { mode, action, command = "Ctrl+S", ok = saved });
              string focusBeforeTab = await JS<string>(form, "document.activeElement.outerHTML.slice(0,250)");
              await Press(form, 0x09);
              string indented = await Text(form);
              bool tab = indented.Length > before.Length && await JS<bool>(form, "!!document.activeElement.closest('.cm-editor')");
              Passed &= tab; Checks.Add(new { mode, action, command = "Tab indentation", ok = tab, before, indented, focusBeforeTab,
                focusAfterTab = await JS<string>(form, "document.activeElement.outerHTML.slice(0,250)") });
              await Press(form, 0x1B); await Press(form, 0x09);
              bool exit = await Text(form) == indented && await JS<bool>(form, "!document.activeElement.closest('.cm-editor')");
              Passed &= exit; Checks.Add(new { mode, action, command = "Escape then Tab", ok = exit });
            }
            if (form.WindowState != FormWindowState.Normal) { form.RestoreWindow(); SetForegroundWindow(form.Handle); await Task.Delay(300); }
          }
        }
        File.WriteAllText(output, Program.Json.Serialize(new { ok = Passed, method = "SendInput caption clicks, editor clicks and keyboard scan codes; no managed focus repair", checks = Checks }));
      } catch (Exception error) { File.WriteAllText(output, Program.Json.Serialize(new { ok = false, error = error.ToString(), checks = Checks })); }
      finally { Cursor.Position = cursor; form.ReleaseBrowser(); form.Close(); }
    }
  }
}
#endif
