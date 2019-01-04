// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System.Net;
using McMaster.Extensions.CommandLineUtils;
using osu.Game.Beatmaps.Formats;
using osu.Server.DifficultyCalculator.Commands;

namespace osu.Server.DifficultyCalculator
{
    [Command]
    [Subcommand("all", typeof(AllCommand))]
    [Subcommand("files", typeof(FilesCommand))]
    [Subcommand("beatmaps", typeof(BeatmapsCommand))]
    [Subcommand("since", typeof(SinceCommand))]
    public class Program
    {
        public static void Main(string[] args)
        {
            LegacyDifficultyCalculatorBeatmapDecoder.Register();
            ServicePointManager.DefaultConnectionLimit = 128;

            CommandLineApplication.Execute<Program>(args);
        }

        public int OnExecute(CommandLineApplication app)
        {
            app.ShowHelp();
            return 1;
        }
    }
}
