using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Core.Diagnostics;
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
	public string? ModAssetsVersion { get; }
	public string? ModAssetsName { get; }

	/// <summary>
	/// The music is a submodule, and a clone without it can still build the code archive
	/// - which is the half that ships from here anyway.
	/// </summary>
	public bool HasAssets => ModAssetsName != null;
	public bool SkipJsonValidation { get; set; }
	public string TargetFramework { get; set; }
	private const string DefaultTargetFramework = "net10.0";


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
		if (File.Exists(modAssetsInfoPath))
		{
			var modAssetsInfo = context.DeserializeJsonFromFile<ModInfo>(modAssetsInfoPath);
			ModAssetsVersion = modAssetsInfo.Version;
			ModAssetsName = modAssetsInfo.ModID;
		}
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
				Console.WriteLine($"VINTAGE_STORY environment variable not set. Defaulting to {DefaultTargetFramework}");
				return DefaultTargetFramework;
			}
            
			// Path to the main API DLL
			var apiDllPath = Path.Combine(vsPath, "VintagestoryAPI.dll");
			if (!File.Exists(apiDllPath))
			{
				Console.WriteLine($"VintagestoryAPI.dll not found at {apiDllPath}. Defaulting to {DefaultTargetFramework}");
				return DefaultTargetFramework;
			}
            
			// Load the assembly and check its target framework
			var assembly = Assembly.LoadFile(apiDllPath);
			var targetFrameworkAttribute = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            
			if (targetFrameworkAttribute != null)
			{
				var frameworkName = targetFrameworkAttribute.FrameworkName;
				Console.WriteLine($"Detected framework: {frameworkName}");

				var match = Regex.Match(frameworkName, @"^\.NETCoreApp,Version=v(\d+)\.(\d+)$");
				if (match.Success)
				{
					return $"net{match.Groups[1].Value}.{match.Groups[2].Value}";
				}
			}

			// Fallback if the attribute is missing or unparseable
			return DefaultTargetFramework;
		}
		catch (Exception ex)
		{
			Console.WriteLine($"Error detecting framework: {ex.Message}");
			return DefaultTargetFramework; // Default to the latest if detection fails
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

		// Staging goes under the solution's own Releases directory. It used to be
		// "../Releases", which is a sibling of the repository - the build both created
		// and cleaned a directory outside the tree it was building.
		var releasePath = $"{Program.SolutionDirectory}/Releases";
		context.EnsureDirectoryExists(releasePath);
		context.CleanDirectory(releasePath);

		var modBuildDir = $"{releasePath}/{context.ModName}";
		PackageModArchive(context, modBuildDir, projectDir, releasePath);
		context.DeleteDirectory(modBuildDir, new DeleteDirectorySettings { Recursive = true });

		if (!context.HasAssets)
		{
			context.Log.Information(
				"Assets submodule is not checked out - packaged the code archive only. " +
				"The music is distributed separately and the mod downloads it at runtime.");
			return;
		}

		var assetsBuildDir = $"{releasePath}/{context.ModAssetsName}";
		PackageModAssetsArchive(context, assetsBuildDir, releasePath);
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
		// package mod
		context.Zip(buildDir, $"{releasePath}/{context.ModName}_{context.AdjustVersionForFilename(context.ModVersion)}.zip");
	}
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageModTask))]
public class DefaultTask : FrostingTask
{
}