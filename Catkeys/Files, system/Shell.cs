using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Win32;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using System.Drawing;
//using System.Linq;
//using System.Xml.Linq;
//using System.Xml.XPath;

using Catkeys;
using static Catkeys.NoClass;

namespace Catkeys
{
	/// <summary>
	/// Windows shell functions.
	/// Windows shell manages files, folders (directories), shortcuts and virtual objects such as Control Panel.
	/// </summary>
	public partial class Shell
	{
		/// <summary>
		/// The same as <see cref="Path_.Normalize"/>(CanBeUrlOrShell|DoNotPrefixLongPath), but ignores non-full path (returns s).
		/// </summary>
		/// <param name="s">File-system path or URL or "::...".</param>
		static string _Normalize(string s)
		{
			s = Path_.ExpandEnvVar(s);
			if(!Path_.IsFullPath(s)) return s; //note: not EEV. Need to expand to ":: " etc, and EEV would not do it.
			return Path_.LibNormalize(s, Path_.NormalizeFlags.DoNotPrefixLongPath, true);
		}

		/// <summary>
		/// flags for <see cref="Run"/>.
		/// </summary>
		[Flags]
		public enum RunFlags
		{
			/// <summary>
			/// Show error message box if fails, for example if file not found.
			/// Note: this does not disable exceptions. Still need exception handling. Or call <see cref="RunSafe"/>.
			/// </summary>
			ShowErrorUI = 1,

			/// <summary>
			/// If started new process, wait until it exits.
			/// Uses <see cref="WaitHandle.WaitOne()"/>.
			/// </summary>
			WaitForExit = 2,
		}

		/// <summary>
		/// Runs/opens a program, document, folder, URL, new email, Control Panel item etc.
		/// By default returns process id. If used flag WaitForExit, returns process exit code.
		/// Returns 0 if did not start new process or did not get process handle, usually because opened the document in an existing process.
		/// </summary>
		/// <param name="s">
		/// What to run. Can be:
		/// Full path of a file or folder. Examples: <c>@"C:\folder\file.txt"</c>, <c>Folders.System + "notepad.exe"</c>, <c>@"%Folders.System%\notepad.exe"</c>.
		/// Filename of a file or folder, like <c>"notepad.exe"</c>. The function calls <see cref="Files.SearchPath"/>.
		/// URL. Examples: <c>"http://a.b.c/d"</c>, <c>"file:///path"</c>.
		/// Email, like <c>"mailto:a@b.c"</c>. Subject, body etc also can be specified, and Google knows how.
		/// Shell object's ITEMIDLIST like <c>":: HexEncodedITEMIDLIST"</c>. See <see cref="Pidl.ToHexString"/>, <see cref="Folders.Virtual"/>. Can be used to open virtual folders and items like Control Panel.
		/// Shell object's parsing name, like <c>"::{CLSID}"</c>. See <see cref="Pidl.ToShellString"/>, <see cref="Folders.VirtualPidl"/>. Can be used to open virtual folders and items like Control Panel.
		/// To run a Windows Store App, use <c>"shell:AppsFolder\WinStoreAppId"</c> format. Examples: <c>"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"</c>, <c>"shell:AppsFolder\windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel"</c>. To discover the string use <see cref="Wnd.Misc.GetWindowsStoreAppId"/> or Google.
		/// Supports environment variables, like <c>@"%TMP%\file.txt"</c>. See <see cref="Path_.ExpandEnvVar"/>.
		/// </param>
		/// <param name="args">
		/// Command line arguments to pass to the program.
		/// This function expands environment variables if starts with "%" or "\"%".
		/// </param>
		/// <param name="flags"></param>
		/// <param name="more">Allows to specify more parameters.</param>
		/// <exception cref="CatException">Failed. For example, the file does not exist.</exception>
		/// <remarks>
		/// It works like when you double-click an icon. It may start new process or not. For example it may just activate window if the program is already running.
		/// Uses API <msdn>ShellExecuteEx</msdn>.
		/// Similar to <see cref="Process.Start(string, string)"/>.
		/// 
		/// The returned process id can be used to find a window of the new process. Example:
		/// <code><![CDATA[
		/// Wnd w = WaitFor.WindowActive(10, "*- Notepad", "Notepad", Shell.Run("notepad.exe"));
		/// ]]></code>
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// Wnd w = Wnd.Find("*- Notepad", "Notepad");
		/// if(w.Is0) { Shell.Run("notepad.exe"); w = WaitFor.WindowActive(Wnd.LastFind); }
		/// w.Activate();
		/// ]]></code>
		/// </example>
		public static int Run(string s, string args = null, RunFlags flags = 0, RunMoreParams more = null)
		{
			var x = new _Api.SHELLEXECUTEINFO();
			x.cbSize = Api.SizeOf(x);
			x.fMask = _Api.SEE_MASK_NOZONECHECKS | _Api.SEE_MASK_NOASYNC | _Api.SEE_MASK_NOCLOSEPROCESS | _Api.SEE_MASK_CONNECTNETDRV | _Api.SEE_MASK_UNICODE;
			x.nShow = Api.SW_SHOWNORMAL;
			if(more != null) {
				more.ProcessHandle = null;
				x.lpVerb = more.Verb;
				x.lpDirectory = Path_.ExpandEnvVar(more.CurrentDirectory);
				if(more.OwnerWindow != null) x.hwnd = more.OwnerWindow.GetWnd().WndWindow;
				switch(more.WindowState) {
				case ProcessWindowStyle.Hidden: x.nShow = Api.SW_HIDE; break;
				case ProcessWindowStyle.Minimized: x.nShow = Api.SW_SHOWMINIMIZED; break;
				case ProcessWindowStyle.Maximized: x.nShow = Api.SW_SHOWMAXIMIZED; break;
				}
			}

			if(0 == (flags & RunFlags.ShowErrorUI)) x.fMask |= _Api.SEE_MASK_FLAG_NO_UI;
			if(x.lpVerb != null) x.fMask |= _Api.SEE_MASK_INVOKEIDLIST; //makes slower. But verbs are rarely used.
			if(0 == (flags & RunFlags.WaitForExit)) x.fMask |= _Api.SEE_MASK_NO_CONSOLE;

			if(!Empty(args)) x.lpParameters = Path_.ExpandEnvVar(args);
			s = Path_.ExpandEnvVar(s);
			if(Empty(s)) throw new ArgumentException();
			Pidl pidl = null;
			if(Path_.LibIsShellPath(s)) { //":: HexEncodedITEMIDLIST" or "::{CLSID}..." (we convert it too because the API somehow does not support many)
				pidl = Pidl.FromString(s); //does not throw
				if(pidl != null) {
					x.lpIDList = pidl;
					x.fMask |= _Api.SEE_MASK_INVOKEIDLIST;
				} else x.lpFile = s;
			} else {
				bool isFullPath = Path_.IsFullPath(s);
				if(isFullPath) {
					s = Path_.LibNormalize(s, Path_.NormalizeFlags.DoNotExpandDosPath | Path_.NormalizeFlags.DoNotPrefixLongPath, true);
					s = Path_.UnprefixLongPath(s); //the API supports prefixed path for exe but not for documents
					if(Files.Misc.DisableRedirection.IsSystem64PathIn32BitProcess(s) && !Files.ExistsAsAny(s)) {
						s = Files.Misc.DisableRedirection.GetNonRedirectedSystemPath(s);
					}
				} else if(!Path_.IsUrl(s)) {
					var s2 = Files.SearchPath(s); //API would search everywhere except in app folder
					if(s2 != null) {
						s = s2;
						isFullPath = true;
					}
				}
				x.lpFile = s;

				if(x.lpDirectory == null && isFullPath) x.lpDirectory = Path_.GetDirectoryPath(s);
			}

			Wnd.Misc.EnableActivate();

			bool ok = false;
			try {
				ok = _Api.ShellExecuteEx(ref x);
			}
			finally {
				pidl?.Dispose();
			}
			if(!ok) throw new CatException(0, $"*run '{s}'");

			bool waitForExit = 0 != (flags & RunFlags.WaitForExit);
			bool callerNeedsHandle = more != null && more.NeedProcessHandle;
			Process_.LibProcessWaitHandle ph = null;
			if(x.hProcess != Zero) {
				if(waitForExit || callerNeedsHandle) ph = new Process_.LibProcessWaitHandle(x.hProcess);
			}

			try {
				Api.AllowSetForegroundWindow(Api.ASFW_ANY);

				if(x.lpVerb != null && !Application.MessageLoop)
					Thread.CurrentThread.Join(50); //need min 5-10 for Properties. And not Sleep.

				if(ph != null) {
					if(waitForExit) ph.WaitOne();
					if(callerNeedsHandle) more.ProcessHandle = ph;
					if(waitForExit) return Api.GetExitCodeProcess(x.hProcess, out var ec) ? (int)ec : 0;
				}
				if(x.hProcess != Zero) return Process_.GetProcessId(x.hProcess);
			}
			finally {
				if(!callerNeedsHandle || more.ProcessHandle == null) {
					if(ph != null) ph.Dispose();
					else if(x.hProcess != Zero) Api.CloseHandle(x.hProcess);
				}
			}

			return 0;

			//tested: works well in MTA apartment.
			//rejected: in QM2, run also has a 'window' parameter. However it just makes limited, unclear etc, and therefore rarely used. Instead use a Find/Run/Wait pattern, like in the examples.
			//rejected: in QM2, run also has 'autodelay'. Better don't add such hidden things. Let the script decide what to do.
		}

		/// <summary>
		/// Calls <see cref="Run"/> and handles exceptions.
		/// If Run throws exception, prints its Message and returns 0.
		/// Handles only exception of type CatException. It is thrown when fails, usually when the file does not exist.
		/// All parameters are the same.
		/// This is useful when you don't care whether Run succeeded, for example in CatMenu menu item command handlers. Using try/catch there would not look good.
		/// </summary>
		public static int RunSafe(string s, string args = null, RunFlags flags = 0, RunMoreParams more = null)
		{
			try {
				return Run(s, args, flags, more);
			}
			catch(CatException e) {
				Print(e.Message);
				return 0;
			}
		}

		/// <summary>
		/// Used to pass more parameters to <see cref="Run"/>.
		/// </summary>
		public class RunMoreParams
		{
			/// <summary>
			/// Initial "current directory" for the new process.
			/// If this is not set (null), the function gets parent directory path of the specified file that is to run, if possible (if its full path is specified or found). It's because some incorrectly designed programs look for their files in "current directory", and fail to start if initial "current directory" is not set to the program's directory.
			/// If this is "" or invalid or the function cannot find full path, the new process will inherit "current directory" of this process.
			/// </summary>
			public string CurrentDirectory;

			/// <summary>
			/// File's right-click menu command, also known as verb. For example "edit", "print", "properties". The default verb is bold in the menu.
			/// Not all menu items will work. Some may have different name than in the menu. Use verb "RunAs" for "Run as administrator".
			/// </summary>
			public string Verb;

			/// <summary>
			/// A window that may be used as owner window of error message box.
			/// Also, new window should be opened on the same screen. However many programs ignore it.
			/// </summary>
			public WndOrControl OwnerWindow;

			/// <summary>
			/// Preferred window state.
			/// Many programs ignore it.
			/// </summary>
			public ProcessWindowStyle WindowState;

			//no. If need, caller can get window and call EnsureInScreen etc.
			//public Screen Screen;
			//this either does not work or I could not find a program that uses default window position (does not save/restore)
			//if(more.Screen != null) { x._14.hMonitor = (IntPtr)more.Screen.GetHashCode(); x.fMask |= _Api.SEE_MASK_HMONITOR; } //GetHashCode gets HMONITOR

			/// <summary>
			/// Get process handle, if possible.
			/// The <see cref="ProcessHandle"/> property will contain it.
			/// </summary>
			public bool NeedProcessHandle;

			/// <summary>
			/// This is an [Out] value.
			/// When the function returns, if <see cref="NeedProcessHandle"/> was set to true, contains process handle in a <see cref="WaitHandle"/> variable.
			/// null if did not start new process (eg opened the document in an existing process) or did not get process handle for some other reason.
			/// Note: WaitHandle is disposable.
			/// </summary>
			/// <example>
			/// <code><![CDATA[
			/// //this code does the same as Shell.Run(@"notepad.exe", flags: Shell.RunFlags.WaitForExit);
			/// var p = new Shell.RunMoreParams() { NeedProcessHandle = true };
			/// Shell.Run(@"notepad.exe", more: p);
			/// using(var h = p.ProcessHandle) h?.WaitOne();
			/// ]]></code>
			/// </example>
			public WaitHandle ProcessHandle { get; internal set; }
		}

		internal partial class _Api
		{
			internal const uint SEE_MASK_CONNECTNETDRV = 0x80;
			internal const uint SEE_MASK_NOZONECHECKS = 0x800000;
			internal const uint SEE_MASK_UNICODE = 0x4000;
			internal const uint SEE_MASK_FLAG_NO_UI = 0x400;
			internal const uint SEE_MASK_INVOKEIDLIST = 0xC;
			internal const uint SEE_MASK_NOCLOSEPROCESS = 0x40;
			internal const uint SEE_MASK_NOASYNC = 0x100;
			internal const uint SEE_MASK_NO_CONSOLE = 0x8000;
			internal const uint SEE_MASK_HMONITOR = 0x200000;
			internal const uint SEE_MASK_WAITFORINPUTIDLE = 0x2000000;

			internal struct SHELLEXECUTEINFO
			{
				public uint cbSize;
				public uint fMask;
				public Wnd hwnd;
				public string lpVerb;
				public string lpFile;
				public string lpParameters;
				public string lpDirectory;
				public int nShow;
				public IntPtr hInstApp;
				public IntPtr lpIDList;
				public string lpClass;
				public IntPtr hkeyClass;
				public uint dwHotKey;

				[StructLayout(LayoutKind.Explicit)]
				internal struct TYPE_1
				{
					[FieldOffset(0)] public IntPtr hIcon;
					[FieldOffset(0)] public IntPtr hMonitor;
				}
				public TYPE_1 _14;
				public IntPtr hProcess;
			}

			[DllImport("shell32.dll", EntryPoint = "ShellExecuteExW", SetLastError = true)]
			internal static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO pExecInfo);

		}
	}
}