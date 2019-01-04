// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

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
