#if TESTING
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MdReader {
  static class ShortcutTests {
    [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32")] static extern IntPtr GetWindow(IntPtr window, uint command);
    static IntPtr TargetWindow;
    static ReaderForm TargetForm;
    static readonly List<string> Checks = new List<string>();
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); Checks.Add(message); }
    static Task Press(ushort key, bool control = true, bool shift = false) { return InputFocusTests.Press(TargetForm,key,control,shift); }
    static async Task TypePath(string path) {
      await Press(0x41);
      await InputFocusTests.TypeText(TargetForm,path);
      await Press(0x0D, false);
    }
    static async Task Until(Func<Task<bool>> condition, string name) {
      for (int i = 0; i < 200; i++) { if (await condition()) return; await Task.Delay(50); }
      throw new TimeoutException(name);
    }
    static async Task<bool> JS(ReaderForm form, string expression) {
      return form.Core != null && await form.Core.ExecuteScriptAsync(expression) == "true";
    }
    static async Task Ready(ReaderForm form) {
      await Until(() => JS(form, "!document.body.inert && document.getElementById('document').getAttribute('aria-busy')!=='true'"), "Document readiness");
    }
    static bool DialogOpen() { var foreground = GetForegroundWindow(); return foreground != TargetWindow && GetWindow(foreground, 4) == TargetWindow; }
    static async Task<string> Value(ReaderForm form) {
      return Program.Json.Deserialize<string>(await form.Core.ExecuteScriptAsync("document.getElementById('source').value"));
    }
    internal static async Task Run(ReaderForm form) {
      var result = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shortcut-result.json");
      try {
        TargetWindow = form.Handle; TargetForm=form;
        string path = Path.Combine(Program.DataRoot, "shortcuts.md"), original = "# Keyboard\n\nOriginal.";
        File.WriteAllText(path, original);
        form.ReceiveArguments(new[] { path });
        for (int i = 0; i < 400 && !form.UiReady; i++) await Task.Delay(50);
        Assert(form.UiReady, "Document opened in isolated test profile");
        await form.Core.ExecuteScriptAsync("document.getElementById('view-split').click()"); await Task.Delay(300);
        SetForegroundWindow(form.Handle);
        await form.Core.ExecuteScriptAsync("var f=document.getElementById('source');f.focus();f.setSelectionRange(f.value.length,f.value.length)");
        await Press(0x58, false);
        string edited = await Value(form);
        Assert(edited.StartsWith(original) && edited.Length == original.Length + 1, "Physical key inserts exactly one character");
        await Press(0x5A); Assert(await Value(form) == original, "Ctrl+Z undoes typed character");
        await Press(0x59); Assert(await Value(form) == edited, "Ctrl+Y restores typed character");
        await Press(0x53);
        for (int i = 0; i < 80 && File.ReadAllText(path) != edited; i++) await Task.Delay(50);
        Assert(File.ReadAllText(path) == edited, "Ctrl+S saves exact edited text to existing file");
        await Ready(form);
        await Press(0x5A); await Press(0x5A, true, true);
        Assert(await Value(form) == edited, "Ctrl+Shift+Z redoes the last edit");
        await Press(0x53);
        await Ready(form);

        foreach (string format in new[] { "bold", "italic", "strike" }) {
          await form.Core.ExecuteScriptAsync("var f=document.getElementById('source');f.focus();f.setSelectionRange(0,1);document.querySelector('[data-format=" + format + "]').click()");
          string formatted = await Value(form);
          Assert(formatted != edited, format + " button changes selected text");
          await Press(0x5A); Assert(await Value(form) == edited, "Ctrl+Z restores text after " + format);
          await Press(0x59); Assert(await Value(form) == formatted, "Ctrl+Y restores " + format);
          await form.Core.ExecuteScriptAsync("document.querySelector('[data-format=" + format + "]').click()");
          Assert(await Value(form) == edited, "Repeat removes " + format);
        }
        await Press(0x53);
        await Ready(form);

        await Press(0x4E); await Ready(form);
        await Press(0x58, false);
        string draft = await Value(form);
        Assert(draft.Length == 1, "Ctrl+N focuses the empty source for physical typing");
        await Press(0x53);
        await Until(() => Task.FromResult(DialogOpen()), "First Save opens a real Windows dialog");
        await Press(0x1B, false);
        await Ready(form);
        Assert(await Value(form) == draft, "Cancel in the real Save dialog retains the draft");
        await form.Core.ExecuteScriptAsync("document.getElementById('source').focus()");
        await Press(0x53);
        await Until(() => Task.FromResult(DialogOpen()), "Repeated first Save opens the chooser");
        string created = Path.Combine(Program.DataRoot, "keyboard-new.md");
        await TypePath(created);
        await Until(() => Task.FromResult(File.Exists(created)), "First Ctrl+S writes the chosen file");
        Assert(File.ReadAllText(created) == draft, "First Ctrl+S saves exact draft through the real chooser");
        await Ready(form);
        await form.Core.ExecuteScriptAsync("document.getElementById('source').focus()");
        await Press(0x53, true, true);
        await Until(() => Task.FromResult(DialogOpen()), "Ctrl+Shift+S opens Save As");
        string copy = Path.Combine(Program.DataRoot, "keyboard-copy.md");
        await TypePath(copy);
        await Until(() => Task.FromResult(File.Exists(copy)), "Save As writes the copy");
        Assert(File.ReadAllText(copy) == draft && File.ReadAllText(created) == draft, "Ctrl+Shift+S preserves the original and creates the exact copy");
        await Ready(form);
        await form.Core.ExecuteScriptAsync("document.getElementById('source').focus()");
        await Press(0x57); await Ready(form);

        for (int i = 0; i < 9; i++) { await Press(0x4E); await Ready(form); }
        var names = Program.Json.Deserialize<string[]>(await form.Core.ExecuteScriptAsync("Array.from(document.querySelectorAll('.file-tab')).map(t=>t.textContent)"));
        Assert(names.Length == 10, "Ten tabs are available for numeric shortcuts");
        for (int i = 1; i <= 9; i++) {
          await Press((ushort)(0x30 + i)); await Ready(form);
          string active = Program.Json.Deserialize<string>(await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .file-tab').textContent"));
          Assert(active == names[i == 9 ? names.Length - 1 : i - 1], "Ctrl+" + i + " selects the expected tab");
        }
        await Press(0x09); await Ready(form);
        Assert(await JS(form, "document.querySelector('.file-tab').getAttribute('aria-selected')==='true'"), "Ctrl+Tab wraps from last to first tab");
        await Press(0x09, true, true); await Ready(form);
        Assert(Program.Json.Deserialize<string>(await form.Core.ExecuteScriptAsync("document.querySelector('.tab-item.active .file-tab').textContent")) == names[9], "Ctrl+Shift+Tab wraps to the last tab");
        for (int i = 0; i < 9; i++) { await Press(0x57); await Ready(form); }

        await Press(0x46); Assert(await JS(form, "!document.getElementById('searchbar').hidden && document.activeElement.id==='search-input'"), "Ctrl+F opens focused search");
        await Press(0x1B, false); Assert(await JS(form, "document.getElementById('searchbar').hidden"), "Escape closes search");
        await Press(0x42); Assert(await JS(form, "document.getElementById('outline-toggle').getAttribute('aria-expanded')==='true'"), "Ctrl+B opens the outline");
        await Press(0x42); Assert(await JS(form, "document.getElementById('outline-toggle').getAttribute('aria-expanded')==='false'"), "Ctrl+B closes the outline");
        await Press(0x45); Assert(await JS(form, "getComputedStyle(document.getElementById('source-pane')).display==='none'"), "Ctrl+E switches from editing to reading");
        await Press(0x45, true, true); Assert(await JS(form, "getComputedStyle(document.getElementById('source-pane')).display!=='none'"), "Ctrl+Shift+E returns to editing with preview");
        await Press(0x70, false); Assert(await JS(form, "document.getElementById('help-dialog').open"), "F1 opens help");
        await Press(0x1B, false); Assert(await JS(form, "!document.getElementById('help-dialog').open"), "Escape closes help");
        File.WriteAllText(path, original + "\nReloaded from disk.");
        await Press(0x52); await Ready(form);
        await Until(async () => await Value(form) == File.ReadAllText(path), "Ctrl+R reloads the changed file");
        Assert(await Value(form) == original + "\nReloaded from disk.", "Ctrl+R reloads exact external contents");
        await Press(0x50); await Until(() => Task.FromResult(DialogOpen()), "Ctrl+P opens a real PDF dialog");
        await Press(0x1B, false);
        await Until(() => JS(form, "!document.getElementById('workspace').inert"), "PDF cancellation releases the interface");
        Assert(await Value(form) == original + "\nReloaded from disk.", "Cancelling Ctrl+P preserves the source");
        await Press(0x4F); await Until(() => Task.FromResult(DialogOpen()), "Ctrl+O opens a real Open dialog");
        await Press(0x1B, false);
        await Ready(form);
        Assert(await JS(form, "document.querySelectorAll('.tab-item').length===1"), "Cancelling Ctrl+O preserves the open tab");
        await form.Core.ExecuteScriptAsync("document.getElementById('source').focus()");
        Assert(await JS(form, "!!document.activeElement.closest('.cm-editor')"), "Editor has keyboard focus after the Open dialog");
        await Press(0x1B, false); await Press(0x09, false);
        Assert(await form.Core.ExecuteScriptAsync("!document.activeElement.closest('.cm-editor')") == "true", "Escape then Tab exits editor focus");
        await Press(0x4E);
        Assert(await form.Core.ExecuteScriptAsync("document.querySelectorAll('.tab-item').length") == "2", "Ctrl+N creates a new tab");
        await Press(0x57);
        Assert(await form.Core.ExecuteScriptAsync("document.querySelectorAll('.tab-item').length") == "1", "Ctrl+W closes untouched new tab without a prompt");
        await Press(0x57);
        for (int i = 0; i < 100 && form.Core != null; i++) await Task.Delay(50);
        Assert(form.Core == null, "Ctrl+W closes saved file and returns to native empty window");
        File.WriteAllText(result, Program.Json.Serialize(new { ok = true, nativeDpi = form.DeviceDpi, method = "Win32 SendInput keyboard scan codes; real Open/Save dialogs; formatting buttons invoked through DOM", checks = Checks }));
      } catch (Exception error) { File.WriteAllText(result, Program.Json.Serialize(new { ok = false, error = error.ToString(), checks = Checks })); }
      finally { form.ReleaseBrowser(); form.Close(); }
    }
  }
}
#endif
