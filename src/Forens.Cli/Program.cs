using System;
using Forens.Cli.Commands;
using McMaster.Extensions.CommandLineUtils;

namespace Forens.Cli
{
    [Command(
        Name = "forens",
        Description = "Windows forensic collection tool — modular collectors, dual-format reports.",
        UsePagerForHelpText = false)]
    [Subcommand(typeof(ListCommand), typeof(CollectCommand), typeof(SearchCommand), typeof(VersionCommand))]
    internal sealed class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                return CommandLineApplication.Execute<Program>(args);
            }
            catch (UnrecognizedCommandParsingException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (CommandParsingException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Internal error: " + ex.Message);
                return 5;
            }
        }

        public int OnExecute(CommandLineApplication app, IConsole console)
        {
            console.WriteLine(app.GetHelpText());
            return 0;
        }
    }
}
