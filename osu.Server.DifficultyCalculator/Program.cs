// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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
    [Subcommand("beatmapsstring", typeof(BeatmapsStringCommand))]
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
