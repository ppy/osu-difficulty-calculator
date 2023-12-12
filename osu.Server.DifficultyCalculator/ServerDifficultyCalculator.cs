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
using osu.Game.Rulesets.Scoring.Legacy;
using osu.Server.DifficultyCalculator.Commands;

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

        private void run(WorkingBeatmap beatmap, Action<int, WorkingBeatmap, Ruleset, MySqlConnection> callback)
        {
            int beatmapId = beatmap.BeatmapInfo.OnlineID;

            try
            {
                if (beatmap.Beatmap.HitObjects.Count == 0)
                {
                    using (var conn = Database.GetSlaveConnection())
                    {
                        if (conn.QuerySingleOrDefault<int>("SELECT `approved` FROM `osu_beatmaps` WHERE `beatmap_id` = @BeatmapId", new { BeatmapId = beatmapId }) > 0)
                            throw new ArgumentException($"Ranked beatmap {beatmapId} has 0 hitobjects!");
                    }
                }

                using (var conn = Database.GetConnection())
                {
                    if (processConverts && beatmap.BeatmapInfo.Ruleset.OnlineID == 0)
                    {
                        foreach (var ruleset in processableRulesets)
                            callback(beatmapId, beatmap, ruleset, conn);
                    }
                    else if (processableRulesets.Any(r => r.RulesetInfo.OnlineID == beatmap.BeatmapInfo.Ruleset.OnlineID))
                        callback(beatmapId, beatmap, beatmap.BeatmapInfo.Ruleset.CreateInstance(), conn);
                }
            }
            catch (Exception e)
            {
                throw new Exception($"{beatmapId} failed with: {e.Message}");
            }
        }

        private void processDifficulty(int beatmapId, WorkingBeatmap beatmap, Ruleset ruleset, MySqlConnection conn)
        {
            foreach (var attribute in ruleset.CreateDifficultyCalculator(beatmap).CalculateAllLegacyCombinations())
            {
                if (dryRun)
                    continue;

                LegacyMods legacyMods = ruleset.ConvertToLegacyMods(attribute.Mods);

                conn.Execute(
                    "INSERT INTO `osu_beatmap_difficulty` (`beatmap_id`, `mode`, `mods`, `diff_unified`) "
                    + "VALUES (@BeatmapId, @Mode, @Mods, @Diff) "
                    + "ON DUPLICATE KEY UPDATE `diff_unified` = @Diff",
                    new
                    {
                        BeatmapId = beatmapId,
                        Mode = ruleset.RulesetInfo.OnlineID,
                        Mods = (int)legacyMods,
                        Diff = attribute.StarRating
                    });

                if (!AppSettings.SKIP_INSERT_ATTRIBUTES)
                {
                    var parameters = new List<object>();

                    foreach (var mapping in attribute.ToDatabaseAttributes())
                    {
                        parameters.Add(new
                        {
                            BeatmapId = beatmapId,
                            Mode = ruleset.RulesetInfo.OnlineID,
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

                if (legacyMods == LegacyMods.None && ruleset.RulesetInfo.Equals(beatmap.BeatmapInfo.Ruleset))
                {
                    double beatLength = beatmap.Beatmap.GetMostCommonBeatLength();
                    double bpm = beatLength > 0 ? 60000 / beatLength : 0;

                    object param = new
                    {
                        BeatmapId = beatmapId,
                        Diff = attribute.StarRating,
                        AR = beatmap.Beatmap.BeatmapInfo.Difficulty.ApproachRate,
                        OD = beatmap.Beatmap.BeatmapInfo.Difficulty.OverallDifficulty,
                        HP = beatmap.Beatmap.BeatmapInfo.Difficulty.DrainRate,
                        CS = beatmap.Beatmap.BeatmapInfo.Difficulty.CircleSize,
                        BPM = Math.Round(bpm, 2),
                        MaxCombo = attribute.MaxCombo,
                    };

                    if (AppSettings.INSERT_BEATMAPS)
                    {
                        conn.Execute(
                            "INSERT INTO `osu_beatmaps` (`beatmap_id`, `difficultyrating`, `diff_approach`, `diff_overall`, `diff_drain`, `diff_size`, `bpm`, `max_combo`) "
                            + "VALUES (@BeatmapId, @Diff, @AR, @OD, @HP, @CS, @BPM, @MaxCombo) "
                            + "ON DUPLICATE KEY UPDATE `difficultyrating` = @Diff, `diff_approach` = @AR, `diff_overall` = @OD, `diff_drain` = @HP, `diff_size` = @CS, `bpm` = @BPM, `max_combo` = @MaxCombo",
                            param);
                    }
                    else
                    {
                        conn.Execute(
                            "UPDATE `osu_beatmaps` SET `difficultyrating` = @Diff, `diff_approach` = @AR, `diff_overall` = @OD, `diff_drain` = @HP, `diff_size` = @CS, `bpm` = @BPM , `max_combo` = @MaxCombo "
                            + "WHERE `beatmap_id` = @BeatmapId",
                            param);
                    }
                }
            }
        }

        private void processLegacyAttributes(int beatmapId, WorkingBeatmap beatmap, Ruleset ruleset, MySqlConnection conn)
        {
            Mod? classicMod = ruleset.CreateMod<ModClassic>();
            Mod[] mods = classicMod != null ? new[] { classicMod } : Array.Empty<Mod>();

            ILegacyScoreSimulator simulator = ((ILegacyRuleset)ruleset).CreateLegacyScoreSimulator();
            LegacyScoreAttributes attributes = simulator.Simulate(beatmap, beatmap.GetPlayableBeatmap(ruleset.RulesetInfo, mods));

            if (dryRun)
                return;

            conn.Execute(
                "INSERT INTO `osu_beatmap_scoring_attribs` (`beatmap_id`, `mode`, `legacy_accuracy_score`, `legacy_combo_score`, `legacy_bonus_score_ratio`) "
                + "VALUES (@BeatmapId, @Mode, @AccuracyScore, @ComboScore, @BonusScoreRatio) "
                + "ON DUPLICATE KEY UPDATE `legacy_accuracy_score` = @AccuracyScore, `legacy_combo_score` = @ComboScore, `legacy_bonus_score_ratio` = @BonusScoreRatio",
                new
                {
                    BeatmapId = beatmapId,
                    Mode = ruleset.RulesetInfo.OnlineID,
                    AccuracyScore = attributes.AccuracyScore,
                    ComboScore = attributes.ComboScore,
                    BonusScoreRatio = attributes.BonusScoreRatio
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
    }
}
