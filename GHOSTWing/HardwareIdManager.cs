using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace GHOSTWing
{
    public static class HardwareIdManager
    {
        private static string? _cachedId;

        public static string GetDeviceId()
        {
            if (_cachedId != null) return _cachedId;

            try
            {
                string rawId = GetMotherboardId() + GetCpuId() + GetVolumeSerial();
                _cachedId = GenerateHash(rawId);
                return _cachedId;
            }
            catch
            {
                // Fallback to a random but stable-ish GUID if hardware reading fails
                return "GW-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
            }
        }

        private static string GetMotherboardId()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT SerialNumber FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return obj["SerialNumber"]?.ToString() ?? "";
                    }
                }
            }
            catch { }
            return "MB-UNKNOWN";
        }

        private static string GetCpuId()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessorId FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return obj["ProcessorId"]?.ToString() ?? "";
                    }
                }
            }
            catch { }
            return "CPU-UNKNOWN";
        }

        private static string GetVolumeSerial()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID = 'C:'"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return obj["VolumeSerialNumber"]?.ToString() ?? "";
                    }
                }
            }
            catch { }
            return "DISK-UNKNOWN";
        }

        private static string GenerateHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder();
                // We'll take first 16 bytes for a cleaner ID
                for (int i = 0; i < 8; i++)
                {
                    builder.Append(bytes[i].ToString("X2"));
                }
                return "GW-" + builder.ToString();
            }
        }
    }
}
