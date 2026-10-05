// Copyright (c) Files Community
// Licensed under the MIT License.

#if !WINDOWS
using Files.Platform.Abstractions.Archives;
using System.IO;

namespace Files.App.Data.Models
{
	/// <summary>
	/// Provides an archive creation support.
	/// </summary>
	public sealed class CompressArchiveModel : ICompressArchiveModel
	{
		private StatusCenterItemProgressModel? _fileSystemProgress;

		private IProgress<StatusCenterItemProgressModel>? _Progress;
		public IProgress<StatusCenterItemProgressModel>? Progress
		{
			get => _Progress;
			set
			{
				_Progress = value;
				_fileSystemProgress = new(
					value,
					false,
					FileSystemStatusCode.InProgress);

				_fileSystemProgress.Report(0);
			}
		}

		/// <inheritdoc/>
		public string ArchivePath { get; set; }

		/// <inheritdoc/>
		public string Directory { get; init; }

		/// <inheritdoc/>
		public string FileName { get; init; }

		/// <inheritdoc/>
		public string Password { get; init; }

		/// <inheritdoc/>
		public IEnumerable<string> Sources { get; init; }

		/// <inheritdoc/>
		public ArchiveFormats FileFormat { get; init; }

		/// <inheritdoc/>
		public ArchiveCompressionLevels CompressionLevel { get; init; }

		/// <inheritdoc/>
		public ArchiveSplittingSizes SplittingSize { get; init; }

		/// <inheritdoc/>
		public ArchiveDictionarySizes DictionarySize { get; init; }

		/// <inheritdoc/>
		public ArchiveWordSizes WordSize { get; init; }

		/// <inheritdoc/>
		public CancellationToken CancellationToken { get; set; }

		/// <inheritdoc/>
		public bool IsCancelled { get; private set; }

		/// <inheritdoc/>
		public int CPUThreads { get; set; }

		public CompressArchiveModel(
			string[] source,
			string directory,
			string fileName,
			int cpuThreads,
			string? password = null,
			ArchiveFormats fileFormat = ArchiveFormats.Zip,
			ArchiveCompressionLevels compressionLevel = ArchiveCompressionLevels.Normal,
			ArchiveSplittingSizes splittingSize = ArchiveSplittingSizes.None,
			ArchiveDictionarySizes dictionarySize = ArchiveDictionarySizes.Auto,
			ArchiveWordSizes wordSize = ArchiveWordSizes.Auto)
		{
			_Progress = new Progress<StatusCenterItemProgressModel>();

			Sources = source;
			Directory = directory;
			FileName = fileName;
			Password = password ?? string.Empty;
			ArchivePath = string.Empty;
			FileFormat = fileFormat;
			CompressionLevel = compressionLevel;
			SplittingSize = splittingSize;
			DictionarySize = dictionarySize;
			WordSize = wordSize;
			CPUThreads = cpuThreads;
		}

		/// <inheritdoc/>
		public string GetArchivePath(string suffix = "")
		{
			return Path.Combine(Directory, $"{FileName}{suffix}{ArchiveExtension}");
		}

		public async Task<bool> RunCreationAsync()
		{
			// LINUX-TODO(archives): encrypted creation, split volumes and custom 7z tuning need backend support.
			if (!string.IsNullOrEmpty(Password) || SplittingSize != ArchiveSplittingSizes.None || DictionarySize != ArchiveDictionarySizes.Auto || WordSize != ArchiveWordSizes.Auto)
				throw new NotSupportedException();
			var service = Ioc.Default.GetRequiredService<IArchiveService>();
			var format = FileFormat switch
			{
				ArchiveFormats.Zip => ArchiveFormat.Zip,
				ArchiveFormats.SevenZip => ArchiveFormat.SevenZip,
				ArchiveFormats.Tar => ArchiveFormat.Tar,
				ArchiveFormats.GZip => ArchiveFormat.TarGz,
				_ => throw new NotSupportedException(),
			};
			if (string.IsNullOrEmpty(ArchivePath))
				ArchivePath = GetArchivePath();
			var result = await service.CreateAsync(Sources.ToArray(), ArchivePath, new ArchiveCreateOptions
			{
				Format = format,
				Progress = new Progress<ArchiveProgress>(progress =>
				{
					if (_fileSystemProgress is null)
						return;
					_fileSystemProgress.FileName = Files.Shared.Helpers.ArchiveDisplayName.Escape(progress.CurrentEntry ?? string.Empty);
					_fileSystemProgress.ItemsCount = progress.EntriesTotal;
					_fileSystemProgress.AddProcessedItemsCount(Math.Max(0, progress.EntriesProcessed - _fileSystemProgress.ProcessedItemsCount));
					_fileSystemProgress.TotalSize = progress.BytesTotal;
					_fileSystemProgress.SetProcessedSize(progress.BytesProcessed);
					_fileSystemProgress.EnumerationCompleted = true;
					_fileSystemProgress.Report();
				}),
				Level = CompressionLevel switch
				{
					ArchiveCompressionLevels.None => ArchiveCompressionLevel.None,
					ArchiveCompressionLevels.Fast or ArchiveCompressionLevels.Low => ArchiveCompressionLevel.Fast,
					ArchiveCompressionLevels.High => ArchiveCompressionLevel.High,
					ArchiveCompressionLevels.Ultra => ArchiveCompressionLevel.Ultra,
					_ => ArchiveCompressionLevel.Normal,
				},
			}, CancellationToken);
			IsCancelled = result.Cancelled;
			if (result.Succeeded)
				_fileSystemProgress?.Report(100);
			return result.Succeeded;
		}

		private string ArchiveExtension => FileFormat switch
		{
			ArchiveFormats.Zip => ".zip",
			ArchiveFormats.SevenZip => ".7z",
			ArchiveFormats.Tar => ".tar",
			ArchiveFormats.GZip => ".tar.gz",
			_ => throw new NotSupportedException(),
		};
	}
}

#endif
