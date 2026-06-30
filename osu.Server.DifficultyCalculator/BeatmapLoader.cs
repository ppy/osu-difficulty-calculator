// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Dapper;
using McMaster.Extensions.CommandLineUtils;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Network;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using osu.Game.Skinning;
using osu.Server.QueueProcessor;
using SharpCompress.Archives.Tar;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace osu.Server.DifficultyCalculator
{
    public static class BeatmapLoader
    {
        public static void PopulateCacheWithSeededFiles(IReporter? reporter = null)
        {
            reporter?.Output("Populating local cache from seeded beatmaps...");

            var req = new WebRequest("https://data.ppy.sh/");
            req.Perform();
            string response = req.GetResponseString()!;
            var matches = Regex.Matches(response, "[0-9]+_[0-9]+_[0-9]+_osu_files.tar.bz2");

            var tarBz2Filename = matches.Last().Value;

            // If the archive is present locally and cache has many files, assume it's already been extracted for simplicity.
            int localBeatmaps = Directory.GetFiles(AppSettings.BEATMAPS_PATH).Length;

            if (File.Exists(tarBz2Filename) && localBeatmaps > 10000)
            {
                reporter?.Output($"Found {tarBz2Filename} locally and populated cache ({localBeatmaps:N0} files), assuming cache is populated already.");
                reporter?.Output("Delete this file to repopulate cache.");
                return;
            }

            string tarFilename = tarBz2Filename.Replace(".bz2", string.Empty);

            try
            {
                if (!File.Exists(tarBz2Filename))
                {
                    req = new FileWebRequest(tarBz2Filename, $"https://data.ppy.sh/{tarBz2Filename}");
                    int? progress = null;
                    req.DownloadProgress += (current, total) =>
                    {
                        int roundedProgress = (int)((double)current / total * 100);

                        if (progress != roundedProgress)
                        {
                            if (roundedProgress == 0)
                                reporter?.Verbose($"Downloading {tarBz2Filename}... {total / 1048576:N0} mb");
                            else if (roundedProgress % 10 == 0)
                                reporter?.Verbose($"Downloading {tarBz2Filename}... {roundedProgress}%");
                        }

                        progress = roundedProgress;
                    };
                    req.Perform();
                }

                if (!File.Exists(tarFilename))
                {
                    reporter?.Verbose($"Extracting {tarBz2Filename}...");

                    using (var stream = File.OpenRead(tarBz2Filename))
                    using (var outStream = File.Create(tarFilename))
                    using (var bz2 = BZip2Stream.Create(stream, CompressionMode.Decompress, false))
                        bz2.CopyTo(outStream);
                }

                using var archive = TarArchive.OpenArchive(tarFilename);

                foreach (var entry in archive.Entries)
                {
                    if (entry.Size == 0)
                        continue;

                    string filename = Path.GetFileName(entry.Key!);

                    reporter?.Verbose($"Extracting {filename}...");

                    using (var outStream = File.Create(Path.Combine(AppSettings.BEATMAPS_PATH, filename)))
                    using (var stream = entry.OpenEntryStream())
                        stream.CopyTo(outStream);
                }
            }
            catch
            {
                // If any error occurs, delete the archive to allow a retry next run.
                File.Delete(tarBz2Filename);
                throw;
            }
            finally
            {
                File.Delete(tarFilename);
            }
        }

        public static WorkingBeatmap GetBeatmap(int beatmapId, bool verbose = false, IReporter? reporter = null)
        {
            string fileLocation = Path.Combine(AppSettings.BEATMAPS_PATH, beatmapId.ToString()) + ".osu";

            bool cachedBeatmapValid = File.Exists(fileLocation);

            if (cachedBeatmapValid && AppSettings.VERIFY_BEATMAP_HASHES)
            {
                using (var conn = DatabaseAccess.GetConnection())
                {
                    using (var stream = File.OpenRead(fileLocation))
                    {
                        string localHash = stream.ComputeMD5Hash();
                        string serverHash = conn.QuerySingle<string>("SELECT checksum FROM osu_beatmaps WHERE beatmap_id = @beatmap_id", new
                        {
                            beatmap_id = beatmapId
                        });

                        if (localHash != serverHash)
                        {
                            reporter?.Warn($"Checksum didn't match for {beatmapId}, ignoring cache");

                            File.Delete(fileLocation);
                            cachedBeatmapValid = false;
                        }
                    }
                }
            }

            if (!cachedBeatmapValid && AppSettings.ALLOW_DOWNLOAD)
            {
                if (verbose)
                    reporter?.Verbose($"Downloading {beatmapId}.");

                var req = new WebRequest(string.Format(AppSettings.DOWNLOAD_PATH, beatmapId))
                {
                    Timeout = 60000,
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

                if (stream.Length == 0)
                    throw new Exception("Beatmap download failed.");

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

            if (!cachedBeatmapValid)
                throw new Exception("Beatmap file does not exist and was not downloaded.");

            return new LoaderWorkingBeatmap(fileLocation);
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

                switch (beatmap.BeatmapInfo.Ruleset.OnlineID)
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
            public override Texture? GetBackground() => null;
            protected override Track? GetBeatmapTrack() => null;
            protected override ISkin? GetSkin() => null;
            public override Stream? GetStream(string storagePath) => null;
        }
    }
}
