﻿using System;
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
using System.Linq;

using Au.Types;

namespace Au
{
	partial class filesystem
	{
		partial class more
		{
			/// <summary>
			/// Temporarily disables file system redirection, to allow this 32-bit process access the 64-bit System32 directory.
			/// </summary>
			public struct DisableRedirection : IDisposable
			{
				bool _redirected;
				IntPtr _redirValue;

				/// <summary>
				/// If <see cref="osVersion.is32BitProcessAnd64BitOS"/>, calls API <msdn>Wow64DisableWow64FsRedirection</msdn>, which disables file system redirection.
				/// The caller can call this without checking OS and process bitness. This function checks it and it is fast.
				/// Always call <see cref="Revert"/> or <b>Dispose</b>, for example use <b>finally</b> or <b>using</b> statement. Not calling it is more dangerous than a memory leak. It is not called by GC.
				/// </summary>
				public void Disable() {
					if (osVersion.is32BitProcessAnd64BitOS)
						_redirected = Api.Wow64DisableWow64FsRedirection(out _redirValue);
				}

				/// <summary>
				/// If redirected, calls API <msdn>Wow64RevertWow64FsRedirection</msdn>.
				/// </summary>
				public void Revert() {
					if (_redirected)
						_redirected = !Api.Wow64RevertWow64FsRedirection(_redirValue);
				}

				/// <summary>
				/// Returns true if <see cref="osVersion.is32BitProcessAnd64BitOS"/> is true and path starts with <see cref="folders.System"/>.
				/// Most such paths are redirected, therefore you may want to disable redirection with this class.
				/// </summary>
				/// <param name="path">Normalized path. This function does not normalize. Also it is unaware of <c>@"\\?\"</c>.</param>
				public static bool IsSystem64PathIn32BitProcess(string path) {
					return 0 != _IsSystem64PathIn32BitProcess(path);
				}

				static int _IsSystem64PathIn32BitProcess(string path) {
					if (!osVersion.is32BitProcessAnd64BitOS) return 0;
					string sysDir = folders.System;
					if (!path.Starts(sysDir, true)) return 0;
					int len = sysDir.Length;
					if (path.Length > len && !pathname.IsSepChar_(path[len])) return 0;
					return len;
				}

				/// <summary>
				/// If <see cref="osVersion.is32BitProcessAnd64BitOS"/> is true and path starts with <see cref="folders.System"/>, replaces that path part with <see cref="folders.SystemX64"/>.
				/// It disables redirection to <see cref="folders.SystemX86"/> for that path.
				/// </summary>
				/// <param name="path">Normalized path. This function does not normalize. Also it is unaware of <c>@"\\?\"</c>.</param>
				/// <param name="ifExistsOnlyThere">Don't replace path if the file or directory exists in the redirected folder or does not exist in the non-redirected folder.</param>
				public static string GetNonRedirectedSystemPath(string path, bool ifExistsOnlyThere = false) {
					int i = _IsSystem64PathIn32BitProcess(path);
					if (i == 0) return path;
					if (ifExistsOnlyThere && filesystem.exists(path)) return path;
					var s = path.ReplaceAt(0, i, folders.SystemX64);
					if (ifExistsOnlyThere && !filesystem.exists(s)) return path;
					return s;
				}

				/// <summary>
				/// Calls <see cref="Revert"/>.
				/// </summary>
				public void Dispose() {
					Revert();
				}
			}
		}
	}
}