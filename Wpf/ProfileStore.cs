using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace KillWind.Wpf
{
    internal sealed class ProfileStore
    {
        private const int SchemaVersion = 1;
        private readonly string directory;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        public ProfileStore()
        {
            directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KillWind", "Profiles");
            Directory.CreateDirectory(directory);
        }

        public string[] List()
        {
            return Directory.GetDirectories(directory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public ProfileRecord Save(string name, ProcessInfo process, IEnumerable<AddressEntry> entries)
        {
            string safeName = SafeName(String.IsNullOrWhiteSpace(name) ? "Untitled Game" : name);
            var record = new ProfileRecord
            {
                schemaVersion = SchemaVersion,
                gameName = name,
                executable = process == null ? "" : process.name,
                executablePath = process == null ? "" : process.path,
                architecture = process == null ? "" : process.architecture,
                addresses = entries.Select(entry => new ProfileAddress
                {
                    description = entry.Description,
                    address = entry.Address,
                    currentValue = entry.CurrentValue,
                    newValue = entry.NewValue,
                    type = entry.Type,
                    frozen = entry.Frozen,
                }).ToArray(),
                savedAt = DateTime.UtcNow.ToString("o"),
            };
            string profileDirectory = Path.Combine(directory, safeName);
            Directory.CreateDirectory(profileDirectory);
            string file = Path.Combine(profileDirectory, "profile.json");
            string temporary = file + ".tmp";
            File.WriteAllText(temporary, serializer.Serialize(record));
            if (File.Exists(file)) File.Replace(temporary, file, null);
            else File.Move(temporary, file);
            return record;
        }

        public ProfileRecord Load(string name)
        {
            string file = Path.Combine(directory, SafeName(name), "profile.json");
            if (!File.Exists(file)) throw new InvalidOperationException("Profile 不存在。");
            var record = serializer.Deserialize<ProfileRecord>(File.ReadAllText(file));
            if (record == null || record.schemaVersion != SchemaVersion) throw new InvalidOperationException("不支持的 Profile schemaVersion。");
            if (record.addresses == null) record.addresses = new ProfileAddress[0];
            return record;
        }

        private static string SafeName(string value)
        {
            string result = value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars()) result = result.Replace(invalid, '_');
            result = result.TrimEnd('.', ' ');
            if (String.IsNullOrWhiteSpace(result)) throw new InvalidOperationException("Profile 名称不能为空。");
            return result.Length > 100 ? result.Substring(0, 100) : result;
        }
    }

    internal sealed class ProfileRecord
    {
        public int schemaVersion { get; set; }
        public string gameName { get; set; }
        public string executable { get; set; }
        public string executablePath { get; set; }
        public string architecture { get; set; }
        public ProfileAddress[] addresses { get; set; }
        public string savedAt { get; set; }
    }

    internal sealed class ProfileAddress
    {
        public string description { get; set; }
        public string address { get; set; }
        public string currentValue { get; set; }
        public string newValue { get; set; }
        public string type { get; set; }
        public bool frozen { get; set; }
    }
}
