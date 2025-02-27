// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using Dapper;
using McMaster.Extensions.CommandLineUtils;
using osu.Server.QueueProcessor;

namespace osu.Server.DifficultyCalculator.Commands
{
    [Command("since", Description = "Calculates the difficulty of all beatmaps following a beatmap id.")]
    public class SinceCommand : CalculatorCommand
    {
        [Argument(0, "marker", Description = "The minimum beatmap id to calculate the difficulty for.")]
        public int Marker { get; set; }

        [Option(CommandOptionType.NoValue, Template = "-r|--ranked", Description = "Only calculate difficulty for ranked/approved/qualified/loved maps.")]
        public bool RankedOnly { get; set; }

        protected override IEnumerable<int> GetBeatmaps()
        {
            using (var conn = DatabaseAccess.GetConnection())
            {
                var condition = CombineSqlConditions(
                    RankedOnly ? "`approved` >= 1" : null,
                    $"`beatmap_id` >= {Marker}",
                    "`deleted_at` IS NULL"
                );

                return conn.Query<int>($"SELECT `beatmap_id` FROM `osu_beatmaps` {condition}");
            }
        }
    }
}
