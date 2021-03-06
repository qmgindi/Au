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

namespace Au
{
	public unsafe partial struct wnd
	{
		/// <summary>
		/// Finds a child control and returns its handle as wnd.
		/// </summary>
		/// <returns>Returns <c>default(wnd)</c> if not found. See also: <see cref="Is0"/>, <see cref="operator +(wnd)"/>.</returns>
		/// <param name="name">
		/// Control name.
		/// String format: [](xref:wildcard_expression).
		/// null means 'can be any'. "" means 'no name'.
		/// 
		/// By default to get control names this function uses <see cref="Name"/>.
		/// Can start with these prefix strings:
		/// - <c>"***text "</c> - use <see cref="ControlText"/>. Slower and less reliable because can get editable text. If a character can be underlined with Alt, insert '&amp;' before it.
		/// - <c>"***elmName "</c> - use <see cref="NameElm"/>. Slower.
		/// - <c>"***wfName "</c> - use .NET Forms control name (see <see cref="WinformsControlNames"/>). Slower and can fail because of [](xref:uac).
		/// - <c>"***id "</c> like <c>"***id 15"</c> - use control id (<see cref="ControlId"/>). See also <see cref="ChildById"/>.
		/// </param>
		/// <param name="cn">
		/// Control class name.
		/// String format: [](xref:wildcard_expression).
		/// null means 'can be any'. Cannot be "".
		/// </param>
		/// <param name="flags"></param>
		/// <param name="also">
		/// Callback function. Called for each matching control.
		/// It can evaluate more properties of the control and return true when they match.
		/// Example: <c>also: t =&gt; t.IsEnabled</c>
		/// </param>
		/// <param name="skip">
		/// 0-based index of matching control.
		/// For example, if 1, the function skips the first matching control and returns the second.
		/// </param>
		/// <exception cref="AuWndException">This variable is invalid (window not found, closed, etc).</exception>
		/// <exception cref="ArgumentException">
		/// - <i>name</i> starts with <c>"***"</c>, but the prefix is invalid.
		/// - <i>cn</i> is "". To match any, use null.
		/// - Invalid wildcard expression (<c>"**options "</c> or regular expression).
		/// </exception>
		/// <remarks>
		/// To create code for this function, use dialog "Find window or control".
		/// </remarks>
		public wnd Child(
			[ParamString(PSFormat.wildex)] string name = null,
			[ParamString(PSFormat.wildex)] string cn = null,
			WCFlags flags = 0, Func<wnd, bool> also = null, int skip = 0)
		{
			//ThrowIfInvalid(); //will be called later
			var f = new wndChildFinder(name, cn, flags, also, skip);
			f.Find(this);
			return f.Result;
		}

		/// <summary>
		/// Returns true if this window contains the specified control.
		/// Calls <see cref="Child"/>.
		/// <note>
		/// Using this function many times with same parameters is inefficient. Instead create new <see cref="wndChildFinder"/> and call <see cref="wndChildFinder.Find"/> or <see cref="HasChild(wndChildFinder)"/>. See example.
		/// </note>
		/// </summary>
		/// <exception cref="AuWndException"/>
		/// <exception cref="ArgumentException"/>
		/// <example>
		/// <code><![CDATA[
		/// //find window that contains certain control, and get the control too
		/// var f = new wndChildFinder("Password*", "Static"); //control properties
		/// wnd w = wnd.find(cn: "#32770", also: t => t.HasChild(f));
		/// print.it(w);
		/// print.it(f.Result);
		/// ]]></code>
		/// </example>
		public bool HasChild(
			[ParamString(PSFormat.wildex)] string name = null,
			[ParamString(PSFormat.wildex)] string cn = null,
			WCFlags flags = 0, Func<wnd, bool> also = null, int skip = 0)
		{
			return default != Child(name, cn, flags, also, skip);
		}

		/// <summary>
		/// Returns true if this window contains the specified control.
		/// Calls <see cref="wndChildFinder.Find"/>.
		/// </summary>
		/// <exception cref="AuWndException"/>
		/// <example>
		/// Find window that contains certain control, and get the control too.
		/// <code><![CDATA[
		/// var cf = new wndChildFinder("Password*", "Static"); //control properties
		/// wnd w = wnd.find(cn: "#32770", also: t => t.HasChild(cf));
		/// print.it(w);
		/// print.it(f.Result);
		/// ]]></code>
		/// </example>
		public bool HasChild(wndChildFinder f)
		{
			return f.Find(this);
		}

		/// <summary>
		/// Returns true if this window contains the specified UI element.
		/// Calls <see cref="elmFinder.Find(wnd, wndChildFinder)"/>.
		/// </summary>
		/// <exception cref="AuWndException"/>
		/// <example>
		/// Find window that contains certain UI element, and get the UI element too.
		/// <code><![CDATA[
		/// var ef = new elmFinder("BUTTON", "OK"); //UI element properties
		/// wnd w = wnd.find(cn: "#32770", also: t => t.HasElm(ef));
		/// print.it(w);
		/// print.it(f.Result);
		/// ]]></code>
		/// </example>
		public bool HasElm(elmFinder f)
		{
			return f.Find(this);
		}

		/// <summary>
		/// Finds a child control by its id and returns its handle as wnd.
		/// </summary>
		/// <returns>Returns <c>default(wnd)</c> if not found. See also: <see cref="Is0"/>, <see cref="operator +(wnd)"/>.</returns>
		/// <param name="id">Control id.</param>
		/// <param name="flags">This function supports flags DirectChild and HiddenToo. If both are set, it is much faster because uses API <msdn>GetDlgItem</msdn>. Else uses API <msdn>EnumChildWindows</msdn>, like <see cref="Child"/>.</param>
		/// <remarks>
		/// To create code for this function, use dialog "Find window or control".
		/// 
		/// Not all controls have a useful id. If control id is not unique or is different in each window instance, this function is not useful.
		/// </remarks>
		/// <exception cref="AuWndException">This variable is invalid (window not found, closed, etc).</exception>
		public wnd ChildById(int id, WCFlags flags = 0)
		{
			ThrowIfInvalid();
			if(flags.Has(WCFlags.DirectChild | WCFlags.HiddenToo)) return Api.GetDlgItem(this, id); //fast

			var d = new _KidEnumData() { wThis = this, id = id }; //info: to avoid garbage delegates, we use _KidEnumData instead of captured variables
			var wParent = this;
			Api.EnumChildWindows(this, (c, p) => {
				ref var x = ref *(_KidEnumData*)p;
				if(c.ControlId == x.id) {
					if(x.flags.Has(WCFlags.DirectChild) && c.ParentGWL_ != x.wThis) return 1;
					if(c.IsVisibleIn_(wParent)) { x.cVisible = c; return 0; }
					if(x.flags.Has(WCFlags.HiddenToo) && x.cHidden.Is0) x.cHidden = c;
				}
				return 1;
			}, &d);
			return d.cVisible.Is0 ? d.cHidden : d.cVisible;
		}

		struct _KidEnumData
		{
			public wnd wThis, cVisible, cHidden;
			public int id;
			public WCFlags flags;
		}

		/// <summary>
		/// Finds all matching child controls.
		/// Returns List containing 0 or more control handles as wnd.
		/// Everything except the return type is the same as with <see cref="Child"/>.
		/// </summary>
		/// <exception cref="AuWndException"/>
		/// <exception cref="ArgumentException"/>
		/// <remarks>
		/// In the returned list, hidden controls (when using WCFlags.HiddenToo) are always after visible controls.
		/// </remarks>
		/// <seealso cref="getwnd.Children"/>
		public wnd[] ChildAll(
			[ParamString(PSFormat.wildex)] string name = null,
			[ParamString(PSFormat.wildex)] string cn = null,
			WCFlags flags = 0, Func<wnd, bool> also = null)
		{
			//ThrowIfInvalid(); //will be called later
			var f = new wndChildFinder(name, cn, flags, also);
			return f.FindAll(this);
		}

		/// <summary>
		/// Finds a direct child control and returns its handle as wnd.
		/// </summary>
		/// <returns>Returns <c>default(wnd)</c> if not found. See also: <see cref="Is0"/>, <see cref="operator +(wnd)"/>. Supports <see cref="lastError"/>.</returns>
		/// <param name="name">
		/// Name.
		/// Full, case-insensitive. Wildcard etc not supported.
		/// null means 'can be any'. "" means 'no name'.
		/// Must include the invisible '&amp;' characters that are used to underline keyboard shortcuts with the Alt key.
		/// </param>
		/// <param name="cn">
		/// Class name.
		/// Full, case-insensitive. Wildcard etc not supported.
		/// null means 'can be any'. Cannot be "".
		/// </param>
		/// <param name="wAfter">If used, starts searching from the next control in the Z order.</param>
		/// <remarks>
		/// Calls API <msdn>FindWindowEx</msdn>.
		/// Faster than <see cref="Child"/>, which uses API <msdn>EnumChildWindows</msdn>.
		/// Can be used only when you know full name and/or class name.
		/// Finds hidden controls too.
		/// </remarks>
		public wnd ChildFast(string name, string cn, wnd wAfter = default)
		{
			//ThrowIfInvalid(); //no, it can be HWND_MESSAGE
			if(Is0) {
				Api.SetLastError(Api.ERROR_INVALID_WINDOW_HANDLE);
				return default;
			}
			return Api.FindWindowEx(this, wAfter, cn, name);
		}

		public partial struct getwnd
		{
			/// <summary>
			/// Gets child controls, including all descendants.
			/// Returns array containing 0 or more control handles as wnd.
			/// </summary>
			/// <param name="onlyVisible">Need only visible controls.</param>
			/// <param name="sortFirstVisible">Place all array elements of hidden controls at the end of the array.</param>
			/// <param name="directChild">Need only direct children, not all descendants.</param>
			/// <exception cref="AuWndException">This variable is invalid (window not found, closed, etc).</exception>
			/// <remarks>
			/// Calls API <msdn>EnumChildWindows</msdn>.
			/// </remarks>
			/// <seealso cref="ChildAll"/>
			public wnd[] Children(bool onlyVisible = false, bool sortFirstVisible = false, bool directChild = false)
			{
				_w.ThrowIfInvalid();
				return Internal_.EnumWindows(Internal_.EnumAPI.EnumChildWindows, onlyVisible, sortFirstVisible, _w, directChild);
			}

			/// <summary>
			/// Gets child controls, including all descendants.
			/// </summary>
			/// <param name="a">Receives window handles as wnd. If null, this function creates new List, else clears before adding items.</param>
			/// <param name="onlyVisible">Need only visible controls.</param>
			/// <param name="sortFirstVisible">Place all array elements of hidden controls at the end of the array.</param>
			/// <param name="directChild">Need only direct children, not all descendants.</param>
			/// <exception cref="AuWndException">This variable is invalid (window not found, closed, etc).</exception>
			/// <remarks>
			/// Use this overload to avoid much garbage when calling frequently with the same List variable. Other overload always allocates new array. This overload in most cases reuses memory allocated for the list variable.
			/// </remarks>
			public void Children(ref List<wnd> a, bool onlyVisible = false, bool sortFirstVisible = false, bool directChild = false)
			{
				_w.ThrowIfInvalid();
				Internal_.EnumWindows2(Internal_.EnumAPI.EnumChildWindows, onlyVisible, sortFirstVisible, _w, directChild, list: a ??= new List<wnd>());
			}

			//rejected: unreliable.
			///// <summary>
			///// Gets list of direct child controls.
			///// Faster than API EnumChildWindows.
			///// Should be used only with windows of current thread. Else it is unreliable because, if some controls are zordered or destroyed while enumerating, some controls can be skipped or retrieved more than once.
			///// </summary>
			//public wnd[] DirectChildrenFastUnsafe(string cn = null)
			//{
			//	wildex wild = cn;
			//	var a = new List<wnd>();
			//	for(wnd c = FirstChild; !c.Is0; c = c.Next) {
			//		if(wild != null && !c._ClassNameIs(wild)) continue;
			//		a.Add(c);
			//	}
			//	return a.ToArray();
			//}
		}

		/// <summary>
		/// Casts this to <see cref="WButton"/>.
		/// </summary>
		public WButton AsButton => new(this);

		/// <summary>
		/// Finds a child button by id and sends a "click" message. Does not use the mouse.
		/// Calls <see cref="WButton.Click(bool)"/>.
		/// </summary>
		/// <param name="buttonId">Control id of the button. This function calls <see cref="ChildById"/> to find the button.</param>
		/// <param name="useElm">Use <see cref="elm.DoAction"/>. If false (default), posts <msdn>BM_CLICK</msdn> message.</param>
		/// <exception cref="NotFoundException">Button not found.</exception>
		/// <exception cref="Exception">Exceptions of <see cref="ChildById"/> and <see cref="WButton.Click(bool)"/>.</exception>
		/// <example>
		/// <code><![CDATA[
		/// wnd.find("Options").ButtonClick(2);
		/// ]]></code>
		/// </example>
		public void ButtonClick(int buttonId, bool useElm = false)
		{
			var c = ChildById(buttonId);
			if(c.Is0) throw new NotFoundException();
			c.AsButton.Click(useElm);
		}

		/// <summary>
		/// Finds a child button by name and sends a "click" message. Does not use the mouse.
		/// Calls <see cref="WButton.Click(bool)"/>.
		/// </summary>
		/// <param name="buttonName">Button name. This function calls <see cref="Child"/> to find the button.</param>
		/// <param name="cn">Button class name to pass to <see cref="Child"/>.</param>
		/// <param name="useElm">Use <see cref="elm.DoAction"/>. If false (default), posts <msdn>BM_CLICK</msdn> message.</param>
		/// <exception cref="NotFoundException">Button not found.</exception>
		/// <exception cref="Exception">Exceptions of <see cref="Child"/> and <see cref="WButton.Click(bool)"/>.</exception>
		/// <example>
		/// <code><![CDATA[
		/// wnd.find("Options").ButtonClick("Cancel");
		/// ]]></code>
		/// </example>
		public void ButtonClick(
			[ParamString(PSFormat.wildex)] string buttonName,
			[ParamString(PSFormat.wildex)] string cn = null,
			bool useElm = false)
		{
			var c = Child(buttonName, cn);
			if(c.Is0) throw new NotFoundException(); //CONSIDER: try to find UI element. Eg toolbar button.
			c.AsButton.Click(useElm);
		}

		/// <summary>
		/// Posts a "menu item clicked" notification (<msdn>WM_COMMAND</msdn>) as if that menu item was clicked. Does not use the mouse.
		/// </summary>
		/// <param name="itemId">Menu item id. Must be in range 1 to 0xffff.</param>
		/// <param name="systemMenu">The menu item is in the title bar's context menu, not in the menu bar. Posts <msdn>WM_SYSCOMMAND</msdn> instead.</param>
		/// <exception cref="AuWndException">Invalid window.</exception>
		/// <exception cref="ArgumentOutOfRangeException">Invalid itemId.</exception>
		/// <remarks>
		/// Works only with standard (classic) menus. The drop-down menu window class name must be "#32768". Works with menu items in window menu bar, system menu and some context menus.
		/// Does not use the menu itself. Just posts WM_COMMAND or WM_SYSCOMMAND message. Even if a menu item with this id does not exist.
		/// This variable is the window that contains the menu bar or system menu. Or the drop-down menu window (class "#32768") that contains the menu item.
		/// </remarks>
		public void MenuClick(int itemId, bool systemMenu = false)
		{
			if((uint)(itemId - 1) >= 0xffff) throw new ArgumentOutOfRangeException();
			ThrowIfInvalid();
			var w = this;
			if(ClassNameIs("#32768") && miscInfo.getGUIThreadInfo(out var g, ThreadId) && !g.hwndMenuOwner.Is0) w = g.hwndMenuOwner;
			w.Post(systemMenu ? Api.WM_SYSCOMMAND : Api.WM_COMMAND, itemId);
			w.MinimalSleepIfOtherThread_();
		}

		//rejected: use elm functions instead.
		///// <summary>
		///// Finds a menu item by name and posts a "menu item clicked" notification as if that menu item was clicked. Does not use the mouse.
		///// Works with all standard menus and some non-standard menus.
		///// </summary>
		///// <param name="itemName">
		///// Menu item name.
		///// String format: [](xref:wildcard_expression).
		///// </param>
		///// <param name="systemMenu">The menu item is in the title bar's context menu, not in the menu bar.</param>
		//public void Click([ParamString(PSFormat.wildex)] string itemName, bool systemMenu = false)
		//{
		//	
		//}

		//rejected: need just 1 function. To get state, use elm.
		///// <summary>
		///// Click standard (classic) menu items, get state.
		///// </summary>
		//public static class menu
		//{

		//}
	}
}

namespace Au.Types
{
	/// <summary>
	/// Flags for <see cref="wnd.Child"/>.
	/// </summary>
	[Flags]
	public enum WCFlags
	{
		/// <summary>Can find hidden controls.</summary>
		HiddenToo = 1,

		/// <summary>Skip indirect descendant controls (children of children and so on).</summary>
		DirectChild = 2,
	}

	/// <summary>
	/// Like <see cref="wnd"/>, but has only button, check box and radio button functions - <b>Click</b>, <b>Check</b> etc.
	/// See also <see cref="wnd.AsButton"/>.
	/// </summary>
	/// <example>
	/// <code><![CDATA[
	/// wnd.find("Options").Child("Cancel").AsButton.Click();
	/// ]]></code>
	/// </example>
	public struct WButton
	{
		/// <summary>
		/// Button handle as wnd.
		/// </summary>
		public wnd W { get; }

		internal WButton(wnd w) { W = w; }

		///
		public static implicit operator wnd(WButton b) => b.W;

		/////
		//public static explicit operator WButton(wnd w) => new(w);

		///
		public override string ToString() => W.ToString();

		/// <summary>
		/// Sends a "click" message to this button control. Does not use the mouse.
		/// </summary>
		/// <param name="useElm">Use <see cref="elm.DoAction"/>. If false (default), posts <msdn>BM_CLICK</msdn> message.</param>
		/// <exception cref="AuWndException">This window is invalid.</exception>
		/// <exception cref="AuException">Failed.</exception>
		/// <remarks>
		/// Works not with all button controls. Sometimes does not work if the window is inactive.
		/// Check boxes and radio buttons also are buttons. This function can click them.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// wnd.find("Options").Child("Cancel").AsButton.Click();
		/// ]]></code>
		/// </example>
		public void Click(bool useElm = false) {
			W.ThrowIfInvalid();
			if (useElm) {
				using var e = elm.fromWindow(W, EObjid.CLIENT); //throws if failed
				e.DoAction();
			} else {
				_PostBmClick(); //async if other thread, because may show a dialog.
			}
			W.MinimalSleepIfOtherThread_();
			//FUTURE: sync better
		}

		void _PostBmClick() {
			var w = W.Window;
			bool workaround = !w.IsActive;
			if (workaround) w.Post(Api.WM_ACTIVATE, 1); //workaround for the documented BM_CLICK bug
			W.Post(BM_CLICK); //it sends WM_LBUTTONDOWN/UP
			if (workaround) w.Post(Api.WM_ACTIVATE, 0);
		}

		/// <summary>
		/// Checks or unchecks this check box. Does not use the mouse.
		/// Calls <see cref="SetCheckState"/> with state 0 or 1.
		/// </summary>
		/// <param name="on">Checks if true, unchecks if false.</param>
		/// <param name="useElm"></param>
		/// <exception cref="AuWndException">This window is invalid.</exception>
		/// <exception cref="AuException">Failed.</exception>
		/// <remarks>
		/// Works not with all button controls. Sometimes does not work if the window is inactive.
		/// If this is a radio button, does not uncheck other radio buttons in its group.
		/// </remarks>
		public void Check(bool on, bool useElm = false) {
			SetCheckState(on ? 1 : 0, useElm);
		}

		/// <summary>
		/// Sets checkbox state. Does not use the mouse.
		/// </summary>
		/// <param name="state">0 unchecked, 1 checked, 2 indeterminate.</param>
		/// <param name="useElm">Use <see cref="elm.DoAction"/>. If false (default), posts <msdn>BM_SETCHECK</msdn> message and also BN_CLICKED notification to the parent window; if that is not possible, instead uses <msdn>BM_CLICK</msdn> message.</param>
		/// <exception cref="ArgumentOutOfRangeException">Invalid state.</exception>
		/// <exception cref="AuWndException">This window is invalid.</exception>
		/// <exception cref="AuException">Failed.</exception>
		/// <remarks>
		/// Does nothing if the check box already has the specified check state (if can get it).
		/// Works not with all button controls. Sometimes does not work if the window is inactive.
		/// If this is a radio button, does not uncheck other radio buttons in its group.
		/// </remarks>
		public void SetCheckState(int state, bool useElm = false) {
			if (state < 0 || state > 2) throw new ArgumentOutOfRangeException();
			W.ThrowIfInvalid();
			int id;
			if (useElm || !_IsCheckbox() || (uint)((id = W.ControlId) - 1) >= 0xffff) {
				using var e = elm.fromWindow(W, EObjid.CLIENT); //throws if failed
				int k = _GetElmCheckState(e);
				if (k == state) return;
				if (useElm) e.DoAction(); else _PostBmClick();
				bool clickAgain = false;
				switch (state) {
					case 0:
						if (k == 1) {
							W.MinimalSleepIfOtherThread_();
							if (GetCheckState(true) == 2) clickAgain = true;
							else return;
						}
						break;
					case 1:
						if (k == 2) clickAgain = true;
						break;
					case 2:
						if (k == 0) clickAgain = true;
						break;
				}
				if (clickAgain) {
					if (useElm) e.DoAction(); else _PostBmClick();
				}
			} else {
				if (state == W.Send(BM_GETCHECK)) return;
				W.Post(BM_SETCHECK, state);
				W.Get.DirectParent.Post(Api.WM_COMMAND, id, (nint)W);
			}
			W.MinimalSleepIfOtherThread_();
		}

		/// <summary>
		/// Gets check state of this check box or radio button.
		/// Calls <see cref="GetCheckState"/> and returns true if it returns 1.
		/// </summary>
		public bool IsChecked(bool useElm = false) {
			return 1 == GetCheckState(useElm);
		}

		/// <summary>
		/// Gets check state of this check box or radio button.
		/// Returns 0 if unchecked, 1 if checked, 2 if indeterminate. Also returns 0 if this is not a button or if failed to get state.
		/// </summary>
		/// <param name="useElm">Use <see cref="elm.State"/>. If false (default) and this button has a standard checkbox style, uses API <msdn>BM_GETCHECK</msdn>.</param>
		public int GetCheckState(bool useElm = false) {
			if (useElm || !_IsCheckbox()) {
				//info: Windows Forms controls are user-drawn and don't have one of the styles, therefore BM_GETCHECK does not work.
				try { //avoid exception in property-get functions
					using var e = elm.fromWindow(W, EObjid.CLIENT, flags: EWFlags.NoThrow);
					if (e == null) return 0;
					return _GetElmCheckState(e);
				}
				catch (Exception ex) { Debug_.Print(ex); } //CONSIDER: if fails, show warning. In all wnd property-get functions.
				return 0;
			} else {
				return (int)W.Send(BM_GETCHECK);
			}
		}

		int _GetElmCheckState(elm e) {
			var state = e.State;
			if (state.Has(EState.MIXED)) return 2;
			if (state.Has(EState.CHECKED)) return 1;
			return 0;
		}

		bool _IsCheckbox() {
			switch ((uint)W.Style & 15) {
				case BS_CHECKBOX:
				case BS_AUTOCHECKBOX:
				case BS_RADIOBUTTON:
				case BS_3STATE:
				case BS_AUTO3STATE:
				case BS_AUTORADIOBUTTON:
					return true;
			}
			return false;
		}

		internal const int BM_CLICK = 0xF5;
		internal const int BM_GETCHECK = 0xF0;
		internal const int BM_SETCHECK = 0xF1;

		internal const uint BS_CHECKBOX = 0x2;
		internal const uint BS_AUTOCHECKBOX = 0x3;
		internal const uint BS_RADIOBUTTON = 0x4;
		internal const uint BS_3STATE = 0x5;
		internal const uint BS_AUTO3STATE = 0x6;
		internal const uint BS_AUTORADIOBUTTON = 0x9;

	}
}
