// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using McMaster.Extensions.CommandLineUtils;
using MySql.Data.MySqlClient;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets;

namespace osu.Server.DifficultyCalculator.Commands
{
    public abstract class CalculatorCommand : CommandBase
    {
        private const string ruleset_library_prefix = "osu.Game.Rulesets";

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

        protected Database MasterDatabase { get; private set; }
        protected Database SlaveDatabase { get; private set; }

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

            MasterDatabase = new Database(AppSettings.ConnectionStringMaster);
            SlaveDatabase = new Database(AppSettings.ConnectionStringSlave ?? AppSettings.ConnectionStringMaster);

            if (AppSettings.UseDocker)
            {
                reporter.Output("Waiting for database...");

                while (true)
                {
                    try
                    {
                        using (var conn = MasterDatabase.GetConnection())
                        {
                            if (conn.QuerySingle<int>("SELECT `count` FROM `osu_counts` WHERE `name` = 'docker_db_step'") == 1)
                                break;
                        }
                    }
                    catch
                    {
                    }

                    Thread.Sleep(1000);
                }
            }

            var rulesetsToProcess = getRulesets();
            var beatmaps = new ConcurrentQueue<int>(GetBeatmaps());

            totalBeatmaps = beatmaps.Count;

            var tasks = new Task[Concurrency];
            for (int i = 0; i < Concurrency; i++)
            {
                tasks[i] = Task.Factory.StartNew(() =>
                {
                    while (beatmaps.TryDequeue(out int toProcess))
                        processBeatmap(toProcess, rulesetsToProcess);
                });
            }

            reporter.Output($"Processing {totalBeatmaps} beatmaps.");

            using (new Timer(_ => outputProgress(), null, 1000, 1000))
                Task.WaitAll(tasks);

            if (AppSettings.UseDocker)
            {
                using (var conn = MasterDatabase.GetConnection())
                {
                    conn.Execute("INSERT INTO `osu_counts` (`name`, `count`) VALUES (@Name, @Count) ON DUPLICATE KEY UPDATE `count` = @Count", new
                    {
                        Name = "docker_db_step",
                        Count = 2
                    });
                }
            }

            reporter.Output("Done.");
        }

        private void processBeatmap(int beatmapId, List<Ruleset> rulesets)
        {
            try
            {
                var localBeatmap = BeatmapLoader.GetBeatmap(beatmapId, Verbose, ForceDownload);
                if (localBeatmap == null)
                {
                    reporter.Warn($"Beatmap {beatmapId} skipped (beatmap file not found).");
                    return;
                }

                using (var conn = MasterDatabase.GetConnection())
                {
                    if (Converts && localBeatmap.BeatmapInfo.RulesetID == 0)
                    {
                        foreach (var ruleset in rulesets)
                            computeDifficulty(beatmapId, localBeatmap, ruleset, conn);
                    }
                    else if (rulesets.Any(r => r.RulesetInfo.ID == localBeatmap.BeatmapInfo.RulesetID))
                        computeDifficulty(beatmapId, localBeatmap, localBeatmap.BeatmapInfo.Ruleset.CreateInstance(), conn);
                }

                reporter.Verbose($"Difficulty updated for beatmap {beatmapId}.");
            }
            catch (Exception e)
            {
                reporter.Error($"{beatmapId} failed with: {e}");
            }

            Interlocked.Increment(ref processedBeatmaps);
        }

        private void computeDifficulty(int beatmapId, WorkingBeatmap beatmap, Ruleset ruleset, MySqlConnection conn)
        {
            foreach (var attribute in ruleset.CreateDifficultyCalculator(beatmap).CalculateAll())
            {
                var legacyMod = attribute.Mods.ToLegacy();

                conn.Execute(
                    "INSERT INTO `osu_beatmap_difficulty` (`beatmap_id`, `mode`, `mods`, `diff_unified`) "
                    + "VALUES (@BeatmapId, @Mode, @Mods, @Diff) "
                    + "ON DUPLICATE KEY UPDATE `diff_unified` = @Diff",
                    new
                    {
                        BeatmapId = beatmapId,
                        Mode = ruleset.RulesetInfo.ID,
                        Mods = (int)legacyMod,
                        Diff = attribute.StarRating
                    });

                var parameters = new List<object>();
                foreach (var mapping in attribute.Map())
                {
                    parameters.Add(new
                    {
                        BeatmapId = beatmapId,
                        Mode = ruleset.RulesetInfo.ID,
                        Mods = (int)legacyMod,
                        Attribute = mapping.id,
                        Value = Convert.ToSingle(mapping.value)
                    });
                }

                conn.Execute(
                    "INSERT INTO `osu_beatmap_difficulty_attribs` (`beatmap_id`, `mode`, `mods`, `attrib_id`, `value`) "
                    + "VALUES (@BeatmapId, @Mode, @Mods, @Attribute, @Value) "
                    + "ON DUPLICATE KEY UPDATE `value` = VALUES(`value`)",
                    parameters.ToArray());

                if (legacyMod == LegacyMods.None && ruleset.RulesetInfo.Equals(beatmap.BeatmapInfo.Ruleset))
                {
                    conn.Execute(
                        "UPDATE `osu_beatmaps` SET `difficultyrating` = @Diff, `diff_approach` = @AR, `diff_overall` = @OD, `diff_drain` = @HP, `diff_size` = @CS "
                        + "WHERE `beatmap_id`= @BeatmapId",
                        new
                        {
                            BeatmapId = beatmapId,
                            Diff = attribute.StarRating,
                            AR = beatmap.Beatmap.BeatmapInfo.BaseDifficulty.ApproachRate,
                            OD = beatmap.Beatmap.BeatmapInfo.BaseDifficulty.OverallDifficulty,
                            HP = beatmap.Beatmap.BeatmapInfo.BaseDifficulty.DrainRate,
                            CS = beatmap.Beatmap.BeatmapInfo.BaseDifficulty.CircleSize
                        });
                }
            }
        }

        private List<Ruleset> getRulesets()
        {
            var rulesetsToProcess = new List<Ruleset>();

            foreach (string file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, $"{ruleset_library_prefix}.*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(file);
                    Type type = assembly.GetTypes().First(t => t.IsPublic && t.IsSubclassOf(typeof(Ruleset)));
                    rulesetsToProcess.Add((Ruleset)Activator.CreateInstance(type, (RulesetInfo)null));

                }
                catch
                {
                    reporter.Error($"Failed to load ruleset ({file})");
                }
            }

            if (Rulesets != null)
                rulesetsToProcess.RemoveAll(r => Rulesets.All(u => u != r.RulesetInfo.ID));

            return rulesetsToProcess;
        }

        private void outputProgress() => reporter.Output($"Processed {processedBeatmaps} / {totalBeatmaps}");

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
