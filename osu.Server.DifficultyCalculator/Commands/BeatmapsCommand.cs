// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System.Collections.Generic;
using McMaster.Extensions.CommandLineUtils;

namespace osu.Server.DifficultyCalculator.Commands
{
    [Command(Name = "beatmaps", Description = "Calculates the difficulty of specific beatmaps.")]
    public class BeatmapsCommand : CalculatorCommand
    {
        [Argument(0, "beatmap", Description = "One or more beatmap ids to calculate the difficulty for.")]
        public int[] BeatmapIds { get; set; }

        protected override IEnumerable<int> GetBeatmaps() => BeatmapIds;
    }
}
