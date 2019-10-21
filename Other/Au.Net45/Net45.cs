﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using _TYPELIBATTR = System.Runtime.InteropServices.ComTypes.TYPELIBATTR;

[module: DefaultCharSet(CharSet.Unicode)]

unsafe class Net45
{
	[STAThread]
	static int Main(string[] args)
	{
		switch(args[0]) {
		case "/typelib":
			return ConvertTypelib(args[1]);
		}
		return 1;
	}

	static int ConvertTypelib(string param)
	{
		var a = param.Split('|');
		string asmDir = a[0], comDll = a[1];

		int hr = LoadTypeLibEx(comDll, 2, out var tl);
		if(hr != 0) {
			Print("Failed to load type library. " + new Win32Exception(hr).Message);
			return -1;
		}

		try {
			var c = new _TypelibConverter { saveDir = asmDir };
			c.Convert(tl);
		}
		catch(Exception ex) { Print("Failed to convert type library. " + ex.Message); return -2; }
		finally { Marshal.ReleaseComObject(tl); }
		return 0;
	}

	[DllImport("oleaut32.dll", EntryPoint = "#183", PreserveSig = true)]
	static extern int LoadTypeLibEx(string szFile, int regkind, out ITypeLib pptlib);

	class _TypelibConverter : ITypeLibImporterNotifySink
	{
		static Dictionary<string, AssemblyBuilder> s_converted = new Dictionary<string, AssemblyBuilder>();

		public string saveDir;

		public Assembly Convert(ITypeLib tl)
		{
			tl.GetLibAttr(out IntPtr ipta);
			var ta = *(_TYPELIBATTR*)ipta;
			tl.ReleaseTLibAttr(ipta);
			var hash = Fnv1((byte*)&ta, sizeof(_TYPELIBATTR)).ToString("x");

			tl.GetDocumentation(-1, out var tlName, out var tlDescription, out var _, out var _);
			var fileName = $"{tlName} {ta.wMajorVerNum}.{ta.wMinorVerNum} #{hash}.dll";
			var netPath = saveDir + fileName;

			if(!s_converted.TryGetValue(fileName, out var asm) || !File.Exists(netPath)) {
				Print($"Converted: {tlName} ({tlDescription}) to \"{fileName}\".");

				var converter = new TypeLibConverter();
				asm = converter.ConvertTypeLibToAssembly(tl, netPath,
					TypeLibImporterFlags.ReflectionOnlyLoading | TypeLibImporterFlags.UnsafeInterfaces,
					this, null, null, tlName, null);
				asm.Save(fileName);
				s_converted[fileName] = asm;
			}
			return asm;
		}

		void ITypeLibImporterNotifySink.ReportEvent(ImporterEventKind eventKind, int eventCode, string eventMsg)
		{
			if(eventKind != ImporterEventKind.NOTIF_TYPECONVERTED) Print("Warning: " + eventMsg);
		}

		Assembly ITypeLibImporterNotifySink.ResolveRef(object typeLib) => Convert(typeLib as ITypeLib);
	}

	static void Print(object o) => Console.WriteLine(o?.ToString());

	//code copied from Au.Util.AHash
	static int Fnv1(byte* data, int lengthBytes)
	{
		uint hash = 2166136261;

		for(int i = 0; i < lengthBytes; i++)
			hash = (hash * 16777619) ^ data[i];

		return (int)hash;
	}

}