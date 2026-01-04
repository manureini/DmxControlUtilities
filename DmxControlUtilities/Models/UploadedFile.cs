using MComponents.Files;

namespace DmxControlUtilities.Files.Models
{
    public class UploadedFile : IFile
    {
        public required string FileName { get; set; }

        public required long Size { get; set; }

        public required Stream Stream { get; set; }

        public string Url => string.Empty;
    }
}
