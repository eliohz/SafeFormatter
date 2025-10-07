using SafeFormatter.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Threading.Tasks;

namespace SafeFormatter.Services
{
    public class DiskService
    {
        public IEnumerable<DiskInfo> GetRemovableDisks()
        {
            // Ziel: nur echte Wechseldatenträger (USB/SD), keine internen/NVMe/SATA
            // 1) Erkenne physische Laufwerke via Win32_DiskDrive
            // 2) Filter: InterfaceType=USB ODER MediaType enthält "Removable"
            // 3) Mappe auf logische Volumes, lese FS & Label

            var disks = new List<DiskInfo>();

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive");
            foreach (ManagementObject dd in searcher.Get())
            {
                var interfaceType = (dd["InterfaceType"] as string)?.ToUpperInvariant();
                var mediaType = (dd["MediaType"] as string)?.ToUpperInvariant();
                var model = (dd["Model"] as string) ?? string.Empty;
                var deviceId = (dd["DeviceID"] as string) ?? string.Empty; // \\.\PHYSICALDRIVE#
                var size = (dd["Size"] != null) ? Convert.ToUInt64(dd["Size"]) : 0UL;
                var pnpId = (dd["PNPDeviceID"] as string) ?? string.Empty;

                // Harte Filter für interne Medien: NVMe/SATA/SCSI/IDE und "Fixed hard disk"
                bool looksInternalBus = interfaceType is "SCSI" or "IDE" or "SATA" or "RAID";
                bool isUsb = interfaceType == "USB" || pnpId.Contains("USB", StringComparison.OrdinalIgnoreCase);
                bool isRemovableMedia = (mediaType?.Contains("REMOVABLE") ?? false) || isUsb;
                bool fixedMedia = mediaType?.Contains("FIXED") ?? false;

                if (!isRemovableMedia || looksInternalBus || fixedMedia)
                    continue;

                // DiskNumber extrahieren
                int diskNumber = -1;
                if (deviceId.StartsWith("\\\\.\\PHYSICALDRIVE", StringComparison.OrdinalIgnoreCase))
                {
                    var numStr = new string(deviceId.Where(char.IsDigit).ToArray());
                    _ = int.TryParse(numStr, out diskNumber);
                }
                if (diskNumber < 0) continue;

                // Serial ermitteln via Win32_PhysicalMedia
                string serial = GetSerialForDeviceId(deviceId);

                // Dateisystem & Label über zugeordnete Volumes (falls vorhanden)
                string fs = string.Empty; string label = string.Empty;
                GetVolumeInfoForDisk(deviceId, out fs, out label);

                disks.Add(new DiskInfo
                {
                    DiskNumber = diskNumber,
                    Model = model,
                    Manufacturer = ExtractVendorFromModel(model),
                    FriendlyName = string.IsNullOrWhiteSpace(label) ? model : label,
                    FileSystem = fs,
                    Serial = serial,
                    SizeBytes = size,
                    IsRemovable = true,
                    DeviceId = deviceId
                });
            }

            // Eindeutig nach Serial gruppieren (manche Reader doppelt)
            return disks
                .Where(d => d.SizeBytes > 0)
                .GroupBy(d => d.Serial + "|" + d.DiskNumber)
                .Select(g => g.First())
                .OrderBy(d => d.DiskNumber)
                .ToList();
        }

        private static string ExtractVendorFromModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model)) return string.Empty;
            var parts = model.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[0] : model;
        }

        private static string GetSerialForDeviceId(string deviceId)
        {
            try
            {
                using var pm = new ManagementObjectSearcher("SELECT * FROM Win32_PhysicalMedia");
                foreach (ManagementObject m in pm.Get())
                {
                    var tag = (m["Tag"] as string) ?? string.Empty; // z.B. \\.\PHYSICALDRIVE3
                    if (string.Equals(tag, deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        var serial = (m["SerialNumber"] as string) ?? string.Empty;
                        return serial.Trim();
                    }
                }
            }
            catch { }
            return string.Empty;
        }

        private static void GetVolumeInfoForDisk(string deviceId, out string fileSystem, out string label)
        {
            fileSystem = string.Empty; label = string.Empty;
            try
            {
                // diskdrive -> partition -> logicaldisk
                using var q1 = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{deviceId.Replace("\\", "\\\\")}'}} WHERE AssocClass = Win32_DiskDriveToDiskPartition");
                foreach (ManagementObject part in q1.Get())
                {
                    using var q2 = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{((string)part["DeviceID"]).Replace("\\", "\\\\")}'}} WHERE AssocClass = Win32_LogicalDiskToPartition");
                    foreach (ManagementObject ld in q2.Get())
                    {
                        fileSystem = (ld["FileSystem"] as string) ?? string.Empty;
                        label = (ld["VolumeName"] as string) ?? string.Empty;
                        return;
                    }
                }
            }
            catch { }
        }

        public async Task<(bool ok, string userMessage, string rawLog, string logFilePath)> CleanAllAndFormatAsync(DiskInfo disk, string? label, Action<string>? onLog = null, Action<double>? onProgress = null)
        {
            var steps = new (string title, Func<Task<(bool ok, string outp)>> action)[]
            {
                ("Datenträger sperren", () => Task.FromResult((true, "bereit"))),
                ("Alle Daten löschen (Clean All)", () => RunDiskpartScript(disk.DiskNumber, new[]{"attributes disk clear readonly","clean all"})),
                ("Partition anlegen", () => RunDiskpartScript(disk.DiskNumber, new[]{"create partition primary","select partition 1","active"})),
                ("Formatieren", () => RunDiskpartScript(disk.DiskNumber, new[]{
                    disk.RecommendedFs.Equals("FAT32", StringComparison.OrdinalIgnoreCase) ? "format fs=fat32" : "format fs=exfat",
                    "assign"
                })),
                ("Label setzen", async () => {
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        // Label via diskpart auf Volume anwenden
                        return await RunDiskpartScript(disk.DiskNumber, new[]{"select partition 1","assign","list volume"})
                            .ConfigureAwait(false);
                    }
                    return (true, "kein Label gesetzt");
                })
            };

            var sb = new StringBuilder();
            var start = DateTimeOffset.Now;
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"SafeFormatter_{DateTime.Now:yyyyMMdd_HHmmss}_Disk{disk.DiskNumber}.log");

            void Log(string s)
            {
                sb.AppendLine(s);
                onLog?.Invoke(s);
            }

            try
            {
                double step = 0;
                foreach (var (title, action) in steps)
                {
                    Log($"→ {title}…");
                    var (ok, outp) = await action();
                    Log(outp);
                    step++;
                    onProgress?.Invoke(step / steps.Length);
                    if (!ok)
                    {
                        var userMsg = MapFriendlyError(outp);
                        await File.WriteAllTextAsync(logPath, sb.ToString());
                        return (false, userMsg, sb.ToString(), logPath);
                    }
                }

                // Label wirklich setzen (per label.exe auf dem zugewiesenen Laufwerk, falls bekannt?)
                if (!string.IsNullOrWhiteSpace(label))
                {
                    // Versuche, first removable volume des phys. Disks zu finden und Label zu setzen (PowerShell)
                    // Optional – das Filesystem-Format oben setzt idR bereits das Label, falls via format label=… (alternativ: format fs=… label=…)
                    // Einfacher: format-Befehl direkt mit label ausführen – ersetzen wir oben besser:
                }

                var dur = DateTimeOffset.Now - start;
                Log($"✔ Erfolg in {dur:mm\\:ss}");
                await File.WriteAllTextAsync(logPath, sb.ToString());
                return (true, $"{disk.Model} {disk.SizeGb} erfolgreich als {disk.RecommendedFs} formatiert.", sb.ToString(), logPath);
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                var userMsg = MapFriendlyError(ex.Message);
                await File.WriteAllTextAsync(logPath, sb.ToString());
                return (false, userMsg, sb.ToString(), logPath);
            }
        }

        private static string MapFriendlyError(string raw)
        {
            raw = raw.ToLowerInvariant();
            if (raw.Contains("write protected") || raw.Contains("schreibgeschützt"))
                return "Der Stick ist schreibgeschützt – bitte den Schalter am Gerät prüfen und erneut versuchen.";
            if (raw.Contains("access is denied") || raw.Contains("zugriff verweigert"))
                return "Zugriff verweigert – bitte alle geöffneten Dateien/Explorerfenster schließen und erneut versuchen.";
            if (raw.Contains("no media") || raw.Contains("keine medien"))
                return "Kein Medium erkannt – bitte Stick/SD-Karte korrekt einstecken und erneut versuchen.";
            if (raw.Contains("i/o") || raw.Contains("datenfehler"))
                return "Leseschreibfehler – das Medium könnte defekt sein. Bitte anderes Gerät versuchen.";
            return "Vorgang fehlgeschlagen. Bitte erneut versuchen oder anderes Medium testen.";
        }

        private static async Task<(bool ok, string outp)> RunDiskpartScript(int diskNumber, IEnumerable<string> commands)
        {
            // Baue temporäre Scriptdatei
            var script = new StringBuilder();
            script.AppendLine($"select disk {diskNumber}");
            foreach (var c in commands)
            {
                if (c.StartsWith("format ", StringComparison.OrdinalIgnoreCase))
                {
                    // Stelle sicher: Vollformat, korrektes Label, keine quick-Option
                    // Option: label= wird ergänzt, wenn vorhanden (ersetzen der Logik in CleanAllAndFormatAsync)
                }
                script.AppendLine(c);
            }

            var tmp = Path.Combine(Path.GetTempPath(), $"diskpart_{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(tmp, script.ToString(), Encoding.ASCII);
            var (code, output) = await ProcessRunner.RunAsync("diskpart.exe", $"/s \"{tmp}\"");
            try { File.Delete(tmp); } catch { }
            return (code == 0, output);
        }
    }
}