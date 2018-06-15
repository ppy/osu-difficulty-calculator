// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using McMaster.Extensions.CommandLineUtils;
using MySql.Data.MySqlClient;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Taiko.Difficulty;

namespace osu.Server.DifficultyCalculator
{
    [Command]
    public class Program
    {
        private const string ruleset_library_prefix = "osu.Game.Rulesets";

        public static void Main(string[] args)
            => CommandLineApplication.Execute<Program>(args);

        [Option]
        public int Concurrency { get; set; } = 1;

        [Option("--allow-converts")]
        public bool Converts { get; set; } = false;

        private readonly Dictionary<string, int> attributeIds = new Dictionary<string, int>();

        private Database database;

        private readonly ConcurrentQueue<int> beatmaps = new ConcurrentQueue<int>();

        private readonly List<Ruleset> rulesets = new List<Ruleset>();

        private int totalBeatmaps;
        private int processedBeatmaps;

        public void OnExecute(CommandLineApplication app, IConsole console)
        {
            if (Concurrency < 1)
            {
                console.Error.WriteLine("Concurrency level must be above 1.");
                return;
            }

            foreach (string file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, $"{ruleset_library_prefix}.*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(file);
                    Type type = assembly.GetTypes().First(t => t.IsPublic && t.IsSubclassOf(typeof(Ruleset)));
                    rulesets.Add((Ruleset)Activator.CreateInstance(type, (RulesetInfo)null));

                }
                catch
                {
                    Console.Error.WriteLine("Failed to load ruleset");
                }
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

            using (var conn = database.GetConnection())
            {
                if (Converts && localBeatmap.BeatmapInfo.RulesetID == 0)
                {
                    foreach (var ruleset in rulesets)
                        computeDifficulty(beatmapId, localBeatmap, ruleset, conn);
                }
                else
                    computeDifficulty(beatmapId, localBeatmap, localBeatmap.BeatmapInfo.Ruleset.CreateInstance(), conn);
            }

            finish($"Difficulty updated for beatmap {beatmapId}.");
        }

        private void computeDifficulty(int beatmapId, WorkingBeatmap beatmap, Ruleset ruleset, MySqlConnection conn)
        {
            foreach (var attribute in ruleset.CreateDifficultyCalculator(beatmap).CalculateAll())
            {
                var legacyMod = toLegacyMod(attribute.Mods);

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
                foreach (var mapping in getAttributeMappings(attribute))
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

        private void finish(string message)
        {
            Interlocked.Increment(ref processedBeatmaps);
            Console.WriteLine($"{processedBeatmaps} / {totalBeatmaps} : {message}");
        }

        private IEnumerable<(int id, object value)> getAttributeMappings(DifficultyAttributes attributes)
        {
            switch (attributes)
            {
                case OsuDifficultyAttributes osu:
                    yield return (1, osu.AimStrain);
                    yield return (3, osu.SpeedStrain);
                    yield return (5, osu.OverallDifficulty);
                    yield return (7, osu.ApproachRate);
                    yield return (9, osu.MaxCombo);
                    break;
                case TaikoDifficultyAttributes taiko:
                    yield return (9, taiko.MaxCombo);
                    yield return (13, taiko.GreatHitWindow);
                    break;
                case ManiaDifficultyAttributes mania:
                    yield return (13, mania.GreatHitWindow);
                    break;
            }

            yield return (11, attributes.StarRating);
        }

        private LegacyMods toLegacyMod(Mod[] mods)
        {
            var value = LegacyMods.None;

            foreach (var mod in mods)
            {
                switch (mod)
                {
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
            }

            return value;
        }
    }
}
