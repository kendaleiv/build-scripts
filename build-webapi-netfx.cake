#addin "Cake.FileHelpers"
#tool "nuget:?package=xunit.runner.console"

var target = Argument("target", "Default");
var version = FileReadText("./version.txt").Trim();

var packageVersion = version;
if (!AppVeyor.IsRunningOnAppVeyor)
{
    packageVersion += "-dev";
}
else if (AppVeyor.Environment.Repository.Branch != "master")
{
    packageVersion += "-alpha" + AppVeyor.Environment.Build.Number;
}

string configuration;
switch (AppVeyor.Environment.Repository.Branch)
{
    case "master":
        configuration = "Release";
        break;
    case "development":
        configuration = "QA";
        break;
    default:
        configuration = "Release";
        break;
}

var artifactsDir = Directory("./artifacts");

// Assume a single solution per repository
var solution = GetFiles("./**/*.sln").First();

Task("Clean")
    .Does(() =>
    {
        CleanDirectory(artifactsDir);
    });

Task("Restore-NuGet-Packages")
    .IsDependentOn("Clean")
    .Does(() =>
    {
        NuGetRestore(solution);
    });

Task("Version")
    .Does(() =>
    {
        foreach (var assemblyInfo in GetFiles("./src/**/AssemblyInfo.cs"))
        {
            CreateAssemblyInfo(
                assemblyInfo.ChangeExtension(".Generated.cs"),
                new AssemblyInfoSettings
                {
                    Version = version,
                    InformationalVersion = packageVersion
                });
        }
    });

Task("Build")
    .IsDependentOn("Restore-NuGet-Packages")
    .IsDependentOn("Version")
    .Does(() =>
    {
        MSBuild(solution, settings =>
            settings.SetConfiguration(configuration)
                .WithProperty("TreatWarningsAsErrors", "True")
                .WithProperty("DeployOnBuild", "True")
                .SetVerbosity(Verbosity.Minimal)
                .AddFileLogger());
    });

Task("Run-Tests")
    .IsDependentOn("Build")
    .Does(() =>
    {
        XUnit2("./tests/**/bin/" + configuration + "/*.Tests.dll");
    });

Task("Package")
    .IsDependentOn("Run-Tests")
    .Does(() =>
    {
        var hostProjectName = GetFiles("./src/**/*.csproj")
            .Single(x => x.GetFilename().FullPath.ToLowerInvariant().Contains("api")
                      || x.GetFilename().FullPath.ToLowerInvariant().Contains("host"))
            .GetFilenameWithoutExtension();

        Zip(
            "./src/" + hostProjectName + "/obj/" + configuration + "/Package/PackageTmp",
            "./artifacts/" + hostProjectName + ".zip"
        );

        var clientProjectPath = GetFiles("./src/**/*.csproj")
            .Single(x => x.GetFilename().FullPath.ToLowerInvariant().Contains("client"))
            .ToString();

        NuGetPack(clientProjectPath, new NuGetPackSettings
        {
            OutputDirectory = artifactsDir,
            Version = packageVersion,
            Properties = new Dictionary<string, string>
            {
                { "Configuration", configuration }
            }
        });
    });

Task("Default")
    .IsDependentOn("Package");

RunTarget(target);
