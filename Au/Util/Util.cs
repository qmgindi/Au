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
using Microsoft.Win32;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using System.Drawing;
using System.Linq;
using System.Reflection.Emit;
using Microsoft.Win32.SafeHandles;
using System.Resources;
using System.Globalization;

using Au.Types;
using static Au.NoClass;

namespace Au.Util
{
	/// <summary>
	/// Miscellaneous classes and functions used in this library. Can be used outside it too.
	/// </summary>
	[CompilerGenerated()]
	class NamespaceDoc
	{
		//SHFB uses this for namespace documentation.
	}

	/// <summary>
	/// Helps to get and release screen DC with the 'using(...){...}' pattern.
	/// Uses API GetDC and ReleaseDC.
	/// </summary>
	internal struct LibScreenDC :IDisposable
	{
		IntPtr _dc;

		public LibScreenDC(int unused) => _dc = Api.GetDC(default);
		public static implicit operator IntPtr(LibScreenDC dc) => dc._dc;
		public void Dispose() => Api.ReleaseDC(default, _dc);
	}

	/// <summary>
	/// Helps to get and release window DC with the 'using(...){...}' pattern.
	/// Uses API GetDC and ReleaseDC.
	/// If w is default(Wnd), gets screen DC.
	/// </summary>
	internal struct LibWindowDC :IDisposable
	{
		IntPtr _dc;
		Wnd _w;

		public LibWindowDC(Wnd w) => _dc = Api.GetDC(_w = w);
		public static implicit operator IntPtr(LibWindowDC dc) => dc._dc;
		public void Dispose() => Api.ReleaseDC(_w, _dc);
		public bool Is0 => _dc == default;
	}

	/// <summary>
	/// Helps to create and delete screen DC with the 'using(...){...}' pattern.
	/// Uses API CreateCompatibleDC and DeleteDC.
	/// </summary>
	internal struct LibCompatibleDC :IDisposable
	{
		IntPtr _dc;

		public LibCompatibleDC(IntPtr dc) => _dc = Api.CreateCompatibleDC(dc);
		public static implicit operator IntPtr(LibCompatibleDC dc) => dc._dc;
		public void Dispose() => Api.DeleteDC(_dc);
	}

	/// <summary>
	/// Creates and manages native bitmap handle and memory DC (GDI device context).
	/// The bitmap is selected in the DC.
	/// </summary>
	public sealed class MemoryBitmap :IDisposable
	{
		IntPtr _dc, _bm, _oldbm;

		/// <summary>
		/// DC handle.
		/// </summary>
		public IntPtr Hdc => _dc;

		/// <summary>
		/// Bitmap handle.
		/// </summary>
		public IntPtr Hbitmap => _bm;

		/// <summary>
		/// Does nothing. Later you can call Create or Attach.
		/// </summary>
		public MemoryBitmap() { }

		/// <summary>
		/// Calls <see cref="Create"/>.
		/// </summary>
		/// <exception cref="AuException">Failed. Probably there is not enough memory for bitmap of specified size (need with*height*4 bytes).</exception>
		public MemoryBitmap(int width, int height)
		{
			if(!Create(width, height)) throw new AuException("*create memory bitmap of specified size");
		}

		//rejected: not obvious, whether it attaches or copies. Also, attaching is rarely used.
		///// <summary>
		///// Calls <see cref="Attach"/>.
		///// </summary>
		//public MemoryBitmap(IntPtr hBitmap)
		//{
		//	Attach(hBitmap);
		//}

		/// <summary>
		/// Deletes the bitmap and DC.
		/// </summary>
		public void Dispose()
		{
			Delete();
			//GC.SuppressFinalize(this); //no. We allow to Create/Attach after calling this.
		}

		///
		~MemoryBitmap() { Delete(); }
		//info: calls DeleteDC. MSDN says that ReleaseDC must be called from the same thread. But does not say it about DeleteDC and others.

		/// <summary>
		/// Deletes the bitmap and DC.
		/// </summary>
		public void Delete()
		{
			if(_dc == default) return;
			if(_bm != default) {
				Api.SelectObject(_dc, _oldbm);
				Api.DeleteObject(_bm);
				_bm = default;
			}
			Api.DeleteDC(_dc);
			_dc = default;
		}

		/// <summary>
		/// Creates new memory DC and bitmap of specified size and selects it into the DC.
		/// Returns false if failed.
		/// In any case deletes previous bitmap and DC.
		/// </summary>
		/// <param name="width">Width, pixels.</param>
		/// <param name="height">Height, pixels.</param>
		public bool Create(int width, int height)
		{
			using(var dcs = new LibScreenDC(0)) {
				Attach(Api.CreateCompatibleBitmap(dcs, width, height));
				return _bm != default;
			}
		}

		/// <summary>
		/// Sets this variable to manage an existing bitmap.
		/// Selects the bitmap into a memory DC.
		/// Deletes previous bitmap and DC.
		/// </summary>
		/// <param name="hBitmap">Native bitmap handle.</param>
		public void Attach(IntPtr hBitmap)
		{
			Delete();
			if(hBitmap != default) {
				_dc = Api.CreateCompatibleDC(default);
				_oldbm = Api.SelectObject(_dc, _bm = hBitmap);
			}
		}

		/// <summary>
		/// Deletes memory DC, clears this variable and returns its bitmap (native bitmap handle).
		/// The returned bitmap is not selected into a DC. Will need to delete it with API DeleteObject.
		/// </summary>
		public IntPtr Detach()
		{
			IntPtr bret = _bm;
			if(_bm != default) {
				Api.SelectObject(_dc, _oldbm);
				Api.DeleteDC(_dc);
				_dc = default; _bm = default;
			}
			return bret;
		}
	}

	/// <summary>
	/// Creates and manages native font handle.
	/// </summary>
	internal sealed class LibNativeFont :IDisposable
	{
		public IntPtr Handle { get; private set; }
		public int HeightOnScreen { get; private set; }

		public LibNativeFont(IntPtr handle) { Handle = handle; }

		public static implicit operator IntPtr(LibNativeFont f) => (f == null) ? default : f.Handle;

		~LibNativeFont() { _Dispose(); }
		public void Dispose() { _Dispose(); GC.SuppressFinalize(this); }
		void _Dispose()
		{
			if(Handle != default) { Api.DeleteObject(Handle); Handle = default; }
		}

		public LibNativeFont(string name, int height, bool calculateHeightOnScreen = false)
		{
			using(var dcs = new LibScreenDC(0)) {
				int h2 = -Math_.MulDiv(height, Api.GetDeviceCaps(dcs, 90), 72);
				Handle = Api.CreateFont(h2, iCharSet: 1, pszFaceName: name); //LOGPIXELSY=90
				if(calculateHeightOnScreen) {
					using(var dcMem = new LibCompatibleDC(dcs)) {
						var of = Api.SelectObject(dcMem, Handle);
						Api.GetTextExtentPoint32(dcMem, "A", 1, out var z);
						HeightOnScreen = z.Height;
						Api.SelectObject(dcMem, of);
					}
				}
			}
		}
	}

	/// <summary>
	/// Gets native module handle.
	/// </summary>
	public static class ModuleHandle
	{
		/// <summary>
		/// Gets native module handle of type's assembly.
		/// </summary>
		public static IntPtr OfType(Type t)
		{
			return t == null ? default : Marshal.GetHINSTANCE(t.Module);

			//Tested these to get caller's module without Type parameter:
			//This is dirty/dangerous and 50 times slower: [MethodImpl(MethodImplOptions.NoInlining)] ... return Marshal.GetHINSTANCE(new StackFrame(1).GetMethod().DeclaringType.Module);
			//This is dirty/dangerous, does not support multi-module assemblies and 12 times slower: [MethodImpl(MethodImplOptions.NoInlining)] ... return Marshal.GetHINSTANCE(Assembly.GetCallingAssembly().GetLoadedModules()[0]);
			//This is dirty/dangerous/untested and 12 times slower: [MethodImpl(MethodImplOptions.AggressiveInlining)] ... return Marshal.GetHINSTANCE(MethodBase.GetCurrentMethod().DeclaringType.Module);
		}

		/// <summary>
		/// Gets native module handle of an assembly.
		/// If the assembly consists of multiple modules, gets its first loaded module.
		/// </summary>
		public static IntPtr OfAssembly(Assembly asm)
		{
			return asm == null ? default : Marshal.GetHINSTANCE(asm.GetLoadedModules()[0]);
		}

		/// <summary>
		/// Gets native module handle of the entry assembly of this appdomain.
		/// If the assembly consists of multiple modules, gets its first loaded module.
		/// </summary>
		public static IntPtr OfAppDomainEntryAssembly()
		{
			return OfAssembly(Assembly_.EntryAssembly);
		}

		/// <summary>
		/// Gets native module handle of Au.dll.
		/// </summary>
		public static IntPtr OfAuDll()
		{
			return Marshal.GetHINSTANCE(typeof(ModuleHandle).Module);
		}

		/// <summary>
		/// Gets native module handle of the program file of this process.
		/// </summary>
		public static IntPtr OfProcessExe()
		{
			return Api.GetModuleHandle(null);
		}
	}

	/// <summary>
	/// Assembly functions.
	/// </summary>
	public static class Assembly_
	{
		/// <summary>
		/// Gets the entry assembly of this appdomain.
		/// Normally instead can be used <see cref="Assembly.GetEntryAssembly"/>, but it returns null if appdomain launched through <see cref="AppDomain.DoCallBack">AppDomain.DoCallBack</see>.
		/// </summary>
		public static Assembly EntryAssembly
		{
			get
			{
				if(_appdomainAssembly == null) {
					var asm = Assembly.GetEntryAssembly(); //fails if this domain launched through DoCallBack
					if(asm == null) asm = AppDomain.CurrentDomain.GetAssemblies()[1]; //[0] is mscorlib, 1 should be our assembly
					_appdomainAssembly = asm;
				}
				return _appdomainAssembly;
			}
		}
		static Assembly _appdomainAssembly;

		/// <summary>
		/// Returns true if Au.dll is installed in the global assembly cache.
		/// </summary>
		internal static bool LibIsAuInGAC => typeof(Assembly_).Assembly.GlobalAssemblyCache;

		/// <summary>
		/// Returns true if Au.dll is compiled to native code using ngen.exe.
		/// It means - no JIT-compiling delay when its functions are called first time in process or appdomain.
		/// </summary>
		internal static bool LibIsAuNgened => IsAssemblyNgened(typeof(Assembly_).Assembly);
		//tested: Module.GetPEKind always gets ILOnly.

		/// <summary>
		/// Returns true if assembly asm is compiled to native code using ngen.exe.
		/// It means - no JIT-compiling delay when its functions are called first time in process or appdomain.
		/// </summary>
		public static bool IsAssemblyNgened(Assembly asm)
		{
			var s = asm.CodeBase;
			if(asm.GlobalAssemblyCache) return s.Contains("/GAC_MSIL/"); //faster and maybe more reliable, but works only with GAC assemblies
			s = Path.GetFileName(s);
			s = s.Insert(s.LastIndexOf('.') + 1, "ni.");
			return default != Api.GetModuleHandle(s);
		}
	}

	//currently not used
	///// <summary>
	///// Extends the .NET <see cref="Marshal"/> class.
	///// </summary>
	//public static class Marshal_
	//{
	//	//currently not used
	//	///// <summary>
	//	///// Increments the reference count of COM object's RCW (managed runtime callable wrapper).
	//	///// </summary>
	//	///// <param name="o">Managed COM object (RCW).</param>
	//	///// <remarks>
	//	///// This function is the opposite of <see cref="Marshal.ReleaseComObject"/>, which decrements the RCW reference count.
	//	///// Call this function when cloning a variable of a type that wraps a managed COM object and calls Marshal.ReleaseComObject when disposing. Without it, after disposing one of the variables, cannot call methods etc of other variable because the RCW then is invalid.
	//	///// This function does not increment the reference count of the native COM object.
	//	///// </remarks>
	//	//public static void AddRefComObject<T>(T o) where T: class
	//	//{
	//	//	//ugly, but .NET does not have a better method for it.

	//	//	var u = Marshal.GetIUnknownForObject(o); //gets native COM pointer and calls its AddRef
	//	//	var o2 = Marshal.GetObjectForIUnknown(u); //increments RCW ref count. Calls QueryInterface and Release of the native COM object.
	//	//											  //var o2 = Marshal.GetTypedObjectForIUnknown(u, typeof(T)); //works too, but MSDN info is unclear. In both cases ToString says it's System.__ComObject.
	//	//	Marshal.Release(u); //because GetIUnknownForObject called AddRef
	//	//	Debug.Assert(ReferenceEquals(o, o2));
	//	//}

	//	//returns new RCW
	//	//internal static object GetUniqueComObject<T>(T o) where T: class
	//	//{
	//	//	var u = Marshal.GetIUnknownForObject(o);
	//	//	var o2 = Marshal.GetUniqueObjectForIUnknown(u); //many QI etc
	//	//	Marshal.Release(u);
	//	//	Debug.Assert(!ReferenceEquals(o, o2));
	//	//	return u2;
	//	//}

	//	//currently not used
	//	///// <summary>
	//	///// Gets another COM interface through <msdn>IUnknown.QueryInterface</msdn>.
	//	///// Returns false if the COM object (iunkFrom) does not support the requested interface or if fails.
	//	///// </summary>
	//	///// <typeparam name="T">IntPtr or an IntPtr-based type. Must have size of IntPtr (exception if not).</typeparam>
	//	///// <param name="iunkFrom">COM object as IUnknown.</param>
	//	///// <param name="iTo">Receives the requested COM interface pointer.</param>
	//	///// <param name="riid">Interface GUID.</param>
	//	//internal static unsafe bool QueryInterface<T>(IntPtr iunkFrom, out T iTo, ref Guid riid) where T : struct
	//	//{
	//	//	if(Unsafe.SizeOf<T>() != IntPtr.Size) throw new ArgumentException();
	//	//	iTo = default;
	//	//	if(0 != Marshal.QueryInterface(iunkFrom, ref riid, out IntPtr ip) || ip == default) return false;
	//	//	iTo = Unsafe.Read<T>(&ip);
	//	//	return true;
	//	//}

	//	//currently not used
	//	///// <summary>
	//	///// Gets another COM interface through <msdn>IServiceProvider.QueryService</msdn>.
	//	///// Returns false if the COM object (iunkFrom) does not support IServiceProvider or the requested interface or if fails.
	//	///// </summary>
	//	///// <typeparam name="T">IntPtr or an IntPtr-based type. Must have size of IntPtr (exception if not).</typeparam>
	//	///// <param name="iunkFrom">COM object as IUnknown.</param>
	//	///// <param name="iTo">Receives the requested COM interface pointer.</param>
	//	///// <param name="guidService">Service GUID. If it is the same as riid, you can use other overload.</param>
	//	///// <param name="riid">Interface GUID.</param>
	//	//internal static unsafe bool QueryService<T>(IntPtr iunkFrom, out T iTo, ref Guid guidService, ref Guid riid) where T:struct
	//	//{
	//	//	if(Unsafe.SizeOf<T>() != IntPtr.Size) throw new ArgumentException();
	//	//	if(iunkFrom==default) throw new ArgumentNullException();
	//	//	iTo = default;
	//	//	if(0 != Api.IUnknown_QueryService(iunkFrom, ref guidService, ref riid, out IntPtr ip) || ip==default) return false;
	//	//	iTo=Unsafe.Read<T>(&ip);
	//	//	return true;
	//	//}

	//	//currently not used
	//	///// <summary>
	//	///// This overload calls <see cref="QueryService{T}(IntPtr, out T, ref Guid, ref Guid)"/> with guidService = riid.
	//	///// </summary>
	//	//internal static unsafe bool QueryService<T>(IntPtr iunkFrom, out T iTo, ref Guid riid) where T : struct
	//	//{
	//	//	return QueryService(iunkFrom, out iTo, ref riid, ref riid);
	//	//}
	//}

	/// <summary>
	/// Functions to work with managed resources.
	/// </summary>
	public static class Resources_
	{

		/// <summary>
		/// Gets an Image or other object from managed resources of appdomain's entry assembly.
		/// Returns null if not found.
		/// </summary>
		/// <param name="name">Resource name, like "example", not like "Project.Properties.Resources.example".</param>
		/// <remarks>
		/// Uses <see cref="ResourceManager.GetObject(string, CultureInfo)"/>.
		/// The Image is not cached. Will need to Dispose.
		/// </remarks>
		public static object GetAppResource(string name)
		{
			try {
				var rm = LibGetAppResourceManager(out var culture);
				return rm?.GetObject(name, culture);
			}
			catch { return null; }

			//info: why need culture? Because much much faster if culture is set to invariant.
		}

		/// <summary>
		/// Gets ResourceManager of appdomain's entry assembly.
		/// Returns null if the assembly does not have resources or if fails.
		/// </summary>
		internal static ResourceManager LibGetAppResourceManager(out CultureInfo culture)
		{
			if(_appResourceManager == null) {
				culture = null;
				var asm = Assembly_.EntryAssembly;
				var s = asm?.GetManifestResourceNames()?.FirstOrDefault(k => k.EndsWith_(".Properties.Resources.resources")); //eg "Project.Properties.Resources.resources". Skip those like "Form1.resources".
				if(s == null) return null; //no resources
				s = s.Remove(s.Length - 10); //remove ".resources", it's documented
				var t = asm.GetType(s); if(t == null) return null;
				var fl = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static; //need NonPublic because default access is internal
				if(t.GetProperty("ResourceManager", fl)?.GetValue(null) is ResourceManager rm) {
					_appResourceCulture = t.GetProperty("Culture")?.GetValue(null) as CultureInfo;
					_appResourceManager = rm;
				} else {
					Debug_.Print("failed to get ResourceManager property");
					_appResourceManager = new ResourceManager(s, asm);
				}
			}
			culture = _appResourceCulture;
			return _appResourceManager;
		}
		static ResourceManager _appResourceManager;
		static CultureInfo _appResourceCulture;

		//rejected: now we have Unsafe.SizeOf<T>().
		///// <summary>
		///// Do not call. Use class TypeSize, which caches the type size.
		///// This is used by TypeSize, not in it, because it is a generic type...
		///// </summary>
		///// <param name="t"></param>
		//[MethodImpl(MethodImplOptions.NoInlining)]
		//internal static int LibGetTypeSize(Type t)
		//{
		//	var dm = new DynamicMethod("SizeOfType", typeof(int), Type.EmptyTypes);
		//	ILGenerator il = dm.GetILGenerator();
		//	il.Emit(OpCodes.Sizeof, t);
		//	il.Emit(OpCodes.Ret);
		//	return (int)dm.Invoke(null, null);
		//	//Print(dm.MethodImplementationFlags);
		//}
	}

	///// <summary>
	///// Gets managed run-time size of type T. Works with any type.
	///// Unlike sizeof, can be used in generic classes too.
	///// Unlike Marshal.SizeOf, gets managed type size (eg 1 for bool), not native type size (eg 4 for bool).
	///// Example: <c>Print(Au.Util.TypeSize&lt;T&gt;.Size);</c>.
	///// </summary>
	///// <typeparam name="T"></typeparam>
	//public static class TypeSize<T>
	//{
	//	/// <summary>
	//	/// Gets T type size.
	//	/// </summary>
	//	public readonly static int Size;
	//	static TypeSize() { Size = Misc.LibGetTypeSize(typeof(T)); }

	//	//speed: quite fast, especially when ngened. When using this generic class, LibGetTypeSize is called once for each type.
	//}

	/// <summary>
	/// Functions for high-DPI screen support.
	/// High DPI means when in Control Panel is set screen text size other than 100%.
	/// </summary>
	public static class Dpi
	{
		/// <summary>
		/// Gets DPI of the primary screen.
		/// On newer Windows versions, users can change DPI without logoff-logon. This function gets the setting that was after logon.
		/// </summary>
		public static int BaseDPI
		{
			get
			{
				if(_baseDPI == 0) {
					using(var dcs = new LibScreenDC(0)) _baseDPI = Api.GetDeviceCaps(dcs, 90); //LOGPIXELSY
				}
				return _baseDPI;
			}
		}
		static int _baseDPI;

		/// <summary>
		/// Gets small icon size that depends on DPI of the primary screen.
		/// Width and Height are <see cref="BaseDPI"/>/6, which is 16 if DPI is 96 (100%).
		/// </summary>
		public static Size SmallIconSize { get { var t = BaseDPI / 6; return new Size(t, t); } }

		/// <summary>
		/// If <see cref="BaseDPI"/> is more than 96, returns stretched i.
		/// Else returns i.
		/// </summary>
		/// <param name="i"></param>
		public static int ScaleInt(int i)
		{
			long dpi = BaseDPI;
			if(dpi > 96) i = (int)(i * dpi / 96);
			return i;
		}

		/// <summary>
		/// If <see cref="BaseDPI"/> is more than 96, returns scaled (stretched) z.
		/// Else returns z.
		/// Note: for images use <see cref="ImageSize"/>.
		/// </summary>
		/// <param name="z"></param>
		public static Size ScaleSize(Size z)
		{
			int dpi = BaseDPI;
			if(dpi > 96) {
				z.Width = (int)((long)z.Width * dpi / 96);
				z.Height = (int)((long)z.Height * dpi / 96);
			}
			return z;
		}

		/// <summary>
		/// If <see cref="BaseDPI"/> is more than 96 and image resolution is different, returns scaled (stretched) image.Size.
		/// Else returns image.Size.
		/// </summary>
		/// <param name="image"></param>
		public static Size ImageSize(Image image)
		{
			if(image == null) return Size.Empty;
			var r = image.Size;
			int dpi = BaseDPI;
			if(dpi > 96) {
				r.Width = (int)((long)r.Width * dpi / (int)Math.Round(image.HorizontalResolution));
				r.Height = (int)((long)r.Height * dpi / (int)Math.Round(image.VerticalResolution));
			}
			return r;
		}
	}

	/// <summary>
	/// Wraps a waitable timer handle. Allows to create, open, set and wait.
	/// More info: API <msdn>CreateWaitableTimer</msdn>.
	/// Note: will need to dispose.
	/// </summary>
	public class WaitableTimer :WaitHandle
	{
		[DllImport("kernel32.dll", EntryPoint = "CreateWaitableTimerW", SetLastError = true)]
		static extern SafeWaitHandle CreateWaitableTimer(Api.SECURITY_ATTRIBUTES lpTimerAttributes, bool bManualReset, string lpTimerName);

		[DllImport("kernel32.dll", SetLastError = true)]
		static extern bool SetWaitableTimer(SafeWaitHandle hTimer, ref long lpDueTime, int lPeriod = 0, IntPtr pfnCompletionRoutine = default, IntPtr lpArgToCompletionRoutine = default, bool fResume = false);

		[DllImport("kernel32.dll", EntryPoint = "OpenWaitableTimerW", SetLastError = true)]
		static extern SafeWaitHandle OpenWaitableTimer(uint dwDesiredAccess, bool bInheritHandle, string lpTimerName);

		WaitableTimer() { }

		/// <summary>
		/// Calls API <msdn>CreateWaitableTimer</msdn> and creates a WaitableTimer object that wraps the timer handle.
		/// </summary>
		/// <param name="manualReset"></param>
		/// <param name="timerName">Timer name. If a timer with this name already exists, opens it if possible. If null, creates unnamed timer.</param>
		/// <exception cref="AuException">Failed. For example, a non-timer kernel object with this name already exists.</exception>
		public static WaitableTimer Create(bool manualReset = false, string timerName = null)
		{
			var h = CreateWaitableTimer(Api.SECURITY_ATTRIBUTES.Common, manualReset, timerName);
			if(h.IsInvalid) {
				var ex = new AuException(0, "*create timer");
				h.SetHandleAsInvalid();
				throw ex;
			}
			return new WaitableTimer() { SafeWaitHandle = h };
		}

		/// <summary>
		/// Calls API <msdn>OpenWaitableTimer</msdn> and creates a WaitableTimer object that wraps the timer handle.
		/// </summary>
		/// <param name="timerName">Timer name. Fails if it does not exist; to open-or-create use <see cref="Create"/>.</param>
		/// <param name="access">.See <msdn>Synchronization Object Security and Access Rights</msdn>. The default value TIMER_MODIFY_STATE|SYNCHRONIZE allows to set and wait.</param>
		/// <exception cref="AuException">Failed. For example, a non-timer kernel object with this name already exists.</exception>
		/// <param name="inheritHandle"></param>
		/// <param name="noException">If fails, return null, don't throw exception. Supports <see cref="Native.GetError"/>.</param>
		/// <exception cref="AuException">Failed. For example, the timer does not exist.</exception>
		public static WaitableTimer Open(string timerName, uint access = Api.TIMER_MODIFY_STATE | Api.SYNCHRONIZE, bool inheritHandle = false, bool noException = false)
		{
			var h = OpenWaitableTimer(access, inheritHandle, timerName);
			if(h.IsInvalid) {
				var e = Native.GetError();
				h.SetHandleAsInvalid();
				if(noException) {
					Native.SetError(e);
					return null;
				}
				throw new AuException(e, "*open timer");
			}
			return new WaitableTimer() { SafeWaitHandle = h };
		}

		/// <summary>
		/// Calls API <msdn>SetWaitableTimer</msdn>.
		/// Returns false if fails. Supports <see cref="Native.GetError"/>.
		/// </summary>
		/// <param name="dueTime">
		/// The time after which the state of the timer is to be set to signaled. It is relative time (from now).
		/// If positive, in milliseconds. If negative, in 100 nanosecond intervals (microseconds*10), see <msdn>FILETIME</msdn>.
		/// Also can be 0, to set minimal time.</param>
		/// <param name="period">The period of the timer, in milliseconds. If 0, the timer is signaled once. If greater than 0, the timer is periodic.</param>
		/// <exception cref="OverflowException">dueTime*10000 is greater than long.MaxValue.</exception>
		public bool Set(long dueTime, int period = 0)
		{
			if(dueTime > 0) dueTime = -checked(dueTime * 10000);
			return SetWaitableTimer(this.SafeWaitHandle, ref dueTime, period, default, default, false);
		}

		/// <summary>
		/// Calls API <msdn>SetWaitableTimer</msdn>.
		/// Returns false if fails. Supports <see cref="Native.GetError"/>.
		/// </summary>
		/// <param name="dueTime">The UTC date/time at which the state of the timer is to be set to signaled.</param>
		/// <param name="period">The period of the timer, in milliseconds. If 0, the timer is signaled once. If greater than 0, the timer is periodic.</param>
		public bool SetAbsolute(DateTime dueTime, int period = 0)
		{
			var t = dueTime.ToFileTimeUtc();
			return SetWaitableTimer(this.SafeWaitHandle, ref t, period, default, default, false);
		}
	}

	/// <summary>
	/// Static functions to open a help topic etc.
	/// </summary>
	public static class Help
	{
		/// <summary>
		/// Opens file "Au Help.chm" and a help topic in it.
		/// The file must be in <see cref="Folders.ThisApp"/>.
		/// </summary>
		/// <param name="topic">Topic file name, like "M_Au_Acc_Find" or "0248143b-a0dd-4fa1-84f9-76831db6714a".</param>
		public static void AuHelp(string topic)
		{
			var s = Folders.ThisApp + "Help/Au Help.chm::/html/" + topic + ".htm";
			Api.HtmlHelp(Api.GetDesktopWindow(), s, 0, 0); //HH_DISPLAY_TOPIC
		}

	}

	/// <summary>
	/// Calls API <msdn>AttachThreadInput</msdn> to attach/detach thread input.
	/// Constructor attaches thread input of this thread to that of the specified thread. <b>Dispose</b> detaches.
	/// </summary>
	internal struct LibAttachThreadInput :IDisposable
	{
		int _tidThis, _tidAttach;

		/// <summary>
		/// Attaches thread input of this thread to that of the specified thread.
		/// </summary>
		public LibAttachThreadInput(int idThreadAttachTo, out bool succeeded)
		{
			_tidThis = Api.GetCurrentThreadId();
			succeeded = Api.AttachThreadInput(_tidThis, idThreadAttachTo, true);
			_tidAttach = succeeded ? idThreadAttachTo : 0;
		}

		/// <summary>
		/// Detaches thread input.
		/// </summary>
		public void Dispose()
		{
			if(_tidAttach != 0) {
				Api.AttachThreadInput(_tidThis, _tidAttach, false);
				_tidAttach = 0;
			}
		}

		/// <summary>
		/// Returns true if AttachThreadInput succeeded and this variable is not disposed.
		/// </summary>
		public bool IsAttached => _tidAttach != 0;
	}

	//rejected: does not work with Key/Text/Paste. And too dirty. Somehow similar code worked in QM2.
	///// <summary>
	///// Switches to another thread and sleeps for the remainder of this time slice of the thread scheduler.
	///// Unlike Sleep(0) etc, works on multi-processor computers too.
	///// </summary>
	///// <remarks>
	///// Causes this thread to yield execution to another thread that is ready to run on ANY logical processor.
	///// If the remainder of this time slice is less than 400 mcs, retries to sleep during the next time slice.
	///// If there are no other ready threads, does not sleep. Then the speed is about 100 mcs.
	///// </remarks>
	//internal struct LibThreadSwitcher :IDisposable
	//{
	//	ulong _processAffinity, _threadAffinity;

	//	public void Dispose()
	//	{
	//		if(_threadAffinity != 0) {
	//			//Print(_threadAffinity);
	//			Api.SetThreadAffinityMask(Api.GetCurrentThread(), _threadAffinity);
	//		}
	//	}

	//	static uint s_nProc = Api.GetMaximumProcessorCount(Api.ALL_PROCESSOR_GROUPS);

	//	/// <summary>
	//	/// Switches to another thread and sleeps for the remainder of this time slice of the thread scheduler.
	//	/// Returns false if fails. Fails if there are more than 64 logical processors.
	//	/// </summary>
	//	public bool Switch()
	//	{
	//		const int c_nTry = 2;
	//		uint nProc = s_nProc;
	//		long t0 = Time.Microseconds;
	//		bool switched, retry = false;
	//		g2:
	//		switched = false;
	//		if(nProc == 1) {
	//			for(int i = 0; i < c_nTry; i++) {
	//				if(switched=Api.SwitchToThread()) break;
	//			}
	//		} else {
	//			if(nProc == 0 || nProc > 64) return false;
	//			if(_processAffinity == 0) {
	//				if(!Api.GetProcessAffinityMask(Api.GetCurrentProcess(), out var amProcess, out var amSystem)) return false;
	//				ulong processAffinity = amProcess, systemAffinity = amSystem;
	//				//Print((int)nProc, processAffinity);
	//				for(int i = 0; i < nProc; i++) {
	//					var bit = 1UL << i;
	//					if((bit & processAffinity) == 0 && (bit & systemAffinity) != 0) return false; //are all bits 1?
	//				}
	//				_processAffinity = processAffinity;
	//			}

	//			var ht = Api.GetCurrentThread();
	//			for(int i = 0; i < c_nTry; i++) {
	//				for(int j = 0; j < nProc; j++) {
	//					var bit = 1UL << j;
	//					if((bit & _processAffinity) == 0) continue; //is this bit in system affinity?
	//					var tam = Api.SetThreadAffinityMask(ht, bit);
	//					if(tam == 0) return false;
	//					if(_threadAffinity == 0) _threadAffinity = tam;
	//					if(switched=Api.SwitchToThread()) {
	//						//Print(i, j);
	//						goto g1;
	//					}
	//				}
	//			}
	//		}
	//		g1:
	//		if(switched) {
	//			long t1 = Time.Microseconds;
	//			if(t1 - t0 < 400) {
	//				//Print("-->", t1 - t0, retry);
	//				if(!retry) { retry = true; goto g2; }
	//				Time.Sleep(1);
	//			}
	//		}
	//		return true;
	//	}
	//}
}
