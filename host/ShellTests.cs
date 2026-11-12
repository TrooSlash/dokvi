#if TESTING
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace MdReader {
  // Observes real Windows Shell launches into an isolated test profile. There is
  // no probe, alternate profile switch or test IPC in the production binary.
  static class ShellTests {
    internal static void Observe(ReaderForm form) {
      var busy = new Mutex(false, "Local\\Dokvi.InstallTest.Busy");
      var timer = new System.Windows.Forms.Timer { Interval = 150 };
      string previous = null;
      timer.Tick += delegate {
        if (File.Exists(Path.Combine(Program.DataRoot, "shell-probe.stop"))) { timer.Stop(); form.Close(); return; }
        if (!form.UiReady) return;
        string state = Program.Json.Serialize(new { pid = Process.GetCurrentProcess().Id, title = form.Text,
          paths = form.Store.Entries.Values.Select(entry => entry.doc.path).Where(path => path != null).ToArray() });
        if (state == previous) return;
        string target = Path.Combine(Program.DataRoot, "shell-probe.json");
        File.WriteAllText(target + ".tmp", state);
        if (File.Exists(target)) File.Replace(target + ".tmp", target, null); else File.Move(target + ".tmp", target);
        previous = state;
      };
      form.FormClosed += delegate { timer.Dispose(); busy.Dispose(); };
      timer.Start();
    }
  }
}
#endif
