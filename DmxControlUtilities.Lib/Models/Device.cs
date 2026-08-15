using System.ComponentModel.DataAnnotations;

namespace DmxControlUtilities.Lib.Models
{
    public class Device
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Display(Name = "Name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// First DMX channel of the device (1 - 512). R = Channel, G = Channel + 1, B = Channel + 2
        /// </summary>
        [Display(Name = "Channel")]
        [Range(1, 512)]
        public int Channel { get; set; } = 1;

        [Display(Name = "Red")]
        [Range(0, 255)]
        public byte R { get; set; }

        [Display(Name = "Green")]
        [Range(0, 255)]
        public byte G { get; set; }

        [Display(Name = "Blue")]
        [Range(0, 255)]
        public byte B { get; set; }
    }
}
