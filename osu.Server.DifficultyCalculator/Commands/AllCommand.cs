// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System.Collections.Generic;
using Dapper;
using McMaster.Extensions.CommandLineUtils;

namespace osu.Server.DifficultyCalculator.Commands
{
    [Command(Name = "all", Description = "Calculates the difficulty of all beatmaps.")]
    public class AllCommand : CalculatorCommand
    {
        [Option(CommandOptionType.NoValue, Template = "-r|--ranked", Description = "Only calculate difficulty for ranked/approved/qualified/loved maps.")]
        public bool RankedOnly { get; set; }

        protected override IEnumerable<int> GetBeatmaps()
        {
            using (var conn = SlaveDatabase.GetConnection())
            {
                var condition = CombineSqlConditions(
                    RankedOnly ? "approved >= 1" : null
                );

                return conn.Query<int>($"SELECT beatmap_id FROM osu_beatmaps {condition}");
            }
        }
    }
}
