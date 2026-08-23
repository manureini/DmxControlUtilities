using DmxControlUtilities.Lib.Services;

// Smoke test: parse all DDF files in the DMXControl 3 UserDevices folder.
// Run with: dotnet run --project DmxControlUtilitiesConsole ddf-test
public static class DdfSmokeTest
{
    public static void Run()
    {
        var service = new DeviceDescriptionService();

        int failed = 0;
        int total = 0;

        foreach (string folder in service.Folders)
        {
            Console.WriteLine($"Folder: {folder}");

            if (!Directory.Exists(folder))
            {
                Console.WriteLine("  (not found)");
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(folder, "*.xml", SearchOption.TopDirectoryOnly))
            {
                total++;

                try
                {
                    DdfParser.Parse(file);
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"  FAILED {Path.GetFileName(file)}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Parsed {total - failed}/{total} files, {failed} failed.");
        Console.WriteLine();

        foreach (var description in service.Descriptions)
        {
            Console.WriteLine($"{description.DisplayName} [{description.ChannelCount} ch, {description.Functions.Count} functions]");

            foreach (var function in description.Functions)
            {
                Console.WriteLine($"  ch {function.DmxChannel,3}  {function.Key,-28} {function.Name,-20} steps: {function.Steps.Count}, ranges: {function.Ranges.Count}");
            }
        }
    }
}
