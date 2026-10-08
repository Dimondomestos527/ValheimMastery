using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ValheimMastery
{
    // Write-ahead replay admission, not an inventory/save journal. A crash after
    // admission may deny a retry, but can never authorize the same debit twice.
    internal sealed class WorkshopDurableRequests
    {
        private readonly string _path;
        private readonly HashSet<string> _ids = new HashSet<string>(StringComparer.Ordinal);
        private bool _loaded, _faulted;
        internal WorkshopDurableRequests(string path) { _path = path; }
        internal bool Admit(long player, string id)
        {
            if (_faulted) throw new IOException("Workshop request journal requires repair.");
            if (player == 0 || !Guid.TryParseExact(id, "N", out _)) return false;
            try
            {
                if (!_loaded) Load();
                // Request IDs are world-global, not transferable between players.
                if (_ids.Contains(id)) return false;
                if (_ids.Count >= 1000000) throw new IOException("Workshop request journal safety limit reached.");
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                byte[] line = Encoding.UTF8.GetBytes(player.ToString(CultureInfo.InvariantCulture) + "|" + id + "\n");
                using (var file = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
                { file.Write(line, 0, line.Length); file.Flush(true); }
                _ids.Add(id);
                return true;
            }
            catch { _faulted = true; throw; }
        }
        private void Load()
        {
            if (File.Exists(_path))
            {
                var info = new FileInfo(_path);
                if (info.Length > 64000000) throw new IOException("Oversized Workshop request journal.");
                using (var file = File.OpenRead(_path))
                {
                    if (file.Length != 0)
                    { file.Seek(-1, SeekOrigin.End); if (file.ReadByte() != '\n') throw new IOException("Truncated Workshop request journal."); }
                }
                foreach (string line in File.ReadLines(_path))
                {
                    string[] fields = line.Split('|');
                    if (fields.Length != 2 || !long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long player) ||
                        player == 0 || !Guid.TryParseExact(fields[1], "N", out _) || !_ids.Add(fields[1]))
                        throw new IOException("Invalid Workshop request journal record.");
                }
            }
            _loaded = true;
        }
    }
}
