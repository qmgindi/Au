﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
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
//using System.Drawing;
//using System.Linq;

using Au;
using Au.Types;
using static Au.AStatic;

static class Program
{
	static Program()//TODO
	{
		APerf.First();
		//AOutput.QM2.UseQM2 = true; AOutput.Clear();
		//ADebug.PrintLoadedAssemblies(true, true);
		//AOutput.LogFile = @"q:\Test\log.txt";
		//AOutput.LogFileTimestamp = true;
	}

	/// <summary>
	/// The main entry point for the application.
	/// </summary>
	[STAThread]
	static void Main(string[] args)
	{
		//AOutput.QM2.Write("ok");
		//return;

		if(args.Length > 0 && args[0] == "/d") {
			UacDragDrop.NonAdminProcess.MainDD(args);
			return;
		}

		//restart as admin if started as non-admin on admin user account
		if(args.Length > 0 && args[0] == "/n") {
			args = args.RemoveAt(0);
		} else if(AUac.OfThisProcess.Elevation == UacElevation.Limited) {
			if(_RestartAsAdmin(args)) return;
		}
		//speed with restarting is the same as when runs as non-admin. The fastest is when started as admin. Because faster when runs as admin.

		Directory.SetCurrentDirectory(AFolders.ThisApp); //because it is c:\windows\system32 when restarted as admin

		_Main(args);
	}

	static void _Main(string[] args)
	{
		//Test(); return;
		//Process.GetCurrentProcess().ProcessorAffinity = (IntPtr)1; //test how works with 1 CPU
		//Task.Run(() => { while(true) { Thread.Sleep(1000); GC.Collect(); } });

		if(CommandLine.OnProgramStarted(args)) return;

		OutputServer.NoNewline = true;
		OutputServer.Start();

		Api.SetErrorMode(Api.GetErrorMode() | Api.SEM_FAILCRITICALERRORS); //disable some error message boxes, eg when removable media not found; MSDN recommends too.
		Api.SetSearchPathMode(Api.BASE_SEARCH_PATH_ENABLE_SAFE_SEARCHMODE); //let SearchPath search in current directory after system directories

		//Application.EnableVisualStyles(); //no, we have manifest
		Application.SetCompatibleTextRenderingDefault(false);

		Settings = new ProgramSettings();

		if(!Settings.GetString("user", out UserGuid)) Settings.Set("user", Guid.NewGuid().ToString());

		ATimer.Every(1000, t => _TimerProc(t));
		//note: timer can make Process Hacker show CPU usage, even if we do nothing. Eg 0.02 if 250, 0.01 if 500, 0 of 1000.
		//Timer1s += () => Print("1 s");
		//Timer1sOr025s += () => Print("0.25 s");

		FMain.ZRunApplication();

		OutputServer.Stop();
	}

	internal static AOutputServer OutputServer = new AOutputServer(true);
	public static ProgramSettings Settings;
	public static FMain MainForm;
	public static FilesModel Model;
	public static RunningTasks Tasks;
	public static string UserGuid;
	public const string AppName = "QM3";

	/// <summary>
	/// Timer with 1 s period.
	/// </summary>
	public static event Action Timer1s;

	/// <summary>
	/// Timer with 1 s period when main window hidden and 0.25 s period when visible.
	/// </summary>
	public static event Action Timer1sOr025s;

	/// <summary>
	/// True if Timer1sOr025s period is 0.25 s (when main window visible), false if 1 s (when hidden).
	/// </summary>
	public static bool IsTimer025 => s_timerCounter > 0;
	static uint s_timerCounter;

	static void _TimerProc(ATimer t)
	{
		bool needFast = MainForm.Visible;
		if(needFast != (s_timerCounter > 0)) t.Start(needFast ? 250 : 1000, false);
		if(needFast) {
			Timer1sOr025s?.Invoke();
			s_timerCounter++;
			if(MousePosChangedWhenProgramVisible != null) {
				var p = AMouse.XY;
				if(p != s_mousePos) {
					s_mousePos = p;
					MousePosChangedWhenProgramVisible(p);
				}
			}
		} else s_timerCounter = 0;
		if(0 == (s_timerCounter & 3)) Timer1s?.Invoke();
	}
	static POINT s_mousePos;

	/// <summary>
	/// When cursor position changed while the main window is visible.
	/// Called at max 0.25 s rate, not for each change.
	/// Cursor can be in any window. Does not depend on UAC.
	/// Receives cursor position in screen.
	/// </summary>
	public static event Action<POINT> MousePosChangedWhenProgramVisible;

	public static EProgramState Loaded;

	static bool _RestartAsAdmin(string[] args)
	{
		return false; //TODO
		if(Debugger.IsAttached) return false;
		try {
			//int pid = 
			Au.Util.LibTaskScheduler.RunTask(@"Au", "Au.Editor", true, args);
			//Api.AllowSetForegroundWindow(pid); //fails and has no sense, because it's Au.CL.exe running as SYSTEM
		}
		catch(Exception ex) { //probably this program is not installed (no scheduled task)
			ADebug.Dialog(ex);
			return false;
		}
		return true;
	}

	public static void Test()
	{
		//ETest.DevTools.CreatePngImagelistFileFromIconFiles_il_tv();
		//ETest.DevTools.CreatePngImagelistFileFromIconFiles_il_tb();
		//ETest.DevTools.CreatePngImagelistFileFromIconFiles_il_tb_big();

		//RunUac.Test();

		//AOutput.QM2.UseQM2 = true; AOutput.Clear();
		//using(var h = AHookWin.Keyboard(k => {
		//	Print($"{k.Key}, {!k.IsUp}");
		//	if(k.Key == KKey.Up && !k.IsUp) 400.ms();

		//	return false;
		//})) ADialog.Show("hook");
	}
}

enum EProgramState
{
	/// <summary>
	/// Before the first workspace fully loaded.
	/// </summary>
	Loading,

	/// <summary>
	/// When fully loaded first workspace etc and created main form handle.
	/// Main form invisible; control handles not created.
	/// </summary>
	LoadedWorkspace,

	/// <summary>
	/// Control handles created.
	/// Main form is either visible now or was visible and now hidden.
	/// </summary>
	LoadedUI,

	/// <summary>
	/// Executing OnFormClosed of main form.
	/// Unloading workspace; stopping everything.
	/// </summary>
	Unloading,

	/// <summary>
	/// After OnFormClosed of main form.
	/// Workspace unloaded; everything stopped.
	/// </summary>
	Unloaded,
}