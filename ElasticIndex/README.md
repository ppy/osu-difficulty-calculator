# ElasticIndex

TODO: Readme

Component for loading [osu!](https://osu.ppy.sh) data into Elasticsearch.

Currently limited to user high scores.

# Requirements

- .NET Core 2.0


# Configuration

The project reads configuration in the following order:
- `appsettings.json`
- `appsettings.{env}.json`
- Environment

where `{env}` is specified by the `APP_ENV` environment variable and defaults to `development`.

# Available settings

Settings should be set in `appsettings` or environment appropriate to the platform, e.g.

`appsettings.json`
```json
{
  "elasticsearch": {
    "host": "http://localhost:9200"
  }
}
```

`Linux / MacOS`
```sh
# note the double underscore
elasticsearch__host=http://localhost:9200 dotnet run
```

---

## `ConnectionStrings:osu`
Standard .NET Connection String to the database.


## `elasticsearch:host`
Elasticsearch host.


## `elasticsearch:prefix`
Assigns a prefix to the indices used.


## `modes`
Game modes to index in a comma separated list.
Available modes are `"osu,fruits,mania,taiko"`.


## `chunk_size`
Batch size when querying from the database.
Defaults to `10000`


## `resume_from`
Cursor value of where to resume reading from.


## `watch`
Sets the program into watch mode.
In watch mode, the program will keep polling for updates to index.


# Index aliasing and resume support
Index aliases are used to support zero-downtime index switches.
When creating new indices, the new index is suffixed with a timestamp.
At the end of the indexing process, the alias is updated to point to the new index.

The program keeps track of progress information by writing to a `_index_meta` index. When starting, it will try to resume from the last known position.

Setting `resume_from=0` will force the indexer to being reading from the beginning.


# TODO
These items are considered important but not implemented yet:
- Watching for new data.
- Handle elasticsearch response errors.
- Support writing metadata info to database.
- Option to ignore existing index and create a new one.
