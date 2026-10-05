// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Thumbnails;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Files.Platform.Linux.Thumbnails
{
	/// <summary>
	/// Configuration for <see cref="LinuxThumbnailService"/>.
	/// </summary>
	public sealed class LinuxThumbnailOptions
	{
		/// <summary>
		/// Gets or sets the XDG cache home; thumbnails live in its <c>thumbnails</c> sub-directory.
		/// </summary>
		public string CacheHome { get; set; } = GetDefaultCacheHome();

		/// <summary>
		/// Gets or sets the maximum number of pixels (width times height) of a source image that will be decoded.
		/// </summary>
		public long MaxImagePixels { get; set; } = 100_000_000;

		/// <summary>
		/// Gets or sets the maximum size in bytes of a source file that will be used for generation.
		/// </summary>
		public long MaxSourceFileBytes { get; set; } = 512L * 1024 * 1024;

		/// <summary>
		/// Gets or sets the maximum size in bytes of a cache entry or external thumbnailer output that will be read.
		/// </summary>
		public long MaxCacheEntryBytes { get; set; } = 32L * 1024 * 1024;

		/// <summary>
		/// Gets or sets a value indicating whether external thumbnailers run inside a bubblewrap sandbox. Defaults to whether <c>bwrap</c> is on PATH.
		/// </summary>
		public bool SandboxExternalThumbnailers { get; set; } = BubblewrapSandbox.IsAvailable();

		/// <summary>
		/// Gets or sets a function that reports whether the sandbox (bwrap) can be used. Exists as a test seam.
		/// </summary>
		public Func<bool> IsSandboxAvailable { get; set; } = BubblewrapSandbox.IsAvailable;

		/// <summary>
		/// Gets or sets a value indicating whether external thumbnailers may run without a sandbox when it is disabled or unavailable. Defaults to <see langword="false"/>.
		/// </summary>
		public bool AllowUnsandboxedExternalThumbnailers { get; set; }

		/// <summary>
		/// Gets or sets the directory under which per-run private thumbnailer directories are created. Defaults to a folder in <c>$XDG_RUNTIME_DIR</c>, else in the cache home.
		/// </summary>
		public string? ThumbnailerTempRoot { get; set; }

		/// <summary>
		/// Gets or sets the maximum number of concurrent generations.
		/// </summary>
		public int MaxConcurrency { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

		/// <summary>
		/// Gets or sets the identifier of the failure directory (<c>fail/&lt;value&gt;</c>).
		/// </summary>
		public string FailureDirectoryName { get; set; } = "files-1.0";

		/// <summary>
		/// Gets or sets the directories searched for <c>*.thumbnailer</c> files, in priority order.
		/// </summary>
		public IList<string> ThumbnailerDirectories { get; set; } = GetDefaultThumbnailerDirectories();

		/// <summary>
		/// Gets or sets a function mapping a file path to its MIME type, used to pick an external thumbnailer. When <see langword="null"/> external thumbnailers are disabled.
		/// </summary>
		public Func<string, string?>? MimeTypeResolver { get; set; }

		/// <summary>
		/// Gets or sets the process runner used for external thumbnailers.
		/// </summary>
		public IThumbnailerProcessRunner ProcessRunner { get; set; } = new ThumbnailerProcessRunner();

		private static string GetDefaultCacheHome()
		{
			var env = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
			if (!string.IsNullOrEmpty(env) && Path.IsPathRooted(env))
				return env;

			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
		}

		private static List<string> GetDefaultThumbnailerDirectories()
		{
			var dirs = new List<string>();
			var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
			dirs.Add(Path.Combine(
				string.IsNullOrEmpty(dataHome) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share") : dataHome,
				"thumbnailers"));

			var dataDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
			if (string.IsNullOrEmpty(dataDirs))
				dataDirs = "/usr/local/share:/usr/share";

			foreach (var dir in dataDirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
				dirs.Add(Path.Combine(dir, "thumbnailers"));

			return dirs;
		}
	}

	/// <summary>
	/// Thumbnail service backed by the XDG Thumbnail Managing Standard cache.
	/// </summary>
	public sealed class LinuxThumbnailService : IThumbnailService, IDisposable
	{
		private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
		{
			".jpg", ".jpeg", ".jpe", ".png", ".webp", ".gif", ".bmp", ".ico", ".wbmp",
		};

		private readonly LinuxThumbnailOptions _options;
		private readonly string _thumbnailRoot;
		private readonly SemaphoreSlim _gate;
		private readonly Lazy<ThumbnailerRegistry> _registry;

		/// <summary>
		/// Initializes a new instance using the process environment.
		/// </summary>
		public LinuxThumbnailService() : this(new LinuxThumbnailOptions())
		{
		}

		/// <summary>
		/// Initializes a new instance with explicit options.
		/// </summary>
		public LinuxThumbnailService(LinuxThumbnailOptions options)
		{
			_options = options;
			_thumbnailRoot = Path.Combine(options.CacheHome, "thumbnails");
			_gate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrency));
			_registry = new Lazy<ThumbnailerRegistry>(() => ThumbnailerRegistry.Load(options.ThumbnailerDirectories));
		}

		/// <inheritdoc/>
		public async Task<byte[]?> GetThumbnailAsync(string path, uint requestedSize, ThumbnailOptions options = ThumbnailOptions.None, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.IsNullOrEmpty(path) || !Path.IsPathRooted(path))
				return null;

			var fullPath = Path.GetFullPath(path);
			FileInfo info;
			try
			{
				info = new FileInfo(fullPath);
				if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0)
					return null;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return null;
			}

			// Thumbnails are never made for files inside the thumbnail cache itself.
			if (fullPath.StartsWith(_thumbnailRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
				return null;

			var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
			var uri = XdgThumbnailNaming.ToFileUri(fullPath);
			var hash = XdgThumbnailNaming.GetHash(uri);
			var bucket = XdgThumbnailNaming.GetBucketName(requestedSize);
			var cachePath = Path.Combine(_thumbnailRoot, bucket, hash + ".png");
			var failPath = Path.Combine(_thumbnailRoot, "fail", _options.FailureDirectoryName, hash + ".png");

			if (!options.HasFlag(ThumbnailOptions.ForceRegenerate))
			{
				var cached = TryReadValid(cachePath, uri, mtime, info.Length);
				if (cached is not null)
					return cached;

				if (TryReadValid(failPath, uri, mtime, null) is not null)
					return null;
			}

			if (options.HasFlag(ThumbnailOptions.ReturnOnlyIfCached))
				return null;

			await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				var png = await Task.Run(() => GenerateAsync(fullPath, uri, bucket, cachePath, cancellationToken), cancellationToken).ConfigureAwait(false);
				if (png is null)
				{
					WriteFailureMarker(failPath, uri, mtime);
					return null;
				}

				var withText = PngTextChunks.Insert(png,
				[
					new("Thumb::URI", uri),
					new("Thumb::MTime", mtime.ToString(CultureInfo.InvariantCulture)),
					new("Thumb::Size", info.Length.ToString(CultureInfo.InvariantCulture)),
				]);
				WriteAtomically(cachePath, withText);
				return withText;
			}
			finally
			{
				_gate.Release();
			}
		}

		/// <inheritdoc/>
		public void Dispose() => _gate.Dispose();

		private byte[]? TryReadValid(string cacheFile, string uri, long mtime, long? size)
		{
			byte[] bytes;
			try
			{
				if (new FileInfo(cacheFile).Length > _options.MaxCacheEntryBytes)
					return null;

				bytes = File.ReadAllBytes(cacheFile);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
			{
				return null;
			}

			var text = PngTextChunks.Read(bytes);
			if (!text.TryGetValue("Thumb::URI", out var storedUri) || storedUri != uri)
				return null;

			if (!text.TryGetValue("Thumb::MTime", out var storedMtime) || storedMtime != mtime.ToString(CultureInfo.InvariantCulture))
				return null;

			if (size is not null && text.TryGetValue("Thumb::Size", out var storedSize) && storedSize != size.Value.ToString(CultureInfo.InvariantCulture))
				return null;

			return bytes;
		}

		private async Task<byte[]?> GenerateAsync(string fullPath, string uri, string bucket, string cachePath, CancellationToken cancellationToken)
		{
			var maxSize = XdgThumbnailNaming.GetBucketSize(bucket);
			if (ImageExtensions.Contains(Path.GetExtension(fullPath)))
				return GenerateImage(fullPath, maxSize);

			return await GenerateExternalAsync(fullPath, uri, maxSize, cachePath, cancellationToken).ConfigureAwait(false);
		}

		private byte[]? GenerateImage(string path, int maxSize)
		{
			try
			{
				if (new FileInfo(path).Length > _options.MaxSourceFileBytes)
					return null;

				using var codec = SKCodec.Create(path);
				if (codec is null)
					return null;

				// Check the declared dimensions before allocating anything (decompression bombs).
				var declared = codec.Info;
				if (declared.Width <= 0 || declared.Height <= 0 || (long)declared.Width * declared.Height > _options.MaxImagePixels)
					return null;

				var origin = codec.EncodedOrigin;
				var longest = Math.Max(declared.Width, declared.Height);
				var decodeSize = longest > maxSize ? codec.GetScaledDimensions((float)maxSize / longest) : declared.Size;
				if (decodeSize.Width <= 0 || decodeSize.Height <= 0 || decodeSize.Width > declared.Width || decodeSize.Height > declared.Height)
					decodeSize = declared.Size;

				var decodeInfo = new SKImageInfo(decodeSize.Width, decodeSize.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
				using var source = new SKBitmap(decodeInfo);
				if (codec.GetPixels(decodeInfo, source.GetPixels()) is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
					return null;

				var swap = origin >= SKEncodedOrigin.LeftTop;
				var width = swap ? source.Height : source.Width;
				var height = swap ? source.Width : source.Height;
				var scale = Math.Min(1f, Math.Min((float)maxSize / width, (float)maxSize / height));
				var targetW = Math.Max(1, (int)Math.Round(width * scale));
				var targetH = Math.Max(1, (int)Math.Round(height * scale));

				using var target = new SKBitmap(new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Premul));
				using (var canvas = new SKCanvas(target))
				{
					canvas.Clear(SKColors.Transparent);
					canvas.Scale((float)targetW / width, (float)targetH / height);
					canvas.Concat(GetOrientationMatrix(origin, source.Width, source.Height));
					using var image = SKImage.FromBitmap(source);
					canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
				}

				using var encoded = SKImage.FromBitmap(target).Encode(SKEncodedImageFormat.Png, 100);
				return encoded?.ToArray();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
			{
				return null;
			}
		}

		private static SKMatrix GetOrientationMatrix(SKEncodedOrigin origin, int w, int h) => origin switch
		{
			SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
			SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
			SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
			SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
			SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
			SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
			SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
			_ => SKMatrix.Identity,
		};

		private async Task<byte[]?> GenerateExternalAsync(string fullPath, string uri, int maxSize, string cachePath, CancellationToken cancellationToken)
		{
			if (new FileInfo(fullPath).Length > _options.MaxSourceFileBytes)
				return null;

			var mime = _options.MimeTypeResolver?.Invoke(fullPath);
			if (string.IsNullOrEmpty(mime))
				return null;

			var entry = _registry.Value.Find(mime);
			var pdfFallback = entry is null && mime == "application/pdf";
			if (pdfFallback)
				entry = new ThumbnailerEntry("pdftoppm -f 1 -singlefile -scale-to %s -png %i %o", "pdftoppm", [mime]);
			if (entry is null && mime.StartsWith("video/", StringComparison.Ordinal))
				entry = new ThumbnailerEntry("ffmpeg -nostdin -v error -protocol_whitelist file -t 10 -i file:%i -vf thumbnail,scale=%s:%s:force_original_aspect_ratio=decrease -frames:v 1 -update 1 -c:v png %o", "ffmpeg", [mime]);
			if (entry is null || (entry.TryExec is not null && !ExecutableExists(entry.TryExec)))
				return null;

			// Each run gets a private directory; the sandbox sees only this directory, never the shared cache.
			var tempDir = Path.Combine(GetThumbnailerTempRoot(), "thumb-" + Guid.NewGuid().ToString("N"));
			try
			{
				EnsureCacheDirectory(tempDir);
				var tempOutput = Path.Combine(tempDir, "out.png");
				var command = entry.BuildCommand(fullPath, uri, pdfFallback ? Path.Combine(tempDir, "out") : tempOutput, (uint)maxSize);
				if (command is null)
					return null;

				var (program, arguments) = command.Value;
				if (_options.SandboxExternalThumbnailers && _options.IsSandboxAvailable())
					(program, arguments) = BubblewrapSandbox.Wrap(program, arguments, tempDir, fullPath);
				else if (!_options.AllowUnsandboxedExternalThumbnailers)
					return null;

				if (!await _options.ProcessRunner.RunAsync(program, arguments, cancellationToken).ConfigureAwait(false))
					return null;

				return ValidateAndReencode(tempOutput, maxSize);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return null;
			}
			finally
			{
				try
				{
					// Recursive delete removes symlinks themselves and does not follow them.
					Directory.Delete(tempDir, true);
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
				{
				}
			}
		}

		private string GetThumbnailerTempRoot()
		{
			if (!string.IsNullOrEmpty(_options.ThumbnailerTempRoot))
				return _options.ThumbnailerTempRoot;

			var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
			return !string.IsNullOrEmpty(runtime) && Path.IsPathRooted(runtime) && Directory.Exists(runtime)
				? Path.Combine(runtime, "files-thumbnailer")
				: Path.Combine(_options.CacheHome, "files", "thumbnailer-tmp");
		}

		/// <summary>
		/// Accepts only a regular, size-capped PNG within the bucket dimensions and returns a freshly encoded copy.
		/// </summary>
		private byte[]? ValidateAndReencode(string outputPath, int maxSize)
		{
			var info = new FileInfo(outputPath);
			if (!info.Exists || info.LinkTarget is not null || (info.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
				return null;

			if (info.Length > _options.MaxCacheEntryBytes || info.Length < 33)
				return null;

			var bytes = File.ReadAllBytes(outputPath);
			if (!PngTextChunks.TryReadDimensions(bytes, out var width, out var height) || width <= 0 || height <= 0 || width > maxSize || height > maxSize)
				return null;

			using var bitmap = SKBitmap.Decode(bytes);
			if (bitmap is null || bitmap.Width != width || bitmap.Height != height)
				return null;

			using var image = SKImage.FromBitmap(bitmap);
			using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
			return encoded?.ToArray();
		}

		private static bool ExecutableExists(string tryExec)
		{
			if (Path.IsPathRooted(tryExec))
				return File.Exists(tryExec);

			foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries))
			{
				if (File.Exists(Path.Combine(dir, tryExec)))
					return true;
			}

			return false;
		}

		private void WriteFailureMarker(string failPath, string uri, long mtime)
		{
			// A 1x1 transparent PNG carrying only the validation keys.
			using var bitmap = new SKBitmap(1, 1);
			bitmap.Erase(SKColors.Transparent);
			using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
			var png = PngTextChunks.Insert(data.ToArray(),
			[
				new("Thumb::URI", uri),
				new("Thumb::MTime", mtime.ToString(CultureInfo.InvariantCulture)),
			]);
			WriteAtomically(failPath, png);
		}

		private static void EnsureCacheDirectory(string directory)
		{
			if (Directory.Exists(directory))
				return;

			if (OperatingSystem.IsWindows())
				Directory.CreateDirectory(directory);
			else
				Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}

		private static void WriteAtomically(string destination, byte[] content)
		{
			var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try
			{
				EnsureCacheDirectory(Path.GetDirectoryName(destination)!);
				var streamOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
				if (!OperatingSystem.IsWindows())
					streamOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

				using (var stream = new FileStream(temp, streamOptions))
				{
					stream.Write(content);
				}

				File.Move(temp, destination, overwrite: true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				TryDelete(temp);
			}
		}

		private static void TryDelete(string path)
		{
			try
			{
				File.Delete(path);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
		}
	}
}
