// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using McMaster.Extensions.CommandLineUtils;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;

namespace osu.Server.DifficultyCalculator
{
    [Command]
    public class Program
    {
        public static void Main(string[] args)
            => CommandLineApplication.Execute<Program>(args);

        [Option]
        public int Concurrency { get; set; } = 1;

        private readonly Dictionary<string, int> attributeIds = new Dictionary<string, int>();

        private Database database;

        private readonly ConcurrentQueue<int> beatmaps = new ConcurrentQueue<int>();

        private int totalBeatmaps;
        private int processedBeatmaps;

        public void OnExecute(CommandLineApplication app, IConsole console)
        {
            if (Concurrency < 1)
            {
                console.Error.WriteLine("Concurrency level must be above 1.");
                return;
            }

            database = new Database(AppSettings.ConnectionString);

            var tasks = new List<Task>();

            using (var conn = database.GetConnection())
            {
                foreach ((int Id, string Name) attrib in conn.Query<(int, string)>("SELECT attrib_id, name FROM osu_difficulty_attribs"))
                    attributeIds[attrib.Name] = attrib.Id;

                totalBeatmaps = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM osu_beatmaps");

                foreach (int id in conn.Query<int>("SELECT beatmap_id FROM osu_beatmaps ORDER BY beatmap_id DESC"))
                    beatmaps.Enqueue(id);
            }

            for (int i = 0; i < Concurrency; i++)
                tasks.Add(processBeatmaps());

            Task.WaitAll(tasks.ToArray());
        }

        private Task processBeatmaps() => Task.Factory.StartNew(() =>
        {
            while (beatmaps.TryDequeue(out int toProcess))
            {
                try
                {
                    processBeatmap(toProcess);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }, TaskCreationOptions.LongRunning);

        private void processBeatmap(int beatmapId)
        {
            string path = Path.Combine(AppSettings.BeatmapsPath, beatmapId + ".osu");
            if (!File.Exists(path))
            {
                finish($"Beatmap {beatmapId} skipped (beatmap file not found).");
                return;
            }

            var localBeatmap = new LocalWorkingBeatmap(path);

            // Todo: For each ruleset
            var playable = localBeatmap.GetPlayableBeatmap(localBeatmap.BeatmapInfo.Ruleset);
            var ruleset = localBeatmap.BeatmapInfo.Ruleset.CreateInstance();

            using (var conn = database.GetConnection())
            {
                foreach (var mod in ruleset.GetModsFor(ModType.DifficultyCalculation))
                {
                    var legacyMod = toLegacyMod(mod);

                    var attributes = new Dictionary<string, object>();
                    double starRating = ruleset.CreateDifficultyCalculator(playable, toModArray(mod)).Calculate(attributes);

                    conn.Execute(
                        "INSERT INTO osu_beatmap_difficulty (beatmap_id, mode, mods, diff_unified) "
                        + "VALUES (@BeatmapId, @Mode, @Mods, @Diff) "
                        + "ON DUPLICATE KEY UPDATE diff_unified = @Diff",
                        new
                        {
                            BeatmapId = beatmapId,
                            Mode = ruleset.RulesetInfo.ID,
                            Mods = (int)legacyMod,
                            Diff = starRating
                        });

                    if (attributes.Count > 0)
                    {
                        var parameters = new List<object>();
                        foreach (var kvp in attributes)
                        {
                            if (!attributeIds.ContainsKey(kvp.Key))
                                continue;

                            parameters.Add(new
                            {
                                BeatmapId = beatmapId,
                                Mode = ruleset.RulesetInfo.ID,
                                Mods = (int)legacyMod,
                                Attribute = attributeIds[kvp.Key],
                                Value = Convert.ToSingle(kvp.Value)
                            });
                        }

                        conn.Execute(
                            "INSERT INTO osu_beatmap_difficulty_attribs (beatmap_id, mode, mods, attrib_id, value) "
                            + "VALUES (@BeatmapId, @Mode, @Mods, @Attribute, @Value) "
                            + "ON DUPLICATE KEY UPDATE value = VALUES(value)",
                            parameters.ToArray());
                    }

                    if (legacyMod == LegacyMods.None && ruleset.RulesetInfo.Equals(localBeatmap.BeatmapInfo.Ruleset))
                    {
                        conn.Execute(
                            "UPDATE osu_beatmaps SET difficultyrating=@Diff, diff_approach=@AR, diff_overall=@OD, diff_drain=@HP, diff_size=@CS "
                            + "WHERE beatmap_id=@BeatmapId",
                            new
                            {
                                BeatmapId = beatmapId,
                                Diff = starRating,
                                AR = localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.ApproachRate,
                                OD = localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.OverallDifficulty,
                                HP = localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.DrainRate,
                                CS = localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.CircleSize
                            });
                    }
                }
            }

            finish($"Difficulty updated for beatmap {beatmapId}.");
        }

        private void finish(string message)
        {
            Interlocked.Increment(ref processedBeatmaps);
            Console.WriteLine($"{processedBeatmaps} / {totalBeatmaps} : {message}");
        }

        private Mod[] toModArray(Mod mod)
        {
            switch (mod)
            {
                case MultiMod multi:
                    return multi.Mods?.SelectMany(toModArray).ToArray() ?? Array.Empty<Mod>();
                default:
                    return new[] { mod };
            }
        }

        private LegacyMods toLegacyMod(Mod mod)
        {
            var value = LegacyMods.None;

            switch (mod)
            {
                case MultiMod multi:
                    if (multi.Mods == null)
                        break;
                    foreach (var m in multi.Mods)
                        value |= toLegacyMod(m);
                    break;
                case ModNoFail _:
                    value |= LegacyMods.NoFail;
                    break;
                case ModEasy _:
                    value |= LegacyMods.Easy;
                    break;
                case ModHidden _:
                    value |= LegacyMods.Hidden;
                    break;
                case ModHardRock _:
                    value |= LegacyMods.HardRock;
                    break;
                case ModSuddenDeath _:
                    value |= LegacyMods.SuddenDeath;
                    break;
                case ModDoubleTime _:
                    value |= LegacyMods.DoubleTime;
                    break;
                case ModRelax _:
                    value |= LegacyMods.Relax;
                    break;
                case ModHalfTime _:
                    value |= LegacyMods.HalfTime;
                    break;
                case ModFlashlight _:
                    value |= LegacyMods.Flashlight;
                    break;
                case ManiaModKey1 _:
                    value |= LegacyMods.Key1;
                    break;
                case ManiaModKey2 _:
                    value |= LegacyMods.Key2;
                    break;
                case ManiaModKey3 _:
                    value |= LegacyMods.Key3;
                    break;
                case ManiaModKey4 _:
                    value |= LegacyMods.Key4;
                    break;
                case ManiaModKey5 _:
                    value |= LegacyMods.Key5;
                    break;
                case ManiaModKey6 _:
                    value |= LegacyMods.Key6;
                    break;
                case ManiaModKey7 _:
                    value |= LegacyMods.Key7;
                    break;
                case ManiaModKey8 _:
                    value |= LegacyMods.Key8;
                    break;
                case ManiaModKey9 _:
                    value |= LegacyMods.Key9;
                    break;
                case ManiaModFadeIn _:
                    value |= LegacyMods.FadeIn;
                    break;
            }

            return value;
        }
    }
}
