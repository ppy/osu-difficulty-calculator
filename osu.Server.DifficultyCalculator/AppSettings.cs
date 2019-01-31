// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Microsoft.Extensions.Configuration;

namespace osu.Server.DifficultyCalculator
{
    public class AppSettings
    {
        public static string ConnectionStringMaster { get; }
        public static string ConnectionStringSlave { get; }

        public static bool ReadOnly { get; }

        public static bool InsertBeatmaps { get; }

        public static string BeatmapsPath { get; }

        public static bool AllowDownload { get; }
        public static string DownloadPath { get; }

        public static bool UseDocker { get; }

        static AppSettings()
        {
            var env = Environment.GetEnvironmentVariable("APP_ENV") ?? "development";
            var config = new ConfigurationBuilder()
                         .AddJsonFile("appsettings.json", true, false)
                         .AddJsonFile($"appsettings.{env}.json", true, false)
                         .AddEnvironmentVariables()
                         .Build();

            ConnectionStringMaster = config.GetConnectionString("master");
            ConnectionStringSlave = config.GetConnectionString("slave");

            ReadOnly = bool.Parse(config["read_only"]);
            InsertBeatmaps = bool.Parse(config["insert_beatmaps"]);

            BeatmapsPath = config["beatmaps_path"];

            AllowDownload = bool.Parse(config["allow_download"]);
            DownloadPath = config["download_path"];

            UseDocker = Environment.GetEnvironmentVariable("DOCKER")?.Contains("1") ?? false;
        }
    }
}
