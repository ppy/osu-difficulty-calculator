// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using Dapper;
using McMaster.Extensions.CommandLineUtils;
using osu.Server.QueueProcessor;

namespace osu.Server.DifficultyCalculator.Commands
{
    [Command(Name = "all", Description = "Calculates the difficulty of all beatmaps in the database.")]
    public class AllCommand : CalculatorCommand
    {
        [Option(CommandOptionType.NoValue, Template = "-r|--ranked", Description = "Only calculate difficulty for ranked/approved/qualified/loved maps.")]
        public bool RankedOnly { get; set; }

        [Option("--sql", Description = "Specify a custom query to limit the scope of beatmaps")]
        public string? CustomQuery { get; set; }

        [Option("--from", Description = "The minimum beatmap id to calculate the difficulty for.")]
        public int StartId { get; set; }

        protected override IEnumerable<int> GetBeatmaps()
        {
            using (var conn = DatabaseAccess.GetConnection())
            {
                var condition = CombineSqlConditions(
                    RankedOnly ? "`approved` >= 1" : null,
                    $"`beatmap_id` >= {StartId}",
                    "`deleted_at` IS NULL",
                    CustomQuery
                );

                return conn.Query<int>($"SELECT `beatmap_id` FROM `osu_beatmaps` {condition}");
            }
        }
    }
}
