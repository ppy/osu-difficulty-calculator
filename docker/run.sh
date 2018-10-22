#!/bin/bash

OUT_DIR='/out'

dotnet build -c:Release -o ${OUT_DIR}

cd ${OUT_DIR}

echo "{
  // comments are supported.
  \"ConnectionStrings\": {
    \"master\": \"Server=${DB_HOST};Port=${DB_PORT};Database=${DB_DATABASE};Uid=${DB_USER};Pwd=${DB_PASS};SslMode=None;\"
  },
  \"beatmaps_path\": \"${BEATMAPS_DIR}\",
  \"allow_download\": false
}" > appsettings.json

dotnet osu.Server.DifficultyCalculator.dll all -m ${MODE} -c ${THREADS}