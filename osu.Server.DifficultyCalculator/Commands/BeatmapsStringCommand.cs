// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System.Collections.Generic;
using System.Linq;
using McMaster.Extensions.CommandLineUtils;

namespace osu.Server.DifficultyCalculator.Commands
{
    [Command("beatmapsstring", Description = "A compatibility mode which accepts a comma-separated list of beatmap ids.")]
    public class BeatmapsStringCommand : CalculatorCommand
    {
        [Argument(0, "beatmaps", Description = "A comma-separated list of beatmap ids.")]
        public string Beatmaps { get; set; }

        protected override IEnumerable<int> GetBeatmaps() => Beatmaps.Split(',').Select(int.Parse);
    }
}
