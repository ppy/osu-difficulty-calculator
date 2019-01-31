// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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
