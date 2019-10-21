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
using Microsoft.Win32;
using System.Runtime.ExceptionServices;
using System.Linq;

using Au.Types;
using static Au.AStatic;

namespace Au
{
	partial class AFile
	{
		/// <summary>
		/// Miscellaneous rarely used file/directory functions.
		/// </summary>
		public static class More
		{

#if false //currently not used
			/// <summary>
			/// Gets HKEY_CLASSES_ROOT registry key of file type or protocol.
			/// The key usually contains subkeys "shell", "DefaultIcon", sometimes "shellex" and more.
			/// For example, for ".txt" can return "txtfile", for ".cs" - "VisualStudio.cs.14.0".
			/// Looks in "HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts" and in HKEY_CLASSES_ROOT.
			/// Returns null if the type/protocol is not registered.
			/// Returns null if fileType does not end with ".extension" and does not start with "protocol:"; also if starts with "shell:".
			/// </summary>
			/// <param name="fileType">
			/// File type extension like ".txt" or protocol like "http:".
			/// Can be full path or URL; the function gets extension or protocol from the string.
			/// Can start with %environment variable%.
			/// </param>
			/// <param name="isFileType">Don't parse fileType, it does not contain full path or URL or environment variables. It is ".ext" or "protocol:".</param>
			/// <param name="isURL">fileType is URL or protocol like "http:". Used only if isFileType == true, ie it is protocol.</param>
			internal static string GetFileTypeOrProtocolRegistryKey(string fileType, bool isFileType, bool isURL)
			{
				if(!isFileType) fileType = GetExtensionOrProtocol(fileType, out isURL);
				else if(isURL) fileType = fileType.RemoveSuffix(1); //"proto:" -> "proto"
				if(Empty(fileType)) return null;

				string R, userChoiceKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts\" + fileType + @"\UserChoice";
				if(ARegistry.GetString(out R, "ProgId", userChoiceKey)) return R;
				if(isURL) return fileType;
				if(ARegistry.GetString(out R, "", fileType, Registry.ClassesRoot)) return R;
				return null;

				//note: IQueryAssociations.GetKey is very slow.
			}

			/// <summary>
			/// Gets file path extension like ".txt" or URL protocol like "http".
			/// Returns null if path does not end with ".extension" and does not start with "protocol:"; also if starts with "shell:".
			/// </summary>
			/// <param name="path">File path or URL. Can be just extension like ".txt" or protocol like "http:".</param>
			/// <param name="isProtocol">Receives true if URL or protocol.</param>
			internal static string GetExtensionOrProtocol(string path, out bool isProtocol)
			{
				isProtocol = false;
				if(Empty(path)) return null;
				if(!PathIsExtension(path)) {
					int i = path.IndexOf(':');
					if(i > 1) {
						path = path.Remove(i); //protocol
						if(path == "shell") return null; //eg "shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"
						isProtocol = true;
					} else {
						path = APath.GetExtension(path);
						if(Empty(path)) return null;
					}
				}
				return path;
			}
#endif

			/// <summary>
			/// Gets <see cref="FileId"/> of a file or directory.
			/// Returns false if fails. Supports <see cref="ALastError"/>.
			/// </summary>
			/// <param name="path">Full path. Supports environment variables (see <see cref="APath.ExpandEnvVar"/>).</param>
			/// <param name="fileId"></param>
			public static unsafe bool GetFileId(string path, out FileId fileId)
			{
				path = APath.LibNormalizeMinimally(path, false);
				fileId = new FileId();
				using var h = Api.CreateFile(path, Api.FILE_READ_ATTRIBUTES, Api.FILE_SHARE_ALL, default, Api.OPEN_EXISTING, Api.FILE_FLAG_BACKUP_SEMANTICS);
				if(h.Is0) return false;
				if(!Api.GetFileInformationByHandle(h, out var k)) return false;
				fileId.VolumeSerialNumber = (int)k.dwVolumeSerialNumber;
				fileId.FileIndex = (long)((ulong)k.nFileIndexHigh << 32 | k.nFileIndexLow);
				return true;
			}

			/// <summary>
			/// Calls <see cref="GetFileId"/> for two paths and returns true if both calls succeed and the ids are equal.
			/// Paths should be normalized. They are passed to API unmodified.
			/// </summary>
			/// <param name="path1"></param>
			/// <param name="path2"></param>
			internal static bool LibIsSameFile(string path1, string path2)
			{
				//try to optimize. No, it's unreliable.
				//int i1 = path1.LastIndexOfAny(s_sep3), i2 = path2.LastIndexOfAny(s_sep3);
				//if(i1 >= 0 && i2 >= 0 && path1[i1] != '~' && path2[i2] != '~') {
				//	i1++; i2++;
				//	if()
				//}

				var ok1 = GetFileId(path1, out var fid1);
				var ok2 = GetFileId(path2, out var fid2);
				if(ok1 && ok2) return fid1 == fid2;
				PrintWarning("GetFileId failed"); //CONSIDER: throw
				return false;
			}

			//static char[] s_sep3 = { '\\', '/', '~' };
#if false
		//this is ~300 times slower than AFile.Move. SHFileOperation too. Use only for files or other shell items in virtual folders. Unfinished.
		public static void RenameFileOrDirectory(string path, string newName)
		{
			APerf.First();
			if(APath.IsInvalidFileName(newName)) throw new ArgumentException("Invalid filename.", nameof(newName));
			path = _PreparePath(path, nameof(path));

			APerf.Next();
			var si = _ShellItem(path, "*rename");
			APerf.Next();
			var fo = new Api.FileOperation() as Api.IFileOperation;
			APerf.Next();
			try {
				fo.SetOperationFlags(4); //FOF_SILENT. Without it shows a hidden dialog that becomes the active window.
				AuException.ThrowIfFailed(fo.RenameItem(si, newName, null), "*rename");
				APerf.Next();
				AuException.ThrowIfFailed(fo.PerformOperations(), "*rename");
				APerf.Next();
			}
			finally {
				Api.ReleaseComObject(fo);
				Api.ReleaseComObject(si);
			}
			APerf.NW();
		}

		static Api.IShellItem _ShellItem(string path, string errMsg)
		{
			var pidl = More.PidlFromString(path, true);
			try {
				var guid = typeof(Api.IShellItem).GUID;
				AuException.ThrowIfFailed(Api.SHCreateItemFromIDList(pidl, guid, out var R), errMsg);
				return R;
			}
			finally { Marshal.FreeCoTaskMem(pidl); }
		}

		static class Api
		{
			[DllImport("shell32.dll", PreserveSig = true)]
			internal static extern int SHCreateItemFromIDList(IntPtr pidl, in Guid riid, out IShellItem ppv);

			[ComImport, Guid("3ad05575-8857-4850-9277-11b85bdb8e09"), ClassInterface(ClassInterfaceType.None)]
			internal class FileOperation { }

			[ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
			internal interface IFileOperation
			{
				[PreserveSig] int Advise(IFileOperationProgressSink pfops, out uint pdwCookie);
				[PreserveSig] int Unadvise(uint dwCookie);
				[PreserveSig] int SetOperationFlags(uint dwOperationFlags);
				[PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
				[PreserveSig] int SetProgressDialog(IOperationsProgressDialog popd);
				[PreserveSig] int SetProperties(IntPtr pproparray); //IPropertyChangeArray
				[PreserveSig] int SetOwnerWindow(AWnd hwndOwner);
				[PreserveSig] int ApplyPropertiesToItem(IShellItem psiItem);
				[PreserveSig] int ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] Object punkItems);
				[PreserveSig] int RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink pfopsItem);
				[PreserveSig] int RenameItems([MarshalAs(UnmanagedType.IUnknown)] Object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
				[PreserveSig] int MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink pfopsItem);
				[PreserveSig] int MoveItems([MarshalAs(UnmanagedType.IUnknown)] Object punkItems, IShellItem psiDestinationFolder);
				[PreserveSig] int CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszCopyName, IFileOperationProgressSink pfopsItem);
				[PreserveSig] int CopyItems([MarshalAs(UnmanagedType.IUnknown)] Object punkItems, IShellItem psiDestinationFolder);
				[PreserveSig] int DeleteItem(IShellItem psiItem, IFileOperationProgressSink pfopsItem);
				[PreserveSig] int DeleteItems([MarshalAs(UnmanagedType.IUnknown)] Object punkItems);
				[PreserveSig] int NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, IFileOperationProgressSink pfopsItem);
				[PreserveSig] int PerformOperations();
				[PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
			}

			[ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
			internal interface IFileOperationProgressSink
			{
				[PreserveSig] int StartOperations();
				[PreserveSig] int FinishOperations(int hrResult);
				[PreserveSig] int PreRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
				[PreserveSig] int PostRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrRename, IShellItem psiNewlyCreated);
				[PreserveSig] int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
				[PreserveSig] int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrMove, IShellItem psiNewlyCreated);
				[PreserveSig] int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
				[PreserveSig] int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrCopy, IShellItem psiNewlyCreated);
				[PreserveSig] int PreDeleteItem(uint dwFlags, IShellItem psiItem);
				[PreserveSig] int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem psiNewlyCreated);
				[PreserveSig] int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
				[PreserveSig] int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem psiNewItem);
				[PreserveSig] int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);
				[PreserveSig] int ResetTimer();
				[PreserveSig] int PauseTimer();
				[PreserveSig] int ResumeTimer();
			}

			[ComImport, Guid("0C9FB851-E5C9-43EB-A370-F0677B13874C"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
			internal interface IOperationsProgressDialog
			{
				[PreserveSig] int StartProgressDialog(AWnd hwndOwner, uint flags);
				[PreserveSig] int StopProgressDialog();
				[PreserveSig] int SetOperation(SPACTION action);
				[PreserveSig] int SetMode(uint mode);
				[PreserveSig] int UpdateProgress(ulong ullPointsCurrent, ulong ullPointsTotal, ulong ullSizeCurrent, ulong ullSizeTotal, ulong ullItemsCurrent, ulong ullItemsTotal);
				[PreserveSig] int UpdateLocations(IShellItem psiSource, IShellItem psiTarget, IShellItem psiItem);
				[PreserveSig] int ResetTimer();
				[PreserveSig] int PauseTimer();
				[PreserveSig] int ResumeTimer();
				[PreserveSig] int GetMilliseconds(out ulong pullElapsed, out ulong pullRemaining);
				[PreserveSig] int GetOperationStatus(out PDOPSTATUS popstatus);
			}

			internal enum SPACTION
			{
				SPACTION_NONE,
				SPACTION_MOVING,
				SPACTION_COPYING,
				SPACTION_RECYCLING,
				SPACTION_APPLYINGATTRIBS,
				SPACTION_DOWNLOADING,
				SPACTION_SEARCHING_INTERNET,
				SPACTION_CALCULATING,
				SPACTION_UPLOADING,
				SPACTION_SEARCHING_FILES,
				SPACTION_DELETING,
				SPACTION_RENAMING,
				SPACTION_FORMATTING,
				SPACTION_COPY_MOVING
			}

			internal enum PDOPSTATUS
			{
				PDOPS_RUNNING = 1,
				PDOPS_PAUSED,
				PDOPS_CANCELLED,
				PDOPS_STOPPED,
				PDOPS_ERRORS
			}


		}
#endif

			/// <summary>
			/// Calls <see cref="EnumDirectory"/> and returns sum of all file sizes.
			/// With default flags, it includes sizes of all descendant files, in this directory and all subdirectories except in inaccessible [sub]directories.
			/// </summary>
			/// <param name="path">Full path.</param>
			/// <param name="flags"><b>EnumDirectory</b> flags.</param>
			/// <exception cref="Exception"><see cref="EnumDirectory"/> exceptions. By default, no exceptions if used full path and the directory exists.</exception>
			/// <remarks>
			/// This function is slow if the directory is large.
			/// Don't use this function for files (throws exception) and drives (instead use <see cref="DriveInfo"/>, it's fast and includes sizes of Recycle Bin and other protected hidden system directories).
			/// </remarks>
			public static long CalculateDirectorySize(string path, FEFlags flags = FEFlags.AndSubdirectories | FEFlags.IgnoreAccessDeniedErrors)
			{
				return EnumDirectory(path, flags).Sum(f => f.Size);
			}

			//rejected: unreliable. Uses registry, where many mimes are incorrect and nonconstant.
			//	Use System.Web.MimeMapping.GetMimeMapping. It uses a hardcoded list, although too small.
			///// <summary>
			///// Gets file's MIME content type, like "text/html" or "image/png".
			///// Returns false if cannot detect it.
			///// </summary>
			///// <param name="file">File name or path or URL or just extension like ".txt". If <i>canAnalyseData</i> is true, must be full path of a file, and the file must exist and can be opened to read; else the function uses just .extension, and the file may exist or not.</param>
			///// <param name="contentType">Result.</param>
			///// <param name="canAnalyseData">If cannot detect from file extension, try to detect from file data.</param>
			///// <exception cref="ArgumentException">Not full path. Only if <i>canAnalyseData</i> is true.</exception>
			///// <exception cref="Exception">Exceptions of <see cref="File.ReadAllBytes"/>. Only if <i>canAnalyseData</i> is true.</exception>
			///// <remarks>
			///// Uses API <msdn>FindMimeFromData</msdn>.
			///// </remarks>
			//public static bool GetMimeContentType(string file, out string contentType, bool canAnalyseData = false)
			//{
			//	if(file.Ends(".cur", true)) { contentType = "image/x-icon"; return true; } //registered without MIME or with text/plain

			//	int hr = Api.FindMimeFromData(default, file, null, 0, null, 0, out contentType, 0);
			//	if(hr != 0 && canAnalyseData) {
			//		file = APath.Normalize(file);
			//		using(var stream = File.OpenRead(file)) {
			//			var data = new byte[256];
			//			int n = stream.Read(data, 0, 256);
			//			hr = Api.FindMimeFromData(default, null, data, n, null, 0, out contentType, 0);
			//		}
			//	}
			//	return hr == 0;
			//	//note: the returned unmanaged string is freed with CoTaskMemFree, which uses HeapFree(GetProcessHeap).
			//	//	In MSDN it is documented incorrectly: "should be freed with the operator delete function".
			//	//	To discover it, call HeapSize(GetProcessHeap) before and after CoTaskMemFree. It returns -1 when called after.
			//}
		}
	}
}

namespace Au.Types
{
#pragma warning disable 660, 661 //no Equals()
	/// <summary>
	/// Contains file properties that can be used to uniquely identify the file on a single computer.
	/// </summary>
	/// <remarks>
	/// Can be used with files and directories.
	/// To get it, use <see cref="AFile.More.GetFileId"/>.
	/// There are many different ways to specify path to the same file or directory. To determine whether two paths represent the same file, get and compare their <b>FileId</b>.
	/// </remarks>
	public struct FileId
	{
		/// <summary>The serial number of the volume (aka disk drive) that contains the file.</summary>
		public int VolumeSerialNumber;
		/// <summary>An identifier that is associated with the file. It is unique in that volume.</summary>
		public long FileIndex;

		///
		public static bool operator ==(FileId a, FileId b)
		{
			return a.FileIndex == b.FileIndex && a.VolumeSerialNumber == b.VolumeSerialNumber;
		}

		///
		public static bool operator !=(FileId a, FileId b) { return !(a == b); }
	}
#pragma warning restore 660, 661
}