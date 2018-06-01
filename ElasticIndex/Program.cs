// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.Globalization;
using System.Threading;
using Dapper;

namespace ElasticIndex
{
    public class Program
    {
        public void Run()
        {
            if (AppSettings.IsWatching) Console.WriteLine("Running in watch mode.");

            bool ranOnce = false;

            while (!ranOnce || AppSettings.IsWatching)
            {
                // When running in watch mode, the indexer should be told to resume from the
                // last known saved point instead of the configured value.
                RunLoop(ranOnce ? null : AppSettings.ResumeFrom);
                ranOnce = true;
                if (AppSettings.IsWatching) Thread.Sleep(10000);
            }
        }

        public void RunLoop(long? resumeFrom)
        {
            var suffix = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

            foreach (var mode in AppSettings.Modes)
            {
                var indexName = $"{AppSettings.Prefix}high_scores_{mode}";
                var upcase = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(mode);
                var className = $"{typeof(HighScore).Namespace}.HighScore{upcase}";

                Type indexerType = typeof(HighScoreIndexer<>)
                    .MakeGenericType(Type.GetType(className, true));

                dynamic indexer = Activator.CreateInstance(indexerType);
                indexer.Suffix = suffix;
                indexer.Name = indexName;
                indexer.ResumeFrom = resumeFrom;
                indexer.Run();
            }
        }

        public static void Main()
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;
            new Program().Run();
        }
    }
}
