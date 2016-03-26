﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.ComponentModel;
//using System.Linq;
//using System.Threading;
//using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
//using System.Runtime.CompilerServices;
//using System.IO;
using System.Windows.Forms;
using System.Drawing;

using static Catkeys.NoClass;
using Catkeys.Util;
using Util = Catkeys.Util;
using static Catkeys.Util.NoClass;
using Catkeys.Winapi;
using Auto = Catkeys.Automation;

#pragma warning disable 649 //unused fields in API structs

namespace Catkeys
{
	/// <summary>
	/// Standard dialogs, tooltips and other windows to show information to the user or get instructions/input from the user.
	/// </summary>
	//[DebuggerStepThrough]
	public static partial class Show
	{

		//Other parts of this class are below in this file, wrapped in regions together with associated enums etc.
	}

	#region MessageDialog

	/// <summary>
	/// MessageDialog return value (user-clicked button).
	/// </summary>
	public enum MDResult
	{
		OK = 1, Cancel = 2, Abort = 3, Retry = 4, Ignore = 5, Yes = 6, No = 7, Timeout = 9, TryAgain = 10, Continue = 11,
	}

	/// <summary>
	/// MessageDialog buttons.
	/// </summary>
	public enum MDButtons
	{
		OK = 0, OKCancel = 1, AbortRetryIgnore = 2, YesNoCancel = 3, YesNo = 4, RetryCancel = 5, CancelTryagainContinue = 6,
		//TODO: consider: Abort = -1 (QM2 mes-). In TaskDialog too.
	}

	/// <summary>
	/// MessageDialog icon.
	/// </summary>
	public enum MDIcon
	{
		None = 0, Error = 0x10, Question = 0x20, Warning = 0x30, Info = 0x40, Shield = 0x50, App = 0x60,
	}

	/// <summary>
	/// MessageDialog flags.
	/// </summary>
	[Flags]
	public enum MDFlag :uint
	{
		DefaultButton2 = 0x100, DefaultButton3 = 0x200, DefaultButton4 = 0x300,
		SystemModal = 0x1000, DisableThreadWindows = 0x2000, HelpButton = 0x4000,
		TryActivate = 0x10000, DefaultDesktopOnly = 0x20000, Topmost = 0x40000, RightAlign = 0x80000, RtlLayout = 0x100000, ServiceNotification = 0x200000,
		//not API flags
		NoSound = 0x80000000,
	}

	public static partial class Show
	{
		/// <summary>
		/// Shows classic message box dialog.
		/// Like System.Windows.Forms.MessageBox.Show but has more options and is always-on-top by default.
		/// </summary>
		/// <param name="owner">Owner window or Zero.</param>
		/// <param name="text">Text.</param>
		/// <param name="buttons">Example: MDButtons.YesNo.</param>
		/// <param name="icon">One of standard icons. Example: MDIcon.Info.</param>
		/// <param name="flags">One or more options. Example: MDFlag.NoTopmost|MDFlag.DefaultButton2.</param>
		/// <param name="title">Title bar text. If omitted, null or "", uses ScriptOptions.DisplayName (default is appdomain name).</param>
		/// <remarks>
		/// These script options are applied: Script.Option.dialogRtlLayout, Script.Option.dialogTopmostIfNoOwner.
		/// </remarks>
		public static MDResult MessageDialog(Wnd owner, string text, MDButtons buttons = 0, MDIcon icon = 0, MDFlag flags = 0, string title = null)
		{
			//const uint MB_SYSTEMMODAL = 0x1000; //same as MB_TOPMOST + adds system icon in title bar (why need it?)
			const uint MB_USERICON = 0x80;
			const uint IDI_APPLICATION = 32512;
			const uint IDI_ERROR = 32513;
			const uint IDI_QUESTION = 32514;
			const uint IDI_WARNING = 32515;
			const uint IDI_INFORMATION = 32516;
			const uint IDI_SHIELD = 106; //32x32 icon. The value is undocumented, but we use it instead of the documented IDI_SHIELD value which on Win7 displays clipped 128x128 icon. Tested: the function does not fail with invalid icon resource id.

			var p = new MSGBOXPARAMS(title);
			p.lpszText=text;

			bool alien = (flags&(MDFlag.DefaultDesktopOnly|MDFlag.ServiceNotification))!=0;
			if(alien) owner=Zero; //API would fail. The dialog is displayed in csrss process.

			if(icon==MDIcon.None) { } //no sound
			else if(icon==MDIcon.Shield || icon==MDIcon.App || flags.HasFlag(MDFlag.NoSound)) {
				switch(icon) {
				case MDIcon.Error: p.lpszIcon=(IntPtr)IDI_ERROR; break;
				case MDIcon.Question: p.lpszIcon=(IntPtr)IDI_QUESTION; break;
				case MDIcon.Warning: p.lpszIcon=(IntPtr)IDI_WARNING; break;
				case MDIcon.Info: p.lpszIcon=(IntPtr)IDI_INFORMATION; break;
				case MDIcon.Shield: p.lpszIcon=(IntPtr)IDI_SHIELD; break;
				case MDIcon.App:
					p.lpszIcon=(IntPtr)IDI_APPLICATION;
					if(Resources.AppIconHandle32!=Zero) p.hInstance=Misc.GetModuleHandleOfAppdomainEntryAssembly();
					//info: C# compiler adds icon to the native resources as IDI_APPLICATION.
					//	If assembly without icon, we set hInstance=0 and then the API shows common app icon.
					//	In any case, it will be the icon displayed in File Explorer etc.
					break;
				}
				p.dwStyle|=MB_USERICON; //disables sound
				icon=0;
			}
			//note: Too difficult to add app icon because it must be in unmanaged resources. If need, use TaskDialog, it accepts icon handle.

			if(Script.Option.dialogRtlLayout) flags|=MDFlag.RtlLayout;
			if(owner.IsZero) {
				flags|=MDFlag.TryActivate; //if foreground lock disabled, activates, else flashes taskbar button; without this flag the dialog woud just sit behind other windows, often unnoticed.
				if(Script.Option.dialogTopmostIfNoOwner) flags|=MDFlag.SystemModal; //makes topmost, always works, but also adds an unpleasant system icon in title bar
																					//if(Script.Option.dialogTopmostIfNoOwner) flags|=MDFlag.Topmost; //often ignored, without a clear reason and undocumented, also noticed other anomalies
			}
			//tested: if owner is child, the API disables its top-level parent.
			//consider: if owner 0, create hidden parent window to:
			//	Avoid adding taskbar icon.
			//	Apply Option.Monitor.
			//consider: if owner 0, and current foreground window is of this thread, let it be owner. Maybe a flag.
			//consider: if owner of other thread, don't disable it. But how to do it without hook? Maybe only inherit owner's monitor.
			//consider: play user-defined sound, eg default "meow".

			p.hwndOwner=owner;

			flags&=~(MDFlag.NoSound); //not API flags
			p.dwStyle|=(uint)buttons | (uint)icon | (uint)flags;

			int R = MessageBoxIndirect(ref p);
			if(R==0) throw new CatkeysException();
			//DoEvents(); //process messages, or later something may not work //TODO
			//WaitForAnActiveWindow(500, 2); //TODO
			return (MDResult)R;

			//tested:
			//user32:MessageBoxTimeout. Undocumented. Too limited etc to be useful. If need timeout, use TaskDialog.
			//shlwapi:SHMessageBoxCheck. Too limited etc to be useful.
			//wtsapi32:WTSSendMessageW. In csrss process, no themes, etc. Has timeout.
		}

		/// <summary>
		/// Shows classic message box dialog.
		/// Returns clicked button's character (as in style), eg 'O' for OK.
		/// You can specify buttons etc in style string, which can contain:
		/// <para>
		/// Buttons: OC OKCancel, YN YesNo, YNC YesNoCancel, ARI AbortRetryIgnore, RC RetryCancel, CTE CancelTryagainContinue.
		/// Icon: x error, ! warning, i info, ? question, v shield, a app.
		/// Flags: s no sound, t topmost, d disable windows.
		/// Default button: 2 or 3.
		/// </para>
		/// </summary>
		/// <param name="text">Text.</param>
		/// <param name="style">Example: "YN!".</param>
		/// <param name="owner">Owner window or Zero.</param>
		/// <param name="title">Title bar text. If omitted, null or "", uses ScriptOptions.DisplayName (default is appdomain name).</param>
		/// <remarks>
		/// These script options are applied: Script.Option.dialogRtlLayout, Script.Option.dialogTopmostIfNoOwner.
		/// </remarks>
		public static char MessageDialog(string text, string style = null, Wnd owner = default(Wnd), string title = null)
		{
			MDButtons buttons = 0;
			MDIcon icon = 0;
			MDFlag flags = 0;

			if(!string.IsNullOrEmpty(style)) {
				if(style.Contains("OC")) buttons=MDButtons.OKCancel;
				else if(style.Contains("YNC")) buttons=MDButtons.YesNoCancel;
				else if(style.Contains("YN")) buttons=MDButtons.YesNo;
				else if(style.Contains("ARI")) buttons=MDButtons.AbortRetryIgnore;
				else if(style.Contains("RC")) buttons=MDButtons.RetryCancel;
				else if(style.Contains("CT")) buttons=MDButtons.CancelTryagainContinue; //not CTC, because Continue returns E

				if(style.Contains("x")) icon=MDIcon.Error;
				else if(style.Contains("?")) icon=MDIcon.Question;
				else if(style.Contains("!")) icon=MDIcon.Warning;
				else if(style.Contains("i")) icon=MDIcon.Info;
				else if(style.Contains("v")) icon=MDIcon.Shield;
				else if(style.Contains("a")) icon=MDIcon.App;

				if(style.Contains("t")) flags|=MDFlag.SystemModal; //MDFlag.Topmost often ignored etc
				if(style.Contains("s")) flags|=MDFlag.NoSound;
				if(style.Contains("d")) flags|=MDFlag.DisableThreadWindows;

				if(style.Contains("2")) flags|=MDFlag.DefaultButton2;
				else if(style.Contains("3")) flags|=MDFlag.DefaultButton3;
			}

			int r = (int)MessageDialog(owner, text, buttons, icon, flags, title);

			return (r>0 && r<12) ? "COCARIYNCCTE"[r] : 'C';
		}

		struct MSGBOXPARAMS
		{
			public int cbSize;
			public Wnd hwndOwner;
			public IntPtr hInstance;
			public string lpszText;
			public string lpszCaption;
			public uint dwStyle;
			public IntPtr lpszIcon;
			public LPARAM dwContextHelpId;
			public IntPtr lpfnMsgBoxCallback;
			public uint dwLanguageId;

			public MSGBOXPARAMS(string title) : this()
			{
				cbSize=Marshal.SizeOf(typeof(MSGBOXPARAMS));
				lpszCaption=_Title(title);
			}
		}

		[DllImport("user32.dll")]
		static extern int MessageBoxIndirect([In] ref MSGBOXPARAMS lpMsgBoxParams);

	}
	#endregion MessageDialog

	#region TaskDialog

	/// <summary>
	/// TaskDialog buttons.
	/// </summary>
	[Flags]
	public enum TDButton
	{
		OK = 1, Yes = 2, No = 4, Cancel = 8, Retry = 0x10, Close = 0x20,
		OKCancel = OK|Cancel, YesNo = Yes|No, RetryCancel = Retry|Cancel
	}

	/// <summary>
	/// TaskDialog icon.
	/// </summary>
	[Flags]
	public enum TDIcon
	{
		Warning = 0xffff, Error = 0xfffe, Info = 0xfffd, Shield = 0xfffc,
		App = 32512 //IDI_APPLICATION
	}

	/// <summary>
	/// TaskDialog flags.
	/// </summary>
	[Flags]
	public enum TDFlag
	{
		CommandLinks = 1, Topmost = 2, NoActivate = 4, NoTaskbarButton = 8,
	}

	/// <summary>
	/// TaskDialog common return value.
	/// </summary>
	public enum TDResult
	{
		Cancel = 0, OK = -1, Retry = -4, Yes = -6, No = -7, Close = -8,
		Timeout = -99
	}

	public static partial class Show
	{
		//This API does not support some features we need.
		//[DllImport("comctl32.dll", EntryPoint = "TaskDialog")]
		//static extern int TaskDialogAPI(Wnd hWndParent, IntPtr hInstance, string pszWindowTitle, string pszMainInstruction, string pszContent, TDButton dwCommonButtons, IntPtr pszIcon, out int pnButton);

		/// <summary>
		/// Shows simple task dialog.
		/// Return clicked button, eg TDResult.OK. Note: TDResult.Cancel value is 0.
		/// </summary>
		/// <param name="owner">Owner window or Zero.</param>
		/// <param name="mainText">Main instruction. Bigger font.</param>
		/// <param name="moreText">Text below main instruction.</param>
		/// <param name="buttons">Examples: TDButton.YesNo, TDButton.OK|TDButton.Close. If omitted or 0, adds OK button.</param>
		/// <param name="icon">One of four standard icons, eg TDIcon.Info.</param>
		/// <param name="flags">Example: TDFlag.CommandLinks|TDFlag.NoActivate.</param>
		/// <param name="customButtons">List of strings "id text" separated by |. Example: <c>"1 One|2 Two|3 Three"</c>. Use TDFlag.CommandLinks in flags to change button style.</param>
		/// <param name="expandedText">Text that the user can show and hide.</param>
		/// <param name="footerText">Text at the bottom of the dialog.</param>
		/// <param name="timeoutS">If not 0, auto-close the dialog after this time, number of seconds.</param>
		/// <param name="title">Title bar text. If omitted, null or "", uses ScriptOptions.DisplayName (default is appdomain name).</param>
		/// <param name="onLinkClick">Enables hyperlinks. A lambda or other kind of delegate callback function to call on link click. Example: <c>Show.TaskDialog("", "<a href=\"example\">link</a>.", onLinkClick: (o, a) => { Out(a.LinkHref); });</c></param>
		/// <remarks>
		/// These script options are applied: Script.Option.dialogRtlLayout, Script.Option.dialogTopmostIfNoOwner.
		/// </remarks>
		public static TDResult TaskDialog(
			Wnd owner, string mainText, string moreText = null, TDButton buttons = 0, TDIcon icon = 0, TDFlag flags = 0,
			string customButtons = null, string expandedText = null, string footerText = null, int timeoutS = 0, string title = null,
			EventHandler<AdvancedTaskDialog.TDEventArgs> onLinkClick = null
			)
		{
			var d = new AdvancedTaskDialog(mainText, moreText, buttons, icon, flags,
				customButtons, expandedText, footerText, timeoutS, title, onLinkClick);
			return d.Show(owner);
		}
		//TODO: consider: return string, eg "OK", "Custom button text first line", "hyperlink href".

		/// <summary>
		/// Shows simple task dialog.
		/// Returns clicked button's character (as in style), eg 'O' for OK.
		/// You can specify buttons etc in style string, which can contain:
		/// <para>
		/// Buttons: O OK, C Cancel, Y Yes, N No, R Retry, L Close.
		/// Icon: x error, ! warning, i info, v shield, a app.
		/// Flags: c command links, t topmost, n don't activate, b no taskbar button.
		/// </para>
		/// </summary>
		/// <param name="mainText">Main instruction. Bigger font.</param>
		/// <param name="moreText">Text below main instruction.</param>
		/// <param name="style">Example: "YN!c".</param>
		/// <param name="owner">Owner window or Zero.</param>
		/// <param name="customButtons">List of strings "id text" separated by |. Example: <c>"1 One|2 Two|3 Three"</c>. Use "c" in style to change button style.</param>
		/// <param name="expandedText">Text that the user can show and hide.</param>
		/// <param name="footerText">Text at the bottom of the dialog.</param>
		/// <param name="timeoutS">If not 0, auto-close the dialog after this time, number of seconds.</param>
		/// <param name="title">Title bar text. If omitted, null or "", uses ScriptOptions.DisplayName (default is appdomain name).</param>
		/// <param name="onLinkClick">Enables hyperlinks. A lambda or other kind of delegate callback function to call on link click. Example: <c>Show.TaskDialog("", "<a href=\"example\">link</a>.", onLinkClick: (o, a) => { Out(a.LinkHref); });</c></param>
		/// <remarks>
		/// These script options are applied: Script.Option.dialogRtlLayout, Script.Option.dialogTopmostIfNoOwner.
		/// </remarks>
		public static char TaskDialog(
			string mainText, string moreText = null, string style = null, Wnd owner = default(Wnd),
			string customButtons = null, string expandedText = null, string footerText = null, int timeoutS = 0, string title = null,
			EventHandler<AdvancedTaskDialog.TDEventArgs> onLinkClick = null
			)
		{
			var d = new AdvancedTaskDialog(mainText, moreText, style,
				customButtons, expandedText, footerText, timeoutS, title, onLinkClick);
			int r = -(int)d.Show(owner);
			return (r>=0 && r<9) ? "COCCRCYNL"[r] : 'C';
		}

		public static int TaskListDialog(
			string[] list, string mainText = null, string moreText = null, Wnd owner = default(Wnd), string style = null, string title = null)
		{
			var d = new AdvancedTaskDialog(mainText, moreText, style, null, title: title);
			d.SetTitleBarText(title);
			d.SetText(mainText, moreText);
			d.SetStyle(style);
			d.SetCustomButtons(list, true);
			d.FlagAllowCancel=true; //instead of TDButton.Cancel; users can add Cancel as custom button
			return (int)d.Show(owner);
		}

		public static int TaskListDialog(
			string list, string mainText = null, string moreText = null, Wnd owner = default(Wnd), string style = null, string title = null)
		{
			return TaskListDialog(_ButtonsStringToArray(list), mainText, moreText, owner, style, title);
		}

		/// <summary>
		/// Shows advanced task dialog.
		/// Wraps Windows API function TaskDialogIndirect. You can find more info MSDN.
		/// </summary>
		/// <example>
		/// <code>
		/// var d = new Show.AdvancedTaskDialog();
		/// d.SetText("Main text.", "More text.\nSupports <A HREF=\"link data\">links</A> if you subscribe to HyperlinkClick event.");
		/// d.SetStyle("OC!");
		/// d.SetExpandedText("Expanded info\nand more info.", true);
		/// d.FlagCanBeMinimized=true;
		/// d.SetCustomButtons("1 one|2 two\nzzz", true);
		/// d.SetRadioButtons("1001 r1|1002 r2");
		/// d.SetTimeout(10, "OK");
		/// d.HyperlinkClicked+=(o, a) => { Out($"{a.Message} {a.LinkHref}"); };
		/// d.ButtonClicked+=(o, a) => { Out($"{a.Message} {a.WParam}"); if(a.WParam==7) a.Return=1; }; //prevent closing if clicked No
		/// d.FlagShowProgressBar=true; d.Timer+=(o, a) => { a.Dialog.Send((uint)Show.AdvancedTaskDialog.TDM_.SET_PROGRESS_BAR_POS, a.WParam/100); };
		/// TDResult b = d.Show();
		/// switch(b) { case TDResult.OK: ... case (TDResult)1: ... }
		/// switch(d.ResultRadioButton) { ... }
		/// if(d.ResultIsChecked) { ... }
		/// </code>
		/// </example>
		/// <remarks>
		/// These script options are applied: Script.Option.dialogRtlLayout, Script.Option.dialogTopmostIfNoOwner.
		/// </remarks>
		public class AdvancedTaskDialog
		{
			[DllImport("comctl32.dll")]
			static extern int TaskDialogIndirect([In] ref TASKDIALOGCONFIG c, out int pnButton, out int pnRadioButton, out int pChecked);

			const int WM_USER = 0x400;

			/// <summary>
			/// Messages that your event handler can send to the dialog.
			/// Reference: MSDN -> "Task Dialog Messages".
			/// </summary>
			public enum TDM_ :uint
			{
				NAVIGATE_PAGE = WM_USER+101,
				CLICK_BUTTON = WM_USER+102, // wParam = Button ID
				SET_MARQUEE_PROGRESS_BAR = WM_USER+103, // wParam = 0 (nonMarque) wParam != 0 (Marquee)
				SET_PROGRESS_BAR_STATE = WM_USER+104, // wParam = new progress state
				SET_PROGRESS_BAR_RANGE = WM_USER+105, // lParam = MAKELPARAM(nMinRange, nMaxRange)
				SET_PROGRESS_BAR_POS = WM_USER+106, // wParam = new position
				SET_PROGRESS_BAR_MARQUEE = WM_USER+107, // wParam = 0 (stop marquee), wParam != 0 (start marquee), lparam = speed (milliseconds between repaints)
				SET_ELEMENT_TEXT = WM_USER+108, // wParam = element (TASKDIALOG_ELEMENTS), lParam = new element text (LPCWSTR)
				CLICK_RADIO_BUTTON = WM_USER+110, // wParam = Radio Button ID
				ENABLE_BUTTON = WM_USER+111, // lParam = 0 (disable), lParam != 0 (enable), wParam = Button ID
				ENABLE_RADIO_BUTTON = WM_USER+112, // lParam = 0 (disable), lParam != 0 (enable), wParam = Radio Button ID
				CLICK_VERIFICATION = WM_USER+113, // wParam = 0 (unchecked), 1 (checked), lParam = 1 (set key focus)
				UPDATE_ELEMENT_TEXT = WM_USER+114, // wParam = element (TASKDIALOG_ELEMENTS), lParam = new element text (LPCWSTR)
				SET_BUTTON_ELEVATION_REQUIRED_STATE = WM_USER+115, // wParam = Button ID, lParam = 0 (elevation not required), lParam != 0 (elevation required)
				UPDATE_ICON = WM_USER+116  // wParam = icon element (TASKDIALOG_ICON_ELEMENTS), lParam = new icon (hIcon if TDF_USE_HICON_* was set, PCWSTR otherwise)
			}

			/// <summary>
			/// Notification messages that your event handler receives.
			/// Reference: MSDN -> "Task Dialog Notifications".
			/// </summary>
			public enum TDN_ :uint
			{
				CREATED = 0,
				NAVIGATED = 1,
				BUTTON_CLICKED = 2,
				HYPERLINK_CLICKED = 3,
				TIMER = 4,
				DESTROYED = 5,
				RADIO_BUTTON_CLICKED = 6,
				DIALOG_CONSTRUCTED = 7,
				VERIFICATION_CLICKED = 8,
				HELP = 9,
				EXPANDO_BUTTON_CLICKED = 10
			}

			/// <summary>
			/// Constants for TDM_.SET_ELEMENT_TEXT and TDM_.UPDATE_ELEMENT_TEXT messages.
			/// </summary>
			public enum TDE_
			{
				CONTENT,
				EXPANDED_INFORMATION,
				FOOTER,
				MAIN_INSTRUCTION
			}

			/// <summary>
			/// Constants for TDM_.UPDATE_ICON message.
			/// </summary>
			public enum TDIE_
			{
				ICON_MAIN,
				ICON_FOOTER
			}

			//TASKDIALOGCONFIG flags.
			[Flags]
			internal enum TDF_
			{
				ENABLE_HYPERLINKS = 0x0001,
				USE_HICON_MAIN = 0x0002,
				USE_HICON_FOOTER = 0x0004,
				ALLOW_DIALOG_CANCELLATION = 0x0008,
				USE_COMMAND_LINKS = 0x0010,
				USE_COMMAND_LINKS_NO_ICON = 0x0020,
				EXPAND_FOOTER_AREA = 0x0040,
				EXPANDED_BY_DEFAULT = 0x0080,
				VERIFICATION_FLAG_CHECKED = 0x0100,
				SHOW_PROGRESS_BAR = 0x0200,
				SHOW_MARQUEE_PROGRESS_BAR = 0x0400,
				CALLBACK_TIMER = 0x0800,
				POSITION_RELATIVE_TO_WINDOW = 0x1000,
				RTL_LAYOUT = 0x2000,
				NO_DEFAULT_RADIO_BUTTON = 0x4000,
				CAN_BE_MINIMIZED = 0x8000,
				TDF_SIZE_TO_CONTENT = 0x1000000, //possibly added later than in Vista, don't know when
			}

			[StructLayout(LayoutKind.Sequential, Pack = 1)]
			struct TASKDIALOG_BUTTON
			{
				public int id;
				public string text;
			}

			delegate int TaskDialogCallbackProc(Wnd hwnd, TDN_ notification, LPARAM wParam, LPARAM lParam, IntPtr data);

			[StructLayout(LayoutKind.Sequential, Pack = 1)]
			struct TASKDIALOGCONFIG
			{
				public int cbSize;
				public Wnd hwndParent;
				public IntPtr hInstance;
				public TDF_ dwFlags;
				public TDButton dwCommonButtons;
				public string pszWindowTitle;
				public IntPtr hMainIcon;
				public string pszMainInstruction;
				public string pszContent;
				public int cButtons;
				public IntPtr pButtons;
				public int nDefaultButton;
				public int cRadioButtons;
				public IntPtr pRadioButtons;
				public int nDefaultRadioButton;
				public string pszVerificationText;
				public string pszExpandedInformation;
				public string pszExpandedControlText;
				public string pszCollapsedControlText;
				public IntPtr hFooterIcon;
				public string pszFooter;
				public TaskDialogCallbackProc pfCallback;
				public IntPtr lpCallbackData;
				public int cxWidth;

				public TASKDIALOGCONFIG(string title) : this()
				{
					cbSize=Marshal.SizeOf(typeof(TASKDIALOGCONFIG));
					pszWindowTitle=_Title(title);
				}
			}

			TASKDIALOGCONFIG c;
			string[] _buttons, _radioButtons; //before showing dialog these will be marshaled to IntPtr

			/// <summary>
			/// Creates new object.
			/// </summary>
			public AdvancedTaskDialog()
			{
				c.cbSize=Marshal.SizeOf(typeof(TASKDIALOGCONFIG));
				FlagRtlLayout=Script.Option.dialogRtlLayout;
			}

			/// <summary>
			/// Creates new object and sets commonly used parameters.
			/// All parameters are the same as of Show.TaskDialog.
			/// </summary>
			public AdvancedTaskDialog(
				string mainText, string moreText = null, TDButton buttons = 0, TDIcon icon = 0, TDFlag flags = 0,
				string customButtons = null, string expandedText = null, string footerText = null, int timeoutS = 0, string title = null,
				EventHandler<TDEventArgs> onLinkClick = null
				) : this()
			{
				SetText(mainText, moreText);
				SetButtons(buttons);
				SetIcon(icon);
				FlagTopmost=flags.HasFlag(TDFlag.Topmost);
				FlagNoActivate=flags.HasFlag(TDFlag.NoActivate);
				FlagNoTaskbarButton=flags.HasFlag(TDFlag.NoTaskbarButton);
				SetCustomButtons(customButtons, flags.HasFlag(TDFlag.CommandLinks));
				SetExpandedText(expandedText);
				SetFooterText(footerText);
				SetTimeout(timeoutS);
				SetTitleBarText(title);
				if(onLinkClick!=null) HyperlinkClicked+=onLinkClick;
			}

			/// <summary>
			/// Creates new object and sets commonly used parameters.
			/// All parameters are the same as of Show.TaskDialog.
			/// </summary>
			public AdvancedTaskDialog(
				string mainText, string moreText, string style,
				string customButtons = null, string expandedText = null, string footerText = null, int timeoutS = 0, string title = null,
				EventHandler<TDEventArgs> onLinkClick = null
				) : this()
			{
				SetText(mainText, moreText);
				SetCustomButtons(customButtons); //this must be before SetStyle, because it can set 'command links'
				SetStyle(style);
				SetExpandedText(expandedText);
				SetFooterText(footerText);
				SetTimeout(timeoutS);
				SetTitleBarText(title);
				if(onLinkClick!=null) HyperlinkClicked+=onLinkClick;
			}

			/// <summary>
			/// Changes title bar text.
			/// If you don't call this method or text is null or "", the dialog uses ScriptOptions.DisplayName (default is appdomain name).
			/// </summary>
			public void SetTitleBarText(string text)
			{
				c.pszWindowTitle=_Title(text);
			}

			/// <summary>
			/// Sets text.
			/// </summary>
			/// <param name="mainText">Main instruction. Bigger font.</param>
			/// <param name="moreText">Text below main instruction.</param>
			public void SetText(string mainText, string moreText = null)
			{
				c.pszMainInstruction=mainText;
				c.pszContent=moreText;
			}

			/// <summary>
			/// Sets common icon.
			/// </summary>
			/// <param name="icon">One of four standard icons, eg TDIcon.Info.</param>
			public void SetIcon(TDIcon icon)
			{
				c.hMainIcon=(IntPtr)(int)icon;
				USE_HICON_MAIN=false;
			}
			/// <summary>
			/// Sets custom icon.
			/// </summary>
			/// <param name="iconHandle">Native icon handle.</param>
			public void SetIcon(IntPtr iconHandle)
			{
				c.hMainIcon=iconHandle;
				USE_HICON_MAIN=iconHandle!=Zero;
				//tested: displays original-size 32 and 16 icons, but shrinks bigger icons to 32.
				//note: for App icon Show() will execute more code. The same for footer icon.
			}

			//TODO: add more SetIcon overloads (maybe also for footer icon): file, resource, Icon.

			/// <summary>
			/// Sets common buttons.
			/// </summary>
			/// <param name="buttons">Examples: TDButton.YesNo, TDButton.OK|TDButton.Close.</param>
			public void SetButtons(TDButton buttons)
			{
				c.dwCommonButtons=buttons;
			}

			/// <summary>
			/// Sets common buttons, icon and some flags.
			/// You can call this function instead of SetButtons(), SetIcon() etc if you prefer to specify all in string:
			/// <para>
			/// Buttons: O OK, C Cancel, Y Yes, N No, R Retry, L Close.
			/// Icon: x error, ! warning, i info, v shield, a app.
			/// Flags: c command links, t topmost, n don't activate, b no taskbar button. Does not unset if not specified.
			/// </para>
			/// </summary>
			/// <param name="buttonsIcon">Example: "YN!".</param>
			public void SetStyle(string style)
			{
				TDStyle t = _ParseStyleString(style);
				SetButtons(t.buttons);
				SetIcon(t.icon);
				if(t.commandLinks) USE_COMMAND_LINKS=true;
				if(t.topmost) FlagTopmost=true; //note: use if(). For other flags too.
				if(t.noActivate) FlagNoActivate=true;
				if(t.noTaskbarButton) FlagNoTaskbarButton=true;
			}

			struct TDStyle { public TDButton buttons; public TDIcon icon; public bool commandLinks, topmost, noActivate, noTaskbarButton; }

			static TDStyle _ParseStyleString(string style)
			{
				TDStyle r = default(TDStyle);
				if(style!=null) {
					foreach(char c in style) {
						switch(c) {
						case 'O': r.buttons|=TDButton.OK; break;
						case 'C': r.buttons|=TDButton.Cancel; break;
						case 'R': r.buttons|=TDButton.Retry; break;
						case 'Y': r.buttons|=TDButton.Yes; break;
						case 'N': r.buttons|=TDButton.No; break;
						case 'L': r.buttons|=TDButton.Close; break;
						case 'x': r.icon|=TDIcon.Error; break;
						case '!': r.icon|=TDIcon.Warning; break;
						case 'i': r.icon|=TDIcon.Info; break;
						case 'v': r.icon|=TDIcon.Shield; break;
						case 'a': r.icon|=TDIcon.App; break;
						case 'c': r.commandLinks=true; break;
						case 't': r.topmost=true; break;
						case 'n': r.noActivate=true; break;
						case 'b': r.noTaskbarButton=true; break;
						}
					}
				}
				return r;
			}

			/// <summary>
			/// Adds custom buttons specified as array and sets button style.
			/// </summary>
			/// <param name="buttons">Array of strings "id text". Example: <c>new string[]{"1 One", "2 Two", "3 Three"}</param>
			/// <param name="asCommandLinks">false - row of classic buttons; true - column of command-link buttons that can have multiline text. Note: SetStyle() also can set this, but cannot unset.</param>
			/// <param name="noCommandLinkIcon">No arrow icon in command-link buttons.</param>
			public void SetCustomButtons(string[] buttons, bool asCommandLinks = false, bool noCommandLinkIcon = false)
			{
				_buttons=buttons;
				USE_COMMAND_LINKS=asCommandLinks && !noCommandLinkIcon;
				USE_COMMAND_LINKS_NO_ICON=asCommandLinks && noCommandLinkIcon;
			}
			/// <summary>
			/// Adds custom buttons specified as string and sets button style.
			/// </summary>
			/// <param name="buttons">List of strings "id text" separated by |. Example: <c>"1 One|2 Two|3 Three"</c></param>
			/// <param name="asCommandLinks">false - row of classic buttons; true - column of command-link buttons that can have multiline text. Note: SetStyle() also can set this, but cannot unset.</param>
			/// <param name="noCommandLinkIcon">No arrow icon in command-link buttons.</param>
			public void SetCustomButtons(string buttons, bool asCommandLinks = false, bool noCommandLinkIcon = false)
			{
				SetCustomButtons(_ButtonsStringToArray(buttons), asCommandLinks, noCommandLinkIcon);
			}

			/// <summary>
			/// Sets default button to one of common buttons.
			/// </summary>
			public void SetDefaultButton(TDButton button) //common button
			{
				//map button flag to internal common button id
				switch(button) {
				case TDButton.OK: c.nDefaultButton=1; break;
				case TDButton.Cancel: c.nDefaultButton=2; break;
				case TDButton.Retry: c.nDefaultButton=4; break;
				case TDButton.Yes: c.nDefaultButton=6; break;
				case TDButton.No: c.nDefaultButton=7; break;
				case TDButton.Close: c.nDefaultButton=8; break;
				default: throw new ArgumentException(); //eg YesNo
				}
			}
			/// <summary>
			/// Sets default button to one of custom buttons.
			/// </summary>
			/// <param name="customButton">Custom button id as specified when calling SetCustomButtons().</param>
			public void SetDefaultButton(int customButton)
			{
				c.nDefaultButton=-customButton; //internally custom button ids are -idSpecified. See _MarshalButtons().
			}

			/// <summary>
			/// Adds radio (option) buttons specified as array.
			/// When Show() returns, use ResultRadioButton to get selected radio button id.
			/// </summary>
			/// <param name="buttons">Array of strings "id text". Example: <c>new string[]{"1 One", "2 Two", "3 Three"}</param>
			/// <param name="defaultId">Check this button. If omitted or 0, checks the first.</param>
			/// <param name="noDefaultButton">Don't check any.</param>
			public void SetRadioButtons(string[] buttons, int defaultId = 0, bool noDefaultButton = false)
			{
				_radioButtons=buttons;
				c.nDefaultRadioButton=defaultId;
				NO_DEFAULT_RADIO_BUTTON=noDefaultButton;
			}
			/// <summary>
			/// Adds radio (option) buttons specified as string.
			/// When Show() returns, use ResultRadioButton to get selected radio button id.
			/// </summary>
			/// <param name="buttons">List of strings "id text" separated by |. Example: <c>"1 One|2 Two|3 Three"</c></param>
			/// <param name="defaultId">Check this button. If omitted or 0, checks the first.</param>
			/// <param name="noDefaultButton">Don't check any.</param>
			public void SetRadioButtons(string buttons, int defaultId = 0, bool noDefaultButton = false)
			{
				SetRadioButtons(_ButtonsStringToArray(buttons), defaultId, noDefaultButton);
			}

			/// <summary>
			/// Adds check box.
			/// When Show() returns, use ResultIsChecked to get its state.
			/// </summary>
			public void SetCheckbox(string text, bool check)
			{
				c.pszVerificationText=text;
				VERIFICATION_FLAG_CHECKED=check;
			}

			/// <summary>
			/// Adds text that the user can show and hide.
			/// </summary>
			/// <param name="text">Text.</param>
			/// <param name="showInFooter">Show the text at the bottom of the dialog.</param>
			public void SetExpandedText(string text, bool showInFooter = false)
			{
				EXPAND_FOOTER_AREA=showInFooter;
				c.pszExpandedInformation=text;
			}

			/// <summary>
			/// Set properties of the control that shows and hides text added by SetExpandedText().
			/// </summary>
			/// <param name="defaultExpanded"></param>
			/// <param name="collapsedText"></param>
			/// <param name="expandedText"></param>
			public void SetExpandControl(bool defaultExpanded, string collapsedText = null, string expandedText = null)
			{
				EXPANDED_BY_DEFAULT=defaultExpanded;
				c.pszCollapsedControlText=collapsedText;
				c.pszExpandedControlText=expandedText;
			}

			/// <summary>
			/// Adds text and icon at the bottom of the dialog.
			/// </summary>
			/// <param name="text">Text.</param>
			/// <param name="icon">One of standard icons, eg TDIcon.Warning.</param>
			public void SetFooterText(string text, TDIcon icon = 0)
			{
				c.pszFooter=text;
				c.hFooterIcon=(IntPtr)(int)icon; USE_HICON_FOOTER=false;
			}
			/// <summary>
			/// Adds text and icon at the bottom of the dialog.
			/// </summary>
			/// <param name="text">Text.</param>
			/// <param name="icon">Native icon handle.</param>
			public void SetFooterText(string text, IntPtr iconHandle)
			{
				c.pszFooter=text;
				c.hFooterIcon=iconHandle; USE_HICON_FOOTER=iconHandle!=Zero;
			}

			/// <summary>
			/// Sets preferred width of the dialog, in dialog units.
			/// If 0 (default), calculates optimal width.
			/// </summary>
			public int Width { set { c.cxWidth=value; } }

			/// <summary>
			/// Sets dialog position.
			/// </summary>
			/// <param name="x"></param>
			/// <param name="y"></param>
			/// <param name="relativeToOwner"></param>
			/// <param name="raw"></param>
			public void SetXY(int x, int y, bool relativeToOwner = false, bool raw = false)
			{
				_x=x; _y=y; POSITION_RELATIVE_TO_WINDOW=relativeToOwner; _xyIsRaw =raw;
			}

			int _x, _y; bool _xyIsRaw;

			/// <summary>
			/// Let the dialog close itself after closeAfterS seconds.
			/// On timeout Show() returns TDResult.Timeout.
			/// <para>Example: <c>d.SetTimeout(30, "OK");</c></para>
			/// </summary>
			public void SetTimeout(int closeAfterS, string timeoutActionText = null, bool noInfo=false)
			{
				_timeoutS=closeAfterS;
				_timeoutActionText=timeoutActionText;
				timeoutNoInfo=noInfo;
            }
			int _timeoutS; bool _timeoutActive, timeoutNoInfo; string _timeoutActionText, _timeoutFooterText;

			public bool FlagAllowCancel { set; private get; }
			public bool FlagRtlLayout { set; private get; }
			public bool FlagCanBeMinimized { set; private get; }
			public bool FlagShowProgressBar { set; private get; }
			public bool FlagShowMarqueeProgressBar { set; private get; }

			public bool FlagTopmost { set { _flagTopmost=value; _flagTopmostChanged=true; } }
			bool _flagTopmost, _flagTopmostChanged;
			public bool FlagNoActivate { set; private get; }
			public bool FlagNoTaskbarButton { set; private get; }

			bool USE_HICON_MAIN;
			bool USE_HICON_FOOTER;
			bool VERIFICATION_FLAG_CHECKED;
			bool EXPAND_FOOTER_AREA;
			bool EXPANDED_BY_DEFAULT;
			bool USE_COMMAND_LINKS;
			bool USE_COMMAND_LINKS_NO_ICON;
			bool NO_DEFAULT_RADIO_BUTTON;
			bool POSITION_RELATIVE_TO_WINDOW;

			//Get results as properties.

			public int ResultRadioButton { get; private set; }
			public bool ResultIsChecked { get; private set; }

			Wnd _dlg; //need in hook proc etc
			bool _lockForegroundWindow;

			public TDResult Show(Wnd owner = default(Wnd))
			{
				ResultRadioButton=0; ResultIsChecked=false;
				_timeoutActive=false;
				_dlg=Zero;
				_lockForegroundWindow=false;

				int hr = 0, R = 0;

				c.hwndParent=owner;

				if(c.pszWindowTitle==null) c.pszWindowTitle=_Title(null);

				TDF_ f = 0;
				if(FlagAllowCancel) f|=TDF_.ALLOW_DIALOG_CANCELLATION;
				if(FlagRtlLayout) f|=TDF_.RTL_LAYOUT;
				if(FlagCanBeMinimized) f|=TDF_.CAN_BE_MINIMIZED;
				if(FlagShowProgressBar) f|=TDF_.SHOW_PROGRESS_BAR;
				if(FlagShowMarqueeProgressBar) f|=TDF_.SHOW_MARQUEE_PROGRESS_BAR;
				if(USE_COMMAND_LINKS) f|=TDF_.USE_COMMAND_LINKS;
				if(USE_COMMAND_LINKS_NO_ICON) f|=TDF_.USE_COMMAND_LINKS_NO_ICON;
				if(EXPAND_FOOTER_AREA) f|=TDF_.EXPAND_FOOTER_AREA;
				if(EXPANDED_BY_DEFAULT) f|=TDF_.EXPANDED_BY_DEFAULT;
				if(NO_DEFAULT_RADIO_BUTTON) f|=TDF_.NO_DEFAULT_RADIO_BUTTON;
				if(USE_HICON_MAIN) f|=TDF_.USE_HICON_MAIN;
				if(USE_HICON_FOOTER) f|=TDF_.USE_HICON_FOOTER;
				if(VERIFICATION_FLAG_CHECKED) f|=TDF_.VERIFICATION_FLAG_CHECKED;
				if(POSITION_RELATIVE_TO_WINDOW) f|=TDF_.POSITION_RELATIVE_TO_WINDOW;
				if(HyperlinkClicked!=null) f|=TDF_.ENABLE_HYPERLINKS;
				if(_timeoutS>0 || Timer!=null || FlagNoActivate) f|=TDF_.CALLBACK_TIMER;
				if(_timeoutS>0) {
					_timeoutActive=true;
					if(!timeoutNoInfo) {
						_timeoutFooterText=c.pszFooter;
						c.pszFooter=_TimeoutFooterText(_timeoutS);
						if(c.hFooterIcon==Zero) c.hFooterIcon=(IntPtr)TDIcon.Info;
					}
				}
				c.dwFlags=f;

				if(!_flagTopmostChanged && owner==Zero) _flagTopmost=Script.Option.dialogTopmostIfNoOwner;

				if((c.hMainIcon==(IntPtr)TDIcon.App || c.hFooterIcon==(IntPtr)TDIcon.App) && Resources.AppIconHandle32!=Zero)
					c.hInstance=Misc.GetModuleHandleOfAppdomainEntryAssembly();
				//info: TDIcon.App is IDI_APPLICATION (32512).
				//Although MSDN does not mention that IDI_APPLICATION can be used when hInstance is NULL, it works. Even works for many other undocumented system resource ids, eg 100.
				//Non-NULL hInstance is ignored for the icons specified as TD_x. It is documented and logical.
				//For App icon we could instead use icon handle, but then the small icon for the title bar and taskbar button can be distorted because shrinked from the big icon. Now extracts small icon from resources.
				//More info in Show.MessageDialog().

				c.pfCallback=_CallbackProc;

				IntPtr hhook = Zero; Api.HookProc hpHolder = null;

				try {
					c.pButtons=_MarshalButtons(_buttons, out c.cButtons, true);
					c.pRadioButtons=_MarshalButtons(_radioButtons, out c.cRadioButtons);

					if(_timeoutActive/* || FlagNoActivate*/) {
						//need to receive mouse and keyboard messages to stop countdown on click or key
						hhook=Api.SetWindowsHookEx(Api.WH_CBT, hpHolder=_HookProcCBT, Zero, Api.GetCurrentThreadId());
						//if(FlagNoActivate) _lockForegroundWindow=true;
					}

					if(FlagNoActivate) _lockForegroundWindow=Api.LockSetForegroundWindow(Api.LSFW_LOCK);

					int rRadioButton, rIsChecked;
#if DEBUG
					hr = _CallTDI(ref c, out R, out rRadioButton, out rIsChecked);
#else
					hr = TaskDialogIndirect(ref c, out R, out _rRadioButton, out _rIsChecked);
#endif
					ResultRadioButton=rRadioButton; ResultIsChecked=rIsChecked!=0;

				} finally {
					if(hhook!=Zero) Api.UnhookWindowsHookEx(hhook);

					_MarshalFreeButtons(ref c.pButtons, ref c.cButtons);
					_MarshalFreeButtons(ref c.pRadioButtons, ref c.cRadioButtons);
				}

				if(hr!=0) throw new Win32Exception(hr);

				if(R==2) R=0; else R=-R; //user button ids are -idSpecified; TDResult.Cancel is 0, other TDResult values negative. See _MarshalButtons().
				return (TDResult)R;
			}

			int _CallbackProc(Wnd w, TDN_ message, LPARAM wParam, LPARAM lParam, IntPtr data)
			{
				EventHandler<TDEventArgs> e = null;
				int R = 0;

				//Out(message);
				switch(message) {
				case TDN_.DIALOG_CONSTRUCTED:
					_dlg=w;
					break;
				case TDN_.CREATED:
					if(FlagNoTaskbarButton) w.ExStyleAdd(Api.WS_EX_TOOLWINDOW);

					if(_x!=0 || _y!=0 || _xyIsRaw) {
						//TODO: implement. Also use monitor.

					}

					if(_flagTopmost) w.ZorderTopmost();

					e=Created;
					break;
				case TDN_.TIMER:
					if(_timeoutActive) {
						int timeElapsed = wParam/1000;
						if(timeElapsed<_timeoutS) {
							if(!timeoutNoInfo) w.SendS((uint)TDM_.UPDATE_ELEMENT_TEXT, (int)TDE_.FOOTER, _TimeoutFooterText(_timeoutS-timeElapsed-1));
						} else {
							_timeoutActive=false;
							w.Send((uint)TDM_.CLICK_BUTTON, -(int)TDResult.Timeout, 0);
						}
					}

					if(_lockForegroundWindow) {
						_lockForegroundWindow=false;
						Api.LockSetForegroundWindow(Api.LSFW_UNLOCK);
						w.FlashStop();
					}

					e=Timer;
					break;
				case TDN_.DESTROYED: e=Destroyed; break;
				case TDN_.BUTTON_CLICKED: e=ButtonClicked; break;
				case TDN_.HYPERLINK_CLICKED: e=HyperlinkClicked; break;
				case TDN_.HELP: e=HelpF1; break;
				default: e=OtherMessages; break;
				}

				if(e!=null) {
					var a = new TDEventArgs(_dlg, message, wParam, lParam);
					e(this, a);
					R=a.Return;
				}

				return R;
			}

			public class TDEventArgs :EventArgs
			{
				public TDEventArgs(Wnd dlg, TDN_ message, LPARAM wParam, LPARAM lParam)
				{
					Dialog=dlg; Message=message; WParam=wParam;
					if(message==TDN_.HYPERLINK_CLICKED) LinkHref=Marshal.PtrToStringUni(lParam); else LinkHref=null;
				}
				public Wnd Dialog { get; private set; }
				public TDN_ Message { get; private set; }
				public LPARAM WParam { get; private set; }
				public string LinkHref { get; private set; }
				public int Return { get; set; }
			}

			public event EventHandler<TDEventArgs> Created, Destroyed, Timer, ButtonClicked, HyperlinkClicked, HelpF1, OtherMessages;

			//Disables timeout on click or key.
			IntPtr _HookProcCBT(int code, LPARAM wParam, IntPtr lParam)
			{
				switch(code) {
				case Api.HCBT_CLICKSKIPPED:
					if(_timeoutActive) switch((uint)wParam) { case Api.WM_LBUTTONUP: case Api.WM_NCLBUTTONUP: goto g1; }
					break;
				case Api.HCBT_KEYSKIPPED:
					g1:
					if(_timeoutActive && _dlg.IsActiveWindow) {
						_timeoutActive=false;
						if(!timeoutNoInfo) {
							if(Empty(_timeoutFooterText)) {
								_dlg.Send((uint)TDM_.UPDATE_ICON, (int)TDIE_.ICON_FOOTER, 0);
								_dlg.SendS((uint)TDM_.SET_ELEMENT_TEXT, (int)TDE_.FOOTER, ""); //null does not change text; however still remains some space for footer

								//c.pszFooter=null; Update(); //don't use this because interferes with the expand/collapse control
							} else _dlg.SendS((uint)TDM_.SET_ELEMENT_TEXT, (int)TDE_.FOOTER, _timeoutFooterText);
						}
					}
					break;
					//case Api.HCBT_ACTIVATE: //does not work. Disables activating this window, but another window is deactivated anyway.
					//	if(_lockForegroundWindow) { //return 1 to prevent activating
					//		if(_dlg==Zero) return (IntPtr)1; //before TDN_.DIALOG_CONSTRUCTED
					//		if(_dlg==(IntPtr)wParam) { if(_dlg.Visible) _lockForegroundWindow=false; else return (IntPtr)1; }
					//		//For our dialog HCBT_ACTIVATE received 4 times: 2 before TDN_.DIALOG_CONSTRUCTED/TDN_.CREATED and 2 after, but before visible.
					//	}
					//	break;
				}

				return Api.CallNextHookEx(Zero, code, wParam, lParam);
			}

			[DllImport("user32.dll", EntryPoint = "SendMessageW")]
			static extern LPARAM SendMessageTASKDIALOGCONFIG(Wnd hWnd, uint msg, LPARAM wParam, [In] ref TASKDIALOGCONFIG c);

			/// <summary>
			/// Applies new properties to the dialog while it is already open.
			/// Can be used for example to create wizard-like dialog with custom buttons "Next" and "Back".
			/// In an event handler, set new properties and then call this method.
			/// </summary>
			public void Update()
			{
				SendMessageTASKDIALOGCONFIG(_dlg, (uint)TDM_.NAVIGATE_PAGE, 0, ref c);
			}

			#region util
#if DEBUG //TODO: consider: use this func always.
			//The API throws 'access violation' exception if some value is invalid (eg unknown flags in dwCommonButtons) or it does not like something.
			//.NET does not allow to handle such exceptions, unless we use [HandleProcessCorruptedStateExceptions] or <legacyCorruptedStateExceptionsPolicy enabled="true"/> in config file.
			//It makes dev/debug more difficult.
			[System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions]
			static int _CallTDI(ref TASKDIALOGCONFIG c, out int pnButton, out int pnRadioButton, out int pChecked)
			{
				pnButton=pnRadioButton=pChecked=0;
				try {
					return TaskDialogIndirect(ref c, out pnButton, out pnRadioButton, out pChecked);
				} catch(Exception e) { throw new Win32Exception($"_CallTDI: {e.Message}"); } //note: not just throw;, and don't add inner exception
			}
#endif

			static IntPtr _MarshalButtons(string[] a, out int cButtons, bool escapeId = false)
			{
				if(a==null || a.Length==0) { cButtons=0; return Zero; }
				cButtons=a.Length;

				int structSize = Marshal.SizeOf(typeof(TASKDIALOG_BUTTON));
				IntPtr R = Marshal.AllocHGlobal(structSize * a.Length);

				for(int i = 0; i < a.Length; i++) {
					TASKDIALOG_BUTTON b; b.id = a[i].ToInt_(out b.text); //"id text" -> TASKDIALOG_BUTTON

					if(string.IsNullOrEmpty(b.text)) { b.text=a[i]; if(string.IsNullOrEmpty(b.text)) b.text=" "; } //exception if null or ""

					if(escapeId) { b.id=-b.id; if(b.id>0) throw new ArgumentException("button id < 0"); } //because 2==IDCANCEL, and most popular custom ids will be 1, 2, 3...

					unsafe { Marshal.StructureToPtr(b, (IntPtr)((byte*)R + (structSize * i)), false); }
				}

				return R;
			}

			static void _MarshalFreeButtons(ref IntPtr a, ref int cButtons)
			{
				if(a==Zero) return;

				int structSize = Marshal.SizeOf(typeof(TASKDIALOG_BUTTON));

				for(int i = 0; i < cButtons; i++) {
					unsafe { Marshal.DestroyStructure((IntPtr)((byte*)a + (structSize * i)), typeof(TASKDIALOG_BUTTON)); }
				}

				Marshal.FreeHGlobal(a);
				a=Zero; cButtons=0;
			}

			string _TimeoutFooterText(int timeLeft)
			{
				var s = new StringBuilder(FlagRtlLayout ? "." : "");
				s.AppendFormat("This dialog will disappear if not clicked in {0} s.", timeLeft);
				if(!Empty(_timeoutActionText)) s.AppendFormat(" Timeout action: {0}.", _timeoutActionText);
				if(FlagRtlLayout) s.Length--;
				if(!Empty(_timeoutFooterText)) s.AppendFormat("\n{0}", _timeoutFooterText);
				return s.ToString();
			}
			#endregion
		} //AdvancedTaskDialog

		static string[] _ButtonsStringToArray(string buttons)
		{
			return buttons==null ? null : buttons.Replace("\r\n", "\n").Replace("\n|", "|").Trim_("\r\n|").Split('|');
			//info: the API adds 2 newlines for \r\n. Only for custom buttons, not for other controls/parts.
		}
	}
	#endregion TaskDialog

	#region InputDialog

	public static partial class Show
	{


		public static bool InputDialog(string s)
		{
			return false;
		}

	}
	#endregion InputDialog

	#region util
	public static partial class Show
	{
		static string _Title(string title) { return string.IsNullOrEmpty(title) ? ScriptOptions.DisplayName : title; }
		//info: IsNullOrEmpty because if "", API TaskDialog uses "ProcessName.exe".

		public static class Resources
		{
			/// <summary>
			/// Gets native icon handle of the entry assembly of current appdomain.
			/// Returns Zero if the assembly is without icon.
			/// The icon is extracted first time and then cached in a static variable. Don't destroy the icon.
			/// </summary>
			public static IntPtr AppIconHandle32 { get { return _GetAppIconHandle(ref _AppIcon32, false); } }
			public static IntPtr AppIconHandle16 { get { return _GetAppIconHandle(ref _AppIcon16, true); } }
			static IntPtr _AppIcon32, _AppIcon16;

			static IntPtr _GetAppIconHandle(ref IntPtr hicon, bool small = false)
			{
				if(hicon==Zero) {
					var asm = Misc.AppdomainAssembly; if(asm==null) return Zero;
					IntPtr hinst = Misc.GetModuleHandleOf(asm);
					int size = small ? 16 : 32;
					hicon = Api.LoadImageRes(hinst, 32512, Api.IMAGE_ICON, size, size, Api.LR_SHARED);
					//note:
					//This is not 100% reliable because the icon id 32512 (IDI_APPLICATION) is undocumented.
					//I could not find a .NET method to get icon directly from native resources of assembly.
					//Could use Icon.ExtractAssociatedIcon(asm.Location), but it always gets 32 icon and is several times slower.
					//Also could use PrivateExtractIcons. But it uses file path, not module handle.
					//Also could use the resource emumeration API...
					//Never mind. Anyway, we use hInstance/resId with MessageBoxIndirect (which does not support handles) etc.
					//info: MSDN says that LR_SHARED gets cached icon regardless of size, but it is not true. Caches each size separately. Tested on Win 10, 7, XP.
				}
				return hicon;
			}
		}
	}
	#endregion util
}
