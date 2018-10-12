// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu/master/LICENCE

using System;
using System.IO;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Network;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;

namespace osu.Server.DifficultyCalculator
{
    public static class BeatmapLoader
    {
        public static WorkingBeatmap GetBeatmap(int beatmapId, bool verbose, bool forceDownload)
        {
            string fileLocation = Path.Combine(AppSettings.BeatmapsPath, beatmapId.ToString()) + ".osu";

            if ((forceDownload || !File.Exists(fileLocation)) && AppSettings.AllowDownload)
            {
                if (verbose)
                    Console.WriteLine($"Downloading {beatmapId}.");

                var req = new FileWebRequest(fileLocation, string.Format(AppSettings.DownloadPath, beatmapId));

                req.Failed += _ =>
                {
                    if (verbose)
                        Console.WriteLine($"Failed to download {beatmapId}.");
                };

                req.Finished += () =>
                {
                    if (verbose)
                        Console.WriteLine($"{beatmapId} successfully downloaded.");
                };

                req.Perform();
            }

            return !File.Exists(fileLocation) ? null : new LoaderWorkingBeatmap(fileLocation);
        }

        private class LoaderWorkingBeatmap : WorkingBeatmap
        {
            private readonly Beatmap beatmap;

            /// <summary>
            /// Constructs a new <see cref="LocalWorkingBeatmap"/> from a .osu file.
            /// </summary>
            /// <param name="file">The .osu file.</param>
            public LoaderWorkingBeatmap(string file)
                : this(File.OpenRead(file))
            {
            }

            private LoaderWorkingBeatmap(Stream stream)
                : this(new StreamReader(stream))
            {
                stream.Dispose();
            }

            private LoaderWorkingBeatmap(StreamReader streamReader)
                : this(Decoder.GetDecoder<Beatmap>(streamReader).Decode(streamReader))
            {
            }

            private LoaderWorkingBeatmap(Beatmap beatmap)
                : base(beatmap.BeatmapInfo)
            {
                this.beatmap = beatmap;

                switch (beatmap.BeatmapInfo.RulesetID)
                {
                    case 0:
                        beatmap.BeatmapInfo.Ruleset = new OsuRuleset().RulesetInfo;
                        break;
                    case 1:
                        beatmap.BeatmapInfo.Ruleset = new TaikoRuleset().RulesetInfo;
                        break;
                    case 2:
                        beatmap.BeatmapInfo.Ruleset = new CatchRuleset().RulesetInfo;
                        break;
                    case 3:
                        beatmap.BeatmapInfo.Ruleset = new ManiaRuleset().RulesetInfo;
                        break;
                }
            }

            protected override IBeatmap GetBeatmap() => beatmap;
            protected override Texture GetBackground() => null;
            protected override Track GetTrack() => null;
        }
    }
}
