using Newtonsoft.Json.Linq;

namespace VintageSymphony.Update;

public class GitHubReleaseFetcher
{
	private static readonly HttpClient SharedClient = new();
	private readonly HttpClient httpClient;

	/// <param name="httpClient">
	/// Left out in normal use; supplied when the caller has a client that can reach
	/// somewhere the default one cannot.
	/// </param>
	public GitHubReleaseFetcher(HttpClient? httpClient = null)
	{
		this.httpClient = httpClient ?? SharedClient;
	}

	
	public async Task<IEnumerable<Release>> GetAllReleasesAsync(string apiUrl)
	{
		try
		{
			if (!httpClient.DefaultRequestHeaders.Contains("User-Agent"))
			{
				httpClient.DefaultRequestHeaders.Add("User-Agent", "VintageSymphony");
			}

			HttpResponseMessage response = await httpClient.GetAsync(apiUrl);
			response.EnsureSuccessStatusCode();
			string content = await response.Content.ReadAsStringAsync();
			JArray releases = JArray.Parse(content);

			// Return all valid releases
			return releases
				.Select(GetRelease)
				.Where(release => release != null)
				.Cast<Release>()
				.OrderByDescending(release => release.Version);
		}
		catch (Exception ex)
		{
			// Handle exceptions (e.g., network errors, JSON parsing errors)
			Console.WriteLine($"An error occurred: {ex.Message}");
			return Enumerable.Empty<Release>();
		}
	}

	private Release? GetRelease(JToken obj)
	{
		var tagName = obj["tag_name"]!.ToString();
		if (tagName.StartsWith("v"))
		{
			tagName = tagName.Substring(1);
		}

		if (obj["assets"] is not JArray assets || assets.Count == 0)
		{
			return null;
		}

		try
		{
			return new Release(new Version(tagName),
				assets[0]["browser_download_url"]!.ToString(),
				assets[0]["name"]!.ToString());
		}
		catch
		{
			return null;
		}

	}
}