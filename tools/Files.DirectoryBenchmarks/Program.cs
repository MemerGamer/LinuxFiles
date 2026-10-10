// Copyright (c) Files Community
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Files.Platform.Abstractions.Enumeration;
using Files.Platform.Linux.Enumeration;
using Files.Platform.Linux.Native;
using Microsoft.Win32.SafeHandles;
using Entry = Files.Platform.Linux.Native.DirectoryBenchmarkNative.Entry;

[assembly: SupportedOSPlatform("linux")]

namespace Files.DirectoryBenchmarks
{
	internal static class Program
	{
		private sealed record Sample(double WallMs, long AllocatedBytes, int Gen0, int Gen1, int Gen2);
		private sealed record Candidate(string Name, Func<string, Task<object>> Run);
		private static long s_sink;

		private static async Task<int> Main(string[] args)
		{
			try
			{
				if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
					throw new PlatformNotSupportedException("Linux x64/arm64 required.");
				var diskParent = Option(args, "--disk-parent") ?? throw new ArgumentException("Supply --disk-parent pointing to a normal disk directory.");
				var tmpfsParent = Option(args, "--tmpfs-parent") ?? "/tmp";
				var runs = int.Parse(Option(args, "--runs") ?? "12", CultureInfo.InvariantCulture);
				if (runs < 10) throw new ArgumentException("At least ten measured runs are required.");
				var rust = !args.Contains("--skip-rust", StringComparer.Ordinal);
				if (rust) DirectoryBenchmarkNative.VerifyLayout();
				SelfCheck(diskParent, rust);
				Console.WriteLine($"# runtime={RuntimeInformation.FrameworkDescription}; os={RuntimeInformation.OSDescription}; arch={RuntimeInformation.ProcessArchitecture}; serverGC={System.Runtime.GCSettings.IsServerGC}; runs={runs}; warmups=3; batch=65536; rust={rust}");
				Console.WriteLine("storage,files,workload,candidate,run,wall_ms,allocated_bytes,gen0,gen1,gen2");
				foreach (var (storage, parent) in new[] { ("tmpfs", tmpfsParent), ("disk", diskParent) })
				{
					foreach (var count in new[] { 10000, 100000 })
					{
						Console.Error.WriteLine($"Generating {storage} fixture: {count} files under {parent}");
						using var fixture = new Fixture(parent, count);
						var current = DirectoryBenchmarkNative.Current(fixture.Path);
						Compare(current, DirectoryBenchmarkNative.Optimized(fixture.Path), count);
						if (rust) Compare(current, DirectoryBenchmarkNative.Rust(fixture.Path), count);
						await VerifyService(fixture.Path, current).ConfigureAwait(false);
						foreach (var workload in new[] { "scan", "scan-materialize-sort-proxy" })
						{
							var candidates = new List<Candidate>
							{
								NativeCandidate("current-posix", DirectoryBenchmarkNative.Current, workload),
								NativeCandidate("optimized-csharp", DirectoryBenchmarkNative.Optimized, workload),
							};
							if (rust) candidates.Add(NativeCandidate("rust-batch", DirectoryBenchmarkNative.Rust, workload));
							await Measure(storage, count, workload, fixture.Path, candidates, runs).ConfigureAwait(false);
						}
						await Measure(storage, count, "production-enumerator-controls", fixture.Path,
							new() { new("current-dotnet-service", path => Service(path, false)),
								new("current-dotnet-service-with-mode", path => Service(path, true)) }, runs).ConfigureAwait(false);
					}
				}
				Console.Error.WriteLine($"Completed; checksum={s_sink}. Fixtures removed.");
				return 0;
			}
			catch (Exception ex)
			{
				Console.Error.WriteLine(ex);
				return 1;
			}
		}

		private static string? Option(string[] args, string key)
		{
			var index = Array.IndexOf(args, key);
			return index < 0 ? null : index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing {key} value.");
		}

		private static Candidate NativeCandidate(string name, Func<string, List<Entry>> scan, string workload)
			=> new(name, path =>
			{
				var entries = scan(path);
				object result = workload == "scan" ? entries : MaterializeAndSort(path, entries);
				return Task.FromResult(result);
			});

		private static List<FileSystemEntryInfo> MaterializeAndSort(string path, List<Entry> entries)
		{
			var items = new List<FileSystemEntryInfo>();
			foreach (var entry in entries)
			{
				var stat = entry.Stat;
				var hidden = entry.Name.StartsWith('.');
				var readOnly = (stat.Mode & 0x80) == 0;
				var attributes = (hidden ? FileAttributes.Hidden : 0) | (readOnly ? FileAttributes.ReadOnly : 0)
					| (stat.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal);
				items.Add(new(entry.Name, Path.Combine(path, entry.Name), stat.IsDirectory, stat.IsSymbolicLink,
					false, null, hidden, readOnly, stat.IsDirectory ? 0 : checked((long)stat.Size),
					DateTime.UnixEpoch, stat.ModifiedUtc, DateTime.UnixEpoch, attributes, (UnixFileMode)(stat.Mode & 0xFFF)));
			}
			items.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
			return items;
		}

		private static async Task<object> Service(string path, bool includeUnixMode)
		{
			var entries = new List<FileSystemEntryInfo>();
			await foreach (var batch in new LinuxFileSystemEnumerator().EnumerateBatchesAsync(path,
				new() { IncludeHidden = true, IncludeUnixMode = includeUnixMode }).ConfigureAwait(false))
				entries.AddRange(batch);
			return entries;
		}

		private static async Task Measure(string storage, int count, string workload, string path, List<Candidate> candidates, int runs)
		{
			for (var warmup = 0; warmup < 3; warmup++)
				foreach (var candidate in candidates)
					Consume(await candidate.Run(path).ConfigureAwait(false));
			var results = candidates.ToDictionary(candidate => candidate.Name, _ => new List<Sample>());
			for (var run = 0; run < runs; run++)
			{
				// Rotate first position; forced GC is outside timing and identical for all candidates.
				for (var slot = 0; slot < candidates.Count; slot++)
				{
					var candidate = candidates[(run + slot) % candidates.Count];
					GC.Collect();
					GC.WaitForPendingFinalizers();
					GC.Collect();
					var allocated = GC.GetTotalAllocatedBytes(precise: true);
					var gen0 = GC.CollectionCount(0);
					var gen1 = GC.CollectionCount(1);
					var gen2 = GC.CollectionCount(2);
					var start = Stopwatch.GetTimestamp();
					var result = await candidate.Run(path).ConfigureAwait(false);
					var wall = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
					var sample = new Sample(wall, GC.GetTotalAllocatedBytes(precise: true) - allocated,
						GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2);
					Consume(result);
					results[candidate.Name].Add(sample);
					WriteRow(storage, count, workload, candidate.Name, (run + 1).ToString(CultureInfo.InvariantCulture), sample);
				}
			}
			foreach (var candidate in candidates)
			{
				var samples = results[candidate.Name];
				Console.WriteLine(FormattableString.Invariant($"{storage},{count},{workload},{candidate.Name},median,{Median(samples.Select(s => s.WallMs)):F3},{Median(samples.Select(s => (double)s.AllocatedBytes)):F0},{Median(samples.Select(s => (double)s.Gen0)):F1},{Median(samples.Select(s => (double)s.Gen1)):F1},{Median(samples.Select(s => (double)s.Gen2)):F1}"));
			}
			Console.Error.WriteLine($"Measured {storage}/{count}/{workload} ({runs} runs each)");
		}

		private static double Median(IEnumerable<double> values)
		{
			var sorted = values.Order().ToArray();
			return (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
		}

		private static void WriteRow(string storage, int count, string workload, string candidate, string run, Sample sample)
			=> Console.WriteLine(FormattableString.Invariant($"{storage},{count},{workload},{candidate},{run},{sample.WallMs:F3},{sample.AllocatedBytes},{sample.Gen0},{sample.Gen1},{sample.Gen2}"));

		private static void Consume(object result)
		{
			if (result is List<Entry> entries)
				foreach (var entry in entries) s_sink = unchecked(s_sink + entry.Name.Length + (long)entry.Stat.Size + (long)entry.Stat.Inode);
			else if (result is List<FileSystemEntryInfo> items)
				foreach (var item in items) s_sink = unchecked(s_sink + item.Name.Length + item.Length + item.LastWriteTimeUtc.Ticks);
			else throw new InvalidOperationException("Unexpected result.");
		}

		private static void Compare(List<Entry> expected, List<Entry> actual, int count)
		{
			if (expected.Count != count || actual.Count != count) throw new InvalidOperationException("Entry count mismatch.");
			var sortedExpected = expected.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
			var sortedActual = actual.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
			if (!sortedExpected.SequenceEqual(sortedActual)) throw new InvalidOperationException("Name/metadata mismatch.");
		}

		private static async Task VerifyService(string path, List<Entry> expected)
		{
			var actual = ((List<FileSystemEntryInfo>)await Service(path, true).ConfigureAwait(false)).ToDictionary(entry => entry.Name, StringComparer.Ordinal);
			if (actual.Count != expected.Count) throw new InvalidOperationException("Service entry count mismatch.");
			foreach (var entry in expected)
			{
				var item = actual[entry.Name];
				if (item.Length != (long)entry.Stat.Size || item.LastWriteTimeUtc != entry.Stat.ModifiedUtc
					|| item.UnixMode != (UnixFileMode)(entry.Stat.Mode & 0xFFF))
					throw new InvalidOperationException("Service metadata mismatch.");
			}
		}

		private static void SelfCheck(string parent, bool rust)
		{
			using var fixture = new Fixture(parent, 20);
			fixture.AddEdges();
			var current = DirectoryBenchmarkNative.Current(fixture.Path);
			Compare(current, DirectoryBenchmarkNative.Optimized(fixture.Path), 25);
			if (rust) Compare(current, DirectoryBenchmarkNative.Rust(fixture.Path), 25);
			foreach (var length in new[] { 0, 19, 25, 40 })
			{
				var bytes = new byte[32];
				System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), (ushort)length);
				try { DirectoryBenchmarkNative.ValidateDirent(bytes); }
				catch (IOException) { continue; }
				throw new InvalidOperationException("Malformed dirent accepted.");
			}
			Console.Error.WriteLine("Differential checks passed: metadata, hidden/Unicode/long/shell-like names, directories, links and malformed batches.");
		}

		private sealed class Fixture : IDisposable
		{
			private readonly int _fd;
			private readonly List<(string Name, bool Directory)> _created = new();
			public string Path { get; }

			public Fixture(string parent, int count)
			{
				Path = System.IO.Path.Combine(System.IO.Path.GetFullPath(parent), "linuxfiles-bench-" + Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
				_fd = PosixNative.OpenAt(PosixNative.AtFdCwd, Path,
					PosixNative.ReadOnlyFlags | PosixNative.ODirectory | PosixNative.ONofollow, out var errno);
				if (_fd < 0) throw PosixNative.CreateException(errno, Path);
				try
				{
					var bytes = new byte[257];
					for (var i = 0; i < count; i++)
					{
						var name = (i % 11 == 0 ? "." : "") + (i % 13 == 0 ? "日本-é-" : "file-") + i.ToString("D6", CultureInfo.InvariantCulture) + ".bin";
						AddFile(name, bytes.AsSpan(0, i % bytes.Length));
					}
				}
				catch { Dispose(); throw; }
			}

			private void AddFile(string name, ReadOnlySpan<byte> bytes)
			{
				var fd = PosixNative.OpenAt(_fd, name, PosixNative.CreateExclusiveFlags, out var errno, 0x180);
				if (fd < 0) throw PosixNative.CreateException(errno, Path);
				_created.Add((name, false));
				using var stream = new FileStream(new SafeFileHandle(fd, ownsHandle: true), FileAccess.Write);
				stream.Write(bytes);
			}

			public void AddEdges()
			{
				AddFile("quote' ; $(touch never)\n.txt", "safe"u8);
				AddFile(new string('x', 255), "long"u8);
				if (!PosixNative.MakeDirectoryAt(_fd, "child", out var errno)) throw PosixNative.CreateException(errno, Path);
				_created.Add(("child", true));
				// ELEVATION_HELPER omits the production symlink helper; creation stays in the owned fixture.
				File.CreateSymbolicLink(System.IO.Path.Combine(Path, "link"), "child");
				_created.Add(("link", false));
				File.CreateSymbolicLink(System.IO.Path.Combine(Path, "dangling"), "absent");
				_created.Add(("dangling", false));
			}

			public void Dispose()
			{
				try
				{
					foreach (var (name, directory) in _created)
						PosixNative.UnlinkAt(_fd, name, directory ? PosixNative.AtRemoveDir : 0, Path);
				}
				finally { PosixNative.Close(_fd); }
				Directory.Delete(Path, recursive: false);
			}
		}
	}
}
