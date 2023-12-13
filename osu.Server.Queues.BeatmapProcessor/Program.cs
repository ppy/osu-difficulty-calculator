// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using JetBrains.Annotations;
using McMaster.Extensions.CommandLineUtils;
using osu.Server.DifficultyCalculator.Commands;

namespace osu.Server.Queues.BeatmapProcessor
{
    [Command]
    public class Program
    {
        [Argument(0, "mode", "The target mode to process the beatmaps from the queue in.")]
        [UsedImplicitly]
        private ProcessingMode processingMode { get; } = ProcessingMode.All;

        [Argument(1, "queue-name", "The name of the queue to watch. The `osu-queue:` prefix must be omitted.")]
        [UsedImplicitly]
        private string queueName { get; } = "beatmap";

        public static void Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        [UsedImplicitly]
        public int OnExecute(CommandLineApplication app)
        {
            new BeatmapProcessor(processingMode, queueName).Run();
            return 0;
        }
    }
}
