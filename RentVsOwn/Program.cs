﻿using System;
using System.Linq;

namespace RentVsOwn
{
    /// <summary>
    ///     Program entry point.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        ///     Defines the entry point of the application.
        /// </summary>
        private static void Main(string[] args)
        {
            var verbose = args.Any(c => string.Equals(c, "-v", StringComparison.CurrentCultureIgnoreCase)) || args.Any(c => string.Equals(c, "--verbose", StringComparison.CurrentCultureIgnoreCase));
#if DEBUG

            // var output = verbose ? new VerboseOutput() : (IOutput)new DebugOutput();
            // var output = verbose ? new VerboseOutput() : (IOutput)new ConsoleOutput();
            var output = verbose ? new TempFileOutput() : (IOutput)new TempFileOutput();
#else
            var output = verbose? new VerboseOutput() : (IOutput)new ConsoleOutput();
#endif

            try
            {
                var scenario = new Scenario
                {
                    Name = "Default Scenario"
                };

                scenario.Run(output);
            }
            catch (Exception exception)
            {
                output.WriteLine("===== Failed =====");
                output.WriteLine($"{exception.GetType()}: {exception.Message}");
            }
            finally
            {
                output.Flush();
            }
        }
    }
}
