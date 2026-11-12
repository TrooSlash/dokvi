#if TESTING
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MdReader {
  static class SessionTests {
    static readonly List<string> checks = new List<string>();
    static string Root { get { return Program.DataRoot; } }
    static string Draft { get { return "# Черновик 🙂\n\n" + String.Join("\n", Enumerable.Range(1, 180).Select(i => "Строка " + i + " — несохранённый текст.")); } }
    static void Assert(bool condition, string label) { if (!condition) throw new Exception(label); checks.Add(label); }
    static async Task<T> JS<T>(ReaderForm form, string script) { return Program.Json.Deserialize<T>(await form.Core.ExecuteScriptAsync(script)); }
    static async Task Poll(Func<Task<bool>> test, string label) {
      var watch = Stopwatch.StartNew(); while (watch.ElapsedMilliseconds < 30000) { if (await test()) return; await Task.Delay(100); } throw new Exception("Timed out: " + label);
    }
    static void Result(string phase, bool ok, string error = null) { File.WriteAllText(Path.Combine(Root, phase + ".json"), Program.Json.Serialize(new { ok = ok, checks = checks, error = error })); }
    static async Task Insert(ReaderForm form, string text) {
      await form.Core.ExecuteScriptAsync("document.getElementById('view-source').click();document.getElementById('source').focus();document.getElementById('source').select()");
      await form.Core.CallDevToolsProtocolMethodAsync("Input.insertText", Program.Json.Serialize(new { text = text }));
    }
    static SessionManifest Manifest() { return Program.Json.Deserialize<SessionManifest>(File.ReadAllText(Path.Combine(Root, "session", "session.json"))); }
    static SessionDocument Snapshot(string id) { return Program.Json.Deserialize<SessionDocument>(File.ReadAllText(Path.Combine(Root, "session", id + ".json"))); }
    internal static async Task Run(ReaderForm form, string[] arguments) {
      NativeTests.Enabled = true;
      string phase = arguments.First(arg => arg.StartsWith("--session-phase=")).Substring("--session-phase=".Length);
      try {
        if (phase == "seed") {
          await form.Call("language", new object[] { "ru" });
          await StorageChecks();
          File.WriteAllText(Path.Combine(Root, "A.md"), "# Original A\r\n", new UTF8Encoding(true));
          File.WriteAllText(Path.Combine(Root, "B.md"), "# Original B");
          form.ReceiveArguments(new[] { Path.Combine(Root, "A.md"), Path.Combine(Root, "B.md") });
          await Poll(async delegate { return form.Core != null && form.UiReady && await JS<bool>(form, "document.querySelectorAll('.tab-item').length===2 && !document.querySelector('#document[aria-busy=true]')"); }, "two open files");
          await form.Core.ExecuteScriptAsync("document.querySelector('.file-tab').click()");
          await Poll(async delegate { return await JS<bool>(form, "document.getElementById('filename').textContent==='A.md'"); }, "activate A");
          await Insert(form, "# Unsaved A\n\nПравки в существующем файле.");
          await form.Core.ExecuteScriptAsync("document.getElementById('new').click()");
          await Poll(async delegate { return await JS<bool>(form, "document.querySelectorAll('.tab-item').length===3 && document.getElementById('filename').textContent.startsWith('Без имени')"); }, "create draft");
          await Insert(form, Draft);
          await form.Core.ExecuteScriptAsync("document.getElementById('source').setSelectionRange(430,430);document.getElementById('source').scrollTop=450");
          await Poll(async delegate {
            await Task.Delay(1);
            try { var saved = Manifest(); return saved.tabs.Count == 3 && saved.tabs[2].mode == "source" && saved.tabs[2].sourceTop > 200 && saved.tabs[2].start == 430 && Snapshot(saved.tabs[2].id).draft == Draft && Snapshot(saved.tabs[0].id).draft.StartsWith("# Unsaved A"); } catch (IOException) { return false; }
          }, "automatic durable checkpoint");
          Assert(File.ReadAllText(Path.Combine(Root, "A.md")) == "# Original A\r\n", "Autosave does not modify the original file");
          Assert(Manifest().activeId == Manifest().tabs[2].id, "Active tab, source mode, scroll and caret checkpointed");
          File.WriteAllText(Path.Combine(Root, "seed-state.json"), Program.Json.Serialize(Manifest()));
          Result(phase, true); // The parent now terminates this real process without FormClosing.
          return;
        }
        if (phase == "empty") {
          Assert(!form.Session.ShouldRestore, "Explicitly closed tabs do not return");
          Assert(form.Core == null, "Empty restart stays native without WebView2");
          Result(phase, true); form.Close(); return;
        }
        if (phase == "blank") {
          form.CreateFromNative();
          await Poll(async delegate { return form.Core != null && form.UiReady && await JS<bool>(form,"!!document.getElementById('source')?.editorMounted && document.getElementById('filename').textContent.startsWith('Без имени') && !document.querySelector('#document[aria-busy=true]')"); }, "new empty document before window close");
          Assert(await JS<bool>(form,"document.getElementById('source').value==='' && !document.querySelector('.tab-item.active').classList.contains('dirty') && !document.getElementById('source-save').disabled"), "Empty new document is unmodified and can still be saved explicitly");
          form.FormClosed += delegate { Assert(!form.Session.ShouldRestore, "Window close removes pristine new tabs from the next session"); Result(phase, true); };
          form.Close(); return;
        }
        if (phase == "missing") {
          string missingPath = Path.Combine(Root, "Missing-original.md");
          const string content = "# Текст из резервной копии\n\nИсходный файл удалён.";
          File.WriteAllText(missingPath, content);
          var doc = form.Store.Load(missingPath);
          await form.Session.Checkpoint(form.Store, null, new SessionManifest { tabs = new List<SessionPosition> { new SessionPosition { id = doc.id, mode = "split", text = content } } });
          File.Delete(missingPath);
          await form.EnsureBrowser();
          await Poll(async delegate { return form.Core != null && form.UiReady && await JS<bool>(form,"!!document.getElementById('source')?.editorMounted && !document.querySelector('#document[aria-busy=true]')"); }, "missing original restored as new draft");
          Assert(form.Store.Get(doc.id).doc.untitled && await JS<bool>(form,"document.querySelector('.tab-item.active').classList.contains('dirty') && document.getElementById('source').value===" + Program.Json.Serialize(content)), "Recovered nonempty copy of a missing file remains modified and protected");
          NativeTests.Answers.Enqueue(2); form.Close(); await Task.Delay(350);
          Assert(!form.IsDisposed && NativeTests.Answers.Count == 0, "Recovered nonempty document still asks before closing and honors Cancel");
          NativeTests.Answers.Enqueue(1);
          form.FormClosed += delegate { Result(phase, true); };
          form.Close(); return;
        }
        if (form.Session.ShouldRestore) await form.EnsureBrowser();
        await Poll(async delegate { return form.Core != null && form.UiReady && await JS<bool>(form, "document.querySelectorAll('.tab-item').length===3 && !document.querySelector('#document[aria-busy=true]')"); }, "restore three tabs");
        if (phase == "recover") {
          var original = Program.Json.Deserialize<SessionManifest>(File.ReadAllText(Path.Combine(Root, "seed-state.json")));
          Assert(await JS<bool>(form, "JSON.stringify(Array.from(document.querySelectorAll('.tab-item'),e=>e.dataset.id))===" + Program.Json.Serialize(Program.Json.Serialize(original.tabs.Select(tab => tab.id).ToArray()))), "Tab order and identities survive forced process termination");
          Assert(await JS<bool>(form, "document.getElementById('source').value===" + Program.Json.Serialize(Draft)), "Unsaved Unicode draft recovered exactly");
          Assert(await JS<bool>(form, "document.querySelector('.tab-item.active').dataset.id===" + Program.Json.Serialize(original.activeId) + " && document.getElementById('workspace').dataset.view==='source' && document.getElementById('source').scrollTop>200 && document.getElementById('source').selectionStart===430"), "Active tab, mode, caret and scroll restored");
          NativeTests.SavePath = null;
          await form.Core.ExecuteScriptAsync("document.getElementById('source-save').click()");
          await Task.Delay(300);
          Assert(await JS<bool>(form, "!document.body.inert && document.getElementById('source').value===" + Program.Json.Serialize(Draft)), "Cancel Save As retains recovered draft");
          await form.Core.ExecuteScriptAsync("document.querySelector('.file-tab').click()");
          await Poll(async delegate { return await JS<bool>(form, "document.getElementById('filename').textContent==='A.md'"); }, "restored A");
          Assert(await JS<bool>(form, "document.getElementById('source').value==='# Unsaved A\\n\\nПравки в существующем файле.' && document.querySelector('.tab-item.active').classList.contains('dirty')"), "Existing-file edits remain unsaved after recovery");
          NativeTests.Answers.Enqueue(2);
          await form.Core.ExecuteScriptAsync("document.getElementById('source-save').click()");
          await Poll(async delegate { return await JS<bool>(form, "!document.body.inert"); }, "external change cancellation");
          Assert(File.ReadAllText(Path.Combine(Root, "A.md")) == "# External A", "Recovery keeps conflict detection and does not overwrite external edits");
          await form.Core.ExecuteScriptAsync("document.querySelectorAll('.file-tab')[1].click()");
          await Poll(async delegate { return await JS<bool>(form, "document.getElementById('filename').textContent==='B.md'"); }, "restored B");
          Assert(await JS<bool>(form, "document.getElementById('source').value==='# External B' && !document.querySelector('.tab-item.active').classList.contains('dirty')"), "Clean tabs reload the latest disk version");
          NativeTests.Answers.Enqueue(1); NativeTests.Answers.Enqueue(2);
          form.Close(); await Task.Delay(500);
          Assert(!form.IsDisposed && Snapshot(original.tabs[0].id).draft.StartsWith("# Unsaved A"), "Cancel later close prompt keeps all earlier unsaved drafts recoverable");
          NativeTests.SavePath = Path.Combine(Root, "Saved-draft.md");
          NativeTests.Answers.Enqueue(1); // Explicitly discard A, preserving its tab for next launch.
          NativeTests.Answers.Enqueue(0); // Save the recovered untitled draft.
          form.FormClosed += delegate { Result(phase, true); };
          form.Close(); return;
        }
        Assert(await JS<bool>(form, "Array.from(document.querySelectorAll('.file-tab span'),e=>e.textContent).join('|')==='A.md|B.md|Saved-draft.md'"), "Normal exit restores saved tabs and renamed draft");
        Assert(await JS<bool>(form, "document.querySelectorAll('.tab-item.dirty').length===0"), "Explicitly discarded changes do not return");
        Assert(File.ReadAllText(Path.Combine(Root, "Saved-draft.md")) == Draft, "Recovered draft saves exact content to chosen destination");
        for (int remaining = 3; remaining > 0; remaining--) {
          await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .tab-close').click()");
          int expected = remaining - 1;
          await Poll(async delegate { return expected == 0 ? form.Core == null : form.Core != null && await JS<bool>(form, "!document.body.inert && document.querySelectorAll('.tab-item').length===" + expected); }, "close restored tab");
        }
        Assert(!form.Session.ShouldRestore, "Closing last tab writes an empty session");
        Result(phase, true); form.Close();
      } catch (Exception error) {
        Result(phase, false, error.ToString()); form.ReleaseBrowser(); form.Close();
      }
    }
    static async Task StorageChecks() {
      string root = Path.Combine(Root, "storage-checks"); Directory.CreateDirectory(root);
      var store = new FileStore(); var doc = store.CreateDocument(); var session = new SessionStore(root);
      var request = new SessionManifest { activeId = doc.id, tabs = new List<SessionPosition> { new SessionPosition { id = doc.id, mode = "split", text = "first" } } };
      await session.Checkpoint(store, null, request);
      request.tabs[0].text = "second 🙂"; await session.Checkpoint(store, null, request);
      string content = Path.Combine(root, "session", doc.id + ".json");
      var lastWrite = File.GetLastWriteTimeUtc(content);
      request.tabs[0].text = null; request.tabs[0].sourceTop = 100; await session.Checkpoint(store, null, request);
      Assert(File.GetLastWriteTimeUtc(content) == lastWrite, "Scrolling only writes small metadata, not document backups");
      File.WriteAllText(content, "{broken");
      File.WriteAllText(Path.Combine(root, "session", "session.json"), "{broken");
      var restored = new SessionStore(root).Restore(new FileStore());
      Assert(restored.documents.Count == 1 && restored.restored[0].text == "first", "Corrupt current snapshot and manifest fall back to atomic backups");
      string missing = Path.Combine(root, "missing.md"); File.WriteAllText(missing, "Original");
      var missingStore = new FileStore(); var missingDoc = missingStore.Load(missing);
      var missingSession = new SessionStore(Path.Combine(root, "missing-profile"));
      await missingSession.Checkpoint(missingStore, null, new SessionManifest { tabs = new List<SessionPosition> { new SessionPosition { id = missingDoc.id, text = "Unsaved missing content" } } });
      File.Delete(missing);
      var missingRestored = missingSession.Restore(new FileStore());
      Assert(missingRestored.documents.Single().untitled && missingRestored.documents.Single().path == null && missingRestored.restored.Single().text == "Unsaved missing content", "Deleted original restored as an untitled copy without data loss");
      string failureRoot = Path.Combine(root, "write-failure"); Directory.CreateDirectory(failureRoot); File.WriteAllText(Path.Combine(failureRoot, "session"), "blocks directory creation");
      bool failed = false;
      try { await new SessionStore(failureRoot).Checkpoint(store, null, request); } catch (IOException) { failed = true; }
      Assert(failed, "Backup write failure is surfaced to the UI");
    }
  }
}
#endif
