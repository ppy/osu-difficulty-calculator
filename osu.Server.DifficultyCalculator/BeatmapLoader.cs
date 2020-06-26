// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using McMaster.Extensions.CommandLineUtils;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Network;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;

namespace osu.Server.DifficultyCalculator
{
    public static class BeatmapLoader
    {
        public static WorkingBeatmap GetBeatmap(int beatmapId, bool verbose = false, bool forceDownload = true, IReporter reporter = null)
        {
            string fileLocation = Path.Combine(AppSettings.BEATMAPS_PATH, beatmapId.ToString()) + ".osu";

            if ((forceDownload || !File.Exists(fileLocation)) && AppSettings.ALLOW_DOWNLOAD)
            {
                if (verbose)
                    reporter?.Verbose($"Downloading {beatmapId}.");

                var req = new WebRequest(string.Format(AppSettings.DOWNLOAD_PATH, beatmapId))
                {
                    AllowInsecureRequests = true
                };

                req.Failed += _ =>
                {
                    if (verbose)
                        reporter?.Error($"Failed to download {beatmapId}.");
                };

                req.Finished += () =>
                {
                    if (verbose)
                        reporter?.Verbose($"{beatmapId} successfully downloaded.");
                };

                req.Perform();

                var stream = req.ResponseStream;

                if (AppSettings.SAVE_DOWNLOADED)
                {
                    using (var fileStream = File.Create(fileLocation))
                    {
                        stream.CopyTo(fileStream);
                        stream.Seek(0, SeekOrigin.Begin);
                    }
                }

                return new LoaderWorkingBeatmap(stream);
            }

            return !File.Exists(fileLocation) ? null : new LoaderWorkingBeatmap(fileLocation);
        }

        private class LoaderWorkingBeatmap : WorkingBeatmap
        {
            private readonly Beatmap beatmap;

            /// <summary>
            /// Constructs a new <see cref="LoaderWorkingBeatmap"/> from a .osu file.
            /// </summary>
            /// <param name="file">The .osu file.</param>
            public LoaderWorkingBeatmap(string file)
                : this(File.OpenRead(file))
            {
            }

            public LoaderWorkingBeatmap(Stream stream)
                : this(new LineBufferedReader(stream))
            {
                stream.Dispose();
            }

            private LoaderWorkingBeatmap(LineBufferedReader reader)
                : this(Decoder.GetDecoder<Beatmap>(reader).Decode(reader))
            {
            }

            private LoaderWorkingBeatmap(Beatmap beatmap)
                : base(beatmap.BeatmapInfo, null)
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
