// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Humanizer;
using McMaster.Extensions.CommandLineUtils;

namespace osu.Server.DifficultyCalculator.Commands
{
    public abstract class CalculatorCommand : CommandBase
    {
        [Option(CommandOptionType.MultipleValue, Template = "-m|--mode <RULESET_ID>", Description = "Ruleset(s) to compute difficulty for.\n"
                                                                                                    + "0 - osu!\n"
                                                                                                    + "1 - osu!taiko\n"
                                                                                                    + "2 - osu!catch\n"
                                                                                                    + "3 - osu!mania")]
        public int[] Rulesets { get; set; }

        [Option(CommandOptionType.NoValue, Template = "-ac|--allow-converts", Description = "Attempt to convert beatmaps to other rulesets to calculate difficulty.")]
        public bool Converts { get; set; } = false;

        [Option(CommandOptionType.SingleValue, Template = "-c|--concurrency", Description = "Number of threads to use. Default 1.")]
        public int Concurrency { get; set; } = 1;

        [Option(CommandOptionType.NoValue, Template = "-d|--force-download", Description = "Force download of all beatmaps.")]
        public bool ForceDownload { get; set; }

        [Option(CommandOptionType.NoValue, Template = "-v|--verbose", Description = "Provide verbose console output.")]
        public bool Verbose { get; set; }

        [Option(CommandOptionType.NoValue, Template = "-q|--quiet", Description = "Disable all console output.")]
        public bool Quiet { get; set; }

        [Option(CommandOptionType.SingleValue, Template = "-l|--log-file", Description = "The file to log output to.")]
        public string LogFile { get; set; }

        [Option(CommandOptionType.NoValue, Template = "-dr|--dry-run", Description = "Don't perform any writes to the database.")]
        public bool DryRun { get; set; }

        private int[] threadBeatmapIds;

        private IReporter reporter;

        private int totalBeatmaps;
        private int processedBeatmaps;

        public void OnExecute(CommandLineApplication app, IConsole console)
        {
            reporter = new Reporter(console, LogFile)
            {
                IsQuiet = Quiet,
                IsVerbose = Verbose
            };

            if (Concurrency < 1)
            {
                reporter.Error("Concurrency level must be above 1.");
                return;
            }

            threadBeatmapIds = new int[Concurrency];

            if (AppSettings.RUN_AS_SANDBOX_DOCKER)
            {
                reporter.Output("Waiting for database...");

                while (true)
                {
                    try
                    {
                        bool initialised = false;

                        using (var conn = Database.GetConnection())
                        {
                            if (conn.QuerySingle<int>("SELECT `count` FROM `osu_counts` WHERE `name` = 'docker_db_step'") >= 1)
                                initialised = true;
                        }

                        if (initialised)
                            break;
                    }
                    catch
                    {
                    }

                    Thread.Sleep(1000);
                }
            }

            var beatmaps = new ConcurrentQueue<int>(GetBeatmaps() ?? Enumerable.Empty<int>());

            totalBeatmaps = beatmaps.Count;

            var tasks = new Task[Concurrency];

            for (int i = 0; i < Concurrency; i++)
            {
                int tmp = i;

                tasks[i] = Task.Factory.StartNew(() =>
                {
                    var calc = new ServerDifficultyCalculator(Rulesets, Converts);

                    while (beatmaps.TryDequeue(out int beatmapId))
                    {
                        threadBeatmapIds[tmp] = beatmapId;
                        reporter.Verbose($"Processing difficulty for beatmap {beatmapId}.");

                        try
                        {
                            var beatmap = BeatmapLoader.GetBeatmap(beatmapId, Verbose, ForceDownload, reporter);

                            // ensure the correct online id is set
                            beatmap.BeatmapInfo.OnlineBeatmapID = beatmapId;

                            calc.ProcessBeatmap(beatmap);
                            reporter.Verbose($"Difficulty updated for beatmap {beatmapId}.");
                        }
                        catch (Exception e)
                        {
                            reporter.Error($"{beatmapId} failed with {e}");
                        }

                        Interlocked.Increment(ref processedBeatmaps);
                    }
                });
            }

            reporter.Output($"Processing {totalBeatmaps} beatmaps.");

            using (new Timer(_ => outputProgress(), null, 1000, 1000))
            using (new Timer(_ => outputHealth(), null, 5000, 5000))
                Task.WaitAll(tasks);

            if (AppSettings.RUN_AS_SANDBOX_DOCKER)
            {
                using (var conn = Database.GetConnection())
                {
                    conn.Execute("INSERT INTO `osu_counts` (`name`, `count`) VALUES (@Name, @Count) ON DUPLICATE KEY UPDATE `count` = @Count", new
                    {
                        Name = "docker_db_step",
                        Count = 2
                    });
                }
            }

            outputProgress();

            reporter.Output("Done.");
        }

        private int lastProgress;

        private void outputProgress()
        {
            int processed = processedBeatmaps;
            reporter.Output($"Processed {processed} / {totalBeatmaps} ({processed - lastProgress}/sec)");
            lastProgress = processed;
        }

        private void outputHealth()
        {
            var process = Process.GetCurrentProcess();
            reporter.Verbose($"Health p:{process.PrivateMemorySize64.Bytes()} v:{process.VirtualMemorySize64.Bytes()} w:{process.WorkingSet64.Bytes()}");

            string threadsString = string.Empty;
            for (int i = 0; i < threadBeatmapIds.Length; i++)
                threadsString += $"{i}:{threadBeatmapIds[i]} ";

            reporter.Verbose($"Threads {threadsString}");
        }

        protected string CombineSqlConditions(params string[] conditions)
        {
            var builder = new StringBuilder();

            foreach (var c in conditions)
            {
                if (string.IsNullOrEmpty(c))
                    continue;

                builder.Append(builder.Length > 0 ? " AND " : " WHERE ");
                builder.Append(c);
            }

            return builder.ToString();
        }

        protected abstract IEnumerable<int> GetBeatmaps();
    }
}
