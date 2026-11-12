using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace MdReader {
  sealed class Document {
    public string id, text, name, path, encoding, revision, lineEnding, assetBase, assetToken, assetVersion, relativeDirectory;
    public long bytes;
    public bool bom, demo, untitled;
  }
  sealed class DocumentBatch {
    public List<Document> documents = new List<Document>();
    public List<string> errors = new List<string>();
    public List<SessionPosition> restored = new List<SessionPosition>();
    public string activeId;
  }
  sealed class Entry { public Document doc; public string root; }
  sealed class ChangedException : IOException { public ChangedException() : base(L.T("Файл изменён на диске.")) {} }
  sealed class FileStore {
    internal const int Limit = 20 * 1024 * 1024;
    internal static readonly HashSet<string> TextExtensions = new HashSet<string>(new[] { ".md", ".markdown", ".mdown", ".mkd", ".txt" }, StringComparer.OrdinalIgnoreCase);
    internal static readonly HashSet<string> ImageExtensions = new HashSet<string>(new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".avif", ".bmp", ".ico" }, StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();
    int untitledSequence;
    internal Document CreateDocument() {
      string name;
      do { int number=++untitledSequence; name=L.T("Без имени")+(number==1 ? "" : " "+number)+".md"; } while (Entries.Values.Any(entry => entry.doc.name == name));
      var doc=new Document { id=Guid.NewGuid().ToString("N"), text="", name=name, encoding="utf-8", lineEnding="\n", untitled=true };
      Entries.Add(doc.id,new Entry {doc=doc});
      return doc;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder name, uint size, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string filename, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    internal static string Canonical(string filename, bool directory = false) {
      filename = Path.GetFullPath(filename);
      using (var handle = CreateFile(filename, 0, 7, IntPtr.Zero, 3, directory ? 0x02000000u : 0, IntPtr.Zero)) {
        if (handle.IsInvalid) throw new IOException(L.T("Не удалось открыть: ") + filename, new System.ComponentModel.Win32Exception());
        var name = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandle(handle, name, (uint)name.Capacity, 0);
        if (length == 0 || length >= name.Capacity) throw new IOException(L.T("Не удалось определить путь файла."));
        string resolved = name.ToString();
        if (resolved.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + resolved.Substring(8);
        return resolved.StartsWith(@"\\?\", StringComparison.Ordinal) ? resolved.Substring(4) : resolved;
      }
    }
    internal static string Within(string root, string relative) {
      if (String.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0 || relative.IndexOf('\0') >= 0) throw new IOException(L.T("Недопустимый путь."));
      string realRoot = Canonical(root, true).TrimEnd(Path.DirectorySeparatorChar);
      string target = Canonical(Path.Combine(realRoot, relative));
      if (!target.StartsWith(realRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException(L.T("Ссылка выходит за пределы папки документа. Откройте файл через «Открыть»."));
      return target;
    }
    internal static Document Read(string filename, string requested) {
      if (!TextExtensions.Contains(Path.GetExtension(filename))) throw new IOException(L.T("Выберите файл Markdown или .txt."));
      var info = new FileInfo(filename);
      if (info.Length > Limit) throw new IOException(L.T("Файл больше 20 МБ."));
      byte[] buffer = File.ReadAllBytes(filename);
      if (buffer.Length > Limit) throw new IOException(L.T("Файл больше 20 МБ."));
      string encoding = requested ?? "auto";
      if (encoding == "auto") {
        if (buffer.Length >= 2 && buffer[0] == 255 && buffer[1] == 254) encoding = "utf-16le";
        else if (buffer.Length >= 2 && buffer[0] == 254 && buffer[1] == 255) encoding = "utf-16be";
        else {
          try { new UTF8Encoding(false, true).GetString(buffer); encoding = "utf-8"; }
          catch (DecoderFallbackException) { encoding = "windows-1251"; }
        }
      }
      string text = Codec(encoding, false).GetString(buffer);
      if (text.StartsWith("\uFEFF", StringComparison.Ordinal)) text = text.Substring(1);
      bool bom = encoding == "utf-8" ? buffer.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }) :
        encoding.StartsWith("utf-16") && buffer.Length >= 2 && ((buffer[0] == 255 && buffer[1] == 254) || (buffer[0] == 254 && buffer[1] == 255));
      var ending = Regex.Match(text, "\r\n|\r|\n");
      return new Document { text = text, name = Path.GetFileName(filename), path = filename, bytes = buffer.Length, encoding = encoding, bom = bom, revision = Program.Hash(buffer), lineEnding = ending.Success ? ending.Value : "\n" };
    }
    static Encoding Codec(string encoding, bool strict) {
      switch (encoding) {
        case "utf-8": return new UTF8Encoding(false, strict);
        case "utf-16le": return new UnicodeEncoding(false, false, strict);
        case "utf-16be": return new UnicodeEncoding(true, false, strict);
        case "windows-1251": return Encoding.GetEncoding(1251, strict ? EncoderFallback.ExceptionFallback : EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
        default: throw new IOException(L.T("Неизвестная кодировка."));
      }
    }
    internal static byte[] Encode(string text, Document format) {
      if (text == null || text.Length > Limit) throw new IOException(L.T("Текст больше 20 МБ."));
      if (!new[] { "\n", "\r", "\r\n" }.Contains(format.lineEnding)) throw new IOException(L.T("Недопустимый перенос строки."));
      text = Regex.Replace(text, "\r\n|\r|\n", delegate(Match match) { return format.lineEnding; });
      byte[] data;
      try { data = Codec(format.encoding, format.encoding == "windows-1251").GetBytes(text); }
      catch (EncoderFallbackException) { throw new IOException(L.T("В Windows-1251 нет некоторых введённых символов. Используйте «Сохранить как…»: копия будет в UTF-8.")); }
      if (format.bom && format.encoding != "windows-1251") {
        byte[] prefix = format.encoding == "utf-8" ? new byte[] { 239, 187, 191 } : format.encoding == "utf-16le" ? new byte[] { 255, 254 } : new byte[] { 254, 255 };
        data = prefix.Concat(data).ToArray();
      }
      if (data.Length > Limit) throw new IOException(L.T("Файл больше 20 МБ."));
      return data;
    }
    internal static string Revision(string filename) { return File.Exists(filename) ? Program.Hash(File.ReadAllBytes(filename)) : null; }
    internal static Document Write(string filename, string text, Document format, string expected) {
      if (!TextExtensions.Contains(Path.GetExtension(filename))) throw new IOException(L.T("Сохраните файл с расширением Markdown или .txt."));
      byte[] data = Encode(text, format);
      Action check = delegate { if (Revision(filename) != expected) throw new ChangedException(); };
      check();
      AtomicFile.Write(filename, data, beforeCommit: check);
      return Read(filename, format.encoding);
    }
    internal Entry Get(string id) {
      Entry entry;
      if (id == null || !Entries.TryGetValue(id, out entry)) throw new IOException(L.T("Документ уже закрыт."));
      return entry;
    }
    internal Document Load(string filename, string encoding = "auto", string rootHint = null, bool force = false) {
      string full = Canonical(filename);
      var existing = Entries.Values.FirstOrDefault(entry => String.Equals(entry.doc.path, full, StringComparison.OrdinalIgnoreCase));
      if (existing != null && !force) return existing.doc;
      string root = existing != null ? existing.root : rootHint ?? Path.GetDirectoryName(full);
      return Register(Read(full, encoding), existing != null ? existing.doc.id : Guid.NewGuid().ToString("N"), root);
    }
    internal Document Register(Document doc, string id, string root) {
      doc.id = id; doc.assetBase = "https://md-assets.invalid/"; doc.assetToken = id; doc.assetVersion = Guid.NewGuid().ToString("N");
      string prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
      string directory = Path.GetDirectoryName(doc.path);
      doc.relativeDirectory = String.Equals(directory, root, StringComparison.OrdinalIgnoreCase) ? "" : directory.Substring(prefix.Length).Replace('\\', '/');
      Entries[id] = new Entry { doc = doc, root = root };
      return doc;
    }
    internal DocumentBatch LoadMany(IEnumerable<string> files) {
      var batch = new DocumentBatch();
      foreach (string file in files.Take(100)) {
        try { var doc = Load(file); if (!batch.documents.Any(item => item.id == doc.id)) batch.documents.Add(doc); }
        catch (Exception error) { batch.errors.Add(Path.GetFileName(file) + ": " + error.Message); }
      }
      return batch;
    }
  }
}
