// Copyright (c) Files Community
// SPDX-License-Identifier: MPL-2.0

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com;
using Windows.Win32.UI.Shell;

namespace Files.App.Storage
{
	public unsafe class TaskbarManager : IDisposable
	{
		private ITaskbarList3? taskbarList;

		private static TaskbarManager? _Default = null;
		public static TaskbarManager Default { get; } = _Default ??= new TaskbarManager();

		public TaskbarManager()
		{
			// LINUX-TODO(taskbar): no-op on Linux; real future work is emitting com.canonical.Unity.LauncherEntry.Update over the Platform DBus session (needs an HWND-free progress abstraction)
			if (!OperatingSystem.IsWindows())
				return;

			Guid CLSID_TaskbarList = typeof(TaskbarList).GUID;
			HRESULT hr = PInvoke.CoCreateInstance(CLSID_TaskbarList, null, CLSCTX.CLSCTX_INPROC_SERVER, out ITaskbarList3? pTaskbarList);

			if (hr.ThrowIfFailedOnDebug().Succeeded)
				hr = pTaskbarList!.HrInit().ThrowIfFailedOnDebug();

			taskbarList = pTaskbarList;
		}

		public HRESULT SetProgressValue(HWND hwnd, ulong ullCompleted, ulong ullTotal)
		{
			return taskbarList?.SetProgressValue(hwnd, ullCompleted, ullTotal) ?? HRESULT.E_FAIL;
		}

		public HRESULT SetProgressState(HWND hwnd, TBPFLAG tbpFlags)
		{
			return taskbarList?.SetProgressState(hwnd, tbpFlags) ?? HRESULT.E_FAIL;
		}

		public void Dispose()
		{
			taskbarList = null;
		}
	}
}
