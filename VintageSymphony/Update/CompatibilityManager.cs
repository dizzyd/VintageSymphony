using System.Text.Json;
using Vintagestory.API.Common;

namespace VintageSymphony.Update;

public class CompatibilityManager
{
	private readonly Dictionary<string, Version> compatibleVersions = new();
	private readonly ILogger logger;

	public CompatibilityManager(ILogger logger)
	{
		this.logger = logger;
	}

	public void LoadCompatibilityData(string filePath)
	{
		try
		{
			if (!File.Exists(filePath))
			{
				logger.Warning($"Compatibility file not found at {filePath}");
				return;
			}

			string jsonContent = File.ReadAllText(filePath);
			using JsonDocument doc = JsonDocument.Parse(jsonContent);
			
			if (!doc.RootElement.TryGetProperty("assetCompatibility", out JsonElement assetCompatibility))
			{
				logger.Warning("No assetCompatibility section found in compatibility.json");
				return;
			}

			foreach (JsonProperty modProperty in assetCompatibility.EnumerateObject())
			{
				string modId = modProperty.Name;
				if (modProperty.Value.TryGetProperty("latestCompatibleVersion", out JsonElement versionElement))
				{
					string versionString = versionElement.GetString();
					if (!string.IsNullOrEmpty(versionString) && Version.TryParse(versionString, out Version version))
					{
						compatibleVersions[modId] = version;
						logger.Debug($"Loaded compatibility constraint for {modId}: {version}");
					}
					else
					{
						logger.Warning($"Invalid version format for {modId}: {versionString}");
					}
				}
			}
		}
		catch (Exception ex)
		{
			logger.Error($"Failed to load compatibility data: {ex.Message}");
		}
	}

	public bool IsVersionCompatible(string modId, Version version)
	{
		if (!compatibleVersions.TryGetValue(modId, out Version compatibleVersion))
		{
			// If no compatibility constraint is defined, allow any version
			return true;
		}

		// Compatible if major and minor versions match, patch can differ
		return version.Major == compatibleVersion.Major && 
		       version.Minor == compatibleVersion.Minor;
	}

	public Version? GetMaxCompatibleVersion(string modId)
	{
		return compatibleVersions.TryGetValue(modId, out Version version) ? version : null;
	}

	public IEnumerable<Release> FilterCompatibleReleases(string modId, IEnumerable<Release> releases)
	{
		return releases.Where(release => IsVersionCompatible(modId, release.Version));
	}
}