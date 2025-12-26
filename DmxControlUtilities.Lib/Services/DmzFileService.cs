using DmxControlUtilities.Lib.Models;
using System.IO.Compression;

namespace DmxControlUtilities.Lib.Services
{
    public class DmzFileService
    {

        public DmzContainer ReadDmzFile(Stream file, string containerFileName)
        {
            file.Seek(0, SeekOrigin.Begin);

            var container = new DmzContainer()
            {
                Name = containerFileName,
            };

            using var archive = new ZipArchive(file, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                var ms = new MemoryStream();
                using var stream = entry.Open();
                stream.CopyTo(ms);
                ms.Seek(0, SeekOrigin.Begin);

                container.Files.Add(new DmzFile()
                {
                    FileName = entry.FullName,
                    FileStream = ms
                });
            }

            return container;
        }

        public void WriteDmzFile(DmzContainer destContainer, string path)
        {
            using var fileStream = new FileStream(path, FileMode.Create);
            WriteDmzFile(destContainer, fileStream);
        }

        public void WriteDmzFile(DmzContainer destContainer, Stream stream)
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, true);

            foreach (var dmzFile in destContainer.Files)
            {
                if (dmzFile.FileName == "checksum.sfv")
                    continue;

                dmzFile.FileStream.Seek(0, SeekOrigin.Begin);

                var entry = archive.CreateEntry(dmzFile.FileName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                dmzFile.FileStream.CopyTo(entryStream);
            }
        }
    }
}
