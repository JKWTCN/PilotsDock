using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProfileManager
{
    public enum RotaryProfileTarget
    {
        N4Pro,
        N4,
        XL
    }

    // Produces a native StreamDock archive; the native importer handles device selection.
    public class StreamDeckProfileConverter
    {
        private readonly Dictionary<string, ZipArchiveEntry> Files = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, JsonObject> Documents = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> PageNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> ExtraPageAssetSources = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Warnings { get; } = [];
        public string ProfileName { get; private set; }
        public bool HasEncoders { get; private set; }
        public RotaryProfileTarget RotaryTarget { get; }
        private string TargetDeviceUUID => RotaryTarget switch
        {
            RotaryProfileTarget.N4 => "MBox-N4",
            RotaryProfileTarget.XL => "XL",
            _ => "N4Pro"
        };
        private int TargetColumns => RotaryTarget == RotaryProfileTarget.XL ? 8 : 5;
        private int TargetRows => RotaryTarget == RotaryProfileTarget.XL ? 4 : 2;
        private int TargetKeyRowOffset => RotaryTarget == RotaryProfileTarget.XL ? 0 : 1;
        private int TargetKeyCapacity => TargetColumns * TargetRows;
        private string RootPath;

        public StreamDeckProfileConverter(ZipArchive archive, RotaryProfileTarget rotaryTarget = RotaryProfileTarget.N4Pro)
        {
            RotaryTarget = rotaryTarget;
            foreach (var entry in archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
            {
                string path = entry.FullName.Replace('\\', '/');
                if (path.StartsWith('/') || path.Contains(':') || path.Split('/').Any(p => p == ".." || p == "."))
                    throw new InvalidDataException($"Invalid profile archive path: {path}");
                if (!Files.TryAdd(path, entry))
                    throw new InvalidDataException($"Duplicate profile archive path: {path}");
            }

            var roots = Files.Keys.Where(IsRootManifestPath).ToList();
            if (roots.Count != 1)
                throw new InvalidDataException("A profile archive must contain exactly one profile root manifest.json.");
            RootPath = roots[0][..^"manifest.json".Length];
            foreach (var path in Files.Keys.Where(p => p.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase)
                && p.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase)))
            {
                using var reader = new StreamReader(Files[path].Open(), Encoding.UTF8);
                Documents.Add(path[RootPath.Length..], JsonNode.Parse(reader.ReadToEnd()) as JsonObject
                    ?? throw new InvalidDataException($"Invalid manifest: {path}"));
            }
            var root = Documents["manifest.json"];
            ProfileName = root["Name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(ProfileName))
                throw new InvalidDataException("The profile has no name.");
            string version = root["Version"]?.ToString();
            if (version != "1.0" && version != "2.0" && version != "3.0")
                throw new InvalidDataException($"Unsupported StreamDeck profile version: {version}");

            foreach (string path in Documents.Keys.Where(p => p.StartsWith("Profiles/", StringComparison.OrdinalIgnoreCase)))
            {
                string[] parts = path.Split('/');
                if (parts.Length != 3)
                    throw new InvalidDataException($"Unsupported nested profile path: {path}");
                string id = DecodePageId(parts[1]);
                string name = id + ".sdProfile";
                if (PageNames.Values.Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Duplicate page UUID: {id}");
                PageNames.Add(parts[1], name);
            }

            HasEncoders = Documents.Values.Any(d => d["Controllers"] is JsonArray controllers
                && controllers.OfType<JsonObject>().Any(c => IsEncoder(c["Type"]?.GetValue<string>())));
            Warn(RotaryTarget == RotaryProfileTarget.XL
                ? Localization.Translate("Scenes will be converted for XL: 32 keys and no knobs. Select XL in StreamDock before importing.")
                : Localization.Translate("Scenes will be converted for {0}: 10 keys and 4 knobs. Select the matching device in StreamDock before importing.", RotaryTarget == RotaryProfileTarget.N4 ? "N4" : "N4Pro"));

            // Validate and prepare everything during preview, before any files are installed.
            foreach (var document in Documents.ToList())
                ConvertDocument(document.Key, document.Value);
            root.Remove("Device");
            root["DeviceModel"] = "20GBA9901";
            root["DeviceUUID"] = TargetDeviceUUID;
            root["DeviceSerialNumber"] = "";
            root["Version"] = "1.0";
            if (root["InstalledByPluginUUID"]?.GetValue<string>() == "com.extension.pilotsdeck")
                root["PreconfiguredName"] = "Profiles/" + ProfileName;
            if (root["Pages"] is JsonObject pages)
            {
                if (pages["Pages"] is not JsonArray list || list.Count == 0)
                    throw new InvalidDataException("The profile has no pages.");
                var converted = new JsonArray();
                foreach (var page in list)
                    converted.Add(ResolvePage(page?.GetValue<string>()));
                string current = pages["Current"]?.GetValue<string>();
                string selected = string.IsNullOrWhiteSpace(current) || (Guid.TryParse(current, out var currentId) && currentId == Guid.Empty)
                    ? converted[0].GetValue<string>() : ResolvePage(current);
                if (!converted.Any(p => p?.GetValue<string>() == selected))
                    selected = converted[0].GetValue<string>();
                root["Pages"] = new JsonObject { ["Current"] = selected, ["Pages"] = converted };
                string source = PageNames.Single(p => p.Value == selected).Key;
                var pageDocument = Documents.Single(d => d.Key.Equals($"Profiles/{source}/manifest.json", StringComparison.OrdinalIgnoreCase)).Value;
                root["Actions"] = pageDocument["Actions"]?.DeepClone() ?? new JsonObject();
            }
            else if (root["Actions"] is not JsonObject)
                throw new InvalidDataException("The profile has neither actions nor pages.");
        }

        internal static bool IsRootManifestPath(string path)
        {
            string[] parts = path.Replace('\\', '/').Split('/');
            if (!parts[^1].Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                return false;
            return parts.Length == 1
                || (parts.Length == 2 && parts[0].EndsWith(".sdProfile", StringComparison.OrdinalIgnoreCase))
                || (parts.Length == 3 && parts[0].Equals("Profiles", StringComparison.OrdinalIgnoreCase)
                    && parts[1].EndsWith(".sdProfile", StringComparison.OrdinalIgnoreCase));
        }

        // StreamDeck 2.x uses a Base32 alphabet without U/X/Y and a Z terminator.
        // UUID bytes are in network order.
        private static string DecodePageId(string name)
        {
            string value = name.EndsWith(".sdProfile", StringComparison.OrdinalIgnoreCase) ? name[..^10] : name;
            if (Guid.TryParse(value, out var guid))
                return guid.ToString();
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTVW";
            if (value.Length != 27 || char.ToUpperInvariant(value[^1]) != 'Z')
                throw new InvalidDataException($"Invalid StreamDeck page identifier: {name}");
            byte[] bytes = new byte[16];
            int buffer = 0, bits = 0, count = 0;
            foreach (char c in value[..^1].ToUpperInvariant())
            {
                int digit = alphabet.IndexOf(c);
                if (digit < 0)
                    throw new InvalidDataException($"Invalid StreamDeck page identifier: {name}");
                buffer = (buffer << 5) | digit;
                bits += 5;
                if (bits >= 8)
                {
                    bits -= 8;
                    bytes[count++] = (byte)(buffer >> bits);
                }
            }
            string hex = Convert.ToHexString(bytes);
            return Guid.ParseExact(hex, "N").ToString();
        }

        private string ResolvePage(string id)
        {
            if (string.IsNullOrEmpty(id))
                throw new InvalidDataException("Missing page reference.");
            if (PageNames.TryGetValue(id, out string name))
                return name;
            string expected = DecodePageId(id) + ".sdProfile";
            return PageNames.Values.FirstOrDefault(n => n.Equals(expected, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"Page reference does not exist: {id}");
        }

        private void Warn(string message)
        {
            if (!Warnings.Contains(message))
                Warnings.Add(message);
        }

        private void ConvertDocument(string path, JsonObject document)
        {
            var keys = new Dictionary<string, JsonObject>();
            var knobs = new JsonObject();
            if (document["Controllers"] is JsonArray controllers)
            {
                foreach (var controller in controllers.OfType<JsonObject>())
                {
                    string type = controller["Type"]?.GetValue<string>() ?? "Keypad";
                    bool encoder = IsEncoder(type);
                    if (!type.Equals("Keypad", StringComparison.OrdinalIgnoreCase) && !encoder)
                    {
                        if (controller["Actions"] is JsonObject other && other.Count > 0)
                            throw new InvalidDataException($"Controller '{type}' requires a device-specific layout conversion; its actions cannot be imported safely.");
                        continue;
                    }
                    if (encoder && !string.IsNullOrEmpty(controller["Background"]?.GetValue<string>()))
                        Warn(Localization.Translate("The encoder strip background is kept as an image resource; each Knob uses its action's own display."));
                    if (controller["Actions"] is JsonObject actions)
                        foreach (var key in actions)
                            AddAction(key.Key, key.Value, encoder);
                }
                document.Remove("Controllers");
            }
            else if (document["Actions"] is JsonObject actions)
                foreach (var key in actions)
                    AddAction(key.Key, key.Value, IsEncoder(key.Value?["Controller"]?.GetValue<string>()));

            void AddAction(string position, JsonNode source, bool encoder)
            {
                if (source is not JsonObject sourceAction)
                    throw new InvalidDataException($"Invalid action at {position}.");
                if (encoder && RotaryTarget == RotaryProfileTarget.XL)
                    throw new InvalidDataException(Localization.Translate("XL has no knobs. Select N4 or N4Pro to import a scene with encoder actions."));
                ParsePosition(position);
                var action = (JsonObject)sourceAction.DeepClone();
                action["Controller"] = encoder ? "Knob" : "Keypad";
                if (action["UUID"]?.GetValue<string>().StartsWith("com.extension.pilotsdeck.", StringComparison.Ordinal) == true
                    && action["Settings"]?["settingsModel"] is JsonObject settings)
                    settings["IsEncoder"] = encoder;
                ConvertAction(action);
                if (encoder)
                    knobs.Add(MapTargetPosition(position, true), action);
                else
                    keys.Add(position, action);
            }

            document.Remove("Device");
            document["DeviceModel"] = "20GBA9901";
            document["DeviceUUID"] = TargetDeviceUUID;
            document["Version"] = "1.0";
            if (string.IsNullOrWhiteSpace(document["Name"]?.GetValue<string>()))
                document["Name"] = ProfileName;

            var ordered = keys.OrderBy(k => ParsePosition(k.Key).Row).ThenBy(k => ParsePosition(k.Key).Column).ToList();
            bool preserveLayout = keys.Keys.All(k => { var p = ParsePosition(k); return p.Column < TargetColumns && p.Row < TargetRows; });
            int continuationCapacity = TargetKeyCapacity - 2;
            int pageCount = keys.Count > TargetKeyCapacity ? (keys.Count + continuationCapacity - 1) / continuationCapacity : 1;
            var pages = new List<JsonObject> { document };
            var pageIds = new List<string>();
            if (pageCount > 1)
            {
                Warn(Localization.Translate("Pages with more than {0} keys are split into linked pages; all original actions are preserved.", TargetKeyCapacity));
                string sourcePrefix = path[..^"manifest.json".Length];
                string firstId = path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ? Guid.NewGuid().ToString() : path.Split('/')[1];
                pageIds.Add(firstId);
                if (sourcePrefix.Length == 0)
                {
                    PageNames.Add(firstId, firstId + ".sdProfile");
                    ExtraPageAssetSources.Add($"Profiles/{firstId}/", sourcePrefix);
                    document["Pages"] = new JsonObject { ["Current"] = firstId, ["Pages"] = new JsonArray(firstId) };
                }
                for (int i = 1; i < pageCount; i++)
                {
                    string id = Guid.NewGuid().ToString();
                    pageIds.Add(id);
                    PageNames.Add(id, id + ".sdProfile");
                    ExtraPageAssetSources.Add($"Profiles/{id}/", sourcePrefix);
                    var continuation = (JsonObject)document.DeepClone();
                    continuation.Remove("Pages");
                    Documents.Add($"Profiles/{id}/manifest.json", continuation);
                    pages.Add(continuation);
                }
            }
            for (int i = 0; i < pageCount; i++)
            {
                var mapped = (JsonObject)knobs.DeepClone();
                int capacity = pageCount > 1 ? continuationCapacity : TargetKeyCapacity;
                foreach (var key in ordered.Skip(i * capacity).Take(capacity).Select((pair, index) => (pair, index)))
                {
                    string position = preserveLayout && pageCount == 1 ? MapTargetPosition(key.pair.Key, false)
                        : $"{key.index % TargetColumns},{key.index / TargetColumns + TargetKeyRowOffset}";
                    mapped.Add(position, key.pair.Value.DeepClone());
                }
                if (i > 0)
                    mapped.Add($"{TargetColumns - 2},{TargetRows - 1 + TargetKeyRowOffset}", PageLink(PageNames[pageIds[i - 1]], Localization.Translate("Previous page")));
                if (i + 1 < pageCount)
                    mapped.Add($"{TargetColumns - 1},{TargetRows - 1 + TargetKeyRowOffset}", PageLink(PageNames[pageIds[i + 1]], Localization.Translate("Next page")));
                pages[i]["Actions"] = mapped;
            }
            if (pageCount > 1 && path.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                var firstPage = (JsonObject)document.DeepClone();
                firstPage.Remove("Pages");
                Documents.Add($"Profiles/{pageIds[0]}/manifest.json", firstPage);
            }
        }

        private static JsonObject PageLink(string target, string title) => new()
        {
            ["ActionID"] = Guid.NewGuid().ToString(),
            ["UUID"] = "com.hotspot.streamdock.profile.openchild",
            ["Controller"] = "Keypad",
            ["LinkedTitle"] = false,
            ["State"] = 0,
            ["Name"] = title,
            ["Settings"] = new JsonObject { ["ProfileUUID"] = target },
            ["States"] = new JsonArray(new JsonObject
            {
                ["Title"] = title, ["ShowTitle"] = true, ["TitleAlignment"] = "middle",
                ["FontFamily"] = "Arial", ["FontSize"] = 10, ["FontStyle"] = "Standard", ["TitleColor"] = "#ffffff"
            })
        };

        private static (int Column, int Row) ParsePosition(string position)
        {
            string[] parts = position.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int column) || !int.TryParse(parts[1], out int row)
                || column < 0 || row < 0)
                throw new InvalidDataException($"Invalid controller position: {position}");
            return (column, row);
        }

        private static bool IsEncoder(string type) => string.Equals(type, "Encoder", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "Knob", StringComparison.OrdinalIgnoreCase);

        private string MapTargetPosition(string position, bool encoder)
        {
            string[] parts = position.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int column) || !int.TryParse(parts[1], out int row)
                || column < 0 || row < 0)
                throw new InvalidDataException($"Invalid controller position: {position}");
            if (encoder)
            {
                if (column >= 4 || row != 0)
                    throw new InvalidDataException(Localization.Translate("Encoder position {0} exceeds the N4 layout's four knobs. Move the extra actions to another page before importing.", position));
                return $"{column},0";
            }
            if (column >= TargetColumns || row >= TargetRows)
                throw new InvalidDataException($"Key position {position} exceeds the target layout.");
            // Storage row numbers are host identifiers, independent of physical vertical order.
            return $"{column},{row + TargetKeyRowOffset}";
        }

        private void ConvertAction(JsonObject action)
        {
            action["ActionID"] ??= Guid.NewGuid().ToString();
            string uuid = action["UUID"]?.GetValue<string>() ?? "";
            string suffix = uuid.StartsWith("com.elgato.streamdeck.", StringComparison.Ordinal) ? uuid[22..] : "";
            string mapped = suffix switch
            {
                "profile.openchild" or "profile.backtoparent" or "profile.rotate" or
                "page.next" or "page.previous" or "page.indicator" or "system.hotkey" => "com.hotspot.streamdock." + suffix,
                _ => null
            };
            if (mapped != null)
            {
                action["UUID"] = mapped;
                if (suffix == "profile.openchild")
                {
                    var settings = action["Settings"] as JsonObject ?? new JsonObject();
                    string target = ResolvePage(settings["ProfileUUID"]?.GetValue<string>());
                    if (action["Settings"] == null)
                        action["Settings"] = settings;
                    settings["ProfileUUID"] = target;
                }
                else if (suffix == "profile.rotate")
                    Warn(Localization.Translate("Scene-switch buttons referencing other profiles must be reassigned in StreamDock."));
            }
            else if (!uuid.StartsWith("com.extension.pilotsdeck.", StringComparison.Ordinal))
                Warn(Localization.Translate("Action requires a compatible StreamDock plugin or manual reassignment: {0}", uuid));
            if (action["Actions"] is JsonArray children)
                foreach (var child in children.OfType<JsonObject>())
                    ConvertAction(child);
        }

        public void Write(string destination)
        {
            // Only create new output files; never overwrite an existing profile archive.
            using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            using var output = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach (var pair in Documents)
            {
                using var writer = new StreamWriter(output.CreateEntry(OutputPath(pair.Key)).Open(), new UTF8Encoding(false));
                writer.Write(pair.Value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            foreach (var pair in Files.Where(p => p.Key.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase)))
            {
                string relative = pair.Key[RootPath.Length..];
                if (Documents.ContainsKey(relative) || relative.Contains("manifest.json.", StringComparison.OrdinalIgnoreCase))
                    continue;
                using var source = pair.Value.Open();
                using var target = output.CreateEntry(OutputPath(relative)).Open();
                source.CopyTo(target);
            }
            foreach (var page in ExtraPageAssetSources)
                foreach (var pair in Files.Where(p => p.Key.StartsWith(RootPath + page.Value, StringComparison.OrdinalIgnoreCase)))
                {
                    string relative = pair.Key[(RootPath.Length + page.Value.Length)..];
                    if (relative.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)
                        || relative.Contains("manifest.json.", StringComparison.OrdinalIgnoreCase)
                        || (page.Value.Length == 0 && relative.StartsWith("Profiles/", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    using var source = pair.Value.Open();
                    using var target = output.CreateEntry(OutputPath(page.Key + relative)).Open();
                    source.CopyTo(target);
                }
        }

        private string OutputPath(string path)
        {
            string[] parts = path.Split('/');
            if (parts.Length >= 3 && parts[0].Equals("Profiles", StringComparison.OrdinalIgnoreCase))
            {
                parts[0] = "profiles";
                parts[1] = PageNames.TryGetValue(parts[1], out string name) ? name
                    : throw new InvalidDataException($"Missing page manifest: {parts[1]}");
            }
            return string.Join('/', parts);
        }
    }
}
