// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.Collections.Concurrent;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Elasticsearch.Net;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Nest;

namespace ElasticIndex
{
    public class HighScoreIndexer<T> where T : Model
    {
        public string Name { get; set; }
        public long? ResumeFrom { get; set; }
        public string Suffix { get; set; }

        private readonly int chunkSize = 10000;
        private readonly IDbConnection dbConnection;
        private readonly ElasticClient elasticClient;

        public HighScoreIndexer()
        {
            if (!string.IsNullOrEmpty(Program.Configuration["chunk_size"]))
                chunkSize = int.Parse(Program.Configuration["chunk_size"]);

            dbConnection = new MySqlConnection(Program.Configuration.GetConnectionString("osu"));
            elasticClient = new ElasticClient
            (
                new ConnectionSettings(new Uri(Program.Configuration["elasticsearch:host"]))
            );
        }

        public void Run()
        {
            var pendingTasks = new ConcurrentBag<Task>();

            string index = findOrCreateIndex(Name);

            // find out if we should be resuming
            var resumeFrom = ResumeFrom ?? IndexMeta.GetByName(index)?.LastId;

            Console.WriteLine();
            Console.WriteLine($"{typeof(T)}, index `{index}`, chunkSize `{chunkSize}`, resume `{resumeFrom}`");
            Console.WriteLine();

            var start = DateTime.Now;
            long count = 0;

            using (dbConnection)
            {
                dbConnection.Open();
                // TODO: retry needs to be added on timeout
                var chunks = Model.Chunk<T>(dbConnection, chunkSize, resumeFrom);
                foreach (var chunk in chunks)
                {
                    var bulkDescriptor = new BulkDescriptor().Index(index);
                    bulkDescriptor.IndexMany(chunk);

                    Task task = elasticClient.BulkAsync(bulkDescriptor);
                    pendingTasks.Add(task);
                    task.ContinueWith(t => pendingTasks.TryTake(out task));

                    // I feel like this is in the wrong place...
                    IndexMeta.Update(new IndexMeta
                    {
                        Index = index,
                        Alias = Name,
                        LastId = chunk.Last().CursorValue,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });

                    count += chunk.Count;
                }
            }

            var span = DateTime.Now - start;
            Console.WriteLine($"{count} records took {span}");
            if (count > 0) Console.WriteLine($"{count / span.TotalSeconds} records/s");

            updateAlias(Name, index);

            // wait for all tasks to complete before exiting.
            Console.WriteLine("Waiting for all tasks to complete...");
            Task.WaitAll(pendingTasks.ToArray());
            Console.WriteLine("All tasks completed.");
        }

        /// <summary>
        /// Attemps to find the matching index or creates a new one.
        /// </summary>
        /// <param name="name">Name of the alias to find the matching index for.</param>
        /// <returns>Name of index found or created.</returns>
        private string findOrCreateIndex(string name)
        {
            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine($"Find or create index for `{name}`...");
            var metas = IndexMeta.GetByAlias(name).ToList();
            var indices = elasticClient.GetIndicesPointingToAlias(name);

            string index = metas.FirstOrDefault(m => indices.Contains(m.Index))?.Index;
            // 3 cases are handled:
            // 1. Index was already aliased and has tracking information; likely resuming from a completed job.
            if (index != null)
            {
                Console.WriteLine($"Found matching aliased index `{index}`.");
                return index;
            }

            // 2. Index has not been aliased and has tracking information; likely resuming from an imcomplete job.
            index = metas.FirstOrDefault()?.Index;
            if (index != null)
            {
                Console.WriteLine($"Found previous index `{index}`.");
                return index;
            }

            // 3. Not aliased and no tracking information; likely starting from scratch
            var suffix = Suffix ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            index = $"{name}_{suffix}";

            Console.WriteLine($"Creating `{index}` for `{name}`.");
            // create by supplying the json file instead of the attributed class because we're not
            // mapping every field but still want everything for _source.
            var json = File.ReadAllText(Path.GetFullPath("schemas/high_scores.json"));
            elasticClient.LowLevel.IndicesCreate<DynamicResponse>(index, json);

            return index;

            // TODO: cases not covered should throw an Exception (aliased but not tracked, etc).
        }

        private void updateAlias(string alias, string index)
        {
            Console.WriteLine($"Updating `{alias}` alias to `{index}`...");

            var aliasDescriptor = new BulkAliasDescriptor();
            var oldIndices = elasticClient.GetIndicesPointingToAlias(alias);

            foreach (var oldIndex in oldIndices)
                aliasDescriptor.Remove(d => d.Alias(alias).Index(oldIndex));

            aliasDescriptor.Add(d => d.Alias(alias).Index(index));

            Console.WriteLine(elasticClient.Alias(aliasDescriptor));

            // TODO: cleanup unaliased indices.
        }
    }
}
