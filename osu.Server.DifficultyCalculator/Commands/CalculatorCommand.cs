// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

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

        [Option(CommandOptionType.MultipleValue, Template = "-m|--mode <RULESET_ID>", Description = "The ruleset(s) to compute difficulty for.\n"
                                                                                                    + "0 - osu!\n"
                                                                                                    + "1 - osu!taiko\n"
                                                                                                    + "2 - osu!catch\n"
                                                                                                    + "3 - osu!mania")]
        public int[] Rulesets { get; set; }

        [Option(CommandOptionType.NoValue, Template = "-ac|--allow-converts", Description = "Attempt to convert beatmaps to other rulesets to calculate difficulty.")]
        public bool Converts { get; set; } = false;

        [Option(CommandOptionType.SingleValue, Template = "-c|--concurrency", Description = "The number of threads to use. Default 1.")]
        public int Concurrency { get; set; } = 1;

        [Option(CommandOptionType.NoValue, Template = "-v|--verbose", Description = "Provide verbose console output.")]
        public bool Verbose { get; set; }

        protected virtual Database Database { get; private set; }

        private int totalBeatmaps;
        private int processedBeatmaps;

        public void OnExecute(CommandLineApplication app, IConsole console)
        {
            if (Concurrency < 1)
            {
                console.Error.WriteLine("Concurrency level must be above 1.");
                return;
            }

            Database = new Database(AppSettings.ConnectionString);

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

            Console.WriteLine($"Processing {totalBeatmaps} beatmaps.");

            using (new Timer(_ => outputProgress(), null, 1000, 1000))
                Task.WaitAll(tasks);

            Console.WriteLine("Done.");
        }

        private void processBeatmap(int beatmapId, List<Ruleset> rulesets)
        {
            try
            {
                string path = Path.Combine(AppSettings.BeatmapsPath, beatmapId + ".osu");
                if (!File.Exists(path))
                {
                    if (Verbose)
                        Console.WriteLine($"Beatmap {beatmapId} skipped (beatmap file not found).");
                    return;
                }

                var localBeatmap = new LocalWorkingBeatmap(path);

                using (var conn = Database.GetConnection())
                {
                    if (Converts && localBeatmap.BeatmapInfo.RulesetID == 0)
                    {
                        foreach (var ruleset in rulesets)
                            computeDifficulty(beatmapId, localBeatmap, ruleset, conn);
                    }
                    else if (rulesets.Any(r => r.RulesetInfo.ID == localBeatmap.BeatmapInfo.RulesetID))
                        computeDifficulty(beatmapId, localBeatmap, localBeatmap.BeatmapInfo.Ruleset.CreateInstance(), conn);
                }

                if (Verbose)
                    Console.WriteLine($"Difficulty updated for beatmap {beatmapId}.");
            }
            catch (Exception e)
            {
                Console.WriteLine($"{beatmapId} failed with: {e}");
            }

            Interlocked.Increment(ref processedBeatmaps);
        }

        private void computeDifficulty(int beatmapId, WorkingBeatmap beatmap, Ruleset ruleset, MySqlConnection conn)
        {
            foreach (var attribute in ruleset.CreateDifficultyCalculator(beatmap).CalculateAll())
            {
                var legacyMod = attribute.Mods.ToLegacy();

                conn.Execute(
                    "INSERT INTO osu_beatmap_difficulty (beatmap_id, mode, mods, diff_unified) "
                    + "VALUES (@BeatmapId, @Mode, @Mods, @Diff) "
                    + "ON DUPLICATE KEY UPDATE diff_unified = @Diff",
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
                    "INSERT INTO osu_beatmap_difficulty_attribs (beatmap_id, mode, mods, attrib_id, value) "
                    + "VALUES (@BeatmapId, @Mode, @Mods, @Attribute, @Value) "
                    + "ON DUPLICATE KEY UPDATE value = VALUES(value)",
                    parameters.ToArray());

                if (legacyMod == LegacyMods.None && ruleset.RulesetInfo.Equals(beatmap.BeatmapInfo.Ruleset))
                {
                    conn.Execute(
                        "UPDATE osu_beatmaps SET difficultyrating=@Diff, diff_approach=@AR, diff_overall=@OD, diff_drain=@HP, diff_size=@CS "
                        + "WHERE beatmap_id=@BeatmapId",
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
                    Console.Error.WriteLine("Failed to load ruleset");
                }
            }

            if (Rulesets != null)
                rulesetsToProcess.RemoveAll(r => Rulesets.All(u => u != r.RulesetInfo.ID));

            return rulesetsToProcess;
        }

        private void outputProgress() => Console.WriteLine($"Processed {processedBeatmaps} / {totalBeatmaps}");

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
