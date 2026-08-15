using System.Management;
using System.Text.RegularExpressions;

namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Enumerates serial ports including USB device details (Windows only, WMI based).
    /// </summary>
    public static partial class SerialPortEnumerator
    {
        public static IReadOnlyList<SerialPortInfo> GetPorts()
        {
            var ports = new List<SerialPortInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, Description, Manufacturer, PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");

                foreach (ManagementObject device in searcher.Get())
                {
                    string? name = device["Name"] as string;
                    string? pnpDeviceId = device["PNPDeviceID"] as string;

                    string? portName = ExtractPortName(name);

                    if (portName == null)
                        continue;

                    (string? vid, string? pid) = ParseVidPid(pnpDeviceId);

                    ports.Add(new SerialPortInfo
                    {
                        Name = portName,
                        Description = device["Description"] as string ?? name,
                        Manufacturer = device["Manufacturer"] as string,
                        DeviceId = pnpDeviceId,
                        VendorId = vid,
                        ProductId = pid,
                        SerialNumber = ParseSerialNumber(pnpDeviceId),
                        FtdiChip = GetFtdiChipName(vid, pid)
                    });
                }
            }
            catch (Exception)
            {
                // WMI unavailable (e.g. non-Windows) - fall back to plain port names.
            }

            // Ports without PNP information (Bluetooth, com0com, ...) are added without details.
            foreach (string portName in System.IO.Ports.SerialPort.GetPortNames())
            {
                if (!ports.Any(p => string.Equals(p.Name, portName, StringComparison.OrdinalIgnoreCase)))
                {
                    ports.Add(new SerialPortInfo { Name = portName });
                }
            }

            return ports.OrderBy(p => p.Name).ToList();
        }

        private static string? ExtractPortName(string? name)
        {
            if (name == null)
                return null;

            var match = PortNameRegex().Match(name);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static (string? Vid, string? Pid) ParseVidPid(string? deviceId)
        {
            if (deviceId == null)
                return (null, null);

            var match = VidPidRegex().Match(deviceId);

            return match.Success
                ? (match.Groups[1].Value, match.Groups[2].Value)
                : (null, null);
        }

        private static string? ParseSerialNumber(string? deviceId)
        {
            if (deviceId == null)
                return null;

            // USB device ids look like: USB\VID_0403&PID_6001\AB0CDEFG
            // FTDI device ids look like: FTDIBUS\VID_0403&PID_6001&A50285BIA\0000
            string[] parts = deviceId.Split('\\');

            if (parts.Length < 3)
                return null;

            // For FTDIBUS the serial is embedded in the middle segment (separated by & or +),
            // for plain USB it is the third segment.
            string candidate = parts[0].StartsWith("FTDIBUS", StringComparison.OrdinalIgnoreCase)
                ? parts[1].Split('&', '+').Last()
                : parts[2];

            // Windows generates "&MI_00"-style ids for composite devices, those are not serials.
            return candidate.Contains('&') ? null : candidate;
        }

        /// <summary>
        /// Maps the USB product id to the FTDI chip name. Only FTDI (VID 0403) devices are mapped.
        /// Common USB DMX dongles using FTDI chips are annotated.
        /// </summary>
        private static string? GetFtdiChipName(string? vid, string? pid)
        {
            if (!string.Equals(vid, "0403", StringComparison.OrdinalIgnoreCase) || pid == null)
                return null;

            return pid.ToUpperInvariant() switch
            {
                "6001" => "FT232R",   // Enttec Open DMX USB and most FTDI DMX dongles
                "6002" => "FT232H",
                "6010" => "FT2232H",
                "6011" => "FT4232H",
                "6014" => "FT232H (single channel)",
                "6015" => "FT-X (FT230X/FT231X/FT234XD)",
                "6040" => "FT4232HA",
                "8372" => "FT8U232AM",
                _ => "FTDI"
            };
        }

        [GeneratedRegex(@"\((COM\d+)\)", RegexOptions.IgnoreCase)]
        private static partial Regex PortNameRegex();

        [GeneratedRegex(@"VID_([0-9A-F]{4})[&+]PID_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
        private static partial Regex VidPidRegex();
    }
}
