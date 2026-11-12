#if TESTING
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MdReader {
  static class StorageTests {
    static readonly List<string> checks = new List<string>();
    static readonly UTF8Encoding utf8 = new UTF8Encoding(false);
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Fails(Action action) {
      try { action(); } catch (IOException) { return; } catch (UnauthorizedAccessException) { return; }
      throw new Exception("Expected a file access failure");
    }
    static void Check(string name, Action<string> test) {
      string root = Path.Combine(Program.DataRoot, Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(root);
      test(Path.Combine(root, "document.md"));
      Assert(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "Temporary file leaked: " + name);
      checks.Add(name);
    }
    internal static void Run() {
      bool ok = false; string error = null;
      try {
        Check("First session write creates an identical UTF-8 backup without BOM", path => {
          const string text = "Черновик 🙂\r\nТекст\n";
          SessionStore.AtomicWrite(path, text);
          Assert(File.ReadAllBytes(path).SequenceEqual(utf8.GetBytes(text)), "Session text or encoding changed");
          Assert(File.ReadAllBytes(path + ".bak").SequenceEqual(File.ReadAllBytes(path)), "Initial backup differs");
        });
        Check("Replacing a session snapshot retains the previous revision in backup", path => {
          SessionStore.AtomicWrite(path, "first");
          SessionStore.AtomicWrite(path, "second");
          SessionStore.AtomicWrite(path, "third");
          Assert(File.ReadAllText(path) == "third" && File.ReadAllText(path + ".bak") == "second", "Wrong backup revision");
          Assert(!File.Exists(path + ".bak.bak"), "Unexpected nested backup");
        });
        Check("Writing without backup preserves an existing backup", path => {
          SessionStore.AtomicWrite(path, "first", false);
          Assert(!File.Exists(path + ".bak"), "Backup created when disabled");
          File.WriteAllText(path + ".bak", "keep");
          SessionStore.AtomicWrite(path, "second", false);
          Assert(File.ReadAllText(path) == "second" && File.ReadAllText(path + ".bak") == "keep", "Backup changed when disabled");
        });
        Check("Empty snapshots remain empty in both current and backup files", path => {
          SessionStore.AtomicWrite(path, "");
          Assert(new FileInfo(path).Length == 0 && new FileInfo(path + ".bak").Length == 0, "Empty snapshot gained bytes");
        });
        Check("Locked session target preserves both revisions and can be retried", path => {
          SessionStore.AtomicWrite(path, "first");
          SessionStore.AtomicWrite(path, "second");
          using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            Fails(() => SessionStore.AtomicWrite(path, "blocked"));
            Assert(File.ReadAllText(path) == "second" && File.ReadAllText(path + ".bak") == "first", "Failed write changed snapshots");
            Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path), "*.tmp").Any(), "Failed write left a temporary file");
          }
          SessionStore.AtomicWrite(path, "retry");
          Assert(File.ReadAllText(path) == "retry" && File.ReadAllText(path + ".bak") == "second", "Retry did not retain old revision");
        });
        Check("Missing parent is reported without creating a directory", path => {
          string parent = Path.Combine(Path.GetDirectoryName(path), "missing");
          Fails(() => SessionStore.AtomicWrite(Path.Combine(parent, "session.json"), "text"));
          Assert(!Directory.Exists(parent), "Storage unexpectedly created parent");
        });
        Check("Document creation and replacement preserve format without session backups", path => {
          var format = new Document { encoding = "utf-16le", bom = true, lineEnding = "\r\n" };
          var first = FileStore.Write(path, "Заголовок\n🙂", format, null);
          var second = FileStore.Write(path, first.text + "\nПравка", first, first.revision);
          Assert(second.text == "Заголовок\r\n🙂\r\nПравка" && second.bom && second.encoding == "utf-16le", "Document format changed");
          Assert(!File.Exists(path + ".bak"), "Document gained a session backup");
        });
        Check("Stale document revision preserves the external edit", path => {
          File.WriteAllText(path, "first", utf8);
          var doc = FileStore.Read(path, "utf-8");
          File.WriteAllText(path, "external", utf8);
          bool rejected = false;
          try { FileStore.Write(path, "overwrite", doc, doc.revision); } catch (ChangedException) { rejected = true; }
          Assert(rejected && File.ReadAllText(path) == "external", "Stale revision overwrote external text");
        });
        Check("Deleted document is not recreated with a stale revision", path => {
          File.WriteAllText(path, "first", utf8);
          var doc = FileStore.Read(path, "utf-8"); File.Delete(path);
          bool rejected = false;
          try { FileStore.Write(path, "overwrite", doc, doc.revision); } catch (ChangedException) { rejected = true; }
          Assert(rejected && !File.Exists(path), "Deleted file was recreated");
        });
        Check("Locked document preserves original bytes and can be saved after unlock", path => {
          File.WriteAllText(path, "original", utf8);
          var doc = FileStore.Read(path, "utf-8");
          using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            Fails(() => FileStore.Write(path, "blocked", doc, doc.revision));
            Assert(File.ReadAllText(path) == "original", "Failed save changed original");
            Assert(!Directory.EnumerateFiles(Path.GetDirectoryName(path), "*.tmp").Any(), "Failed save left a temporary file");
          }
          var saved = FileStore.Write(path, "retry", doc, doc.revision);
          Assert(saved.text == "retry", "Save after unlock failed");
        });
        Check("Pre-commit conflict preserves external text and the existing backup", path => {
          SessionStore.AtomicWrite(path, "original");
          bool rejected = false;
          try {
            AtomicFile.Write(path, utf8.GetBytes("overwrite"), path + ".bak", () => {
              File.WriteAllText(path, "external", utf8);
              throw new ChangedException();
            });
          } catch (ChangedException) { rejected = true; }
          Assert(rejected && File.ReadAllText(path) == "external" && File.ReadAllText(path + ".bak") == "original", "Conflict guard failed");
        });
        Check("Successful pre-commit check retains the previous bytes in backup", path => {
          SessionStore.AtomicWrite(path, "original");
          AtomicFile.Write(path, utf8.GetBytes("replacement"), path + ".bak", () => {
            Assert(File.ReadAllText(path) == "original", "Target changed before validation");
          });
          Assert(File.ReadAllText(path) == "replacement" && File.ReadAllText(path + ".bak") == "original", "Validated replacement failed");
        });
        ok = true;
      } catch (Exception failure) { error = failure.ToString(); Environment.ExitCode = 1; }
      File.WriteAllText(Path.Combine(Program.DataRoot, "storage-result.json"), Program.Json.Serialize(new { ok = ok, checks = checks, error = error }));
    }
  }
}
#endif
