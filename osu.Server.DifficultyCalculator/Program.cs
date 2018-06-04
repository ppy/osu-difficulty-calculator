// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using MySql.Data.MySqlClient;
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

        // Todo: Move to configuration
        private const string connection_string = "Server=localhost;Database=osu;User ID=root;SslMode=None;Pooling=true; Min pool size = 50; Max pool size = 200; Charset=utf8;";
        private const string beatmaps_path = "osu";

        private readonly Dictionary<string, int> attributeIds = new Dictionary<string, int>();

        private Database database;

        public void OnExecute(CommandLineApplication app, IConsole console)
        {
            database = new Database(connection_string);

            using (var reader = database.RunQuery("SELECT attrib_id, name FROM osu_difficulty_attribs"))
            {
                while (reader.Read())
                    attributeIds[reader.GetString(1)] = reader.GetInt32(0);
            }

            var tasks = new List<Task>();

            using (var reader = database.RunQuery("SELECT beatmap_id, checksum FROM osu_beatmaps ORDER BY beatmap_id ASC"))
                while (reader.Read())
                    tasks.Add(processBeatmap(reader.GetInt32(0)));

            Task.WaitAll(tasks.ToArray());
        }

        private async Task processBeatmap(int beatmapId)
        {
            string path = Path.Combine(beatmaps_path, beatmapId + ".osu");
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

                        database.RunNonQuery(
                            "INSERT INTO osu_beatmap_difficulty (beatmap_id, mode, mods, diff_unified) VALUES (@beatmap_id, @mode, @mods, @diff_unified) ON DUPLICATE KEY UPDATE diff_unified = @diff_unified",
                            new MySqlParameter("@beatmap_id", beatmapId),
                            new MySqlParameter("@mode", ruleset.RulesetInfo.ID),
                            new MySqlParameter("@mods", (int)legacyMod),
                            new MySqlParameter("@diff_unified", starRating));

                        if (attributes.Count > 0)
                        {
                            string command = "INSERT INTO osu_beatmap_difficulty_attribs (beatmap_id, mode, mods, attrib_id, value) VALUES ";

                            var parameters = new List<MySqlParameter>
                            {
                                new MySqlParameter("@beatmap_id", beatmapId),
                                new MySqlParameter("@mode", ruleset.RulesetInfo.ID),
                                new MySqlParameter("@mods", (int)legacyMod)
                            };

                            int i = 0;
                            foreach (var kvp in attributes)
                            {
                                if (!attributeIds.ContainsKey(kvp.Key))
                                    continue;

                                command += $"(@beatmap_id, @mode, @mods, @attrib_id{i}, @value{i}),";
                                parameters.Add(new MySqlParameter($"@attrib_id{i}", attributeIds[kvp.Key]));
                                parameters.Add(new MySqlParameter($"@value{i}", Convert.ToSingle(kvp.Value)));

                                i++;
                            }

                            command = command.TrimEnd(',') + " ON DUPLICATE KEY UPDATE value = VALUES(value)";
                            database.RunNonQuery(command, parameters.ToArray());
                        }

                        if (legacyMod == LegacyMods.None && ruleset.RulesetInfo.Equals(localBeatmap.BeatmapInfo.Ruleset))
                        {
                            database.RunNonQuery(
                                "UPDATE osu_beatmaps SET difficultyrating=@difficultyrating, diff_approach=@diff_approach, diff_overall=@diff_overall, diff_drain=@diff_drain, diff_size=@diff_size WHERE beatmap_id=@beatmap_id",
                                new MySqlParameter("@difficultyrating", starRating),
                                new MySqlParameter("@beatmap_id", beatmapId),
                                new MySqlParameter("@diff_approach", localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.ApproachRate),
                                new MySqlParameter("@diff_overall", localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.OverallDifficulty),
                                new MySqlParameter("@diff_drain", localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.DrainRate),
                                new MySqlParameter("@diff_size", localBeatmap.Beatmap.BeatmapInfo.BaseDifficulty.CircleSize)
                            );
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
