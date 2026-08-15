namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Information about a serial port, enriched with USB device details when available.
    /// </summary>
    public class SerialPortInfo
    {
        /// <summary>
        /// The port name, e.g. "COM3".
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Friendly device description, e.g. "USB Serial Port (COM3)".
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The device manufacturer.
        /// </summary>
        public string? Manufacturer { get; set; }

        /// <summary>
        /// The PNP device id, e.g. "FTDIBUS\VID_0403+PID_6001+AB0CDEFG\0000".
        /// </summary>
        public string? DeviceId { get; set; }

        /// <summary>
        /// USB vendor id as hex string, e.g. "0403" (FTDI). Null for non-USB ports.
        /// </summary>
        public string? VendorId { get; set; }

        /// <summary>
        /// USB product id as hex string, e.g. "6001" (FT232). Null for non-USB ports.
        /// </summary>
        public string? ProductId { get; set; }

        /// <summary>
        /// USB serial number of the device, if present.
        /// </summary>
        public string? SerialNumber { get; set; }

        /// <summary>
        /// True when the device is a USB device (has a VID/PID).
        /// </summary>
        public bool IsUsb => VendorId != null;

        /// <summary>
        /// True when the device is an FTDI chip (USB vendor id 0403).
        /// </summary>
        public bool IsFtdi => VendorId != null &&
            string.Equals(VendorId, "0403", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Name of the FTDI chip when known, e.g. "FT232R". Null for non-FTDI devices.
        /// </summary>
        public string? FtdiChip { get; set; }
    }
}
