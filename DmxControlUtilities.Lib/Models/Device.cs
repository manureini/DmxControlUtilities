using DmxControlUtilities.Lib.Models.Ddf;
using System.ComponentModel.DataAnnotations;

namespace DmxControlUtilities.Lib.Models
{
    public class Device
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Display(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Id of the <see cref="DeviceDescription"/> (DDF file name without extension) this device was created from.
        /// </summary>
        [Display(Name = "Description")]
        public string DescriptionId { get; set; } = string.Empty;

        /// <summary>
        /// First DMX channel of the device (1 - 512). Function channels are added as offsets.
        /// </summary>
        [Display(Name = "Channel")]
        [Range(1, 512)]
        public int Channel { get; set; } = 1;

        /// <summary>
        /// Current values of the device's functions, keyed by <see cref="DdfFunction.Key"/>.
        /// </summary>
        public Dictionary<string, byte> Values { get; set; } = new();

        public byte GetValue(string pKey)
        {
            return Values.TryGetValue(pKey, out byte value) ? value : (byte)0;
        }

        public void SetValue(string pKey, byte pValue)
        {
            Values[pKey] = pValue;
        }

        /// <summary>
        /// Approximate RGB color of the device based on its rgb functions (for display in grids/timelines).
        /// </summary>
        public (byte R, byte G, byte B) GetRgb()
        {
            return (GetValue("rgb/red"), GetValue("rgb/green"), GetValue("rgb/blue"));
        }
    }
}
