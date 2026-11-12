#if TESTING
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace MdReader {
  static class FocusRoutingTests {
    internal static void Run() {
      var checks = new List<object>();
      bool passed = true;
      try {
        foreach (bool document in new[] { false, true }) foreach (string action in new[] { "minimize", "maximize", "restore" }) {
          using (var form = new ReaderForm(new string[0])) {
            // An unshown form exercises WinForms key routing without desktop input.
            if (document) { form.View = new WebView2(); form.Controls.Add(form.View); form.UiReady = true; }
            var buttons = form.Controls.Cast<Control>().SelectMany(c => c.Controls.Cast<Control>()).OfType<CaptionButton>().ToArray();
            form.ActiveControl = buttons[action == "minimize" ? 0 : 1];
            if (action == "minimize") { form.MinimizeWindow(); form.RestoreWindow(); }
            else if (action == "maximize") form.ToggleMaximize();
            else { form.ToggleMaximize(); form.ToggleMaximize(); }
            var state = form.WindowState;
            bool handled = (bool)typeof(ReaderForm).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { Keys.Enter });
            bool ok = !handled && form.WindowState == state;
            passed &= ok;
            checks.Add(new { document, action, ok, enterConsumedByHost = handled, stateBefore = state.ToString(), stateAfter = form.WindowState.ToString() });
          }
        }
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "focus-routing-result.json"), Program.Json.Serialize(new { ok = passed, method = "Unshown WinForms form; window command followed by framework dialog-key preprocessing. No mouse/taskbar/typing claim.", checks }));
      } catch (Exception error) {
        passed = false;
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "focus-routing-result.json"), Program.Json.Serialize(new { ok = false, error = error.ToString(), checks }));
      }
      System.Environment.ExitCode = passed ? 0 : 1;
    }
  }
}
#endif
