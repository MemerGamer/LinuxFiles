// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Volumes;
using Files.Platform.Linux.Volumes;
using Files.Platform.Tests.SystemIntegration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace Files.Platform.Tests.Volumes
{
	/// <summary>
	/// All tests talk to <see cref="FakeUDisks2"/> on a private bus. The real system bus and real devices are never touched.
	/// </summary>
	[TestClass]
	public sealed class UDisks2VolumeServiceTests
	{
		private const string Stick = "/org/freedesktop/UDisks2/block_devices/sdb1";
		private const string Backup = "/org/freedesktop/UDisks2/block_devices/sdb2";
		private const string StickDrive = "/org/freedesktop/UDisks2/drives/Generic_Flash";

		private static async Task<(PrivateBus Bus, FakeUDisks2 Fake, UDisks2VolumeService Service)> StartAsync()
		{
			var bus = PrivateBus.Start();
			var fake = await FakeUDisks2.StartAsync(bus.Address);

			fake.AddDrive("Generic_Flash", removable: true);
			fake.AddDrive("Samsung_SSD", removable: false, ejectable: false, canPowerOff: false);
			fake.AddDrive("Optical", removable: true, optical: true, canPowerOff: false);

			fake.AddBlock("sdb1", "Generic_Flash", "STICK", "vfat", 8_000_000_000, ["/run/media/u/STICK"]);
			fake.AddBlock("sdb2", "Generic_Flash", "BACKUP", "ext4", 4_000_000_000, null);
			fake.AddBlock("sda1", "Samsung_SSD", "EFI", "vfat", 500_000_000, ["/boot/efi"], system: true);
			fake.AddBlock("sda2", "Samsung_SSD", null, "btrfs", 900_000_000_000, ["/"], system: true);
			fake.AddBlock("sda3", "Samsung_SSD", "Swapish", "ext4", 1_000_000, null, system: true);
			fake.AddBlock("loop0", null, null, "squashfs", 1_000_000, null, loop: true);
			fake.AddBlock("loop1", null, null, "squashfs", 1_000_000, ["/snap/core/1"], loop: true);
			fake.AddBlock("sdc1", null, "Hidden", "ext4", 1_000_000, null, ignore: true);
			fake.AddBlock("sr0", "Optical", "DISC", "iso9660", 700_000_000, null);

			return (bus, fake, new UDisks2VolumeService(bus.Address));
		}

		[TestMethod]
		public async Task ListsVisibleVolumesWithLabelsFlagsAndMountPoints()
		{
			var (bus, fake, service) = await StartAsync();
			using (bus) using (fake) using (service)
			{
				Assert.IsTrue(await service.IsAvailableAsync());
				var volumes = await service.GetVolumesAsync();

				CollectionAssert.AreEqual(new[] { "/dev/sda2", "/dev/sdb1", "/dev/sdb2", "/dev/sr0" }, volumes.Select(v => v.Device).ToArray());

				var stick = volumes.Single(v => v.Id == Stick);
				Assert.AreEqual("STICK", stick.Label);
				Assert.AreEqual("vfat", stick.FileSystem);
				Assert.AreEqual(8_000_000_000UL, stick.Size);
				Assert.IsTrue(stick.IsRemovable);
				Assert.IsFalse(stick.IsOptical);
				Assert.IsTrue(stick.IsMounted);
				Assert.AreEqual("/run/media/u/STICK", stick.MountPoint);
				Assert.AreEqual(StickDrive, stick.DriveId);
				Assert.IsTrue(stick.CanEject);
				Assert.IsTrue(stick.CanPowerOff);

				// Unmounted removable volumes are listed so the sidebar can offer to mount them
				var backup = volumes.Single(v => v.Id == Backup);
				Assert.IsFalse(backup.IsMounted);
				Assert.AreEqual("BACKUP", backup.Label);

				var disc = volumes.Single(v => v.Device == "/dev/sr0");
				Assert.IsTrue(disc.IsOptical);
				Assert.IsTrue(disc.IsRemovable);

				var root = volumes.Single(v => v.Device == "/dev/sda2");
				Assert.IsTrue(root.IsSystem);
				Assert.IsFalse(root.IsRemovable);
				Assert.AreEqual("/", root.MountPoint);
			}
		}

		[TestMethod]
		public async Task MountSendsFilesystemMountWithPolkitInteractionEnabled()
		{
			var (bus, fake, service) = await StartAsync();
			using (bus) using (fake) using (service)
			{
				var mountPoint = await service.MountAsync(Backup);

				Assert.AreEqual("/run/media/test/sdb2", mountPoint);
				var call = fake.CallsSnapshot().Single();
				Assert.AreEqual(Backup, call.Path);
				Assert.AreEqual("org.freedesktop.UDisks2.Filesystem", call.Interface);
				Assert.AreEqual("Mount", call.Member);
				Assert.AreEqual(false, call.Options["auth.no_user_interaction"]);
			}
		}

		[TestMethod]
		public async Task UnmountSendsFilesystemUnmount()
		{
			var (bus, fake, service) = await StartAsync();
			using (bus) using (fake) using (service)
			{
				await service.UnmountAsync(Stick);

				var call = fake.CallsSnapshot().Single();
				Assert.AreEqual((Stick, "org.freedesktop.UDisks2.Filesystem", "Unmount"), (call.Path, call.Interface, call.Member));
				Assert.AreEqual(false, call.Options["auth.no_user_interaction"]);
			}
		}

		[TestMethod]
		public async Task EjectUnmountsEveryMountedVolumeOfTheDriveThenEjectsIt()
		{
			var (bus, fake, service) = await StartAsync();
			using (bus) using (fake) using (service)
			{
				fake.SetMounts("sdb2", "/run/media/u/BACKUP");

				await service.EjectAsync(Stick);

				var calls = fake.CallsSnapshot().Select(c => (c.Path, c.Interface.Split('.').Last(), c.Member)).ToArray();
				CollectionAssert.AreEquivalent(new[] { (Stick, "Filesystem", "Unmount"), (Backup, "Filesystem", "Unmount") }, calls[..2]);
				Assert.AreEqual((StickDrive, "Drive", "Eject"), calls[2]);
				Assert.AreEqual(3, calls.Length);
			}
		}

		[TestMethod]
		public async Task PowerOffEjectsNothingButPowersOffTheDrive()
		{
			var (bus, fake, service) = await StartAsync();
			using (bus) using (fake) using (service)
			{
				await service.PowerOffAsync(Stick);

				var calls = fake.CallsSnapshot();
				Assert.AreEqual("Unmount", calls[0].Member);
				Assert.AreEqual((StickDrive, "org.freedesktop.UDisks2.Drive", "PowerOff"), (calls[1].Path, calls[1].Interface, calls[1].Member));
			}
		}

		[TestMethod]
		public async Task PowerOffOfADriveThatCannotPowerOffIsNotSupportedAndSendsNothing()
		{
			var (bus, fake, service) = await StartAsync();
			using (bus) using (fake) using (service)
			{
				var ex = await Assert.ThrowsExactlyAsync<VolumeOperationException>(() => service.PowerOffAsync("/org/freedesktop/UDisks2/block_devices/sda2"));
				Assert.AreEqual(VolumeError.NotSupported, ex.Error);
				Assert.IsTrue(fake.CallsSnapshot().Any(c => c.Member == "Unmount"));
				Assert.IsFalse(fake.CallsSnapshot().Any(c => c.Member == "PowerOff"));
			}
		}

		[TestMethod]
		public async Task PolkitDenialAndBusyDeviceAreMapped()
		{
			var (bus, fake, service) = await StartAsync();
			using (bus) using (fake) using (service)
			{
				fake.Errors["org.freedesktop.UDisks2.Filesystem.Mount"] = "org.freedesktop.UDisks2.Error.NotAuthorizedDismissed";
				var denied = await Assert.ThrowsExactlyAsync<VolumeOperationException>(() => service.MountAsync(Backup));
				Assert.AreEqual(VolumeError.NotAuthorized, denied.Error);

				fake.Errors["org.freedesktop.UDisks2.Filesystem.Unmount"] = "org.freedesktop.UDisks2.Error.DeviceBusy";
				var busy = await Assert.ThrowsExactlyAsync<VolumeOperationException>(() => service.UnmountAsync(Stick));
				Assert.AreEqual(VolumeError.Busy, busy.Error);
			}
		}

		[TestMethod]
		public async Task AlreadyMountedReturnsTheExistingMountPoint()
		{
			var (bus, fake, service) = await StartAsync();
			using (bus) using (fake) using (service)
			{
				fake.Errors["org.freedesktop.UDisks2.Filesystem.Mount"] = "org.freedesktop.UDisks2.Error.AlreadyMounted";
				Assert.AreEqual("/run/media/u/STICK", await service.MountAsync(Stick));
			}
		}

		[TestMethod]
		public async Task InvalidIdsAreRejectedWithoutTouchingTheBus()
		{
			var (bus, fake, service) = await StartAsync();
			using (bus) using (fake) using (service)
			{
				foreach (var bad in new[] { "", "/", "/org/freedesktop/UDisks2/drives/Generic_Flash", "/org/freedesktop/UDisks2/block_devices/", "/org/freedesktop/UDisks2/block_devices/sdb1/../x", "/etc/passwd" })
				{
					var ex = await Assert.ThrowsExactlyAsync<VolumeOperationException>(() => service.MountAsync(bad));
					Assert.AreEqual(VolumeError.NotFound, ex.Error);
				}

				Assert.AreEqual(0, fake.CallsSnapshot().Count);
			}
		}

		[TestMethod]
		public async Task ChangeSignalsRaiseAddedChangedAndRemoved()
		{
			var (bus, fake, service) = await StartAsync();
			using (bus) using (fake) using (service)
			{
				var events = new BlockingCollection<VolumeChangedEventArgs>();
				service.VolumesChanged += (_, e) => events.Add(e);
				Assert.IsTrue(await service.StartWatchingAsync());

				fake.AddBlock("sdd1", "Generic_Flash", "NEW", "vfat", 1_000, null);
				fake.EmitInterfacesAdded("/org/freedesktop/UDisks2/block_devices/sdd1");
				var added = await Next(events);
				Assert.AreEqual(VolumeChangeKind.Added, added.Kind);
				Assert.AreEqual("NEW", added.Volume.Label);

				fake.SetMounts("sdd1", "/run/media/u/NEW");
				fake.EmitPropertiesChanged("/org/freedesktop/UDisks2/block_devices/sdd1");
				var changed = await Next(events);
				Assert.AreEqual(VolumeChangeKind.Changed, changed.Kind);
				Assert.AreEqual("/run/media/u/NEW", changed.Volume.MountPoint);

				fake.Remove("/org/freedesktop/UDisks2/block_devices/sdd1");
				fake.EmitInterfacesRemoved("/org/freedesktop/UDisks2/block_devices/sdd1");
				var removed = await Next(events);
				Assert.AreEqual(VolumeChangeKind.Removed, removed.Kind);
				Assert.AreEqual("/dev/sdd1", removed.Volume.Device);

				service.StopWatching();
			}
		}

		[TestMethod]
		public async Task MissingServiceIsReportedAsUnavailable()
		{
			using var bus = PrivateBus.Start();
			using var service = new UDisks2VolumeService(bus.Address);

			Assert.IsFalse(await service.IsAvailableAsync());
			Assert.AreEqual(0, (await service.GetVolumesAsync()).Count);
			Assert.IsFalse(await service.StartWatchingAsync());
		}

		[TestMethod]
		public async Task UnreachableBusIsReportedAsUnavailable()
		{
			using var service = new UDisks2VolumeService("unix:path=/nonexistent/fbus");
			Assert.IsFalse(await service.IsAvailableAsync());
		}

		// Taking on a pool thread: D-Bus callers may be continued on the connection's receive thread, which must not block
		private static Task<VolumeChangedEventArgs> Next(BlockingCollection<VolumeChangedEventArgs> events)
			=> Task.Run(() => events.TryTake(out var e, TimeSpan.FromSeconds(5)) ? e : throw new TimeoutException("No volume change arrived."));
	}
}
