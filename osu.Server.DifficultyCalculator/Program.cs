// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private readonly Dictionary<string, int> attributeIds = new Dictionary<string, int>();

        private Database database;

        public void OnExecute(CommandLineApplication app, IConsole console)
        {
            database = new Database(AppSettings.ConnectionString);

            using (var conn = database.GetConnection())
            {
                foreach ((int Id, string Name) attrib in conn.Query<(int, string)>("SELECT attrib_id, name FROM osu_difficulty_attribs"))
                    attributeIds[attrib.Name] = attrib.Id;
            }

            var tasks = new List<Task>();

            using (var conn = database.GetConnection())
            {
                foreach (int id in conn.Query<int>("SELECT beatmap_id FROM osu_beatmaps ORDER BY beatmap_id ASC"))
                    tasks.Add(processBeatmap(id));
            }

            Task.WaitAll(tasks.ToArray());
        }

        private async Task processBeatmap(int beatmapId)
        {
            string path = Path.Combine(AppSettings.BeatmapsPath, beatmapId + ".osu");
            if (!File.Exists(path))
                return;

            await Task.Run(async () =>
            {
                var localBeatmap = new LocalWorkingBeatmap(path);

                // Todo: For each ruleset
                var playable = localBeatmap.GetPlayableBeatmap(localBeatmap.BeatmapInfo.Ruleset);
                var ruleset = localBeatmap.BeatmapInfo.Ruleset.CreateInstance();

                foreach (var mod in ruleset.GetModsFor(ModType.DifficultyCalculation))
                {
                    await Task.Run(() =>
                    {
                        var legacyMod = toLegacyMod(mod);

                        var attributes = new Dictionary<string, object>();
                        double starRating = ruleset.CreateDifficultyCalculator(playable, toModArray(mod)).Calculate(attributes);

                        using (var conn = database.GetConnection())
                        {
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
                        }

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

                            using (var conn = database.GetConnection())
                            {
                                conn.Execute(
                                    "INSERT INTO osu_beatmap_difficulty_attribs (beatmap_id, mode, mods, attrib_id, value) "
                                    + "VALUES (@BeatmapId, @Mode, @Mods, @Attribute, @Value) "
                                    + "ON DUPLICATE KEY UPDATE value = VALUES(value)",
                                    parameters.ToArray());
                            }
                        }

                        if (legacyMod == LegacyMods.None && ruleset.RulesetInfo.Equals(localBeatmap.BeatmapInfo.Ruleset))
                        {
                            using (var conn = database.GetConnection())
                            {
                                conn.Execute(
                                    "UPDATE osu_beatmaps SET difficultyrating=@Diff, diff_approach=@ApproachRate, diff_overall=@OverallDifficulty, diff_drain=@DrainRate, diff_size=@CircleSize "
                                    + "WHERE beatmap_id=@BeatmapId",
                                    new
                                    {
                                        BeatmapId = beatmapId,
                                        Diff = starRating,
                                        ApproachRate = localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.ApproachRate,
                                        OverallDifficulty = localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.OverallDifficulty,
                                        DrainRate = localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.DrainRate,
                                        CircleSize = localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.CircleSize
                                    });
                            }
                        }
                    });
                }
            });
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
