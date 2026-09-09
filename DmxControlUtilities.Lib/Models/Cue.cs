using System.ComponentModel.DataAnnotations;

namespace DmxControlUtilities.Lib.Models
{
    public class Cue
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string Name { get; set; } = string.Empty;

        public string Comment { get; set; } = string.Empty;

        public CueTrigger Trigger { get; set; } = CueTrigger.Manual;

        [Display(Name = "Trigger time (ms)")]
        [Range(0, int.MaxValue)]
        public int TriggerMilliseconds { get; set; }

        [Display(Name = "Fade (ms)")]
        [Range(0, int.MaxValue)]
        public int FadeMilliseconds { get; set; }

        [Display(Name = "Delay (ms)")]
        [Range(0, int.MaxValue)]
        public int DelayMilliseconds { get; set; }

        public Dictionary<Guid, Dictionary<string, byte>> DeviceValues { get; set; } = new();

        public Cue Clone()
        {
            return new Cue
            {
                Id = Id,
                Name = Name,
                Comment = Comment,
                Trigger = Trigger,
                TriggerMilliseconds = TriggerMilliseconds,
                FadeMilliseconds = FadeMilliseconds,
                DelayMilliseconds = DelayMilliseconds,
                DeviceValues = DeviceValues.ToDictionary(d => d.Key, d => new Dictionary<string, byte>(d.Value))
            };
        }
    }
}
