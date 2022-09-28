FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
WORKDIR /app

# Handle project files and dependencies first for caching benefits
COPY *.sln ./
COPY osu.Server.DifficultyCalculator/osu.Server.DifficultyCalculator.csproj ./osu.Server.DifficultyCalculator/
COPY osu.Server.Queues.BeatmapProcessor/osu.Server.Queues.BeatmapProcessor.csproj ./osu.Server.Queues.BeatmapProcessor/

RUN dotnet restore

# Copy everything else and build
COPY . ./
WORKDIR /app/osu.Server.DifficultyCalculator
RUN dotnet publish -c Release -o out
# get rid of bloat
RUN rm -rf ./out/runtimes ./out/osu.Game.Resources.dll ./out/osuTK.dll

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:6.0
WORKDIR /app
COPY --from=build-env /app/osu.Server.DifficultyCalculator/out .
ENTRYPOINT ["dotnet", "osu.Server.DifficultyCalculator.dll"]
