// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using MySqlConnector;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects.Legacy;
using osu.Game.Rulesets.Scoring.Legacy;
using osu.Server.DifficultyCalculator.Commands;
using osu.Server.QueueProcessor;

namespace osu.Server.DifficultyCalculator
{
    public class ServerDifficultyCalculator
    {
        private static readonly List<Ruleset> available_rulesets = getRulesets();

        private readonly bool processConverts;
        private readonly bool dryRun;
        private readonly List<Ruleset> processableRulesets = new List<Ruleset>();

        public ServerDifficultyCalculator(int[]? rulesetIds = null, bool processConverts = true, bool dryRun = false)
        {
            this.processConverts = processConverts;
            this.dryRun = dryRun;

            if (rulesetIds != null)
            {
                foreach (int id in rulesetIds)
                    processableRulesets.Add(available_rulesets.Single(r => r.RulesetInfo.OnlineID == id));
            }
            else
            {
                processableRulesets.AddRange(available_rulesets);
            }
        }

        public void Process(WorkingBeatmap beatmap, ProcessingMode mode)
        {
            switch (mode)
            {
                case ProcessingMode.All:
                    ProcessDifficulty(beatmap);
                    ProcessLegacyAttributes(beatmap);
                    break;

                case ProcessingMode.Difficulty:
                    ProcessDifficulty(beatmap);
                    break;

                case ProcessingMode.ScoreAttributes:
                    ProcessLegacyAttributes(beatmap);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported processing mode supplied");
            }
        }

        public void ProcessDifficulty(WorkingBeatmap beatmap) => run(beatmap, processDifficulty);

        public void ProcessLegacyAttributes(WorkingBeatmap beatmap) => run(beatmap, processLegacyAttributes);

        public void NotifyBeatmapSetReprocessed(long beatmapSetId)
        {
            if (dryRun)
                return;

            using (var conn = DatabaseAccess.GetConnection())
            {
                conn.Execute(@"INSERT INTO `bss_process_queue` (`beatmapset_id`, `status`) VALUES (@beatmapset_id, 2)", new
                {
                    beatmapset_id = beatmapSetId,
                });
            }
        }

        public void NotifyBeatmapReprocessed(long beatmapId)
        {
            if (dryRun)
                return;

            using (var conn = DatabaseAccess.GetConnection())
            {
                conn.Execute(
                    """
                     INSERT INTO `bss_process_queue` (`beatmapset_id`, `status`)
                     VALUES ((SELECT `beatmapset_id` FROM `osu_beatmaps` WHERE `beatmap_id` = @beatmap_id), 2)
                     """,
                    new
                    {
                        beatmap_id = beatmapId,
                    });
            }
        }

        private void run(WorkingBeatmap beatmap, Action<ProcessableItem, MySqlConnection> callback)
        {
            try
            {
                bool ranked;

                using (var conn = DatabaseAccess.GetConnection())
                {
                    ranked = conn.QuerySingleOrDefault<int>("SELECT `approved` FROM `osu_beatmaps` WHERE `beatmap_id` = @BeatmapId", new
                    {
                        BeatmapId = beatmap.BeatmapInfo.OnlineID
                    }) > 0;

                    if (ranked && beatmap.Beatmap.HitObjects.Count == 0)
                        throw new ArgumentException($"Ranked beatmap {beatmap.BeatmapInfo.OnlineInfo} has 0 hitobjects!");
                }

                using (var conn = DatabaseAccess.GetConnection())
                {
                    if (processConverts && beatmap.BeatmapInfo.Ruleset.OnlineID == 0)
                    {
                        foreach (var ruleset in processableRulesets)
                            callback(new ProcessableItem(beatmap, ruleset, ranked), conn);
                    }
                    else if (processableRulesets.Any(r => r.RulesetInfo.OnlineID == beatmap.BeatmapInfo.Ruleset.OnlineID))
                        callback(new ProcessableItem(beatmap, beatmap.BeatmapInfo.Ruleset.CreateInstance(), ranked), conn);
                }
            }
            catch (Exception e)
            {
                throw new Exception($"{beatmap.BeatmapInfo.OnlineID} failed with: {e.Message}");
            }
        }

        private void processDifficulty(ProcessableItem item, MySqlConnection conn)
        {
            foreach (var (mods, attributes) in item.Ruleset.CreateDifficultyCalculator(item.WorkingBeatmap).CalculateAllLegacyCombinations())
            {
                if (dryRun)
                    continue;

                LegacyMods legacyMods = item.Ruleset.ConvertToLegacyMods(mods);

                conn.Execute(
                    "INSERT INTO `osu_beatmap_difficulty` (`beatmap_id`, `mode`, `mods`, `diff_unified`) "
                    + "VALUES (@BeatmapId, @Mode, @Mods, @Diff) "
                    + "ON DUPLICATE KEY UPDATE `diff_unified` = @Diff",
                    new
                    {
                        BeatmapId = item.BeatmapID,
                        Mode = item.RulesetID,
                        Mods = (int)legacyMods,
                        Diff = attributes.StarRating
                    });

                if (item.Ranked && !AppSettings.SKIP_INSERT_ATTRIBUTES)
                {
                    var parameters = new List<object>();

                    foreach (var mapping in attributes.ToDatabaseAttributes())
                    {
                        parameters.Add(new
                        {
                            BeatmapId = item.BeatmapID,
                            Mode = item.RulesetID,
                            Mods = (int)legacyMods,
                            Attribute = mapping.attributeId,
                            Value = Convert.ToSingle(mapping.value)
                        });
                    }

                    conn.Execute(
                        "INSERT INTO `osu_beatmap_difficulty_attribs` (`beatmap_id`, `mode`, `mods`, `attrib_id`, `value`) "
                        + "VALUES (@BeatmapId, @Mode, @Mods, @Attribute, @Value) "
                        + "ON DUPLICATE KEY UPDATE `value` = VALUES(`value`)",
                        parameters.ToArray());
                }

                if (legacyMods == LegacyMods.None && item.Ruleset.RulesetInfo.Equals(item.WorkingBeatmap.BeatmapInfo.Ruleset))
                {
                    double beatLength = item.WorkingBeatmap.Beatmap.GetMostCommonBeatLength();
                    double bpm = beatLength > 0 ? 60000 / beatLength : 0;

                    int countCircle = 0;
                    int countSlider = 0;
                    int countSpinner = 0;

                    foreach (var obj in item.WorkingBeatmap.Beatmap.HitObjects.OfType<IHasLegacyHitObjectType>())
                    {
                        if ((obj.LegacyType & LegacyHitObjectType.Circle) > 0)
                            countCircle++;
                        if ((obj.LegacyType & LegacyHitObjectType.Slider) > 0 || (obj.LegacyType & LegacyHitObjectType.Hold) > 0)
                            countSlider++;
                        if ((obj.LegacyType & LegacyHitObjectType.Spinner) > 0)
                            countSpinner++;
                    }

                    object param = new
                    {
                        BeatmapId = item.BeatmapID,
                        Diff = attributes.StarRating,
                        AR = item.WorkingBeatmap.BeatmapInfo.Difficulty.ApproachRate,
                        OD = item.WorkingBeatmap.BeatmapInfo.Difficulty.OverallDifficulty,
                        HP = item.WorkingBeatmap.BeatmapInfo.Difficulty.DrainRate,
                        CS = item.WorkingBeatmap.BeatmapInfo.Difficulty.CircleSize,
                        BPM = Math.Round(bpm, 2),
                        MaxCombo = attributes.MaxCombo,
                        CountCircle = countCircle,
                        CountSlider = countSlider,
                        CountSpinner = countSpinner,
                        CountTotal = countCircle + countSlider + countSpinner
                    };

                    if (AppSettings.INSERT_BEATMAPS)
                    {
                        conn.Execute(
                            "INSERT INTO `osu_beatmaps` (`beatmap_id`, `difficultyrating`, `diff_approach`, `diff_overall`, `diff_drain`, `diff_size`, `bpm`, `max_combo`, `countNormal`, `countSlider`, `countSpinner`, `countTotal`) "
                            + "VALUES (@BeatmapId, @Diff, @AR, @OD, @HP, @CS, @BPM, @MaxCombo, @CountCircle, @CountSlider, @CountSpinner, @CountTotal) "
                            + "ON DUPLICATE KEY UPDATE `difficultyrating` = @Diff, `diff_approach` = @AR, `diff_overall` = @OD, `diff_drain` = @HP, `diff_size` = @CS, `bpm` = @BPM, `max_combo` = @MaxCombo, `countNormal` = @CountCircle, `countSlider` = @CountSlider, `countSpinner` = @CountSpinner, `countTotal` = @CountTotal",
                            param);
                    }
                    else
                    {
                        conn.Execute(
                            "UPDATE `osu_beatmaps` SET `difficultyrating` = @Diff, `diff_approach` = @AR, `diff_overall` = @OD, `diff_drain` = @HP, `diff_size` = @CS, `bpm` = @BPM , `max_combo` = @MaxCombo, `countNormal` = @CountCircle, `countSlider` = @CountSlider, `countSpinner` = @CountSpinner, `countTotal` = @CountTotal "
                            + "WHERE `beatmap_id` = @BeatmapId",
                            param);
                    }
                }
            }
        }

        private void processLegacyAttributes(ProcessableItem item, MySqlConnection conn)
        {
            Mod? classicMod = item.Ruleset.CreateMod<ModClassic>();
            Mod[] mods = classicMod != null ? new[] { classicMod } : Array.Empty<Mod>();

            ILegacyScoreSimulator simulator = ((ILegacyRuleset)item.Ruleset).CreateLegacyScoreSimulator();
            LegacyScoreAttributes attributes = simulator.Simulate(item.WorkingBeatmap, item.WorkingBeatmap.GetPlayableBeatmap(item.Ruleset.RulesetInfo, mods));

            if (dryRun)
                return;

            conn.Execute(
                "INSERT INTO `osu_beatmap_scoring_attribs` (`beatmap_id`, `mode`, `legacy_accuracy_score`, `legacy_combo_score`, `legacy_bonus_score_ratio`, `legacy_bonus_score`, `max_combo`) "
                + "VALUES (@BeatmapId, @Mode, @AccuracyScore, @ComboScore, @BonusScoreRatio, @BonusScore, @MaxCombo) "
                + "ON DUPLICATE KEY UPDATE `legacy_accuracy_score` = @AccuracyScore, `legacy_combo_score` = @ComboScore, `legacy_bonus_score_ratio` = @BonusScoreRatio, `legacy_bonus_score` = @BonusScore, `max_combo` = @MaxCombo",
                new
                {
                    BeatmapId = item.BeatmapID,
                    Mode = item.RulesetID,
                    AccuracyScore = attributes.AccuracyScore,
                    ComboScore = attributes.ComboScore,
                    BonusScoreRatio = attributes.BonusScoreRatio,
                    BonusScore = attributes.BonusScore,
                    MaxCombo = attributes.MaxCombo
                });
        }

        private static List<Ruleset> getRulesets()
        {
            const string ruleset_library_prefix = "osu.Game.Rulesets";

            var rulesetsToProcess = new List<Ruleset>();

            foreach (string file in Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, $"{ruleset_library_prefix}.*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(file);
                    Type type = assembly.GetTypes().First(t => t.IsPublic && t.IsSubclassOf(typeof(Ruleset)));
                    rulesetsToProcess.Add((Ruleset)Activator.CreateInstance(type)!);
                }
                catch
                {
                    throw new Exception($"Failed to load ruleset ({file})");
                }
            }

            return rulesetsToProcess;
        }

        private readonly record struct ProcessableItem(WorkingBeatmap WorkingBeatmap, Ruleset Ruleset, bool Ranked)
        {
            public int BeatmapID => WorkingBeatmap.BeatmapInfo.OnlineID;
            public int RulesetID => Ruleset.RulesetInfo.OnlineID;
        }
    }
}
