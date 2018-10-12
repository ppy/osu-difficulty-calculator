// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace osu.Server.DifficultyCalculator
{
    public class AppSettings
    {
        public static string ConnectionString { get; }
        public static string BeatmapsPath { get; }

        public static bool AllowDownload { get; }
        public static string DownloadPath { get; }

        static AppSettings()
        {
            var env = Environment.GetEnvironmentVariable("APP_ENV") ?? "development";
            var config = new ConfigurationBuilder()
                         .SetBasePath(Directory.GetCurrentDirectory())
                         .AddJsonFile("appsettings.json", true, false)
                         .AddJsonFile($"appsettings.{env}.json", true, false)
                         .AddEnvironmentVariables()
                         .Build();

            ConnectionString = config.GetConnectionString("osu");
            BeatmapsPath = config["beatmaps_path"];

            AllowDownload = bool.Parse(config["allow_download"]);
            DownloadPath = config["download_path"];
        }
    }
}
