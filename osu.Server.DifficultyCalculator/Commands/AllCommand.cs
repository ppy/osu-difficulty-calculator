// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using Dapper;
using McMaster.Extensions.CommandLineUtils;

namespace osu.Server.DifficultyCalculator.Commands
{
    [Command(Name = "all", Description = "Calculates the difficulty of all beatmaps in the database.")]
    public class AllCommand : CalculatorCommand
    {
        [Option(CommandOptionType.NoValue, Template = "-r|--ranked", Description = "Only calculate difficulty for ranked/approved/qualified/loved maps.")]
        public bool RankedOnly { get; set; }

        protected override IEnumerable<int> GetBeatmaps()
        {
            using (var conn = Database.GetSlaveConnection())
            {
                var condition = CombineSqlConditions(
                    RankedOnly ? "`approved` >= 1" : null,
                    "`deleted_at` IS NULL"
                );

                return conn.Query<int>($"SELECT `beatmap_id` FROM `osu_beatmaps` {condition}");
            }
        }
    }
}
