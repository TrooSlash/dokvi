using System;
using System.IO;

namespace MdReader {
  static class AtomicFile {
    internal static void Write(string path, byte[] bytes, string backup = null, Action beforeCommit = null) {
      string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
      try {
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough)) {
          stream.Write(bytes, 0, bytes.Length);
          stream.Flush(true);
        }
        // Recheck the document revision after writing, immediately before replacement.
        if (beforeCommit != null) beforeCommit();
        if (File.Exists(path)) File.Replace(temporary, path, backup);
        else { File.Move(temporary, path); if (backup != null) Write(backup, bytes); }
      } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
  }
}
