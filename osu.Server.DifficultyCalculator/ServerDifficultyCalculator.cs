// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using JetBrains.Annotations;
using MySqlConnector;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets;

namespace osu.Server.DifficultyCalculator
{
    public class ServerDifficultyCalculator
    {
        private static readonly List<Ruleset> available_rulesets = getRulesets();

        private readonly bool processConverts;
        private readonly List<Ruleset> processableRulesets = new List<Ruleset>();

        public ServerDifficultyCalculator(int[] rulesetIds = null, bool processConverts = true)
        {
            this.processConverts = processConverts;

            if (rulesetIds != null)
            {
                foreach (int id in rulesetIds)
                    processableRulesets.Add(available_rulesets.Single(r => r.RulesetInfo.ID == id));
            }
            else
            {
                processableRulesets.AddRange(available_rulesets);
            }
        }

        public void ProcessBeatmap(WorkingBeatmap beatmap)
        {
            Debug.Assert(beatmap.BeatmapInfo.OnlineBeatmapID != null, "beatmap.BeatmapInfo.OnlineBeatmapID != null");

            int beatmapId = beatmap.BeatmapInfo.OnlineBeatmapID.Value;

            try
            {
                if (beatmap.Beatmap.HitObjects.Count == 0)
                {
                    using (var conn = Database.GetSlaveConnection())
                    {
                        if (conn?.QuerySingleOrDefault<int>("SELECT `approved` FROM `osu_beatmaps` WHERE `beatmap_id` = @BeatmapId", new { BeatmapId = beatmapId }) > 0)
                            throw new ArgumentException($"Ranked beatmap {beatmapId} has 0 hitobjects!");
                    }
                }

                using (var conn = Database.GetConnection())
                {
                    if (processConverts && beatmap.BeatmapInfo.RulesetID == 0)
                    {
                        foreach (var ruleset in processableRulesets)
                            computeDifficulty(beatmapId, beatmap, ruleset, conn);
                    }
                    else if (processableRulesets.Any(r => r.RulesetInfo.ID == beatmap.BeatmapInfo.RulesetID))
                        computeDifficulty(beatmapId, beatmap, beatmap.BeatmapInfo.Ruleset.CreateInstance(), conn);
                }
            }
            catch (Exception e)
            {
                throw new Exception($"{beatmapId} failed with: {e.Message}");
            }
        }

        private void computeDifficulty(int beatmapId, WorkingBeatmap beatmap, Ruleset ruleset, [CanBeNull] MySqlConnection conn)
        {
            foreach (var attribute in ruleset.CreateDifficultyCalculator(beatmap).CalculateAll())
            {
                var legacyMod = attribute.Mods.ToLegacy();

                conn?.Execute(
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

                conn?.Execute(
                    "INSERT INTO `osu_beatmap_difficulty_attribs` (`beatmap_id`, `mode`, `mods`, `attrib_id`, `value`) "
                    + "VALUES (@BeatmapId, @Mode, @Mods, @Attribute, @Value) "
                    + "ON DUPLICATE KEY UPDATE `value` = VALUES(`value`)",
                    parameters.ToArray());

                if (legacyMod == LegacyMods.None && ruleset.RulesetInfo.Equals(beatmap.BeatmapInfo.Ruleset))
                {
                    object param = new
                    {
                        BeatmapId = beatmapId,
                        Diff = attribute.StarRating,
                        AR = beatmap.Beatmap.BeatmapInfo.BaseDifficulty.ApproachRate,
                        OD = beatmap.Beatmap.BeatmapInfo.BaseDifficulty.OverallDifficulty,
                        HP = beatmap.Beatmap.BeatmapInfo.BaseDifficulty.DrainRate,
                        CS = beatmap.Beatmap.BeatmapInfo.BaseDifficulty.CircleSize,
                        BPM = 60000 / beatmap.Beatmap.GetMostCommonBeatLength()
                    };

                    if (AppSettings.INSERT_BEATMAPS)
                    {
                        conn?.Execute(
                            "INSERT INTO `osu_beatmaps` (`beatmap_id`, `difficultyrating`, `diff_approach`, `diff_overall`, `diff_drain`, `diff_size`, `bpm`) "
                            + "VALUES (@BeatmapId, @Diff, @AR, @OD, @HP, @CS, @BPM) "
                            + "ON DUPLICATE KEY UPDATE `difficultyrating` = @Diff, `diff_approach` = @AR, `diff_overall` = @OD, `diff_drain` = @HP, `diff_size` = @CS, `bpm` = @BPM",
                            param);
                    }
                    else
                    {
                        conn?.Execute(
                            "UPDATE `osu_beatmaps` SET `difficultyrating` = @Diff, `diff_approach` = @AR, `diff_overall` = @OD, `diff_drain` = @HP, `diff_size` = @CS, `bpm` = @BPM "
                            + "WHERE `beatmap_id` = @BeatmapId",
                            param);
                    }
                }
            }
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
                    rulesetsToProcess.Add((Ruleset)Activator.CreateInstance(type));
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
