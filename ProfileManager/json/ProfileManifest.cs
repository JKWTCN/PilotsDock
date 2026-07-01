using CFIT.AppLogger;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ProfileManager.json
{
    public class ProfileManifest
    {
        public static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        [JsonIgnore]
        public string ProfileDirectory { get; set; }

        [JsonIgnore]
        public string ProfileDirectoryCleaned { get { return ProfileDirectory.Replace("-", "").Replace(".sdProfile", "", StringComparison.InvariantCultureIgnoreCase); } }

        [JsonIgnore]
        public ProfileController ProfileController { get; protected set; }

        public class ManifestDevice
        {
            public string Model { get; set; }
            public string UUID { get; set; }
            public string SerialNumber { get; set; }

            [JsonIgnore]
            public string DeckName { get; set; }

            [JsonIgnore]
            public StreamDeckTypeEnum DeckType { get; set; } = StreamDeckTypeEnum.UNKNOWN;

            [JsonIgnore]
            public string Hash { get; set; }
        }

        // In-memory helper only — never serialized back (StreamDock manifests use flat
        // DeviceModel/DeviceUUID/DeviceSerialNumber fields, not a nested Device object).
        [JsonIgnore]
        public ManifestDevice Device { get; set; }

        // StreamDock flat format fields.
        [JsonPropertyName("DeviceModel")]
        public string DeviceModel { get; set; }

        [JsonPropertyName("DeviceUUID")]
        public string DeviceUUID { get; set; }

        [JsonPropertyName("DeviceSerialNumber")]
        public string DeviceSerialNumber { get; set; }

        // Helper property to get the actual device model (supports both formats)
        [JsonIgnore]
        public string ActualDeviceModel { get { return Device?.Model ?? DeviceModel; } }

        // Helper property to get the actual device UUID (supports both formats)
        [JsonIgnore]
        public string ActualDeviceUUID { get { return Device?.UUID ?? DeviceUUID; } }

        // Helper property to get the actual device serial (supports both formats)
        [JsonIgnore]
        public string ActualDeviceSerial { get { return Device?.SerialNumber ?? DeviceSerialNumber; } }

        // Helper property to check if this is a StreamDock manifest
        [JsonIgnore]
        public bool IsStreamDockFormat { get { return !string.IsNullOrEmpty(DeviceModel); } }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string InstalledByPluginUUID { get; set; } = null;

        [JsonPropertyName("Name")]
        public string ProfileName { get; set; }

        public JsonNode Pages { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string PreconfiguredName { get; set; } = null;

        public string Version { get; set; }

        [JsonIgnore]
        public bool IsPreparedForSwitching { get { return InstalledByPluginUUID != null && PreconfiguredName != null && HasMapping; } }

        [JsonIgnore]
        public bool IsChanged { get; set; } = false;

        [JsonIgnore]
        public bool DeleteFlag { get; set; } = false;

        [JsonIgnore]
        public bool HasMapping { get { return ProfileMapping != null; } }

        [JsonIgnore]
        public ProfileMapping ProfileMapping { get; set; } = null;

        public override string ToString()
        {
            return $"Manifest: Name {ProfileName} | IsPrepared {IsPreparedForSwitching} | Directory {ProfileDirectory} | DeckID {(Device?.Hash ?? "N/A")}";
        }

        public void SetDeviceInfo(DeviceInfo deviceInfo)
        {
            if (Device == null)
                Device = new ManifestDevice();
            Device.DeckName = deviceInfo.Name;
            Device.DeckType = deviceInfo.Type;
            Logger.Verbose($"DeviceInfo was set @ {this}");
        }

        // VSD Craft (StreamDock) derives the device id it sends to plugins as
        // MD5(DeviceUUID + DeviceSerialNumber). ProfileManager must use the same
        // algorithm so the DeckId stored in ProfileMappings.json matches the
        // device.id the Plugin receives at runtime.
        public static string CreateDeviceHash(string deviceUUID, string deviceSerial)
        {
            return Tools.CreateMD5($"{deviceUUID ?? string.Empty}{deviceSerial ?? string.Empty}");
        }

        public static ProfileManifest LoadManifest(string json)
        {
            var manifest = JsonSerializer.Deserialize<ProfileManifest>(json);
            if (manifest != null)
            {
                // StreamDock flat format (DeviceModel/DeviceUUID present)
                if (!string.IsNullOrEmpty(manifest.DeviceUUID) || !string.IsNullOrEmpty(manifest.DeviceSerialNumber))
                {
                    manifest.Device = new ManifestDevice
                    {
                        Model = manifest.DeviceModel,
                        UUID = manifest.DeviceUUID,
                        SerialNumber = manifest.DeviceSerialNumber,
                        Hash = CreateDeviceHash(manifest.DeviceUUID, manifest.DeviceSerialNumber)
                    };
                    Logger.Verbose($"StreamDock format detected - Device object created from flat properties (Hash {manifest.Device.Hash})");
                }
                // Legacy StreamDeck nested format
                else if (manifest.Device != null)
                {
                    manifest.Device.Hash = Tools.CreateMD5(manifest.Device.UUID);
                }
                else
                {
                    // Incomplete manifest data - create placeholder Device to prevent null reference
                    manifest.Device = new ManifestDevice
                    {
                        Model = "Unknown",
                        UUID = Guid.NewGuid().ToString(),
                        Hash = "UNKNOWN"
                    };
                    Logger.Warning($"Incomplete manifest '{manifest.ProfileName}' - missing Device info, using placeholder");
                }
            }
            return manifest;
        }

        public static ProfileManifest LoadManifest(string path, string folder, ProfileController controller)
        {
            var manifest = LoadManifest(File.ReadAllText(path));

            manifest.ProfileDirectory = folder;
            manifest.ProfileController = controller;
            Logger.Verbose($"Manifest was loaded: {manifest.ProfileName}");

            return manifest;
        }

        // Writes the manifest back to disk while preserving all original fields
        // (Actions, Pages, pageuuid, AppIdentifier, etc.) that the ProfileManifest
        // POCO does not model. Only PilotsDock's own fields
        // (InstalledByPluginUUID / PreconfiguredName) are updated on the original JSON.
        public static void WriteManifest(string path, ProfileManifest manifest)
        {
            JsonNode root;
            string original = null;
            if (File.Exists(path) && (new FileInfo(path)).Length > 0)
            {
                original = File.ReadAllText(path);
                root = JsonNode.Parse(original) ?? new JsonObject();
            }
            else
            {
                root = JsonSerializer.SerializeToNode(manifest, WriteOptions) ?? new JsonObject();
            }

            // Update only the fields PilotsDock manages.
            if (manifest.InstalledByPluginUUID != null)
                root["InstalledByPluginUUID"] = manifest.InstalledByPluginUUID;
            else if (root["InstalledByPluginUUID"] != null)
                root["InstalledByPluginUUID"] = null;

            if (manifest.PreconfiguredName != null)
                root["PreconfiguredName"] = manifest.PreconfiguredName;
            else if (root["PreconfiguredName"] != null)
                root["PreconfiguredName"] = null;

            File.WriteAllText(path, root.ToJsonString(WriteOptions));
            manifest.IsChanged = false;
            Logger.Debug($"Manifest was saved: {manifest}");
        }
    }
}
