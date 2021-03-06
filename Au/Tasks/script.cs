﻿using Au.Types;
using Au.More;
using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Reflection;
//using System.Linq;

//CONSIDER: script.canPause. Let user explicitly insert this at all points where the script can be safely paused. Also option to allow to pause at every key/mouse/etc function.

namespace Au
{
	/// <summary>
	/// Contains static functions to work with script tasks: run, get properties.
	/// A script task is a running script, except if role editorExtension. Each script task is a separate process.
	/// </summary>
	/// <seealso cref="process"/>
	public static class script
	{
		/// <summary>
		/// In a process of a script with role miniProgram (defaut) returns script file name without extension.
		/// In other processes returns <see cref="AppDomain.FriendlyName"/>, like "MainAssemblyName".
		/// </summary>
		public static string name => s_name ??= AppDomain.CurrentDomain.FriendlyName; //info: in framework 4 with ".exe", now without (now it is entry assembly name)
		internal static string s_name;

		/// <summary>
		/// If this script task has been started from editor, returns script's role property. Else returns <b>ExeProgram</b>.
		/// </summary>
		public static SRole role => s_role;
		internal static SRole s_role;

		/// <summary>
		/// Gets path of script file in workspace.
		/// </summary>
		/// <remarks>
		/// The default compiler adds <see cref="PathInWorkspaceAttribute"/> to the main assembly. Then at run time this property returns its value. Returns null if compiled by some other compiler.
		/// </remarks>
		public static string path => s_pathInWorkspace ??= Assembly.GetEntryAssembly()?.GetCustomAttribute<PathInWorkspaceAttribute>()?.Path;
		static string s_pathInWorkspace;
		//note: GetEntryAssembly returns null in func called by host through coreclr_create_delegate.

		/// <summary>
		/// Returns true if the build configuration of the main assembly is Debug. Returns false if Release (optimize true).
		/// </summary>
		public static bool isDebug => s_debug ??= AssemblyUtil_.IsDebug(Assembly.GetEntryAssembly());
		static bool? s_debug;
		//note: GetEntryAssembly returns null in func called by host through coreclr_create_delegate.

		#region run

		/// <summary>
		/// Starts executing a script. Does not wait.
		/// </summary>
		/// <returns>
		/// Native process id of the task process.
		/// Returns 0 if task start is deferred because a task is running (see meta option ifRunning).
		/// Returns 0 if role editorExtension; then waits until the task ends.
		/// Returns -1 if failed, for example if the script contains errors or cannot run because a task is running.
		/// </returns>
		/// <param name="script">Script name like "Script5.cs", or path like @"\Folder\Script5.cs".</param>
		/// <param name="args">Command line arguments. In script it will be variable <i>args</i>. Should not contain '\0' characters.</param>
		/// <exception cref="FileNotFoundException">Script file not found.</exception>
		public static int run(string script, params string[] args)
			=> _Run(0, script, args, out _);

		/// <summary>
		/// Starts executing a script and waits until the task ends.
		/// More info: <see cref="run"/>.
		/// </summary>
		/// <returns>The exit code of the task process. See <see cref="Environment.ExitCode"/>.</returns>
		/// <exception cref="FileNotFoundException">Script file not found.</exception>
		/// <exception cref="AuException">Failed to start script.</exception>
		public static int runWait(string script, params string[] args)
			=> _Run(1, script, args, out _);

#pragma warning disable CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)
		/// <summary>
		/// Starts executing a script, waits until the task ends and then gets <see cref="writeResult"/> text.
		/// More info: <see cref="run"/>.
		/// </summary>
		/// <param name="results">Receives <see cref="writeResult"/> text.</param>
		/// <returns>The exit code of the task process. See <see cref="Environment.ExitCode"/>.</returns>
		/// <exception cref="FileNotFoundException">Script file not found.</exception>
		/// <exception cref="AuException">Failed to start script.</exception>
		public static int runWait(out string results, string script, params string[] args)
			=> _Run(3, script, args, out results);

		/// <summary>
		/// Starts executing a script, waits until the task ends and gets <see cref="writeResult"/> text in real time.
		/// More info: <see cref="run"/>.
		/// </summary>
		/// <param name="results">Receives <see cref="writeResult"/> output whenever the task calls it.</param>
		/// <returns>The exit code of the task process. See <see cref="Environment.ExitCode"/>.</returns>
		/// <exception cref="FileNotFoundException">Script file not found.</exception>
		/// <exception cref="AuException">Failed to start script.</exception>
		public static int runWait(Action<string> results, string script, params string[] args)
			=> _Run(3, script, args, out _, results);
#pragma warning restore CS1573 // Parameter has no matching param tag in the XML comment (but other parameters do)

		static int _Run(int mode, string script, string[] args, out string resultS, Action<string> resultA = null) {
			var w = WndMsg_; if (w.Is0) throw new AuException("Au editor not found."); //CONSIDER: run editor program, if installed
			bool waitMode = 0 != (mode & 1), needResult = 0 != (mode & 2); resultS = null;
			using var tr = new _TaskResults();
			if (needResult && !tr.Init()) throw new AuException("*get task results");

			var data = Serializer_.Serialize(script, args, tr.pipeName);
			int pid = (int)WndCopyData.Send<byte>(w, 100, data, mode);
			switch ((RunResult_)pid) {
			case RunResult_.failed: return !waitMode ? -1 : throw new AuException("*start task");
			case RunResult_.notFound: throw new FileNotFoundException($"Script '{script}' not found.");
			case RunResult_.editorThread: case RunResult_.deferred: return 0;
			}

			if (waitMode) {
				using var hProcess = WaitHandle_.FromProcessId(pid, Api.SYNCHRONIZE | Api.PROCESS_QUERY_LIMITED_INFORMATION);
				if (hProcess == null) throw new AuException("*wait for task");

				if (!needResult) hProcess.WaitOne(-1);
				else if (!tr.WaitAndRead(hProcess, resultA)) throw new AuException("*get task result");
				else if (resultA == null) resultS = tr.ResultString;

				if (!Api.GetExitCodeProcess(hProcess.SafeWaitHandle.DangerousGetHandle(), out pid)) pid = int.MinValue;
			}
			return pid;
		}

		internal enum RunResult_ //note: sync with ERunResult in Au.CL.cpp.
		{
			failed = 0,
			deferred = -1,
			notFound = -2,
			editorThread = -3,
		}

		unsafe struct _TaskResults : IDisposable
		{
			Handle_ _hPipe;
			public string pipeName;
			string _s;
			StringBuilder _sb;

			public bool Init() {
				var tid = Api.GetCurrentThreadId();
				pipeName = @"\\.\pipe\Au.CL-" + tid.ToString();
				_hPipe = Api.CreateNamedPipe(pipeName,
					Api.PIPE_ACCESS_INBOUND | Api.FILE_FLAG_OVERLAPPED, //use async pipe because also need to wait for task process exit
					Api.PIPE_TYPE_MESSAGE | Api.PIPE_READMODE_MESSAGE | Api.PIPE_REJECT_REMOTE_CLIENTS,
					1, 0, 0, 0, Api.SECURITY_ATTRIBUTES.ForPipes);
				return !_hPipe.Is0;
			}

			public bool WaitAndRead(WaitHandle hProcess, Action<string> results) {
				bool R = false;
				char* b = null; const int bLen = 7900;
				var ev = new ManualResetEvent(false);
				try {
					var ha = new WaitHandle[2] { ev, hProcess };
					for (bool useSB = false; ; useSB = results == null) {
						var o = new Api.OVERLAPPED { hEvent = ev.SafeWaitHandle.DangerousGetHandle() };
						if (!Api.ConnectNamedPipe(_hPipe, &o)) {
							int e = lastError.code;
							if (e != Api.ERROR_PIPE_CONNECTED) {
								if (e != Api.ERROR_IO_PENDING) break;
								int wr = WaitHandle.WaitAny(ha);
								if (wr != 0) { Api.CancelIo(_hPipe); R = true; break; } //task ended
								if (!Api.GetOverlappedResult(_hPipe, ref o, out _, false)) { Api.DisconnectNamedPipe(_hPipe); break; }
							}
						}

						if (b == null) b = (char*)MemoryUtil.Alloc(bLen);
						bool readOK;
						while (((readOK = Api.ReadFile(_hPipe, b, bLen, out int n, null)) || (lastError.code == Api.ERROR_MORE_DATA)) && n > 0) {
							n /= 2;
							if (!readOK) useSB = true;
							if (useSB) { //rare
								_sb ??= new StringBuilder(bLen);
								if (results == null && _s != null) _sb.Append(_s);
								_s = null;
								_sb.Append(b, n);
							} else {
								_s = new string(b, 0, n);
							}
							if (readOK) {
								if (results != null) {
									results(ResultString);
									_sb?.Clear();
								}
								break;
							}
							//note: MSDN says must use OVERLAPPED with ReadFile too, but works without it.
						}
						Api.DisconnectNamedPipe(_hPipe);
						if (!readOK) break;
					}
				}
				finally {
					ev.Dispose();
					MemoryUtil.Free(b);
				}
				return R;
			}

			public string ResultString => _s ?? _sb?.ToString();

			public void Dispose() => _hPipe.Dispose();
		};

		/// <summary>
		/// Writes a string result for the task that called <see cref="runWait(out string, string, string[])"/> or <see cref="runWait(Action{string}, string, string[])"/> to run this task, or for the program that executed "Au.CL.exe" to run this task with command line like "Au.CL.exe **Script5.cs".
		/// Returns false if this task was not started in such a way. Returns null if failed to write, except when s is null/"".
		/// </summary>
		/// <param name="s">A string. This function does not append newline characters.</param>
		/// <remarks>
		/// The <b>RunWait</b>(Action) overload can read the string in real time.
		/// The <b>RunWait</b>(out string) overload gets all strings joined when the task ends.
		/// The program that executed "Au.CL.exe" to run this task with command line like "Au.CL.exe **Script5.cs" can read the string from the redirected standard output in real time, or the string is written to its console in real time. The string encoding is UTF8; if you use a bat file or cmd.exe and want to get correct Unicode text, execute this before, to change console code page to UTF-8: <c>chcp 65001</c>.
		/// </remarks>
		public static unsafe bool writeResult(string s) {
			var pipeName = Environment.GetEnvironmentVariable("script.writeResult.pipe");
			if (pipeName == null) return false;
			if (!s.NE()) {
				if (!Api.WaitNamedPipe(pipeName, 3000)) goto ge;
				using (var pipe = Api.CreateFile(pipeName, Api.GENERIC_WRITE, 0, default, Api.OPEN_EXISTING, 0)) {
					if (pipe.Is0) goto ge;
					fixed (char* p = s) if (!Api.WriteFile(pipe, p, s.Length * 2, out _)) goto ge;
				}
			}
			return true;
			ge:
			Debug_.PrintNativeError_();
			return false;
		}

		#endregion

		/// <summary>
		/// Adds various features to this script task (running script): tray icon, exit on Ctrl+Alt+Delete, etc.
		/// Tip: in Options -> Templates you can set default code for new scripts.
		/// </summary>
		/// <param name="trayIcon">Add standard tray icon. See <see cref="trayIcon"/>.</param>
		/// <param name="sleepExit">End this process when computer is going to sleep or hibernate.</param>
		/// <param name="lockExit">
		/// End this process when the active desktop has been switched (PC locked, Ctrl+Alt+Delete, screen saver, etc, except UAC consent).
		/// Then to end this process you can use hotkeys Win+L (lock computer) and Ctrl+Alt+Delete.
		/// Most mouse, keyboard, clipboard and window functions don't work when other desktop is active. Many of them then throw exception, and the script would end anyway.
		/// </param>
		/// <param name="debug">Call <see cref="DebugTraceListener.Setup"/>(usePrint: true).</param>
		/// <param name="exception">What to do on unhandled exception (event <see cref="AppDomain.UnhandledException"/>).</param>
		/// <param name="f_">[](xref:caller_info). Don't use. Or set = null to disable script editing via the tray icon.</param>
		/// <exception cref="InvalidOperationException">Already called.</exception>
		/// <remarks>
		/// Calling this function is optional. However it should be called if compiling the script with a non-default compiler (eg Visual Studio) if you want the task behave the same (invariant culture, STAThread, unhandled exception action).
		/// 
		/// Does nothing if role editorExtension.
		/// </remarks>
		public static void setup(bool trayIcon = false, bool sleepExit = false, bool lockExit = false, bool debug = false, UExcept exception = UExcept.Print | UExcept.Exit, [CallerFilePath] string f_ = null) {
			if (role == SRole.EditorExtension) return;
			if (s_setup) throw new InvalidOperationException("script.setup already called");
			s_setup = true;

			s_setupException = exception;
			if (!s_appModuleInit) AppModuleInit_(); //if role miniProgram, called by MiniProgram_.Init; else if default compiler, the call is compiled into code; else called now.

			//info: default false, because slow and rarely used.
			if (debug) DebugTraceListener.Setup(usePrint: true);

			if (trayIcon) {
				TrayIcon_(sleepExit, lockExit, f_: f_);
			} else if (sleepExit || lockExit) {
				Au.run.thread(() => {
					if (sleepExit) {
						var w = wnd.Internal_.CreateWindowDWP(messageOnly: false, t_eocWP = (w, m, wp, lp) => {
							if (m == Api.WM_POWERBROADCAST && wp == Api.PBT_APMSUSPEND) ExitOnSleepOrDesktopSwitch_(sleep: true);
							return Api.DefWindowProc(w, m, wp, lp);
						}); //message-only windows don't receive WM_POWERBROADCAST
					}
					if (lockExit) HookDesktopSwitch_();
					while (Api.GetMessage(out var m) > 0) Api.DispatchMessage(m);
				});
			}
		}
		static bool s_setup;
		[ThreadStatic] static WNDPROC t_eocWP;

		/// <summary>
		/// If role miniProgram or exeProgram, default compiler adds module initializer that calls this. If using other compiler, called from <b>Setup</b>.
		/// </summary>
		[EditorBrowsable(EditorBrowsableState.Never), NoDoc]
		public static void AppModuleInit_() {
			s_appModuleInit = true;

			//#if !DEBUG
			process.thisProcessCultureIsInvariant = true;
			//#endif

			AppDomain.CurrentDomain.ProcessExit += (_, _) => { Exiting_ = true; };

			AppDomain.CurrentDomain.UnhandledException += (_, u) => {
				if (!u.IsTerminating) return; //never seen, but anyway
				Exiting_ = true;
				var e = (Exception)u.ExceptionObject; //probably non-Exception object is impossible in C#
				s_unhandledException = e;
				if (s_setupException.Has(UExcept.Print)) print.it(e);
				if (s_setupException.Has(UExcept.Dialog)) dialog.showError("Task failed", e.ToStringWithoutStack(), flags: DFlags.Wider, expandedText: e.ToString());
				if (s_setupException.Has(UExcept.Exit)) Environment.Exit(-1);
				//if (s_setupException.Has(UExcept.DisableWER)) Api.WerAddExcludedApplication(process.thisExePath, false);
				//info: setup32.dll disables WER for Au.Task.exe and Au.Task32.exe.
			};

			if (role == SRole.ExeProgram) {
				//set STA thread if Main without [MTAThread]
				if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) { //speed: 150 mcs
					if (null == Assembly.GetEntryAssembly().EntryPoint.GetCustomAttribute<MTAThreadAttribute>()) { //1.5 ms
						process.ThisThreadSetComApartment_(ApartmentState.STA); //1.6 ms
					}
				}
			}
		}
		static bool s_appModuleInit;
		static UExcept s_setupException = UExcept.Print | UExcept.Exit;
		internal static Exception s_unhandledException; //for process.thisProcessExit

		internal static bool Exiting_ { get; private set; }

		internal static WinEventHook HookDesktopSwitch_() {
			return new WinEventHook(EEvent.SYSTEM_DESKTOPSWITCH, 0, k => {
				if (miscInfo.isInputDesktop()) return;
				if (0 != process.getProcessId("consent.exe")) return; //UAC
				k.hook.Dispose();
				ExitOnSleepOrDesktopSwitch_(sleep: false);
			});
			//tested: on Win+L works immediately. OS switches desktop 2 times. At first briefly, then makes defaul again, then on key etc switches again to show password field.
		}

		internal static void ExitOnSleepOrDesktopSwitch_(bool sleep) {
			print.it($"<>Info: task <open {path}|||script.setup>{name}<> ended because of {(sleep ? "PC sleep" : "switched desktop")} at {DateTime.Now.ToShortTimeString()}.");
			Environment.Exit(2);
		}

		/// <summary>
		/// Ensures that multiple script tasks that call this function don't run simultaneously. Like C# 'lock' keyword for threads.
		/// </summary>
		/// <param name="mutex">Mutex name. Only tasks that use same mutex cannot run simultaneously.</param>
		/// <param name="wait">If a task is running (other or same), wait max this milliseconds. If 0 (default), does not wait. If -1, waits without a timeout.</param>
		/// <param name="silent">Don't print "cannot run".</param>
		/// <exception cref="InvalidOperationException">Already called.</exception>
		/// <remarks>
		/// If cannot run because a task is running (other or same), calls <c>Environment.Exit(3);</c>.
		/// </remarks>
		public static void single(string mutex = "Au-mutex-script.single", int wait = 0, bool silent = false) {
			//FUTURE: parameter bool endOther. Like meta ifRunning restart.

			var m = Api.CreateMutex(null, false, mutex ?? "Au-mutex-script.single"); //tested: don't need Api.SECURITY_ATTRIBUTES.ForLowIL
			if (default != Interlocked.CompareExchange(ref s_singleMutex, m, default)) { Api.CloseHandle(m); throw new InvalidOperationException(); }
			var r = Api.WaitForSingleObject(s_singleMutex, wait);
			//print.it(r);
			if (r is not (0 or Api.WAIT_ABANDONED)) {
				if (!silent) print.it($"<>Note: script task <open {path}|||script.single>{name}<> cannot run because a task is running.");
				Environment.Exit(3);
			}
			//never mind: should release mutex.
			//	Cannot release in process exit event. It runs in another thread.
			//	Cannot use UsingEndAction, because then caller code must be like 'using var single = script.single();'.
			//return new(() => Api.ReleaseMutex(s_singleMutex));
		}
		static IntPtr s_singleMutex;

		/// <summary>
		/// Adds standard tray icon.
		/// </summary>
		/// <param name="delay">Delay, milliseconds.</param>
		/// <param name="init">Called before showing the tray icon. Can set its properties and event handlers.</param>
		/// <param name="menu">Called before showing context menu. Can add menu items. Menu item actions must not block messages etc for long time; if need, run in other thread or process (<see cref="script.run"/>).</param>
		/// <param name="f_">[](xref:caller_info). Don't use. Or set = null to disable script editing via the tray icon.</param>
		/// <remarks>
		/// Uses other thread. The <i>init</i> and <i>menu</i> actions run in that thread too. It dispatches messages, therefore they also can set timers (<see cref="timerm"/>), create hidden windows, etc. Current thread does not have to dispatch messages.
		/// 
		/// Does nothing if role editorExtension.
		/// </remarks>
		/// <example>
		/// Shows how to change icon and tooltip.
		/// <code><![CDATA[
		/// script.trayIcon(init: t => { t.Icon = icon.stock(StockIcon.HELP); t.Tooltip = "Example"; });
		/// ]]></code>
		/// Shows how to add menu items.
		/// <code><![CDATA[
		/// script.trayIcon(menu: (t, m) => {
		/// 	m["Example"] = o => { dialog.show("Example"); };
		/// 	m["Run other script"] = o => { script.run("Example"); };
		/// });
		/// ]]></code>
		/// </example>
		/// <seealso cref="Au.trayIcon"/>
		public static void trayIcon(int delay = 500, Action<trayIcon> init = null, Action<trayIcon, popupMenu> menu = null, [CallerFilePath] string f_ = null) {
			if (role == SRole.EditorExtension) return;
			TrayIcon_(false, false, delay, init, menu, f_);
		}

		internal static void TrayIcon_(bool sleepExit, bool lockExit, int delay = 500, Action<trayIcon> init = null, Action<trayIcon, popupMenu> menu = null, [CallerFilePath] string f_ = null) {
			Au.run.thread(() => {
				Thread.Sleep(delay);
				var ti = new trayIcon { Tooltip = script.name, sleepExit_ = sleepExit, lockExit_ = lockExit };
				init?.Invoke(ti);
				ti.Icon ??= icon.trayIcon();
				bool canEdit = f_ != null && editor.Available;
				if (canEdit) ti.Click += _ => editor.OpenAndGoToLine(f_, 0);
				ti.MiddleClick += _ => Environment.Exit(2);
				ti.RightClick += e => {
					var m = new popupMenu();
					if (menu != null) {
						menu(ti, m);
						if (m.Last != null && !m.Last.IsSeparator) m.Separator();
					}
					if (canEdit) m["Open script\tClick"] = _ => editor.OpenAndGoToLine(f_, 0);
					m["End task\tM-click" + (sleepExit ? ", Sleep" : null) + (lockExit ? ", Win+L, Ctrl+Alt+Delete" : null)] = _ => Environment.Exit(2);
					if (canEdit) m["End and edit"] = _ => { editor.OpenAndGoToLine(f_, 0); Environment.Exit(2); };
					m.Show(MSFlags.AlignCenterH | MSFlags.AlignRectBottomTop, /*excludeRect: ti.GetRect(out var r1) ? r1 : null,*/ owner: ti.Hwnd);
				};
				ti.Visible = true;
				while (Api.GetMessage(out var m) > 0) {
					Api.TranslateMessage(m);
					Api.DispatchMessage(m);
				}
			});
		}

#if false //not sure is it useful. Unreliable. Should use hook to detect user-pressed, but then UAC makes less reliable. Can instead use script.setup (Win+L, Ctrl+Alt+Delete and sleep-exit are reliable). If useful, can instead add script.setup parameter keyExit.
		/// <summary>
		/// Sets a hotkey that ends this process.
		/// </summary>
		/// <param name="hotkey">
		/// See <see cref="keys.more.Hotkey.Register"/>.
		/// Good hotkeys are Pause, madia/browser/launch keys, Insert, Up, PageUp. Don't use F12, Shift+numpad, hotkeys with Esc, Space, Tab, Sleep. Don't use keys and modifiers used in script (<b>keys.send</b> etc).
		/// </param>
		/// <param name="exitCode">Process exit code. See <see cref="Environment.Exit"/>.</param>
		/// <exception cref="InvalidOperationException">Calling second time.</exception>
		/// <remarks>
		/// This isn't a reliable way to end script. Consider <see cref="Setup"/> parameters <i>sleepExit</i>, <i>lockExit</i>.
		/// - Does not work if the hotkey is registered by any process or used by Windows.
		/// - Does not work or is delayed if user input is blocked by an <see cref="inputBlocker"/> or keyboard hook. For example <see cref="keys.send"/> and similar functions block input by default.
		/// - Most single-key and Shift+key hotkeys don't work when the active window has higher UAC integrity level (eg admin) than this process. Media keys may work.
		/// - If several processes call this function with same hotkey, the hotkey ends one process at a time.
		/// </remarks>
		public static void exitHotkey(KHotkey hotkey, int exitCode = 0) {
			if (Role == SRole.EditorExtension) return;
			run.thread(() => {
				var (mod, key) = keys.more.Hotkey.Normalize_(hotkey);
				var atom = Api.GlobalAddAtom("Au.EndHotkey");

#if true
				while (!Api.RegisterHotKey(default, atom, mod, key)) Thread.Sleep(100); //several tasks may register same hotkey
				Api.GetMessage(out _, default, Api.WM_HOTKEY, Api.WM_HOTKEY);
				Environment.Exit(exitCode);
#else
				//workaround for: no WM_HOTKEY if the active window has higher UAC IL.
				//	Use WM_SETHOTKEY too. It works with higher IL windows.
				//	But can't use it alone. It is unreliable. Incorrectly documented, partially working, etc. Eg does not work when taskbar is active.
				//rejected. Too dirty and unreliable.
				//	And don't encourage to use a hotkey to end script, as it is unreliable. Let use EndOnDesktopSwitch or EndOnSleep.

				bool workaround = hotkey.Mod is 0 or KMod.Shift && !uacInfo.isAdmin;
				var timerW = workaround ? Api.SetTimer(default, 0, 1000, null) : default; //avoid creating windows etc for short scripts

				var timerR = Api.RegisterHotKey(default, atom, mod, key) ? default : Api.SetTimer(default, 0, 100, null);

				while (Api.GetMessage(out var m) > 0) {
					if (m.message == Api.WM_HOTKEY && m.wParam == atom) {
						Environment.Exit(exitCode);
					}

					if (m.message == Api.WM_TIMER && m.wParam != default) {
						if (m.wParam == timerR) {
							//workaround for: fails to register if another task uses the same hotkey
							if (Api.RegisterHotKey(default, atom, mod, key)) Api.KillTimer(default, timerR);
						} else if (m.wParam == timerW) {
							Api.KillTimer(default, timerW);
							var w = WndUtil.CreateMessageOnlyWindow((w, m, wp, lp) => {
								//WndUtil.PrintMsg(w, m, wp, lp); //no WM_SYSCOMMAND
								//if (m == Api.WM_ACTIVATE && wp == 1) w.Post(Api.WM_HOTKEY, atom);
								if (m == Api.WM_ACTIVATE && wp == 1) Environment.Exit(exitCode);
								return Api.DefWindowProc(w, m, wp, lp);
							}, wnd.Internal_.WindowClassDWP);
							w.SetStyle(WS.VISIBLE, WSFlags.Add); //or w.ShowL(true); //does not make visible because it is a message-only window, but enables wm_sethotkey
							var r = Api.DefWindowProc(w, Api.WM_SETHOTKEY, Math2.MakeWord((int)key, (int)hotkey.Mod), default); //not MakeLparam
						}
					}

					Api.DispatchMessage(m);
				}
#endif
				//don't delete hotkey/atom/window, because in most cases the process ends not because of the hotkey
			}, background: true, sta: false);
		}
#endif

		/// <summary>
		/// Finds editor's message-only window used with WM_COPYDATA etc.
		/// </summary>
		internal static wnd WndMsg_ => s_wndMsg.FindFast(null, "Aedit.m3gVxcTJN02pDrHiQ00aSQ", true);
		static wnd.Cached_ s_wndMsg;

		/// <summary>
		/// Contains static functions to interact with the script editor, if available.
		/// </summary>
		public static class editor
		{
			/// <summary>
			/// Returns true if editor is running.
			/// </summary>
			public static bool Available => !WndMsg_.Is0;

			/// <summary>
			/// Opens the specified source file (script etc) and sets code editor's current position at the start of the specified line.
			/// Does nothing if editor isn't running.
			/// </summary>
			/// <param name="file">Source file. Can be full path, or relative path in workspace, or file name with ".cs".</param>
			/// <param name="line">1-based line index. If 0, just opens file.</param>
			public static void OpenAndGoToLine(string file, int line) {
				var w = WndMsg_; if (w.Is0) return;
				Api.AllowSetForegroundWindow(w.ProcessId);
				WndCopyData.Send<char>(w, 4, file, line);
			}

			/// <summary>
			/// Gets icon string in specified format.
			/// Returns null if editor isn't running or if file does not exist.
			/// </summary>
			/// <param name="file">File/folder path etc, or icon name. See <see cref="EGetIcon"/>.</param>
			/// <param name="what">The format of input and output strings.</param>
			public static string GetIcon(string file, EGetIcon what) {
				var del = IconNameToXaml_;
				if (del != null) return del(file, what);

				var w = WndMsg_; if (w.Is0) return null;
				WndCopyData.SendReceive<char>(w, (int)Math2.MakeLparam(10, (int)what), file, out string r);
				return r;
				//rejected: add option to get serialized Bitmap instead. Now loads XAML in this process. It is 230 ms and +27 MB.
				//	Nothing good if the toolbar etc also uses XAML icons directly, eg for non-script items. And serializing is slow.
				//	Now not actual because of cache.
			}

			//FUTURE: if editor isn't running, let GetIcon("icon name") try to get icon directly from database if available.
			//public static string IconDatabasePath { get; set; }

			/// <summary>
			/// Editor sets this. Library uses it to avoid sendmessage.
			/// </summary>
			internal static Func<string, EGetIcon, string> IconNameToXaml_;
		}
	}
}

namespace Au.Types
{
	/// <summary>
	/// <see cref="script.role"/>.
	/// </summary>
	public enum SRole
	{
		/// <summary>
		/// The task runs as normal .exe program.
		/// It can be started from editor or not. It can run on computers where editor not installed.
		/// </summary>
		ExeProgram,

		/// <summary>
		/// The task runs in Au.Task.exe process, started from editor.
		/// </summary>
		MiniProgram,

		/// <summary>
		/// The task runs in editor process.
		/// </summary>
		EditorExtension,
	}

	/// <summary>
	/// Flags for <see cref="script.setup"/> parameter <i>exception</i>. Defines what to do on unhandled exception.
	/// Default flags is <b>Print</b> and <b>Exit</b>, even if <b>Setup</b> not called (with default compiler only).
	/// </summary>
	[Flags]
	public enum UExcept
	{
		/// <summary>
		/// Display exception info in output.
		/// </summary>
		Print = 1,

		/// <summary>
		/// Show dialog with exception info.
		/// </summary>
		Dialog = 2,

		/// <summary>
		/// Call <see cref="Environment.Exit"/>. It prevents slow exit (Windows error reporting, writing events to the Windows event log, etc).
		/// Note: then instead of <see cref="AppDomain.UnhandledException"/> event is <see cref="AppDomain.ProcessExit"/> event. But <see cref="process.thisProcessExit"/> indicates exception as usually.
		/// Info: the editor setup program disables Windows error reporting for tasks with role miniProgram (default). See <msdn>WerAddExcludedApplication</msdn>.
		/// </summary>
		Exit = 4,

		/// <summary>
		/// Display exception info in output, show dialog with exception info, and call <see cref="Environment.Exit"/>.
		/// Same as <c>UExcept.Print | UExcept.Dialog | UExcept.Exit</c>.
		/// </summary>
		PrintDialogExit = Print | Dialog | Exit,

		//rejected. Setup disables for miniProgram tasks.
		///// <summary>
		///// Disable Windows error reporting for this program.
		///// Note: calls <msdn>WerAddExcludedApplication</msdn>, which saves this setting for this program in the registry. To undo it, call <msdn>WerRemoveExcludedApplication</msdn>.
		///// Not used with flag <b>Exit</b>.
		///// </summary>
		//DisableWER = 8,
	}

	/// <summary>
	/// For <see cref="script.editor.GetIcon"/>.
	/// </summary>
	public enum EGetIcon
	{
		/// <summary>
		/// Input is a file or folder in current workspace. Can be relative path in workspace (like @"\Folder\File.cs") or full path or filename.
		/// Output must be icon name, like "*Pack.Icon color", where color is like #RRGGBB or color name. See menu -> Tools -> Icons.
		/// </summary>
		PathToIconName,

		/// <summary>
		/// Input is a file or folder in current workspace (see <b>PathToIconName</b>).
		/// Output must be icon XAML.
		/// </summary>
		PathToIconXaml,

		/// <summary>
		/// Input is icon name (see <b>PathToIconName</b>).
		/// Output must be icon XAML.
		/// </summary>
		IconNameToXaml,

		//PathToGdipBitmap,
		//IconNameToGdipBitmap,
	}

	/// <summary>
	/// The default compiler adds this attribute to the main assembly if role is miniProgram or exeProgram.
	/// </summary>
	[AttributeUsage(AttributeTargets.Assembly)]
	public sealed class PathInWorkspaceAttribute : Attribute {
		/// <summary>Path of main file in workspace.</summary>
		public readonly string Path;

		///
		public PathInWorkspaceAttribute(string path) { Path = path; }
	}

	/// <summary>
	/// The default compiler adds this attribute to the main assembly if using non-default references (meta r) that aren't in editor's folder or its subfolder "Libraries". Allows to find them at run time. Only if role miniProgram (default).
	/// </summary>
	[AttributeUsage(AttributeTargets.Assembly)]
	public sealed class RefPathsAttribute : Attribute
	{
		/// <summary>Dll paths separated with |.</summary>
		public readonly string Paths;

		/// <param name="paths">Dll paths separated with |.</param>
		public RefPathsAttribute(string paths) { Paths = paths; }
	}
}
