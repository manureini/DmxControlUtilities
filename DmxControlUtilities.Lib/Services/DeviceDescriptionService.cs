using DmxControlUtilities.Lib.Models.Ddf;

namespace DmxControlUtilities.Lib.Services
{
    /// <summary>
    /// Loads and caches <see cref="DeviceDescription"/> objects parsed from DDF (XML) files.
    /// Sources are the DMXControl 3 LibDevices and UserDevices folders by default.
    /// </summary>
    public class DeviceDescriptionService
    {
        public const string UserDevicesFolderName = @"DMXControl Projects e.V\DMXControl\Kernel\UserDevices";

        public const string LibDevicesFolderName = @"DMXControl3\Kernel\LibDevices";

        private readonly IReadOnlyList<string> mFolders;
        private readonly object mLock = new();

        private List<DeviceDescription>? mDescriptions;

        public DeviceDescriptionService() : this((IReadOnlyList<string>?)null)
        {
        }

        public DeviceDescriptionService(string? pFolder)
            : this(string.IsNullOrWhiteSpace(pFolder) ? null : new[] { pFolder })
        {
        }

        public DeviceDescriptionService(IReadOnlyList<string>? pFolders)
        {
            mFolders = pFolders is { Count: > 0 }
                ? pFolders
                : new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), UserDevicesFolderName),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), LibDevicesFolderName),
                };
        }

        /// <summary>
        /// Creates a service backed by a fixed set of descriptions (e.g. for tests).
        /// </summary>
        public DeviceDescriptionService(params DeviceDescription[] pDescriptions)
        {
            mFolders = Array.Empty<string>();
            mDescriptions = pDescriptions.ToList();
        }

        public IReadOnlyList<string> Folders => mFolders;

        public string Folder => mFolders.FirstOrDefault() ?? string.Empty;

        public IReadOnlyList<DeviceDescription> Descriptions
        {
            get
            {
                lock (mLock)
                {
                    mDescriptions ??= Load();
                    return mDescriptions;
                }
            }
        }

        public DeviceDescription? GetDescription(string pId)
        {
            return Descriptions.FirstOrDefault(d => d.Id == pId);
        }

        public void Reload()
        {
            lock (mLock)
            {
                mDescriptions = Load();
            }
        }

        private List<DeviceDescription> Load()
        {
            var result = new List<DeviceDescription>();

            foreach (string folder in mFolders)
            {
                if (!Directory.Exists(folder))
                    continue;

                foreach (string file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        result.Add(DdfParser.Parse(file));
                    }
                    catch
                    {
                        // Skip files which are not valid DDFs.
                    }
                }
            }

            return result.OrderBy(d => d.Vendor).ThenBy(d => d.Model).ToList();
        }
    }
}
