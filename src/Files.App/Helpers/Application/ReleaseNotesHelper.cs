// Copyright (c) Files Community
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Reflection;

namespace Files.App.Helpers
{
	/// <summary>
	/// Fetches the LinuxFiles GitHub release notes (Markdown) for the running version.
	/// </summary>
	public static class ReleaseNotesHelper
	{
		private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

		private const int MaxResponseBytes = 1024 * 1024;

		private static readonly SemaphoreSlim fetchLock = new(1, 1);

		private static string? cachedMarkdown;

		/// <summary>
		/// Gets the running version as written in &lt;Version&gt;, for example 0.1.0-alpha8.
		/// </summary>
		public static string AppVersionName { get; } = GetAppVersionName();

		private static string GetAppVersionName()
		{
			var assembly = typeof(ReleaseNotesHelper).Assembly;
			var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
				?? assembly.GetName().Version?.ToString(3)
				?? string.Empty;

			// Drop build metadata ("+<commit>") in case a build adds it.
			var metadataStart = version.IndexOf('+');
			return metadataStart >= 0 ? version[..metadataStart] : version;
		}

		/// <summary>
		/// Gets the release body for the running version, or null when it is unavailable (offline, not found, rate limited or empty).
		/// A successful result is cached for the session; failures are retried on the next call.
		/// </summary>
		public static async Task<string?> GetLinuxReleaseNotesAsync(CancellationToken cancellationToken = default)
		{
			if (cachedMarkdown is not null)
				return cachedMarkdown;

			await fetchLock.WaitAsync(cancellationToken);
			try
			{
				if (cachedMarkdown is not null)
					return cachedMarkdown;

				using var client = new HttpClient() { Timeout = RequestTimeout, MaxResponseContentBufferSize = MaxResponseBytes };
				using var request = new HttpRequestMessage(HttpMethod.Get, Constants.ExternalUrl.LinuxReleaseApiUrl);
				if (!request.Headers.UserAgent.TryParseAdd($"LinuxFiles/{AppVersionName}"))
					request.Headers.UserAgent.ParseAdd("LinuxFiles");
				request.Headers.Accept.ParseAdd("application/vnd.github+json");
				request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

				using var response = await client.SendAsync(request, cancellationToken);
				if (!response.IsSuccessStatusCode)
				{
					App.Logger.LogInformation("Release notes for {Tag} are unavailable: HTTP {StatusCode}", Constants.ExternalUrl.LinuxReleaseTag, (int)response.StatusCode);
					return null;
				}

				var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
				using var document = JsonDocument.Parse(content);
				if (document.RootElement.ValueKind is JsonValueKind.Object &&
					document.RootElement.TryGetProperty("body", out var body) &&
					body.ValueKind is JsonValueKind.String &&
					body.GetString() is { } markdown &&
					!string.IsNullOrWhiteSpace(markdown))
					cachedMarkdown = markdown;

				return cachedMarkdown;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				App.Logger.LogInformation(ex, "Failed to fetch the release notes for {Tag}", Constants.ExternalUrl.LinuxReleaseTag);
				return null;
			}
			finally
			{
				fetchLock.Release();
			}
		}
	}
}
