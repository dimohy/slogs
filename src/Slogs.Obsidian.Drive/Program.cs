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
