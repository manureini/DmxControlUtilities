using System.Drawing;

namespace DmxControlUtilities.Web.Models
{
    public class PresetModel
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string HtmlColor { get; set; } = string.Empty;
        public Color Color { get; set; }
    }
}
