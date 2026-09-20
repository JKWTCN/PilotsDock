using CFIT.AppLogger;
using PilotsDeck.StreamDeck.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PilotsDeck.StreamDeck
{
    // HID identifies the source; the host still supplies the current action context.
    internal sealed class N4InputBridge
    {
        private readonly object gate = new();
        private readonly Dictionary<string, Queue<Click>> clicks = new();
        private readonly Dictionary<string, StreamDeckEvent> appearances = new();
        private readonly Dictionary<string, bool> pendingUps = new();
        private readonly Dictionary<string, string> hostDevices = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> loggedBindings = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> loggedUnmapped = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> loggedUnmatched = new();
        private readonly HashSet<string> connectedSerials = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> configuredSerials;
        private sealed record Click(int Column, bool Touch, long Time);
        private sealed record Reader(IntPtr Handle, string Serial, bool Pro);

        public N4InputBridge(StreamDeckInfoMessage info)
        {
            foreach (var device in info.devices) hostDevices[device.id] = device.name;
            configuredSerials = new(App.Configuration.N4DeviceSerials ?? new(), StringComparer.OrdinalIgnoreCase);
        }

        public void DeviceChanged(StreamDeckEvent e)
        {
            if (e.Event == "deviceDidConnect") hostDevices[e.device] = e.deviceInfo?.name;
            else
            {
                hostDevices.Remove(e.device);
                foreach (var context in appearances.Where(p => p.Value.device == e.device).Select(p => p.Key).ToArray())
                {
                    appearances.Remove(context);
                    pendingUps.Remove(context);
                }
            }
            lock (gate) clicks.Clear();
        }

        public async Task<StreamDeckEvent> TranslateAsync(StreamDeckEvent e, CancellationToken cancellation)
        {
            if (e.Event == "willAppear" || e.Event == "willDisappear")
            {
                pendingUps.Remove(e.context);
                loggedUnmatched.Remove(e.context);
                if (e.Event == "willAppear") appearances[e.context] = e;
                else appearances.Remove(e.context);
                lock (gate) clicks.Clear(); // Do not match reports from the previous page.
                return e;
            }
            bool down = e.Event is "keyDown" or "dialDown";
            bool up = e.Event is "keyUp" or "dialUp";
            if (!down && !up && e.Event != "touchTap") return e;
            appearances.TryGetValue(e.context ?? "", out var appearance);
            string controller = e.payload?.controller ?? appearance?.payload?.controller;
            if (!string.Equals(controller, AppConfiguration.SdKnob, StringComparison.OrdinalIgnoreCase)) return e;
            if (up && pendingUps.Remove(e.context, out bool touch)) return touch ? null : e;

            int? column = (e.payload?.coordinates ?? appearance?.payload?.coordinates)?.column;
            string serial = ResolveSerial(e.device ?? appearance?.device);
            if (serial == null || column is null or < 0 or > 3)
            {
                if (loggedUnmapped.Add(e.device ?? appearance?.device ?? "unknown"))
                    Logger.Warning($"N4 HID mapping unavailable: device={e.device ?? appearance?.device}, column={column}; using host events.");
                return e;
            }
            Click click = null;
            long started = Environment.TickCount64;
            do
            {
                lock (gate)
                {
                    if (clicks.TryGetValue(serial, out var queue))
                    {
                        long now = Environment.TickCount64;
                        while (queue.Count > 0 && now - queue.Peek().Time > 500) queue.Dequeue();
                        int count = queue.Count;
                        for (int i = 0; i < count; i++)
                        {
                            var candidate = queue.Dequeue();
                            if (click == null && candidate.Column == column) click = candidate;
                            else queue.Enqueue(candidate);
                        }
                    }
                }
                if (click != null || e.Event == "touchTap") break;
                await Task.Delay(5, cancellation);
            } while (Environment.TickCount64 - started < 120);
            if (click == null && e.Event != "touchTap" && loggedUnmatched.Add(e.context))
                Logger.Warning($"N4 HID report not matched: serial={serial}, column={column}, event={e.Event}; using host event.");
            if (down) pendingUps[e.context] = click?.Touch == true;
            if (click?.Touch != true || e.Event == "touchTap") return e;
            Logger.Debug($"N4 HID touch: serial={serial}, column={column}, context={e.context}");
            e.Event = "touchTap";
            e.payload ??= new StreamDeckEvent.Payload();
            e.payload.ticks = 1;
            e.payload.pressed = false;
            return e;
        }

        private string ResolveSerial(string device)
        {
            if (string.IsNullOrEmpty(device)) return null;
            lock (gate)
            {
                if (configuredSerials.TryGetValue(device, out var serial))
                    return serial != null && connectedSerials.Contains(serial) ? serial.ToUpperInvariant() : null;
                if (connectedSerials.Contains(device)) return device.ToUpperInvariant();
                // StreamDock's info.devices includes saved/offline devices. Its ID
                // is MD5(UTF8(device name + USB serial)), not the serial itself.
                // Verify the full hash against this host ID; do not guess by count.
                if (hostDevices.TryGetValue(device, out string name) && !string.IsNullOrEmpty(name))
                {
                    foreach (string candidate in connectedSerials)
                    {
                        string id = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(name + candidate)));
                        if (!string.Equals(id, device, StringComparison.OrdinalIgnoreCase)) continue;
                        if (loggedBindings.Add(device))
                            Logger.Information($"N4 HID device bound: host={device}, name={name}, serial={candidate}");
                        return candidate;
                    }
                }
                // Opaque host IDs can only be automatically bound when unambiguous.
                if (hostDevices.Count == 1 && hostDevices.ContainsKey(device) && connectedSerials.Count == 1)
                    return connectedSerials.First();
                return null;
            }
        }

        public Task RunAsync(CancellationToken cancellation) => Task.Run(async () =>
        {
            var readers = new Dictionary<string, Reader>();
            try
            {
                long nextScan = 0;
                while (!cancellation.IsCancellationRequested)
                {
                    if (Environment.TickCount64 >= nextScan)
                    {
                        Scan(readers);
                        nextScan = Environment.TickCount64 + 2000;
                    }
                    foreach (var entry in readers.ToArray())
                    {
                        if (cancellation.IsCancellationRequested) break;
                        var reader = entry.Value;
                        byte[] buffer = new byte[2048];
                        nuint length = (nuint)buffer.Length;
                        uint result = Native.Read(reader.Handle, buffer, ref length, 20);
                        if (result == 0 && length <= (nuint)buffer.Length) Accept(reader, buffer, (int)length);
                        else if (result != 0x05000302 && result != 0x05000301)
                        {
                            Logger.Warning($"N4 HID read failed for {reader.Serial}: 0x{result:X8}; reconnecting.");
                            Native.Destroy(reader.Handle);
                            readers.Remove(entry.Key);
                            lock (gate)
                            {
                                connectedSerials.Remove(reader.Serial);
                                clicks.Remove(reader.Serial);
                            }
                        }
                    }
                    await Task.Delay(readers.Count == 0 ? 250 : 1, cancellation);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex) { Logger.Warning($"N4 HID input unavailable; using host events: {ex.Message}"); }
            finally
            {
                lock (gate) { connectedSerials.Clear(); clicks.Clear(); }
                foreach (var reader in readers.Values) Native.Destroy(reader.Handle);
            }
        });

        private void Scan(Dictionary<string, Reader> readers)
        {
            var seen = new HashSet<string>();
            IntPtr head = Native.Enumerate(0, 0);
            try
            {
                for (IntPtr current = head; current != IntPtr.Zero;)
                {
                    var info = Marshal.PtrToStructure<Native.DeviceInfo>(current);
                    bool pro = info.Vendor == 0x5548 && info.Product is 0x1008 or 0x1021;
                    bool n4 = (info.Vendor == 0x6602 && info.Product == 0x1001)
                        || (info.Vendor == 0x6603 && info.Product == 0x1007);
                    string path = Marshal.PtrToStringAnsi(info.Path);
                    string serial = Marshal.PtrToStringUni(info.Serial)?.ToUpperInvariant();
                    if ((pro || n4) && info.UsagePage == 0xFFA0 && info.Usage == 1
                        && path != null && !string.IsNullOrEmpty(serial))
                    {
                        seen.Add(path);
                        if (!readers.ContainsKey(path))
                        {
                            IntPtr handle = IntPtr.Zero;
                            try
                            {
                                uint result = Native.Create(current, out handle);
                                if (result != 0 || handle == IntPtr.Zero)
                                    throw new InvalidOperationException($"N4 transport_create: 0x{result:X8}");
                                result = Native.SetReportSize(handle, 513, 1025, 0);
                                if (result != 0) throw new InvalidOperationException($"N4 setReportSize: 0x{result:X8}");
                                // transport_create only constructs TransportDevice/Session.
                                // Session.read returns empty while Device.isOpen() is false.
                                // As in cppsdk DeviceManager, this input-report query opens
                                // our process's HID handle. It sends no output commands.
                                byte[] firmware = new byte[513];
                                result = Native.GetFirmwareVersion(handle, firmware, (nuint)firmware.Length);
                                if (result != 0)
                                    throw new InvalidOperationException($"N4 get_firmware_version/open: 0x{result:X8}");
                                readers[path] = new Reader(handle, serial, pro);
                                handle = IntPtr.Zero;
                                Logger.Information($"N4 HID reader connected: {serial}");
                            }
                            finally { if (handle != IntPtr.Zero) Native.Destroy(handle); }
                        }
                    }
                    current = info.Next;
                }
            }
            finally { Native.FreeEnumeration(head); }
            foreach (string path in readers.Keys.Where(p => !seen.Contains(p)).ToArray())
            {
                Native.Destroy(readers[path].Handle);
                readers.Remove(path);
            }
            lock (gate)
            {
                connectedSerials.Clear();
                foreach (var reader in readers.Values) connectedSerials.Add(reader.Serial);
                foreach (string serial in clicks.Keys.Where(s => !connectedSerials.Contains(s)).ToArray()) clicks.Remove(serial);
            }
        }

        private void Accept(Reader reader, byte[] data, int length)
        {
            // Same ACK ... OK offsets as cppsdk; ignore ACK ARX coordinate streams.
            if (length < 11 || data[0] != 0x41 || data[1] != 0x43 || data[2] != 0x4B
                || data[5] != 0x4F || data[6] != 0x4B) return;
            int key = data[9], value = data[10];
            bool touch = key >= 0x40 && key <= 0x43 && value == 0;
            int column = touch ? key - 0x40 : Array.IndexOf(new[] { 0x37, 0x35, 0x33, 0x36 }, key);
            if (column < 0 || (!touch && value != (reader.Pro ? 1 : 0))) return;
            Logger.Debug($"N4 HID report: serial={reader.Serial}, column={column}, touch={touch}");
            lock (gate)
            {
                if (!clicks.TryGetValue(reader.Serial, out var queue)) clicks[reader.Serial] = queue = new();
                while (queue.Count >= 64) queue.Dequeue();
                queue.Enqueue(new Click(column, touch, Environment.TickCount64));
            }
        }

        private static class Native
        {
            private const string Dll = "Native/N4/transport.dll";
            static Native()
            {
                NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, (name, assembly, searchPath) =>
                    name == Dll ? NativeLibrary.Load(System.IO.Path.Combine(AppContext.BaseDirectory, Dll),
                        assembly, DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.SafeDirectories)
                        : IntPtr.Zero);
            }
            [StructLayout(LayoutKind.Sequential)]
            internal struct DeviceInfo
            {
                internal IntPtr Path;
                internal ushort Vendor, Product;
                internal IntPtr Serial;
                internal ushort Release;
                internal IntPtr Manufacturer, ProductName;
                internal ushort UsagePage, Usage;
                internal int Interface;
                internal IntPtr Next;
                internal int BusType;
            }
            [DllImport(Dll, EntryPoint = "transport_hid_enumerate", CallingConvention = CallingConvention.Cdecl)]
            internal static extern IntPtr Enumerate(ushort vendor, ushort product);
            [DllImport(Dll, EntryPoint = "transport_hid_free_enumeration", CallingConvention = CallingConvention.Cdecl)]
            internal static extern void FreeEnumeration(IntPtr head);
            [DllImport(Dll, EntryPoint = "transport_create", CallingConvention = CallingConvention.Cdecl)]
            internal static extern uint Create(IntPtr info, out IntPtr handle);
            [DllImport(Dll, EntryPoint = "transport_destroy", CallingConvention = CallingConvention.Cdecl)]
            internal static extern uint Destroy(IntPtr handle);
            [DllImport(Dll, EntryPoint = "transport_set_reportSize", CallingConvention = CallingConvention.Cdecl)]
            internal static extern uint SetReportSize(IntPtr handle, ushort input, ushort output, ushort feature);
            [DllImport(Dll, EntryPoint = "transport_get_firmware_version", CallingConvention = CallingConvention.Cdecl)]
            internal static extern uint GetFirmwareVersion(IntPtr handle, [Out] byte[] firmware, nuint length);
            [DllImport(Dll, EntryPoint = "transport_read", CallingConvention = CallingConvention.Cdecl)]
            internal static extern uint Read(IntPtr handle, [Out] byte[] response, ref nuint length, int timeout);
        }
    }
}
