﻿//#define USE_WTS

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
using System.Globalization;

using Au.Types;
using Au.Util;

namespace Au
{
	/// <summary>
	/// Contains static functions to work with processes (find, enumerate, get basic info, etc).
	/// </summary>
	/// <seealso cref="AThisProcess"/>
	/// <seealso cref="ATask"/>
	/// <seealso cref="Process"/>
	public static unsafe class AProcess
	{
		/// <summary>
		/// Gets process executable file name (like "notepad.exe") or full path.
		/// Returns null if fails.
		/// </summary>
		/// <param name="processId">Process id.</param>
		/// <param name="fullPath">
		/// Get full path.
		/// Note: Fails to get full path if the process belongs to another user session, unless current process is running as administrator; also fails to get full path of some system processes.
		/// </param>
		/// <param name="noSlowAPI">When the fast API QueryFullProcessImageName fails, don't try to use another much slower API WTSEnumerateProcesses. Not used if <i>fullPath</i> is true.</param>
		/// <remarks>
		/// This function is much slower than getting window name or class name.
		/// </remarks>
		/// <seealso cref="AWnd.ProgramName"/>
		/// <seealso cref="AWnd.ProgramPath"/>
		/// <seealso cref="AWnd.ProcessId"/>
		public static string GetName(int processId, bool fullPath = false, bool noSlowAPI = false) {
			if (processId == 0) return null;
			string R = null;

			//var t = ATime.PerfMicroseconds;
			//if(s_time != 0) AOutput.Write(t - s_time);
			//s_time = t;

			using var ph = Handle_.OpenProcess(processId);
			if (!ph.Is0) {
				//In non-admin process fails if the process is of another user session.
				//Also fails for some system processes: nvvsvc, nvxdsync, dwm. For dwm fails even in admin process.

				//getting native path is faster, but it gets like "\Device\HarddiskVolume5\Windows\System32\notepad.exe" and I don't know API to convert to normal
				if (_QueryFullProcessImageName(ph, !fullPath, out var s)) {
					R = s;
					if (APath.IsPossiblyDos_(R)) {
						if (fullPath || _QueryFullProcessImageName(ph, false, out s)) {
							R = APath.ExpandDosPath_(s);
							if (!fullPath) R = _GetFileName(R);
						}
					}
				}
			} else if (!noSlowAPI && !fullPath) { //the slow way. Can get only names, not paths.
				using (new _AllProcesses(out var p, out int n)) {
					for (int i = 0; i < n; i++)
						if (p[i].processID == processId) {
							R = p[i].GetName(cannotOpen: true);
							break;
						}
				}
			}

			return R;

			//Would be good to cache process names here. But process id can be reused quickly. Use GetNameCached_ instead.
			//	tested: a process id is reused after creating ~100 processes (and waiting until exits). It takes ~2 s.
			//	The window finder is optimized to call this once for each process and not for each window.
		}

		/// <summary>
		/// Same as GetName, but faster when called several times for same window, like <c>if(w.ProgramName=="A" || w.ProgramName=="B")</c>.
		/// </summary>
		internal static string GetNameCached_(AWnd w, int processId, bool fullPath = false) {
			if (processId == 0) return null;
			var cache = _LastWndProps.OfThread;
			cache.Begin(w);
			var R = fullPath ? cache.ProgramPath : cache.ProgramName;
			if (R == null) {
				R = GetName(processId, fullPath);
				if (fullPath) cache.ProgramPath = R; else cache.ProgramName = R;
			}
			return R;
		}

		class _LastWndProps
		{
			AWnd _w;
			long _time;
			internal string ProgramName, ProgramPath;

			internal void Begin(AWnd w) {
				var t = Api.GetTickCount64();
				if (w != _w || t - _time > 300) { _w = w; ProgramName = ProgramPath = null; }
				_time = t;
			}

			[ThreadStatic] static _LastWndProps _ofThread;
			internal static _LastWndProps OfThread => _ofThread ??= new _LastWndProps();
		}

		[SkipLocalsInit]
		static bool _QueryFullProcessImageName(IntPtr hProcess, bool getFilename, out string s) {
			s = null;
			using ABuffer<char> b = new(null);
			for (; ; b.More()) {
				int n = b.n;
				if (Api.QueryFullProcessImageName(hProcess, getFilename, b.p, ref n)) {
					s = getFilename ? _GetFileName(b.p, n) : new(b.p, 0, n);
					return true;
				}
				if (ALastError.Code != Api.ERROR_INSUFFICIENT_BUFFER) return false;
			}
		}

#if USE_WTS //simple, safe, but ~2 times slower
		struct _AllProcesses :IDisposable
		{
			ProcessInfo_* _p;

			public _AllProcesses(out ProcessInfo_* p, out int count)
			{
				if(WTSEnumerateProcessesW(default, 0, 1, out p, out count)) _p = p; else _p = null;
			}

			public void Dispose()
			{
				if(_p != null) WTSFreeMemory(_p);
			}

			[DllImport("wtsapi32.dll", SetLastError = true)]
			static extern bool WTSEnumerateProcessesW(IntPtr serverHandle, uint reserved, uint version, out ProcessInfo_* ppProcessInfo, out int pCount);

			[DllImport("wtsapi32.dll", SetLastError = false)]
			static extern void WTSFreeMemory(ProcessInfo_* memory);
		}
#else //the .NET Process class uses this. But it creates about 0.4 MB of garbage.
		struct _AllProcesses : IDisposable
		{
			ProcessInfo_* _p;

			public _AllProcesses(out ProcessInfo_* pi, out int count) {
				_p = null;
				Api.SYSTEM_PROCESS_INFORMATION* b = null;
				try {
					for (int na = 500_000; ;) {
						AMemory.FreeAlloc(ref b, na);
						int status = Api.NtQuerySystemInformation(5, b, na, out na);
						//AOutput.Write(na); //~300_000, Win10, year 2021
						if (status == 0) break;
						if (status != Api.STATUS_INFO_LENGTH_MISMATCH) throw new AuException(status);
					}

					int nProcesses = 0, nbNames = 0;
					for (var p = b; ; p = (Api.SYSTEM_PROCESS_INFORMATION*)((byte*)p + p->NextEntryOffset)) {
						nProcesses++;
						nbNames += p->NameLength; //bytes, not chars
						if (p->NextEntryOffset == 0) break;
					}
					count = nProcesses;
					_p = (ProcessInfo_*)AMemory.Alloc(nProcesses * sizeof(ProcessInfo_) + nbNames);
					ProcessInfo_* r = _p;
					char* names = (char*)(_p + nProcesses);
					for (var p = b; ; p = (Api.SYSTEM_PROCESS_INFORMATION*)((byte*)p + p->NextEntryOffset), r++) {
						r->processID = (int)p->UniqueProcessId;
						r->sessionID = (int)p->SessionId;
						int len = p->NameLength / 2;
						r->nameLen = len;
						if (len > 0) {
							//copy name to _p memory because it's in the huge buffer that will be released in this func
							r->namePtr = names;
							AMemory.Copy((char*)p->NamePtr, names, len * 2);
							names += len;
						} else r->namePtr = null; //Idle

						if (p->NextEntryOffset == 0) break;
					}
					pi = _p;
				}
				finally { AMemory.Free(b); }
			}

			public void Dispose() {
				AMemory.Free(_p);
			}
		}
#endif

		//Use ProcessInfo_ and ProcessInfo because with WTSEnumerateProcessesW _ProcessName must be IntPtr, and then WTSFreeMemory frees its memory.
		//	AllProcesses() converts ProcessInfo_ to ProcessInfo where Name is string. Almost same speed.
		internal unsafe struct ProcessInfo_
		{
#pragma warning disable 649 //never used
			public int sessionID;
			public int processID;
			public char* namePtr;
			public int nameLen;
#if USE_WTS
			public IntPtr userSid;
#endif
#pragma warning restore 649

			/// <summary>
			/// Gets process executable file name (like "notepad.exe"). Not full path.
			/// If contains looks like a DOS path and !cannotOpen, tries to unexpand DOS path.
			/// Don't call multiple times, because always converts from raw char*.
			/// </summary>
			public string GetName(bool cannotOpen = false) {
				if (namePtr == null) {
					if (processID == 0) return "Idle";
					return null;
				}
				string R = new(namePtr, 0, nameLen);
				if (!cannotOpen && APath.IsPossiblyDos_(R)) {
					using var ph = Handle_.OpenProcess(processID);
					if (!ph.Is0 && _QueryFullProcessImageName(ph, false, out var s)) {
						R = _GetFileName(APath.ExpandDosPath_(s));
					}
				}
				return R;
			}
		}

		/// <summary>
		/// Gets basic info of all processes: name, id, session id.
		/// </summary>
		/// <param name="ofThisSession">Get processes only of this user session (skip services etc).</param>
		/// <exception cref="AuException">Failed. Unlikely.</exception>
		public static ProcessInfo[] AllProcesses(bool ofThisSession = false) {
			using (new _AllProcesses(out var p, out int n)) {
				if (n == 0) throw new AuException();
				int sessionId = 0, ns = n;
				if (ofThisSession) {
					sessionId = AThisProcess.SessionId;
					for (int i = 0; i < n; i++) if (p[i].sessionID != sessionId) ns--;
				}
				var a = new ProcessInfo[ns];
				for (int i = 0, j = 0; i < n; i++) {
					if (ofThisSession && p[i].sessionID != sessionId) continue;
					a[j++] = new ProcessInfo(p[i].GetName(), p[i].processID, p[i].sessionID);
				}
				return a;
			}
		}

		/// <summary>
		/// Gets process ids of all processes of the specified program.
		/// Returns array containing 0 or more elements.
		/// </summary>
		/// <param name="processName">
		/// Process executable file name, like "notepad.exe".
		/// String format: [](xref:wildcard_expression).
		/// </param>
		/// <param name="fullPath">
		/// <i>processName</i> is full path.
		/// Note: Fails to get full path if the process belongs to another user session, unless current process is running as administrator; also fails to get full path of some system processes.
		/// </param>
		/// <param name="ofThisSession">Get processes only of this user session.</param>
		/// <exception cref="ArgumentException">
		/// - <i>processName</i> is "" or null.
		/// - Invalid wildcard expression (<c>"**options "</c> or regular expression).
		/// </exception>
		public static int[] GetProcessIds([ParamString(PSFormat.AWildex)] string processName, bool fullPath = false, bool ofThisSession = false) {
			if (processName.NE()) throw new ArgumentException();
			List<int> a = null;
			GetProcessesByName_(ref a, processName, fullPath, ofThisSession);
			return a?.ToArray() ?? Array.Empty<int>();
		}

		/// <summary>
		/// Gets process id of the first found process of the specified program.
		/// Returns 0 if not found.
		/// More info: <see cref="GetProcessIds"/>.
		/// </summary>
		/// <exception cref="ArgumentException"/>
		public static int GetProcessId([ParamString(PSFormat.AWildex)] string processName, bool fullPath = false, bool ofThisSession = false) {
			if (processName.NE()) throw new ArgumentException();
			List<int> a = null;
			return GetProcessesByName_(ref a, processName, fullPath, ofThisSession, true);
		}

		internal static int GetProcessesByName_(ref List<int> a, AWildex processName, bool fullPath = false, bool ofThisSession = false, bool first = false) {
			a?.Clear();

			int sessionId = ofThisSession ? AThisProcess.SessionId : 0;

			using (new _AllProcesses(out var p, out int n)) {
				for (int i = 0; i < n; i++) {
					if (ofThisSession && p[i].sessionID != sessionId) continue;
					string s = fullPath ? GetName(p[i].processID, true) : p[i].GetName();
					if (s == null) continue;

					if (processName.Match(s)) {
						if (first) return p[i].processID;
						a ??= new List<int>();
						a.Add(p[i].processID);
					}
				}
			}

			return 0;
		}

		static string _GetFileName(char* s, int len) {
			if (s == null) return null;
			char* ss = s + len;
			for (; ss > s; ss--) if (ss[-1] == '\\' || ss[-1] == '/') break;
			return new string(ss, 0, len - (int)(ss - s));
		}

		static string _GetFileName(string s) {
			fixed (char* p = s) return _GetFileName(p, s.Length);
		}

		/// <summary>
		/// Gets version info of process executable file.
		/// Return null if fails.
		/// </summary>
		/// <param name="processId">Process id.</param>
		public static FileVersionInfo GetVersionInfo(int processId) {
			var s = GetName(processId, true);
			if (s != null) {
				try { return FileVersionInfo.GetVersionInfo(s); } catch { }
			}
			return null;
		}

		/// <summary>
		/// Gets description of process executable file.
		/// Return null if fails.
		/// </summary>
		/// <param name="processId">Process id.</param>
		/// <remarks>
		/// Calls <see cref="GetVersionInfo"/> and <see cref="FileVersionInfo.FileDescription"/>.
		/// </remarks>
		public static string GetDescription(int processId) => GetVersionInfo(processId)?.FileDescription;

		/// <summary>
		/// Gets process id from handle.
		/// Returns 0 if failed. Supports <see cref="ALastError"/>.
		/// Calls API <msdn>GetProcessId</msdn>.
		/// </summary>
		/// <param name="processHandle">Process handle.</param>
		public static int ProcessIdFromHandle(IntPtr processHandle) => Api.GetProcessId(processHandle); //fast

		/// <summary>
		/// Terminates (ends) the specified process.
		/// Returns false if failed. Supports <see cref="ALastError"/>.
		/// </summary>
		/// <param name="processId">Process id.</param>
		/// <param name="exitCode">Process exit code.</param>
		/// <remarks>
		/// This function does not try to end process "softly" (close main window). Unsaved data will be lost.
		/// Alternatives: run taskkill.exe or pskill.exe (download). See <see cref="ARun.Console"/>. More info on the internet.
		/// </remarks>
		public static bool Terminate(int processId, int exitCode = 0) {
			if (Api.WTSTerminateProcess(default, processId, exitCode)) return true;
			bool invalidParam = ALastError.Code == Api.ERROR_INVALID_PARAMETER;
			if (!invalidParam) {
				using var hp = Handle_.OpenProcess(processId, Api.PROCESS_TERMINATE);
				if (!hp.Is0) {
					return Api.TerminateProcess(hp, exitCode);
				}
			}
			return false;
		}

		/// <summary>
		/// Terminates (ends) all processes of the specified program or programs.
		/// Returns the number of successfully terminated processes.
		/// </summary>
		/// <param name="processName">
		/// Process executable file name, like "notepad.exe".
		/// String format: [](xref:wildcard_expression).
		/// </param>
		/// <param name="allSessions">Processes of any user session. If false (default), only processes of this user session.</param>
		/// <param name="exitCode">Process exit code.</param>
		/// <exception cref="ArgumentException">
		/// - <i>processName</i> is "" or null.
		/// - Invalid wildcard expression (<c>"**options "</c> or regular expression).
		/// </exception>
		public static int Terminate(string processName, bool allSessions = false, int exitCode = 0) {
			int n = 0;
			foreach (int pid in GetProcessIds(processName, ofThisSession: !allSessions)) {
				if (Terminate(pid, exitCode)) n++;
			}
			return n;
		}

		/// <summary>
		/// Suspends or resumes the specified process.
		/// Returns false if failed. Supports <see cref="ALastError"/>.
		/// </summary>
		/// <param name="suspend">true suspend, false resume.</param>
		/// <param name="processId">Process id.</param>
		/// <remarks>
		/// If suspended multiple times, must be resumed the same number of times.
		/// </remarks>
		public static bool Suspend(bool suspend, int processId) {
			using var hp = Handle_.OpenProcess(processId, Api.PROCESS_SUSPEND_RESUME);
			if (!hp.Is0) {
				int status = suspend ? Api.NtSuspendProcess(hp) : Api.NtResumeProcess(hp);
				ALastError.Code = status;
				return status == 0;
			}
			return false;
		}

		/// <summary>
		/// Suspends or resumes all processes of the specified program or programs.
		/// Returns the number of successfully suspended/resumed processes.
		/// </summary>
		/// <param name="suspend">true suspend, false resume.</param>
		/// <param name="processName">
		/// Process executable file name, like "notepad.exe".
		/// String format: [](xref:wildcard_expression).
		/// </param>
		/// <param name="allSessions">Processes of any user session. If false (default), only processes of this user session.</param>
		/// <exception cref="ArgumentException">
		/// - <i>processName</i> is "" or null.
		/// - Invalid wildcard expression (<c>"**options "</c> or regular expression).
		/// </exception>
		/// <remarks>
		/// If suspended multiple times, must be resumed the same number of times.
		/// </remarks>
		public static int Suspend(bool suspend, string processName, bool allSessions = false) {
			int n = 0;
			foreach (int pid in GetProcessIds(processName, ofThisSession: !allSessions)) {
				if (Suspend(suspend, pid)) n++;
			}
			return n;
		}

		/// <summary>
		/// Gets user session id of a process.
		/// Returns -1 if failed. Supports <see cref="ALastError"/>.
		/// Calls API <msdn>ProcessIdToSessionId</msdn>.
		/// </summary>
		/// <param name="processId">Process id.</param>
		public static int GetSessionId(int processId) {
			if (!Api.ProcessIdToSessionId(processId, out var R)) return -1;
			return R;
		}
	}
}

namespace Au.Types
{
	/// <summary>
	/// Contains process name (like "notepad.exe"), id, name and user session id.
	/// </summary>
	public record ProcessInfo(string Name, int Id, int SessionId);
	//use record to auto-implement ==, eg for code like var a=AProcess.AllProcesses(); 5.s(); AOutput.Write(AProcess.AllProcesses().Except(a));

	///// <summary>
	///// Contains process id, name and session id.
	///// </summary>
	//public struct ProcessInfo
	//{
	//	/// <summary>Executable file name, like "notepad.exe".</summary>
	//	public string Name;

	//	/// <summary>Process id.</summary>
	//	public int Id;

	//	/// <summary>User session id.</summary>
	//	public int SessionId;

	//	//public IntPtr UserSid; //where is its memory?

	//	///
	//	public ProcessInfo(int session, int pid, string name) {
	//		SessionId = session; Id = pid; Name = name;
	//	}

	//	///
	//	public override string ToString() => Name;
	//}
}
