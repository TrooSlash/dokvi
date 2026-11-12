using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MdReader {
  sealed class SessionPosition {
    public string id, mode;
    public double readTop, sourceTop;
    public int start, end;
    // Only changed documents carry text across the bridge.
    public string text;
    public bool forgetDraft = false;
  }
  sealed class SessionManifest {
    public int version = 1;
    public string activeId;
    public List<SessionPosition> tabs = new List<SessionPosition>();
  }
  sealed class SessionDocument {
    public int version = 1;
    public Document doc;
    public string root, draft;
  }
  sealed class SessionStore {
    readonly string directory;
    readonly Dictionary<string, SessionDocument> documents = new Dictionary<string, SessionDocument>();
    string lastManifest;
    internal SessionStore(string dataRoot) { directory = Path.Combine(dataRoot, "session"); }
    string ManifestPath { get { return Path.Combine(directory, "session.json"); } }
    internal bool Exists { get { return File.Exists(ManifestPath) || File.Exists(ManifestPath + ".bak"); } }
    internal bool ShouldRestore { get { var manifest = Read<SessionManifest>(ManifestPath, ValidManifest); return manifest == null ? Exists : manifest.tabs.Count > 0; } }
    static bool ValidId(string id) { return id == "sample" || (id != null && Regex.IsMatch(id, "^[a-f0-9]{32}$")); }
    static string Normal(string text) { return text.Replace("\r\n", "\n").Replace('\r', '\n'); }
    static T Read<T>(string path, Func<T, bool> valid) where T : class {
      foreach (string candidate in new[] { path, path + ".bak" }) {
        try {
          if (!File.Exists(candidate) || new FileInfo(candidate).Length > 256L * 1024 * 1024) continue;
          var value = Program.Json.Deserialize<T>(File.ReadAllText(candidate, Encoding.UTF8));
          if (value != null && valid(value)) return value;
        } catch (IOException) {} catch (UnauthorizedAccessException) {} catch (ArgumentException) {} catch (InvalidOperationException) {}
      }
      return null;
    }
    static bool ValidManifest(SessionManifest value) {
      return value.version == 1 && value.tabs != null && value.tabs.Count <= 1000 && value.tabs.All(tab => tab != null && ValidId(tab.id)) && value.tabs.Select(tab => tab.id).Distinct().Count() == value.tabs.Count;
    }
    static bool ValidDocument(SessionDocument value) {
      return value.version == 1 && value.doc != null && ValidId(value.doc.id) && value.doc.text != null && value.doc.text.Length <= FileStore.Limit && (value.draft == null || value.draft.Length <= FileStore.Limit) && !String.IsNullOrEmpty(value.doc.name);
    }
    internal DocumentBatch Restore(FileStore store) {
      var batch = new DocumentBatch();
      if (!Exists) return batch;
      var manifest = Read<SessionManifest>(ManifestPath, ValidManifest);
      if (manifest == null) {
        batch.errors.Add(L.T("Не удалось прочитать сохранённую сессию. Резервные копии оставлены в папке «session» приложения."));
        return batch;
      }
      foreach (var position in manifest.tabs) {
        var snapshot = Read<SessionDocument>(Path.Combine(directory, position.id + ".json"), value => ValidDocument(value) && value.doc.id == position.id);
        if (snapshot == null) { batch.errors.Add(L.T("Не удалось восстановить одну вкладку: резервная копия повреждена.")); continue; }
        var doc = snapshot.doc;
        string text = snapshot.draft ?? Normal(doc.text);
        if (!doc.untitled && !doc.demo) {
          try {
            string canonical = FileStore.Canonical(doc.path);
            if (snapshot.draft == null) doc = FileStore.Read(canonical, doc.encoding);
            else doc.path = canonical; // Keep the original revision to detect external edits on Save.
            string root = Path.GetDirectoryName(canonical);
            if (!String.IsNullOrEmpty(snapshot.root) && canonical.StartsWith(snapshot.root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) root = snapshot.root;
            doc = store.Register(doc, position.id, root);
          } catch (Exception error) {
            if (!(error is IOException) && !(error is UnauthorizedAccessException) && !(error is ArgumentException)) throw;
            doc.path = null; doc.untitled = true; doc.assetBase = doc.assetToken = doc.assetVersion = doc.relativeDirectory = null;
            batch.errors.Add(L.T("«{0}» недоступен на диске. Текст восстановлен как черновик; при сохранении выберите файл.", doc.name));
          }
        }
        if (doc.untitled) { doc.path = null; store.Entries[doc.id] = new Entry { doc = doc }; }
        if (snapshot.draft == null && !doc.untitled) text = Normal(doc.text);
        // Clone view state; it never owns the backup's baseline text.
        batch.restored.Add(new SessionPosition { id = doc.id, mode = position.mode, readTop = position.readTop, sourceTop = position.sourceTop, start = position.start, end = position.end, text = snapshot.draft != null || doc.untitled ? text : null });
        batch.documents.Add(doc);
      }
      batch.activeId = batch.documents.Any(doc => doc.id == manifest.activeId) ? manifest.activeId : batch.documents.Select(doc => doc.id).LastOrDefault();
      return batch;
    }
    // Prepare on the UI thread, then write on a worker. No controls or FileStore
    // collections are accessed by the worker and bridge requests are serialized.
    internal Task Checkpoint(FileStore store, Document demo, SessionManifest input) {
      if (input == null || !ValidManifest(input)) throw new IOException(L.T("Недопустимая сессия."));
      var updates = new Dictionary<string, SessionDocument>();
      var forget = new HashSet<string>();
      var manifest = new SessionManifest { activeId = input.activeId };
      foreach (var position in input.tabs) {
        var doc = position.id == "sample" ? demo : store.Get(position.id).doc;
        SessionDocument previous; documents.TryGetValue(doc.id, out previous);
        string text = position.text ?? (previous == null ? Normal(doc.text) : previous.draft ?? Normal(previous.doc.text));
        if (doc.untitled && text.Length == 0) continue;
        if (text.Length > FileStore.Limit) throw new IOException(L.T("Черновик больше 20 МБ. Сохраните его в файл."));
        string draft = doc.untitled || text != Normal(doc.text) ? text : null;
        var snapshot = new SessionDocument { doc = doc, root = doc.path == null || doc.demo ? null : store.Get(doc.id).root, draft = draft };
        bool sameDocument = previous != null && (Object.ReferenceEquals(previous.doc, doc) || (previous.doc.path == doc.path && previous.doc.revision == doc.revision && previous.doc.text == doc.text && previous.doc.name == doc.name && previous.doc.encoding == doc.encoding && previous.doc.bom == doc.bom && previous.doc.untitled == doc.untitled));
        if (!sameDocument || previous.draft != draft) updates[doc.id] = snapshot;
        if (position.forgetDraft) { updates[doc.id] = snapshot; forget.Add(doc.id); }
        manifest.tabs.Add(new SessionPosition { id = doc.id, mode = new[] { "read", "source", "split" }.Contains(position.mode) ? position.mode : "read", readTop = Math.Max(0, position.readTop), sourceTop = Math.Max(0, position.sourceTop), start = Math.Max(0, position.start), end = Math.Max(0, position.end) });
      }
      if (!manifest.tabs.Any(tab => tab.id == manifest.activeId)) manifest.activeId = manifest.tabs.Select(tab => tab.id).LastOrDefault();
      return Task.Run(delegate {
        Directory.CreateDirectory(directory);
        foreach (var pair in updates) {
          AtomicWrite(Path.Combine(directory, pair.Key + ".json"), Program.Json.Serialize(pair.Value));
          if (forget.Contains(pair.Key)) AtomicWrite(Path.Combine(directory, pair.Key + ".json.bak"), Program.Json.Serialize(pair.Value), false);
          documents[pair.Key] = pair.Value;
        }
        string json = Program.Json.Serialize(manifest);
        if (json != lastManifest) {
          AtomicWrite(ManifestPath, json);
          // Both manifests describe the latest tab membership. A fallback must
          // not resurrect a tab the user deliberately closed or discarded.
          AtomicWrite(ManifestPath + ".bak", json, false);
          lastManifest = json;
        }
        var live = new HashSet<string>(manifest.tabs.Select(tab => tab.id));
        foreach (string id in documents.Keys.Where(id => !live.Contains(id)).ToArray()) {
          documents.Remove(id);
          foreach (string suffix in new[] { ".json", ".json.bak" }) {
            try { File.Delete(Path.Combine(directory, id + suffix)); } catch (IOException) {} catch (UnauthorizedAccessException) {}
          }
        }
      });
    }
    internal static void AtomicWrite(string path, string text, bool backup = true) {
      AtomicFile.Write(path, new UTF8Encoding(false).GetBytes(text), backup ? path + ".bak" : null);
    }
  }
}
