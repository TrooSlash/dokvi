#if TESTING
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MdReader {
  static class BenchmarkTests {
    static void Write(string name, object value) {
      string path = Path.Combine(Program.DataRoot, name + ".json");
      File.WriteAllText(path + ".tmp", Program.Json.Serialize(value));
      File.Move(path + ".tmp", path);
    }
    static async Task Wait(Func<Task<bool>> condition) {
      var clock = Stopwatch.StartNew();
      while (!await condition()) {
        if (clock.ElapsedMilliseconds > 120000) throw new TimeoutException("Benchmark readiness timed out");
        await Task.Delay(30);
      }
    }
    internal static async Task Run(ReaderForm form, string[] args) {
      string scenario = args.First(arg => arg.StartsWith("--benchmark=")).Substring(12);
      try {
        if (scenario != "empty") {
          await form.EnsureBrowser();
          await Wait(async () => form.UiReady && await form.Core.ExecuteScriptAsync(
            "document.getElementById('document').getAttribute('aria-busy')!=='true' && !document.body.inert") == "true");
          if (scenario == "formats") await Wait(async () => await form.Core.ExecuteScriptAsync(
            "!!document.querySelector('.mermaid-diagram svg') && !!document.querySelector('.katex')") == "true");
        }
        Write("ready", new { ok = true, scenario, readyMs = (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds,
          dpi = form.DeviceDpi, runtime = form.Core == null ? null : form.Core.Environment.BrowserVersionString });
        await Wait(() => Task.FromResult(File.Exists(Path.Combine(Program.DataRoot, "measure.done"))));
        if (scenario == "release") {
          await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
          await Wait(() => Task.FromResult(form.Core == null));
          Write("released", new { ok = true, nativeOnly = form.Core == null });
          await Wait(() => Task.FromResult(File.Exists(Path.Combine(Program.DataRoot, "release.done"))));
          Write("result", new { ok = true, nativeOnly = form.Core == null });
          return;
        }
        double inputMs = 0, previewMs = 0;
        bool paced = scenario == "large" && args.Contains("--benchmark-paced");
        bool exactText = false, undoRedo = false, savedExact = false;
        if (scenario != "empty") {
          await form.Core.ExecuteScriptAsync("document.getElementById('view-split').click();document.getElementById('source').focus();document.getElementById('source').setSelectionRange(0,0)");
          string original = paced ? Program.Json.Deserialize<string>(await form.Core.ExecuteScriptAsync("document.getElementById('source').value")) : null;
          string inserted = "";
          var clock = Stopwatch.StartNew();
          for (int i = 0; i < 30; i++) {
            string chunk = "probe-" + i + "\n"; inserted += chunk;
            await form.Core.CallDevToolsProtocolMethodAsync("Input.insertText", Program.Json.Serialize(new { text = chunk }));
            if (paced) await Task.Delay(Math.Max(0, (int)(800 * (i + 1) - clock.ElapsedMilliseconds)));
          }
          inputMs = clock.Elapsed.TotalMilliseconds;
          await Wait(async () => await form.Core.ExecuteScriptAsync("document.getElementById('document').textContent.includes('probe-29')") == "true");
          previewMs = clock.Elapsed.TotalMilliseconds - inputMs;
          if (paced) {
            string expected = inserted + original;
            exactText = await SourceEquals(form, expected);
            await Task.Delay(600);
            await Key(form, "Backspace", "Backspace", 8);
            string deleted = inserted.Substring(0, inserted.Length - 1) + original;
            bool backspace = await SourceEquals(form, deleted);
            await Key(form, "z", "KeyZ", 90, 2);
            bool undo = await SourceEquals(form, expected);
            await Key(form, "y", "KeyY", 89, 2);
            undoRedo = backspace && undo && await SourceEquals(form, deleted);
            await Key(form, "s", "KeyS", 83, 2);
            string path = form.Store.Entries.Values.Single().doc.path;
            await Wait(() => Task.FromResult(File.ReadAllText(path) == deleted));
            savedExact = File.ReadAllText(path) == deleted;
            if (!exactText || !undoRedo || !savedExact) throw new Exception("Paced edit lost text, undo/redo or saved content");
          }
        }
        Write("result", new { ok = true, input30Ms = inputMs, previewAfterInputMs = previewMs, paced, exactText, undoRedo, savedExact });
      } catch (Exception error) { Write("result", new { ok = false, error = error.ToString() }); }
      finally { form.ReleaseBrowser(); form.Close(); }
    }
    static async Task<bool> SourceEquals(ReaderForm form, string expected) {
      return Program.Json.Deserialize<string>(await form.Core.ExecuteScriptAsync("document.getElementById('source').value")) == expected;
    }
    static async Task Key(ReaderForm form, string key, string code, int virtualKey, int modifiers = 0) {
      foreach (string type in new[] { "keyDown", "keyUp" })
        await form.Core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", Program.Json.Serialize(new { type, key, code, windowsVirtualKeyCode = virtualKey, modifiers }));
      await Task.Delay(150);
    }
  }
}
#endif
