#addin "nuget:?package=CodeFileSanity&version=0.0.36"
var nVikaToolPath = GetFiles("./tools/NVika.MSBuild.*/tools/NVika.exe").First();

///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target = Argument("target", "Build");
var configuration = Argument("configuration", "Release");

var rootDirectory = new DirectoryPath("..");
var tempDirectory = new DirectoryPath("temp");
var solution = rootDirectory.CombineWithFilePath("osu.Server.sln");

///////////////////////////////////////////////////////////////////////////////
// TASKS
///////////////////////////////////////////////////////////////////////////////

Task("Compile")
    .Does(() => {
        DotNetCoreBuild(solution.FullPath, new DotNetCoreBuildSettings {
            Configuration = configuration,
        });
    });

// windows only because both inspectcore and nvika depend on net45
Task("InspectCode")
    .IsDependentOn("Compile")
    .Does(() => {
        var inspectcodereport = tempDirectory.CombineWithFilePath("inspectcodereport.xml");
        var cacheDir = tempDirectory.Combine("inspectcode");

        DotNetCoreTool(rootDirectory.FullPath,
            "jb", $@"inspectcode ""{solution}"" --output=""{inspectcodereport}"" --caches-home=""{cacheDir}"" --verbosity=WARN");
        DotNetCoreTool(rootDirectory.FullPath, "nvika", $@"parsereport ""{inspectcodereport}"" --treatwarningsaserrors");
    });

Task("CodeFileSanity")
    .Does(() => {
        ValidateCodeSanity(new ValidateCodeSanitySettings {
            RootDirectory = rootDirectory.FullPath,
            IsAppveyorBuild = AppVeyor.IsRunningOnAppVeyor
        });
    });

Task("Build")
    .IsDependentOn("CodeFileSanity")
    .IsDependentOn("InspectCode");

RunTarget(target);
