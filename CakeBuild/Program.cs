using System.Reflection;
using System.Runtime.Versioning;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Path = System.IO.Path;

namespace CakeBuild;

public static class Program
{
	public static string SolutionDirectory = null!;

	public static int Main(string[] args)
	{
		SolutionDirectory = FindSolutionDirectory();
		return new CakeHost()
			.UseContext<BuildContext>()
			.Run(args);
	}

	static string FindSolutionDirectory()
	{
		string? directory = Directory.GetCurrentDirectory();

		while (directory != null)
		{
			string[] solutionFiles = Directory.GetFiles(directory, "*.sln");
			if (solutionFiles.Length > 0)
			{
				return directory;
			}

			directory = Directory.GetParent(directory)?.FullName;
		}

		throw new Exception("Failed to find solution file");
	}
}

public class BuildContext : FrostingContext
{
	public const string ProjectName = "VintageSymphony";
	public string BuildConfiguration { get; set; }
	public string ModVersion { get; }
	public string ModName { get; }
	public string ModAssetsVersion { get; }
	public string ModAssetsName { get; }
	public bool SkipJsonValidation { get; set; }
	public string TargetFramework { get; set; }


	public BuildContext(ICakeContext context)
		: base(context)
	{
		BuildConfiguration = context.Argument("configuration", "Release");
		SkipJsonValidation = context.Argument("skipJsonValidation", false);
		TargetFramework = context.Argument("framework", DetectTargetFramework());

		var modInfoPath = Path.Combine(Program.SolutionDirectory, ProjectName, "modinfo.json");
		var modInfo = context.DeserializeJsonFromFile<ModInfo>(modInfoPath);
		ModVersion = modInfo.Version;
		ModName = modInfo.ModID;

		var modAssetsInfoPath = Path.Combine(Program.SolutionDirectory, "Assets", "modinfo.json");
		var modAssetsInfo = context.DeserializeJsonFromFile<ModInfo>(modAssetsInfoPath);
		ModAssetsVersion = modAssetsInfo.Version;
		ModAssetsName = modAssetsInfo.ModID;
	}
	
	public string AdjustVersionForFilename(string version)
	{
		if (TargetFramework == "net7.0")
		{
			return version + "-vs120";
		}
		return version;
	}
	
	private string DetectTargetFramework()
	{
		try
		{
			// Get the Vintage Story API path from environment variable
			var vsPath = Environment.GetEnvironmentVariable("VINTAGE_STORY");
			if (string.IsNullOrEmpty(vsPath))
			{
				Console.WriteLine("VINTAGE_STORY environment variable not set. Defaulting to net8.0");
				return "net8.0";
			}
            
			// Path to the main API DLL
			var apiDllPath = Path.Combine(vsPath, "VintagestoryAPI.dll");
			if (!File.Exists(apiDllPath))
			{
				Console.WriteLine($"VintagestoryAPI.dll not found at {apiDllPath}. Defaulting to net8.0");
				return "net8.0";
			}
            
			// Load the assembly and check its target framework
			var assembly = Assembly.LoadFile(apiDllPath);
			var targetFrameworkAttribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            
			if (targetFrameworkAttribute != null)
			{
				var frameworkName = targetFrameworkAttribute.FrameworkName;
				Console.WriteLine($"Detected framework: {frameworkName}");
                
				if (frameworkName.Contains(".NETCoreApp,Version=v7."))
					return "net7.0";
				else if (frameworkName.Contains(".NETCoreApp,Version=v8."))
					return "net8.0";
			}
            
			// Fallback to checking assembly references for .NET version indicators
			return "net8.0"; // Default to the latest if can't determine
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error detecting framework: {ex.Message}");
			return "net8.0"; // Default to the latest if detection fails
		}
	}

}

[TaskName("ValidateJson")]
public sealed class ValidateJsonTask : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
	{
		if (context.SkipJsonValidation)
		{
			return;
		}

		var jsonFiles = context.GetFiles(Path.Combine(Program.SolutionDirectory, BuildContext.ProjectName, "assets",
			"**", "*.json"));
		foreach (var file in jsonFiles)
		{
			try
			{
				var json = File.ReadAllText(file.FullPath);
				JToken.Parse(json);
			}
			catch (JsonException ex)
			{
				throw new Exception(
					$"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}", ex);
			}
		}
	}
}

[TaskName("Build")]
[IsDependentOn(typeof(ValidateJsonTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
	{
		var projectFile = Path.Combine(Program.SolutionDirectory, BuildContext.ProjectName,
			$"{BuildContext.ProjectName}.csproj");
		context.DotNetClean(projectFile,
			new DotNetCleanSettings
			{
				Configuration = context.BuildConfiguration
			});


		context.DotNetPublish(projectFile,
			new DotNetPublishSettings
			{
				Configuration = context.BuildConfiguration,
				Framework = context.TargetFramework
			});
	}
}

[TaskName("PackageMod")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PackageModTask : FrostingTask<BuildContext>
{
	public override void Run(BuildContext context)
	{
		var projectDir = Path.Combine(Program.SolutionDirectory, BuildContext.ProjectName);

		var releasePath = $"{Program.SolutionDirectory}/Releases";
		context.EnsureDirectoryExists(releasePath);

		context.EnsureDirectoryExists("../Releases");
		context.CleanDirectory("../Releases");

		var modBuildDir = $"../Releases/{context.ModName}";
		PackageModArchive(context, modBuildDir, projectDir, releasePath);

		var assetsBuildDir = $"../Releases/{context.ModAssetsName}";
		PackageModAssetsArchive(context, assetsBuildDir, releasePath);


		context.DeleteDirectory(modBuildDir, new DeleteDirectorySettings { Recursive = true });
	}

	private static void PackageModAssetsArchive(BuildContext context, string assetsBuildDir, string releasePath)
	{
		var musicAssetsPath = $"{assetsBuildDir}/assets/{context.ModName}/music/";
		context.EnsureDirectoryExists(musicAssetsPath);

		context.CopyFiles($"{Program.SolutionDirectory}/Assets/music/*.ogg", musicAssetsPath);
		context.CopyFiles($"{Program.SolutionDirectory}/Assets/music/musicconfig.json", musicAssetsPath);
		context.CopyFiles($"{Program.SolutionDirectory}/Assets/music/musicconfig-readme.txt", musicAssetsPath);
		context.CopyFile($"{Program.SolutionDirectory}/Assets/modinfo.json", $"{assetsBuildDir}/modinfo.json");
		context.CopyFile($"{Program.SolutionDirectory}/Assets/attributions.txt", $"{assetsBuildDir}/attributions.txt");
		context.Zip(assetsBuildDir, $"{releasePath}/{context.ModAssetsName}_{context.ModAssetsVersion}.zip");
	}

	private static void PackageModArchive(BuildContext context, string buildDir, string projectDir, string releasePath)
	{
		// Copy mod DLL
		context.EnsureDirectoryExists(buildDir);
		context.CopyFiles($"{projectDir}/bin/{context.BuildConfiguration}/{BuildContext.ProjectName}.dll", buildDir);

		// Copy mod debug symbols
		if (context.BuildConfiguration == "Debug")
		{
			context.CopyFiles($"{projectDir}/bin/{context.BuildConfiguration}/{BuildContext.ProjectName}.pdb",
				buildDir);
		}

		// copy modinfo.json
		context.CopyFile($"{projectDir}/modinfo.json", $"{buildDir}/modinfo.json");
		context.EnsureDirectoryExists($"{buildDir}/assets/vintagesymphony/config/");
		context.CopyFile($"{projectDir}/compatibility.json", $"{buildDir}/assets/vintagesymphony/config/compatibility.json");

		// package mod
		context.Zip(buildDir, $"{releasePath}/{context.ModName}_{context.AdjustVersionForFilename(context.ModVersion)}.zip");
	}
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageModTask))]
public class DefaultTask : FrostingTask
{
}