// Copyright (c) Files Community
// Licensed under the MIT License.

using Files.Platform.Abstractions.Appearance;
using Files.Platform.Linux.Windowing;
using Files.Platform.Tests.Clipboard;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Files.Platform.Tests.Windowing
{
	[TestClass]
	[DoNotParallelize]
	public sealed unsafe partial class X11AppearanceIntegrationTests
	{
		private static bool _privateDisplay;

		[ClassInitialize]
		public static void StartXvfb(TestContext context)
		{
			var previousDisplay = Environment.GetEnvironmentVariable("DISPLAY");
			X11ClipboardIntegrationTests.StartXvfb(context);
			_privateDisplay = Environment.GetEnvironmentVariable("DISPLAY") != previousDisplay;
		}

		[ClassCleanup]
		public static void StopXvfb() => X11ClipboardIntegrationTests.StopXvfb();

		[TestMethod]
		[DataRow("i3", "_NET_WM_NAME")]
		[DataRow("Openbox", "WM_NAME")]
		public void Detection_ReadsIndependentCompositorOwner(string managerName, string nameProperty)
		{
			WithWindows(managerName, (display, window, compositor) =>
			{
				SetName(display, compositor, "picom", nameProperty);
				SetCompositor(display, compositor);
				using var chrome = X11WindowChrome.TryCreate(window);
				Assert.IsNotNull(chrome);
				Assert.AreEqual(OpacitySupport.Supported, X11AppearanceSupport.Current.WindowOpacity);
				Assert.AreEqual(BackdropMode.Transparent, X11AppearanceSupport.Current.Resolve(BackdropMode.Transparent, false));
				Assert.IsFalse(X11AppearanceSupport.Current.Blur);
			});
		}

		[TestMethod]
		public void Detection_RefreshesWhenCompositorPublishesItsNameAfterAcquiringSelection()
		{
			WithWindows("i3", (display, window, compositor) =>
			{
				SetCompositor(display, compositor);
				using var chrome = X11WindowChrome.TryCreate(window);
				Assert.IsNotNull(chrome);
				Assert.AreEqual(OpacitySupport.Unknown, X11AppearanceSupport.Current.WindowOpacity);
				SetName(display, compositor, "picom", "_NET_WM_NAME");
				XSync(display, false);
				Assert.IsTrue(SpinWait.SpinUntil(() => X11AppearanceSupport.Current.WindowOpacity == OpacitySupport.Supported, 5000),
					"Publishing the compositor name did not refresh capabilities.");
			});
		}

		[TestMethod]
		public void CompositorToggle_RefreshesCapabilitiesWithoutClientSideDecorations()
		{
			WithWindows("Xfwm4", (display, window, compositor) =>
			{
				using var chrome = X11WindowChrome.TryCreate(window);
				Assert.IsNotNull(chrome);
				Assert.IsFalse(chrome.SupportsClientSideDecorations);
				Assert.AreEqual(OpacitySupport.NoCompositor, X11AppearanceSupport.Current.WindowOpacity);
				Assert.AreEqual(BackdropMode.Solid, X11AppearanceSupport.Current.Resolve(BackdropMode.Transparent, false));
				var notifications = 0;
				EventHandler changed = (_, _) => Interlocked.Increment(ref notifications);
				X11AppearanceSupport.Changed += changed;
				try
				{
					SetCompositor(display, compositor);
					Assert.IsTrue(SpinWait.SpinUntil(() => X11AppearanceSupport.Current.WindowOpacity == OpacitySupport.Supported &&
						Volatile.Read(ref notifications) == 1, 5000), "Enabling compositing did not refresh capabilities.");
					Assert.AreEqual(BackdropMode.Transparent, X11AppearanceSupport.Current.Resolve(BackdropMode.Transparent, false));
					SetCompositor(display, 0);
					Assert.IsTrue(SpinWait.SpinUntil(() => X11AppearanceSupport.Current.WindowOpacity == OpacitySupport.NoCompositor &&
						Volatile.Read(ref notifications) == 2, 5000), "Disabling compositing did not refresh capabilities.");
					Assert.AreEqual(BackdropMode.Solid, X11AppearanceSupport.Current.Resolve(BackdropMode.Transparent, false));
				}
				finally { X11AppearanceSupport.Changed -= changed; }
			});
		}

		private static void WithWindows(string managerName, Action<nint, nuint, nuint> test)
		{
			if (!_privateDisplay)
				Assert.Inconclusive("Private Xvfb display unavailable.");
			XInitThreads();
			var display = XOpenDisplay(0);
			Assert.AreNotEqual((nint)0, display);
			var root = XDefaultRootWindow(display);
			var manager = XCreateSimpleWindow(display, root, 0, 0, 1, 1, 0, 0, 0);
			var compositor = XCreateSimpleWindow(display, root, 0, 0, 1, 1, 0, 0, 0);
			var window = XCreateSimpleWindow(display, root, 0, 0, 1, 1, 0, 0, 0);
			var previousDesktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
			try
			{
				Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", "");
				SetName(display, manager, managerName, "_NET_WM_NAME");
				XChangeProperty(display, root, XInternAtom(display, "_NET_SUPPORTING_WM_CHECK", false),
					XInternAtom(display, "WINDOW", false), 32, 0, (byte*)&manager, 1);
				XSync(display, false);
				test(display, window, compositor);
			}
			finally
			{
				SetCompositor(display, 0);
				XDeleteProperty(display, root, XInternAtom(display, "_NET_SUPPORTING_WM_CHECK", false));
				XDestroyWindow(display, window);
				XDestroyWindow(display, compositor);
				XDestroyWindow(display, manager);
				XCloseDisplay(display);
				Environment.SetEnvironmentVariable("XDG_CURRENT_DESKTOP", previousDesktop);
			}
		}

		private static void SetName(nint display, nuint window, string name, string property)
		{
			var bytes = Encoding.UTF8.GetBytes(name);
			fixed (byte* data = bytes)
				XChangeProperty(display, window, XInternAtom(display, property, false),
					XInternAtom(display, property == "WM_NAME" ? "STRING" : "UTF8_STRING", false), 8, 0, data, bytes.Length);
		}

		private static void SetCompositor(nint display, nuint owner)
		{
			XSetSelectionOwner(display, XInternAtom(display, "_NET_WM_CM_S0", false), owner, 0);
			XSync(display, false);
		}

		[LibraryImport("libX11.so.6")]
		private static partial int XInitThreads();
		[LibraryImport("libX11.so.6")]
		private static partial nint XOpenDisplay(nint name);
		[LibraryImport("libX11.so.6")]
		private static partial int XCloseDisplay(nint display);
		[LibraryImport("libX11.so.6")]
		private static partial nuint XDefaultRootWindow(nint display);
		[LibraryImport("libX11.so.6")]
		private static partial nuint XCreateSimpleWindow(nint display, nuint parent, int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);
		[LibraryImport("libX11.so.6")]
		private static partial int XDestroyWindow(nint display, nuint window);
		[LibraryImport("libX11.so.6", StringMarshalling = StringMarshalling.Utf8)]
		private static partial nuint XInternAtom(nint display, string name, [MarshalAs(UnmanagedType.Bool)] bool onlyIfExists);
		[LibraryImport("libX11.so.6")]
		private static partial int XChangeProperty(nint display, nuint window, nuint property, nuint type, int format, int mode, byte* data, int elements);
		[LibraryImport("libX11.so.6")]
		private static partial int XDeleteProperty(nint display, nuint window, nuint property);
		[LibraryImport("libX11.so.6")]
		private static partial int XSetSelectionOwner(nint display, nuint selection, nuint owner, nuint time);
		[LibraryImport("libX11.so.6")]
		private static partial int XSync(nint display, [MarshalAs(UnmanagedType.Bool)] bool discard);
	}
}
