namespace Slogs.Obsidian.Drive;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Any(arg => arg is "-h" or "--help" or "/?"))
            {
                Console.WriteLine(DriveOptions.HelpText);
                return 0;
            }

            if (args.Any(arg => arg is "--version" or "-v"))
            {
                Console.WriteLine(SlogsObsidianDriveInstaller.ProductVersion);
                return 0;
            }

            if (SlogsObsidianDriveInstaller.IsInstallerCommand(args))
            {
                return await SlogsObsidianDriveInstaller.RunAsync(args);
            }

            var options = DriveOptions.Parse(args);
            await SlogsObsidianDriveApplication.RunAsync(options);
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(DriveOptions.HelpText);
            return 2;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
