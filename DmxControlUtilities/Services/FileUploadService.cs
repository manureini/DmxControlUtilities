using DmxControlUtilities.Models;
using MComponents.Files;
using Microsoft.AspNetCore.Components.Forms;

namespace DmxControlUtilities.Services
{
    public class FileUploadService : IFileUploadService
    {
        public async Task<IFile> UploadFile(IBrowserFile pFile, IDictionary<string, string> pAdditionalHeaders, Action<IBrowserFile, long> pOnProgressChanged, CancellationToken pCancellationToken)
        {
            var stream = pFile.OpenReadStream(maxAllowedSize: 100 * 1024 * 1024, cancellationToken: pCancellationToken);

            var ms = new MemoryStream();
            await stream.CopyToAsync(ms, pCancellationToken);

            ms.Seek(0, SeekOrigin.Begin);

            var file = new UploadedFile()
            {
                FileName = pFile.Name,
                Size = pFile.Size,
                Stream = ms
            };

            return file;
        }
    }
}
