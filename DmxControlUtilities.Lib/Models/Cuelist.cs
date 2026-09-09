using System.ComponentModel.DataAnnotations;

namespace DmxControlUtilities.Lib.Models
{
    public class Cuelist
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string Name { get; set; } = string.Empty;

        public bool Loop { get; set; }

        public List<Cue> Cues { get; set; } = new();

        public Cuelist Clone()
        {
            return new Cuelist
            {
                Id = Id,
                Name = Name,
                Loop = Loop,
                Cues = Cues.Select(c => c.Clone()).ToList()
            };
        }
    }
}
