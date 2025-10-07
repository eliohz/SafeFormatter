using System;

namespace SafeFormatter.Models
{
    public class DiskInfo
    {
        public int DiskNumber { get; set; }
        public string Model { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string FileSystem { get; set; } = string.Empty;
        public string Serial { get; set; } = string.Empty; // Seriennummer/UniqueId
        public ulong SizeBytes { get; set; }
        public string SizeGb => Math.Round(SizeBytes / 1_000_000_000.0, 1) + " GB";
        public bool IsRemovable { get; set; }
        public string DeviceId { get; set; } = string.Empty; // e.g. \\.\PHYSICALDRIVE3

        public string RecommendedFs => (SizeBytes <= 32UL * 1024UL * 1024UL * 1024UL) ? "FAT32" : "exFAT";
    }
}