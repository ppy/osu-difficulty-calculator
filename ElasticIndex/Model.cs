// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-server/master/LICENCE

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Dapper.Contrib.Extensions;

namespace ElasticIndex
{
    [CursorColumn("id")]
    public abstract class Model
    {
        public abstract long CursorValue { get; }

        public static IEnumerable<List<T>> Chunk<T>(
            IDbConnection dbConnection,
            int chunkSize = 10000,
            long? resumeFrom = null) where T : Model
        {
            long? lastId = resumeFrom ?? 0;
            Console.WriteLine($"Starting from {lastId}...");

            var cursorColumn = (typeof(T).GetCustomAttributes(typeof(CursorColumnAttribute), true).First() as CursorColumnAttribute).Name;
            var table = (typeof(T).GetCustomAttributes(typeof(TableAttribute), true).First() as TableAttribute).Name;

            while (lastId != null)
            {
                string query = $"select * from {table} where {cursorColumn} > @lastId order by {cursorColumn} asc limit @chunkSize;";
                var parameters = new { lastId, chunkSize };
                Console.WriteLine("{0} {1}", query, parameters);
                var queryResult = dbConnection.Query<T>(query, parameters).AsList();

                lastId = queryResult.LastOrDefault()?.CursorValue;
                if (lastId.HasValue) yield return queryResult;
            }
        }
    }
}
