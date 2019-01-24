// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.IO;
using McMaster.Extensions.CommandLineUtils;

namespace osu.Server.DifficultyCalculator.Commands
{
    [Command(Name = "files", Description = "Computes the difficulty of all files in the beatmaps path.")]
    public class FilesCommand : CalculatorCommand
    {
        protected override IEnumerable<int> GetBeatmaps()
        {
            var ids = new List<int>();

            foreach (var f in Directory.GetFiles(AppSettings.BeatmapsPath))
            {
                var filename = Path.GetFileNameWithoutExtension(f);

                if (int.TryParse(filename.Split('.')[0], out var id))
                    ids.Add(id);
            }

            return ids;
        }
    }
}
