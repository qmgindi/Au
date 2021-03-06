using Au;
using Au.Types;
using System;
using System.Collections.Generic;
using System.IO;
//using System.Linq;
using Au.More;
using System.Runtime.CompilerServices;

namespace Au
{
	/// <summary>
	/// Popup menu.
	/// </summary>
	/// <remarks>
	/// Can be used everywhere: in automation scripts, WPF apps, other apps, etc.
	/// Also can be used as a popup list and supports many items with scrollbar.
	/// 
	/// Menu item text can include hotkey after '\t' character and/or tooltip after '\0' character and optional space character. Examples: <c>"Text\t Hotkey"</c>, <c>"Text\0 Tooltip"</c>, <c>"Text\t Hotkey\0 Tooltip"</c>. Character with prefix &amp; (eg 'A' in <c>"Save &amp;As"</c>) will be underlined (depends on Windows settings and <see cref="MSFlags"/>) and can be used to select the item with keyboard.
	/// 
	/// Keyboard, mouse:
	/// - Enter, Tab, Space - close the menu and execute the focused item. Or show the submenu.
	/// - Esc - close the menu or current submenu.
	/// - Left - close current submenu.
	/// - Right - open submenu.
	/// - Down, Up, PageDown, PageUp, End, Home - focus other item.
	/// - underlined menu item character - close the menu and execute the item. Or show the submenu. See <see cref="MSFlags.Underline"/>.
	/// - Alt, Win, F10, Apps, Back - close menus.
	/// - click outside - close the menu.
	/// - middle click - close the menu.
	/// - right click - show context menu (if used constructor with parameters).
	/// 
	/// While a menu is open, it captures many keyboard keys, even when its thread isn't the foreground thread.
	/// 
	/// Not thread-safe. All functions must be called in same thread, unless documented otherwise.
	/// </remarks>
	/// <example>
	/// <code><![CDATA[
	/// var m = new popupMenu("example");
	/// m["One"] = o => print.it(o);
	/// m["Two\0Tooltip", image: icon.stock(StockIcon.DELETE)] = o => { print.it(o); dialog.show(o.ToString()); };
	/// m.Submenu("Submenu", m => {
	/// 	m["Three"] = o => print.it(o);
	/// 	m["Four"] = o => print.it(o);
	/// });
	/// m["notepad"] = o => run.itSafe(folders.System + "notepad.exe");
	/// m.Show();
	/// ]]></code>
	/// </example>
	public unsafe partial class popupMenu : MTBase
	{
		/// <summary>
		/// Represents a menu item in <see cref="popupMenu"/>.
		/// </summary>
		/// <remarks>
		/// Most properties cannot be changed while the menu is open. Can be changed <b>Tag</b>, <b>Tooltip</b>, <b>IsChecked</b> and <b>IsDisabled</b>.
		/// </remarks>
		public class MenuItem : MTItem
		{
			readonly popupMenu _m;
			internal string hotkey;
			internal byte checkType; //1 checkbox, 2 radio
			internal bool checkDontClose;
			internal bool rawText;

			internal MenuItem(popupMenu m, bool isDisabled, bool isChecked = false) {
				_m = m;
				_isDisabled = isDisabled;
				_isChecked = isChecked;
				checkDontClose = m.CheckDontClose;
				rawText = m.RawText;
			}

			/// <summary>Gets item action.</summary>
			public Action<MenuItem> Clicked => base.clicked as Action<MenuItem>;

			/// <summary>Gets menu item id.</summary>
			public int Id { get; init; }

			/// <summary>true if is a submenu-item.</summary>
			public bool IsSubmenu { get; init; }

			/// <summary>true if is a separator.</summary>
			public bool IsSeparator { get; init; }

			/// <summary>
			/// Gets or sets disabled state.
			/// </summary>
			public bool IsDisabled {
				get => _isDisabled || IsSeparator;
				set {
					if (value != _isDisabled && !IsSeparator) {
						_isDisabled = value;
						_m._Invalidate(this);
					}
				}
			}
			bool _isDisabled;

			/// <summary>
			/// Gets or sets checked state.
			/// </summary>
			/// <exception cref="InvalidOperationException">The 'set' function throws if the item isn't checkable. Use <see cref="popupMenu.AddCheck"/> or <see cref="popupMenu.AddRadio"/>.</exception>
			public bool IsChecked {
				get => _isChecked;
				set {
					if (checkType == 0) throw new InvalidOperationException();
					if (value != _isChecked) {
						_isChecked = value;
						_m._Invalidate(this);
					}
				}
			}
			bool _isChecked;

			/// <summary>Gets or sets whether to use bold font.</summary>
			public bool FontBold { get; set; }

			/// <summary>Gets or sets background color.</summary>
			public ColorInt BackgroundColor { get; set; }

			//public string Hotkey => hotkey;
		}

		readonly List<MenuItem> _a = new();
		int _lastId; //to auto-generate item ids
		bool _addedNewItems;
		(SIZE window, SIZE client, int border) _size;
		NativeScrollbar_ _scroll;
		(popupMenu child, popupMenu parent, MenuItem item, timerm timer) _sub;
		(POINT p, bool track, bool left, bool right, bool middle) _mouse;
		int _iHot = -1;
		MSFlags _flags;
		MenuItem _result;

		static popupMenu() {
			WndUtil.RegisterWindowClass("Au.popupMenu", etc: new() { style = Api.CS_HREDRAW | Api.CS_VREDRAW | Api.CS_DROPSHADOW, mCursor = MCursor.Arrow });
		}

		/// <summary>
		/// Use this constructor for various context menus of your app.
		/// </summary>
		/// <remarks>
		/// Users cannot right-click a menu item and open/select it in editor.
		/// </remarks>
		public popupMenu() { }

		/// <summary>
		/// Use this constructor in scripts.
		/// </summary>
		/// <param name="name">Menu name. Must be a unique valid filename. Currently not used. Can be null.</param>
		/// <param name="f_">[](xref:caller_info)</param>
		/// <param name="l_">[](xref:caller_info)</param>
		/// <remarks>
		/// This overload sets <see cref="MTBase.ExtractIconPathFromCode"/> = true.
		/// 
		/// Users can right-click an item to open/select it in editor, unless <i>f</i> = null.
		/// </remarks>
		public popupMenu(string name, [CallerFilePath] string f_ = null, [CallerLineNumber] int l_ = 0) : base(name, f_, l_) {
			ExtractIconPathFromCode = true;
		}

		#region add

		MenuItem _Add(MenuItem mi, string text, MTImage image, int l_, Delegate click = null) {
			_ThreadTrap();
			_OpenTrap("cannot add items while the menu is open. To add to submenu, use the submenu variable.");
			if (!mi.IsSeparator) {
				if (!mi.rawText && text.Lenn() >= 2) {
					int i = text.IndexOf('\t', 1);
					if (i > 0) {
						var s = text;
						text = s[..i];
						if (s.Eq(++i, ' ')) i++;
						mi.hotkey = s[i..];
					}
				}
				mi.Set_(this, text, click, image, l_);
			}
			_a.Add(mi);
			_addedNewItems = true;
			Last = mi;
			return mi;
		}

		/// <summary>
		/// Adds menu item with explicitly specified id.
		/// </summary>
		/// <param name="id">Item id that <see cref="Show"/> will return if clicked this item.</param>
		/// <param name="text">Item text. Can include hotkey, tooltip and underlined character, like <c>"Te&amp;xt\t Hotkey\0 Tooltip"</c>; more info: <see cref="popupMenu"/>.</param>
		/// <param name="image">Item image. Read here: <see cref="MTBase"/>.</param>
		/// <param name="disable">Disabled state.</param>
		/// <param name="l_">[](xref:caller_info)</param>
		public MenuItem Add(int id, string text, MTImage image = default, bool disable = false, [CallerLineNumber] int l_ = 0)
			=> _Add(new(this, disable) { Id = _lastId = id }, text, image, l_);

		/// <summary>
		/// Adds menu item with auto-generated id.
		/// </summary>
		/// <param name="text">Item text. Can include hotkey, tooltip and underlined character, like <c>"Te&amp;xt\t Hotkey\0 Tooltip"</c>; more info: <see cref="popupMenu"/>.</param>
		/// <param name="image">Item image. Read here: <see cref="MTBase"/>.</param>
		/// <param name="disable">Disabled state.</param>
		/// <param name="l_">[](xref:caller_info)</param>
		/// <remarks>
		/// Assigns id = the last specified or auto-generated id + 1. If not using explicitly specified ids, auto-generated ids are 1, 2, 3... Submenu-items, separators and items with action don't auto-generate ids.
		/// </remarks>
		public MenuItem Add(string text, MTImage image = default, bool disable = false, [CallerLineNumber] int l_ = 0)
			=> _Add(new(this, disable) { Id = ++_lastId }, text, image, l_);

		/// <summary>
		/// Adds menu item with action (callback function) that is executed on click.
		/// </summary>
		/// <param name="text">Item text. Can include hotkey, tooltip and underlined character, like <c>"Te&amp;xt\t Hotkey\0 Tooltip"</c>; more info: <see cref="popupMenu"/>.</param>
		/// <param name="click">Action executed on click.</param>
		/// <param name="image">Item image. Read here: <see cref="MTBase"/>.</param>
		/// <param name="disable">Disabled state.</param>
		/// <param name="l_">[](xref:caller_info)</param>
		/// <remarks>
		/// This function is the same as the indexer. The difference is, <b>Add</b> returns <b>MenuItem</b> object of the added item. When using the indexer, to access the item use <see cref="Last"/>. These codes are the same: <c>var v=m.Add("text", o=>{});"</c> and <c>m["text"]=o=>{}; var v=m.Last;</c>.
		/// </remarks>
		public MenuItem Add(string text, Action<MenuItem> click, MTImage image = default, bool disable = false, [CallerLineNumber] int l_ = 0)
			=> _Add(new(this, disable), text, image, l_, click);

		/// <summary>
		/// Adds menu item with action (callback function) that is executed on click.
		/// </summary>
		/// <param name="text">Item text. Can include hotkey, tooltip and underlined character, like <c>"Te&amp;xt\t Hotkey\0 Tooltip"</c>; more info: <see cref="popupMenu"/>.</param>
		/// <param name="image">Item image. Read here: <see cref="MTBase"/>.</param>
		/// <param name="disable">Disabled state.</param>
		/// <param name="l_">[](xref:caller_info)</param>
		/// <value>Action executed on click. Can be null.</value>
		/// <remarks>
		/// This function is the same as <see cref="Add(string, Action{MenuItem}, MTImage, bool, int)"/>. The difference is, <b>Add</b> returns <b>MenuItem</b> object of the added item. When using the indexer, to access the item use <see cref="Last"/>. These codes are the same: <c>var v=m.Add("text", o=>{});"</c> and <c>m["text"]=o=>{}; var v=m.Last;</c>.
		/// </remarks>
		public Action<MenuItem> this[string text, MTImage image = default, bool disable = false, [CallerLineNumber] int l_ = 0] {
			set { Add(text, value, image, disable, l_); }
		}

		/// <summary>
		/// Adds menu item to be used as a checkbox.
		/// </summary>
		/// <param name="text">Item text. Can include hotkey, tooltip and underlined character, like <c>"Te&amp;xt\t Hotkey\0 Tooltip"</c>; more info: <see cref="popupMenu"/>.</param>
		/// <param name="check">Checked state.</param>
		/// <param name="click">Action executed on click.</param>
		/// <param name="disable">Disabled state.</param>
		/// <param name="image">Item image. Read here: <see cref="MTBase"/>.</param>
		/// <param name="l_">[](xref:caller_info)</param>
		/// <remarks>
		/// When clicked, <see cref="MenuItem.IsChecked"/> state is changed.
		/// </remarks>
		public MenuItem AddCheck(string text, bool check = false, Action<MenuItem> click = null, bool disable = false, MTImage image = default, [CallerLineNumber] int l_ = 0)
			=> _Add(new(this, disable, check) { checkType = 1 }, text, image, l_, click);

		/// <summary>
		/// Adds menu item to be used as a radio button in a group of such items.
		/// </summary>
		/// <param name="text">Item text. Can include hotkey, tooltip and underlined character, like <c>"Te&amp;xt\t Hotkey\0 Tooltip"</c>; more info: <see cref="popupMenu"/>.</param>
		/// <param name="check">Checked state.</param>
		/// <param name="click">Action executed on click.</param>
		/// <param name="disable">Disabled state.</param>
		/// <param name="image">Item image. Read here: <see cref="MTBase"/>.</param>
		/// <param name="l_">[](xref:caller_info)</param>
		/// <remarks>
		/// When clicked an unchecked radio item, its <see cref="MenuItem.IsChecked"/> state becomes true; <b>IsChecked</b> of other group items become false.
		/// </remarks>
		public MenuItem AddRadio(string text, bool check = false, Action<MenuItem> click = null, bool disable = false, MTImage image = default, [CallerLineNumber] int l_ = 0)
			=> _Add(new(this, disable, check) { checkType = 2 }, text, image, l_, click);

		/// <summary>
		/// Adds menu item that opens a submenu.
		/// Used like <c>m.Submenu("Example", m => { /* add submenu items */ });</c>.
		/// </summary>
		/// <param name="text">Item text. Can include hotkey, tooltip and underlined character, like <c>"Te&amp;xt\t Hotkey\0 Tooltip"</c>; more info: <see cref="popupMenu"/>.</param>
		/// <param name="opening">Action called whenever opening the submenu and should add items to it.</param>
		/// <param name="image">Item image. Read here: <see cref="MTBase"/>.</param>
		/// <param name="disable">Disabled state.</param>
		/// <param name="l_">[](xref:caller_info)</param>
		/// <remarks>
		/// The submenu is other <b>popupMenu</b> object. It inherits many properties of this menu; see property documentation.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// m.Submenu("Example", m => {
		/// 	m["A"] = o => { print.it(o); };
		/// 	m["B"] = o => { print.it(o); };
		/// });
		/// ]]></code>
		/// This code shows dynamically created menu of files in a folder and subfolders. Subfolder files are retrieved when opening the submenu.
		/// <code><![CDATA[
		/// var m=new popupMenu();
		/// _Dir(m, new DirectoryInfo(@"C:\"));
		/// m.Show();
		/// 
		/// static void _Dir(popupMenu m, DirectoryInfo dir) {
		/// 	foreach (var v in dir.EnumerateFileSystemInfos()) {
		/// 		if(v.Attributes.Has(FileAttributes.System|FileAttributes.Hidden)) continue;
		/// 		if(v.Attributes.Has(FileAttributes.Directory)) {
		/// 			m.Submenu(v.Name, m=> _Dir(m, v as DirectoryInfo));
		/// 		} else {
		/// 			m[v.Name]=o=>print.it(v.FullName);
		/// 		}
		/// 		m.Last.File = v.FullName;
		/// 	}
		/// }
		/// ]]></code>
		/// </example>
		public MenuItem Submenu(string text, Action<popupMenu> opening, MTImage image = default, bool disable = false, [CallerLineNumber] int l_ = 0)
			=> _Add(new(this, disable) { IsSubmenu = true }, text, image, l_, opening);

		/// <summary>
		/// Adds menu item that opens a reusable submenu.
		/// </summary>
		/// <param name="text">Item text. Can include hotkey, tooltip and underlined character, like <c>"Te&amp;xt\t Hotkey\0 Tooltip"</c>; more info: <see cref="popupMenu"/>.</param>
		/// <param name="opening">Func called whenever opening the submenu and should return the submenu object. Can return null.</param>
		/// <param name="image">Item image. Read here: <see cref="MTBase"/>.</param>
		/// <param name="disable">Disabled state.</param>
		/// <param name="l_">[](xref:caller_info)</param>
		/// <remarks>
		/// The caller creates the submenu (creates the <see cref="popupMenu"/> object and adds items) and can reuse it many times. Other overload does not allow to create <b>popupMenu</b> and reuse same object.
		/// The submenu does not inherit properties of this menu.
		/// </remarks>
		/// <example>
		/// <code><![CDATA[
		/// var m2 = new popupMenu(); m2.AddCheck("C1"); m2.AddCheck("C2");
		/// m.Submenu("Submenu", () => m2);
		/// ]]></code>
		/// </example>
		public MenuItem Submenu(string text, Func<popupMenu> opening, MTImage image = default, bool disable = false, [CallerLineNumber] int l_ = 0)
			=> _Add(new(this, disable) { IsSubmenu = true }, text, image, l_, opening);

		/// <summary>
		/// Adds separator.
		/// </summary>
		public void Separator()
			=> _Add(new(this, isDisabled: true) { IsSeparator = true }, null, default, 0);

		/// <summary>
		/// Gets the last added menu item.
		/// </summary>
		public MenuItem Last { get; private set; }

		/// <summary>
		/// Gets added items, except separators.
		/// </summary>
		/// <remarks>
		/// Allows to set properties of multiple items in single place instead of after each 'add item' code line.
		/// 
		/// Does not get items in submenus. Submenus are separate <b>popupMenu</b> objects and you can use their <b>Items</b> property.
		/// </remarks>
		public IEnumerable<MenuItem> Items {
			get {
				_ThreadTrap();
				foreach (var v in _a) {
					if (!v.IsSeparator) yield return v;
				}
			}
		}

		/// <summary>
		/// Don't use &amp; character for keyboard shortcut. Also don't use tab character for hotkey.
		/// This property is applied to items added afterwards; submenus inherit it.
		/// </summary>
		public bool RawText { get; set; }

		#endregion

		#region show, close

		/// <summary>
		/// Shows the menu and waits until closed.
		/// Returns id of the selected item when closed, or 0 if cancelled.
		/// </summary>
		/// <param name="flags"></param>
		/// <param name="xy">Menu position in screen. If null (default), uses mouse position by default. It depends on flags.</param>
		/// <param name="excludeRect">The menu should not overlap this rectangle in screen.</param>
		/// <param name="owner">Owner window. The menu will be automatically closed when destroying its owner window.</param>
		/// <exception cref="InvalidOperationException">The menu is open or is submenu.</exception>
		public int Show(MSFlags flags = 0, POINT? xy = null, RECT? excludeRect = null, AnyWnd owner = default) {
			_ThreadTrap();
			_OpenTrap("this menu is already open");
			if (_sub.parent != null) throw new InvalidOperationException("this is a submenu");
			if (_a.Count == 0) return 0;

			Api.ReleaseCapture(); //winforms still capturing on MouseClick etc, and menu would be like disabled

			if (!flags.Has(MSFlags.Underline)) if (0 != Api.SystemParametersInfo(Api.SPI_GETKEYBOARDCUES, 0)) flags |= MSFlags.Underline;

			_Show(flags, xy, excludeRect, owner.Hwnd);

			int R = 0;

			WindowsHook hKey = null;
			timerm timer = null;
			try {
				var wFore = wnd.active;
				bool foreground = wFore.IsOfThisThread;

				//to close with mouse use timer. Mouse hook may not work because of UAC.
				int mouseState = _GetMouseState();
				timer = new(t => { //close if mouse clicked a non-menu window or if activated another window
					int ms = _GetMouseState();
					bool clicked = ms != mouseState;
					mouseState = ms;
					if (clicked) _CloseIfClickedNotMenu(wnd.fromMouse());
					else if (wnd.active != wFore) Close();
				});
				timer.Every(30);

				static int _GetMouseState() =>
					(keys.gui.getKeyState(KKey.MouseLeft) & 0x1)
					| ((keys.gui.getKeyState(KKey.MouseRight) & 0x1) << 1)
					| ((keys.gui.getKeyState(KKey.MouseMiddle) & 0x1) << 2)
					;
				//note: use only toggled state. Pressed state may change to "no" when mouse is already in a non-menu window although was in a menu window at the time of the mouse event.
				//note: in some cases toggled state may not change when clicked. Eg when clicked a taskbar button that activates another window. Then helps if (wnd.active!=wFore) Close();.

				void _CloseIfClickedNotMenu(wnd w) {
					//if(!w.Get.Owners(andThisWindow: true).Contains(_w)) Close(); //no, user may want nested root menus, although it is rare
					if (w != _w && !w.ClassNameIs("Au.popupMenu")) Close();
				}

				if (!foreground) {
					//never mind: hooks don't work if active window has higher UAC IL. Then use timer and mouse/Esc toggle state.
					hKey = WindowsHook.Keyboard(g => {
						if (KeyboardHook != null && !g.IsUp) {
							switch (KeyboardHook(this, g)) {
							case MKHook.None: return;
							case MKHook.Close: _w.Post(Api.WM_CLOSE); return;
							}
						}
#if true
						var k = g.Key;
						if (!_IsCancelKey(k)) {
							if (_IsPassKey(k)) return;
							g.BlockEvent();
						}
						if (!g.IsUp) _w.Post(Api.WM_KEYDOWN, (int)k, 0); //else _w.Post(Api.WM_KEYUP, (int)k, 0xC0000001);
#else //unfinished. The idea was to call TranslateMessage, and if then PeekMessage gets wm_char...
					var k = g.Key;
					if(_IsCancelKey(k)) {
						_w.Post(Api.WM_CLOSE);
						return;
					}
					if (!g.IsUp) {
						var ok=Api.TranslateMessage(new() { hwnd = _w, message = Api.WM_KEYDOWN, wParam = (int)k });
						//print.it(ok);
						if(Api.PeekMessage(out var v, _w, Api.WM_CHAR, Api.WM_CHAR, Api.PM_NOREMOVE)) print.it("peek", v);
					}
					
#endif
					});
				}

				//var pmo = new PrintMsgOptions(Api.WM_TIMER, Api.WM_MOUSEMOVE, Api.WM_NCMOUSEMOVE, Api.WM_PAINT, 0x138a /*SC_WORK_IDLE*/, Api.WM_USER, int.MinValue) { WindowProperties = true };
				//print.it("in");
				_MessageLoop();
				//print.it("out");
				void _MessageLoop() {
					do {
						for (; ; ) {
							if (_w.Is0) return;
							if (!Api.PeekMessage(out var m, default, 0, 0, Api.PM_NOREMOVE)) break;
							if (m.message == Api.WM_QUIT) return; //let outer loop get the message (tested)
							bool handled = false;
							if (m.message is Api.WM_LBUTTONDOWN or Api.WM_RBUTTONDOWN or Api.WM_MBUTTONDOWN or Api.WM_NCLBUTTONDOWN or Api.WM_NCRBUTTONDOWN or Api.WM_NCMBUTTONDOWN) {
								_CloseIfClickedNotMenu(m.hwnd);
								if (_w.Is0) return; //let outer loop get the message
							} else if (m.message is Api.WM_KEYDOWN or Api.WM_SYSKEYDOWN) {
								handled = _WmKeydown(m);
								if (!handled && _w.Is0) return; //let outer loop get the message. Used for keys that close the menu but must be passed to the app, eg Alt. If Esc, handled is true.
							}
							if (!Api.PeekMessage(out m, default, 0, 0, Api.PM_REMOVE)) break;
							if (handled) continue;
							//WndUtil.PrintMsg(m, pmo);
							if (m.message == Api.WM_CHAR) {
								_TopMenu()._WmChar((char)m.wParam);
								continue;
							}
							Api.TranslateMessage(m);
							Api.DispatchMessage(m);
						}
					}
					while (Api.WaitMessage());
					//why this strange loop?
					//	If the click or keydown closed the menu and wants to pass the message to the app, before passing it need to exit the loop, else bad things may happen.
					//	Or could use GetMessage + PostMessage, but it is probably more dirty; need to repost all queued messages, to avoid eg lbuttondown after lbuttonup.
				}
			}
			finally {
				hKey?.Dispose();
				timer?.Stop();
				if (!_w.Is0) Api.DestroyWindow(_w);
			}

			var b = _result;
			if (b != null) {
				if (b.clicked != null) {
					if (b.actionThread) run.thread(() => _ExecItem(), background: false); else _ExecItem();
					void _ExecItem() {
						var action = b.clicked as Action<MenuItem>;
						try { action(b); }
						catch (Exception ex) when (!b.actionException) { print.warning(ex.ToString(), -1); }
					}
				}
				R = b.Id;
			}

			return R;
		}

		/// <summary>
		/// Returns true if the menu window is open.
		/// </summary>
		public bool IsOpen => !_w.Is0;

		/// <summary>
		/// After closing the menu gets the selected item, or null if cancelled.
		/// </summary>
		public MenuItem Result => _result;

		void _OpenTrap(string error = null) {
			if (IsOpen) throw new InvalidOperationException(error);
		}

		void _ShowSubmenu(MenuItem b, bool focusFirst = false, popupMenu m = null) {
			if (b == _sub.item) return;
			_sub.child?.Close();

			bool contextMenu = m != null;
			if (!contextMenu) {
				if (b.clicked == null) return;
				if (b.clicked is Action<popupMenu> menu) {
					m = _sourceFile == null ? new() : new popupMenu(null, _sourceFile, b.sourceLine);
					base._CopyProps(m);
					m.CheckDontClose = CheckDontClose;
					m.RawText = RawText;
					menu(m);
				} else if (b.clicked is Func<popupMenu> func) {
					m = func();
				}
				if (m == null || m._a.Count == 0) return;
			}

			_sub.child = m;
			_sub.item = b;
			m._sub.parent = this;
			if (focusFirst && m._iHot < 0) m._iHot = 0;

			if (contextMenu) {
				m._Show(0, null, null, _w);
			} else {
				var r = _ItemRect(b, inScreen: true);
				r.Inflate(-_size.border, _size.border);

				m._Show(_flags & ~(MSFlags)0xffffff, new(r.right, r.top), r, _w);
			}
		}

		void _Show(MSFlags flags, POINT? xy, RECT? excludeRect, wnd owner) { //CONSIDER: _Show(MSPosition pos = null) //then could precisely specify offsets etc
			if (_a.Count == 0) return;

			_result = null;
			_flags = flags;

			RECT cr = default;
			bool byCaret = flags.Has(MSFlags.ByCaret) && miscInfo.getTextCursorRect(out cr, out _);
			POINT p = byCaret ? new(cr.left, cr.bottom) : xy ?? mouse.xy;

			var scrn = screen.of(p);
			_dpi = scrn.Dpi;
			var rs = scrn.WorkArea;
			rs.Inflate(-4, -4);

			if (byCaret) {
				if (excludeRect == null) {
					cr.Inflate(50, 1);
					excludeRect = cr;
					flags |= MSFlags.AlignRectBottomTop;
				}
			} else {
				if (flags.Has(MSFlags.ScreenCenter)) {
					p = new(rs.CenterX, rs.CenterY);
					flags |= MSFlags.AlignCenterH | MSFlags.AlignCenterV;
				} else if (excludeRect == null && !flags.Has(MSFlags.AlignCenterH | MSFlags.AlignCenterV)) excludeRect = new(p.x, p.y, 1, 1);
			}

			if (_addedNewItems) {
				_addedNewItems = false;
				_Images();
			}

			_scroll = new(true, i => _a[i].rect.top, i => _a[i].rect.bottom);

			WS style = WS.POPUP | WS.DLGFRAME; //3-pixel frame
			WSE estyle = WSE.TOOLWINDOW | WSE.NOACTIVATE | WSE.TOPMOST;
			SIZE z = _Measure(rs.Width * 19 / 20);

			bool needScroll = z.height > rs.Height;
			if (needScroll) {
				z.height = rs.Height;
				style |= WS.VSCROLL;
			}

			if (byCaret && !flags.HasAny(MSFlags.AlignRight | MSFlags.AlignCenterH)) p.x = Math.Max(p.x - _z.image - _z.check - _z.textPaddingX - 3, rs.left);

			RECT r = new(0, 0, z.width, z.height);
			Dpi.AdjustWindowRectEx(_dpi, ref r, style, estyle);
			_size.window = r.Size; _size.client = z; _size.border = -r.top;
			Api.CalculatePopupWindowPosition(p, r.Size, (uint)flags & 0xffffff, excludeRect.GetValueOrDefault(), out r);

			_w = WndUtil.CreateWindow(_WndProc, true, "Au.popupMenu", null, style, estyle, r.left, r.top, r.Width, r.Height, owner);
			_SetScrollbar(needScroll);

			_w.ShowL(true);

			_mouse.p = _w.MouseClientXY;
		}

		/// <summary>
		/// Creates and shows simple popup menu. Without images, actions, submenus.
		/// Returns selected item id, or 0 if cancelled.
		/// </summary>
		/// <param name="items">
		/// Menu items. Can be string[], List&lt;string&gt; or string like "One|Two|Three".
		/// Item id can be optionally specified like "1 One|2 Two|3 Three". If missing, uses id of previous non-separator item + 1. Example: "One|Two|100 Three Four" //1|2|100|101.
		/// For separators use null or empty strings: "One|Two||Three|Four".
		/// </param>
		/// <param name="flags"></param>
		/// <param name="xy">Menu position in screen. If null (default), uses mouse position by default. It depends on flags.</param>
		/// <param name="excludeRect">The menu should not overlap this rectangle in screen.</param>
		/// <param name="owner">Owner window. The menu will be automatically closed when destroying its owner window.</param>
		/// <remarks>
		/// The function adds menu items and calls <see cref="Show"/>. Returns when menu closed. All parameters except <i>items</i> are same as of <b>Show</b>.
		/// </remarks>
		/// <seealso cref="dialog.showList"/>
		public static int showSimple(DStringList items, MSFlags flags = 0, POINT? xy = null, RECT? excludeRect = null, AnyWnd owner = default) {
			var a = items.ToArray();
			var m = new popupMenu();
			foreach (var v in a) {
				var s = v;
				if (s.NE()) {
					m.Separator();
				} else {
					if (s.ToInt(out int id, 0, out int end)) {
						if (s.Eq(end, ' ')) end++;
						s = s[end..];
						m.Add(id, s);
					} else {
						m.Add(s);
					}
				}
			}
			return m.Show(flags, xy, excludeRect, owner);
		}

		/// <summary>
		/// Closes the menu and its submenus.
		/// </summary>
		/// <param name="ancestorsToo">If this is a submenu, close the root menu with all submenus.</param>
		/// <remarks>
		/// Can be called from any thread.
		/// Does nothing if not open.
		/// </remarks>
		public void Close(bool ancestorsToo = false) {
			if (!IsOpen) return;
			if (_IsOtherThread) {
				_w.Post(Api.WM_CLOSE, ancestorsToo ? 1 : 0);
			} else {
				var w = _w;
				if (ancestorsToo) {
					for (var pm = _sub.parent; pm != null; pm = pm._sub.parent) {
						w = pm._w;
						pm._result = _result;
					}
				}
				Api.DestroyWindow(w);
			}
		}

		private protected override void _WmNcdestroy() {
			//print.it("destroy", _name);
			_sub.timer?.Stop();
			var pa = _sub.parent;
			if (pa != null) {
				pa._sub.child = null;
				pa._sub.item = null;
			} else {
				_w.Post(0);
			}
			_z?.Dispose(); _z = null;
			_scroll = null;
			_sub = default;
			_size = default;
			_mouse = default;
			_iHot = -1;
			base._WmNcdestroy();
		}

		#endregion

		nint _WndProc(wnd w, int msg, nint wParam, nint lParam) {
			//var pmo = new PrintMsgOptions(Api.WM_NCHITTEST, Api.WM_SETCURSOR, Api.WM_MOUSEMOVE, Api.WM_NCMOUSEMOVE, 0x10c1);
			//if (WndUtil.PrintMsg(out string s, w, msg, wParam, lParam, pmo)) print.it("<><c green>" + s + "<>");
			//WndUtil.PrintMsg(w, msg, wParam, lParam);

			if (_scroll.WndProc(w, msg, wParam, lParam)) return default;

			switch (msg) {
			case Api.WM_NCCREATE:
				_WmNccreate(w);
				break;
			case Api.WM_NCDESTROY:
				_WmNcdestroy();
				break;
			case Api.WM_CLOSE:
				Close(ancestorsToo: 0 != (wParam & 1));
				return default;
			//case Api.WM_THEMECHANGED: //don't need for a menu window
			//	_z?.Dispose();
			//	_z = new _Metrics(this);
			//	Api.InvalidateRect(w);
			//	break;
			case Api.WM_ERASEBKGND:
				return default;
			case Api.WM_PAINT:
				using (var bp = new BufferedPaint(w, true)) _Render(bp.DC, bp.UpdateRect);
				return default;
			case Api.WM_MOUSEACTIVATE:
				return Api.MA_NOACTIVATE;
			case Api.WM_MOUSEMOVE:
				_WmMousemove(lParam, fake: false);
				return default;
			case Api.WM_MOUSELEAVE:
				_WmMouseleave();
				return default;
			case >= Api.WM_LBUTTONDOWN and <= Api.WM_MBUTTONUP:
				_WmMousebutton(msg, lParam);
				return default;
			case Api.WM_GETOBJECT:
				if (_WmGetobject(wParam, lParam, out var r1)) return r1;
				break;
			case Api.WM_USER + 50: //posted by acc dodefaultaction
				if (IsOpen) {
					int i = (int)wParam;
					if ((uint)i < _a.Count) _Click(i);
				}
				return default;
			}

			var R = Api.DefWindowProc(w, msg, wParam, lParam);

			switch (msg) {
			case Api.WM_NCPAINT:
				_WmNcpaint();
				break;
			}

			return R;
		}

		void _SetScrollbar(bool needScroll) {
			if (needScroll) {
				_scroll.SetRange(_a.Count);

				_scroll.PosChanged += (sb, part) => {
					_sub.child?.Close();

					int pos = _scroll.Pos;
					Api.InvalidateRect(_w);

					if (part <= -2) { //if mouse wheel, update hot item, submenu, tooltip
						var p = _w.MouseClientXY;
						if (_w.ClientRect.Contains(p)) _WmMousemove(Math2.MakeLparam(p), fake: true);
					}
				};
				_scroll.Visible = true;
			} else {
				_scroll.NItems = _a.Count;
			}
		}

		int _HitTest(POINT p, bool failIfDisabled = false) {
			p.y += _scroll.Offset;
			for (int i = 0; i < _a.Count; i++) {
				if (_a[i].rect.Contains(p)) return (failIfDisabled && _a[i].IsDisabled) ? -1 : i;
			}
			return -1;
		}

		RECT _ItemRect(MenuItem k, bool inScreen = false) {
			var r = k.rect;
			r.Offset(0, -_scroll.Offset);
			if (inScreen) _w.MapClientToScreen(ref r);
			return r;
		}

		void _WmMousemove(nint lParam, bool fake) {
			var p = Math2.NintToPOINT(lParam);

			//prevent selecting item when mouse position does not change. It would interfere with keyboard navigation.
			if (!fake && p == _mouse.p) return; _mouse.p = p;

			int i = _HitTest(p, failIfDisabled: true);
			if (i != _iHot) {
				_SetHotItem(i);
				if (i >= 0) {
					var b = _a[i];
					int submenuDelay = _SubmenuTimer(b.IsSubmenu ? b : null);
					_SetTooltip(b, _ItemRect(b), lParam, submenuDelay);
				} else {
					_HideTooltip();
					_SubmenuTimer();
				}
			}

			if (_iHot >= 0 != _mouse.track) {
				var t = new Api.TRACKMOUSEEVENT(_w, _iHot >= 0 ? Api.TME_LEAVE : Api.TME_LEAVE | Api.TME_CANCEL);
				_mouse.track = Api.TrackMouseEvent(ref t) && _iHot >= 0;
			}

			_sub.parent?._SubmenuMouseMove();
		}

		int _SubmenuTimer(MenuItem item = null) {
			if (item == null && _sub.child == null) return 0;

			_sub.timer ??= new(t => {
				if (t.Tag is MenuItem mi) {
					if (FocusedItem == mi) _ShowSubmenu(mi);
				} else if (_sub.child != null && !_sub.child._w.Rect.Contains(mouse.xy)) {
					_sub.child.Close();
				}
			});
			int R = Api.SystemParametersInfo(Api.SPI_GETMENUSHOWDELAY, 400);
			_sub.timer.Tag = item;
			_sub.timer.After(R);
			return R;
		}

		void _SubmenuMouseMove() {
			if (FocusedItem != _sub.item) {
				_SetHotItem(_a.IndexOf(_sub.item));
				_sub.timer.Stop();
			}
		}

		void _WmMouseleave() {
			_mouse.track = false;
			if (_iHot < 0) return;
			if (_sub.child?._w.Rect.Contains(mouse.xy) ?? false) return;
			_SetHotItem(-1);
			_SubmenuTimer();
		}

		void _WmMousebutton(int msg, nint lParam) {
			switch (msg) {
			case Api.WM_LBUTTONDOWN: _mouse.left = true; return;
			case Api.WM_LBUTTONUP: if (!_mouse.left) return; _mouse.left = false; break;
			case Api.WM_RBUTTONDOWN: _mouse.right = true; return;
			case Api.WM_RBUTTONUP: if (!_mouse.right) return; _mouse.right = false; break;
			case Api.WM_MBUTTONDOWN: _mouse.middle = true; return;
			case Api.WM_MBUTTONUP: if (_mouse.middle) Close(ancestorsToo: true); return;
			default: return;
			}
			var p = Math2.NintToPOINT(lParam);
			int i = _HitTest(p);
			if (i < 0) return;
			if (msg == Api.WM_LBUTTONUP) _Click(i);
			else _ContextMenu(_a[i]);
		}

		void _Click(int i, bool keyboard = false) {
			var b = _a[i];
			if (b.IsDisabled) return;

			if (b.checkType > 0) {
				if (b.checkType == 1) {
					b.IsChecked ^= true;
				} else if (!b.IsChecked) {
					for (int j = i; --j >= 0 && _Uncheck(j);) { }
					for (int j = i; ++j < _a.Count && _Uncheck(j);) { }
					b.IsChecked = true;

					bool _Uncheck(int j) {
						var v = _a[j];
						if (v.checkType != 2) return false;
						v.IsChecked = false;
						return true;
					}
				}
				if (b.checkDontClose) return;
			}

			if (b.IsSubmenu) {
				if (keyboard) _SetHotItem(i, ensureVisible: true);
				_ShowSubmenu(b, focusFirst: keyboard);
			} else {
				_result = b;
				Close(ancestorsToo: true);
			}
		}

		/// <summary>
		/// Don't close menu when clicked a checkbox or radio item.
		/// This property is applied to items added afterwards; submenus inherit it.
		/// </summary>
		public bool CheckDontClose { get; set; }

		void _ContextMenu(MenuItem b) {
			if (b.IsSeparator) return;
			var (canEdit, canGo, goText) = MTItem.CanEditOrGoToFile_(_sourceFile, b);
			if (canEdit || canGo) {
				var m = new popupMenu();
				if (canEdit) m["Edit menu item"] = _ => script.editor.OpenAndGoToLine(_sourceFile, b.sourceLine);
				if (canGo) m[goText] = _ => b.GoToFile_();
				_ShowSubmenu(b, m: m);
			}
		}

		popupMenu _TopMenu() {
			var m = this; while (m._sub.child != null) m = m._sub.child;
			return m;
		}

		bool _TopHot(out (popupMenu m, int i, MenuItem b) r) {
			var m = this; while (m._sub.child != null) m = m._sub.child;
			int i = m._iHot;
			r = (m, i, i < 0 ? null : m._a[i]);
			return i >= 0;
		}

		/// <summary>
		/// Gets or sets the focused menu item.
		/// </summary>
		/// <remarks>
		/// The focused item visually shows the menu item that would be executed if clicked or pressed Enter, Tab or Space key. It changes when the user moves the mouse or presses navigation keys (arrows, End, Home, PageDown, PageUp).
		/// This property can be set before showing the menu or when it is open.
		/// </remarks>
		public MenuItem FocusedItem {
			get => _iHot >= 0 ? _a[_iHot] : null;
			set {
				_ThreadTrap();
				int i = -1;
				if (value != null) {
					i = _a.IndexOf(value);
					if (i < 0) throw new ArgumentException();
				}
				if (_w.Is0) {
					_iHot = i;
				} else {
					_SetHotItem(i, ensureVisible: true);
				}
			}
		}

		void _SetHotItem(int i, bool ensureVisible = false) {
			if (i != _iHot) {
				if (_iHot >= 0) _Invalidate(_iHot);
				if ((_iHot = i) >= 0) _Invalidate(_iHot);
			}
			if (ensureVisible && i >= 0) {
				int pos = _scroll.Pos, y = _scroll.Offset, hei = _size.client.height;
				var r = _a[i].rect;
				if (r.top < y) {
					while (pos > 0 && r.top < y) y -= _a[--pos].rect.Height;
				} else if (r.bottom > y + hei) {
					while (pos < _scroll.Max && r.bottom > y + hei) y += _a[++pos].rect.Height;
				} else return;
				_scroll.Pos = pos;
			}
		}

		bool _WmKeydown(in MSG msg) { //called for root menu
			KKey k = (KKey)(int)msg.wParam;
			if (_IsCancelKey(k)) {
				Close();
				return false;
			} else if (_IsOkKey(k)) {
				if (_TopHot(out var v)) v.m._Click(v.i, keyboard: true);
			} else if (k == KKey.Escape) {
				_TopMenu().Close();
			} else if (k == KKey.Left) {
				var m = _TopMenu();
				if (m != this) m.Close();
			} else if (k == KKey.Right) {
				if (_TopHot(out var v) && v.b.IsSubmenu && !v.b.IsDisabled) v.m._ShowSubmenu(v.b, focusFirst: true);
			} else if (k is KKey.Down or KKey.Up or KKey.PageDown or KKey.PageUp or KKey.End or KKey.Home) {
				_TopMenu()._KeyNavigate(k);
			} else if (_IsPassKey(k)) {
				return false;
			} else { //eg a char key. Translate to get wm_char (we'll eat it), but eat this wm_keydown.
				Api.TranslateMessage(msg);
			}
			return true;
		}

		void _KeyNavigate(KKey k) { //called for top menu
			int i = _scroll.KeyNavigate(_iHot, k); if (i == _iHot) return;
			while ((uint)i < _a.Count && _a[i].IsSeparator) i += k is KKey.Home or KKey.Down or KKey.PageDown ? 1 : -1;
			_SetHotItem(Math.Clamp(i, 0, _a.Count - 1), ensureVisible: true);
		}

		void _WmChar(char c) { //called for top menu
			if (c <= ' ') return;
			char cl = char.ToLowerInvariant(c), cu = char.ToUpperInvariant(c);
			for (int i = 0; i < _a.Count; i++) {
				var b = _a[i];
				if (b.rawText || b.IsDisabled) continue;
				string s = b.Text;
				int j = StringUtil.FindUnderlineChar(s);
				if (j < 0) continue;
				c = s[j];
				if (c == cu || c == cl) {
					_Click(i, keyboard: true);
					break;
				}
			}
		}

		static bool _IsOkKey(KKey k) => k is KKey.Enter or KKey.Tab or KKey.Space;
		static bool _IsCancelKey(KKey k) => k is KKey.Alt or KKey.Win or KKey.RWin or KKey.F10 or KKey.Apps or KKey.Back;
		static bool _IsPassKey(KKey k)
			=> k is KKey.Ctrl or KKey.Shift or KKey.CapsLock or KKey.NumLock or KKey.ScrollLock
			or KKey.PrintScreen or KKey.Pause or KKey.Insert
			or (>= KKey.F1 and <= KKey.F24) || keys.isMod(KMod.Ctrl | KMod.Alt | KMod.Win);

		/// <summary>
		/// Gets or sets callback function that decides how to respond to pressed keys (default, close, ignore, block).
		/// </summary>
		/// <remarks>
		/// The function is called on each key down event while the menu is open. Only if current thread is not in the foreground.
		/// To block a key, call <see cref="HookData.Keyboard.BlockEvent"/>.
		/// The function must be as fast as possible.
		/// </remarks>
		public Func<popupMenu, HookData.Keyboard, MKHook> KeyboardHook { get; set; }
	}
}

namespace Au.Types
{
	/// <summary>
	/// Flags for <see cref="popupMenu"/> <b>ShowX</b> methods.
	/// </summary>
	/// <remarks>
	/// Most flags are for API <msdn>TrackPopupMenuEx</msdn>.
	/// </remarks>
	[Flags]
	public enum MSFlags
	{
		/// <summary>Show by caret (text cursor) position. If not possible, depends on flag <b>ScreenCenter</b> or parameter <i>xy</i>.</summary>
		ByCaret = 0x1000000,

		/// <summary>Show in center of screen containing mouse pointer.</summary>
		ScreenCenter = 0x2000000,

		/// <summary>Underline characters preceded by &amp;, regardless of Windows settings. More info: <see cref="StringUtil.RemoveUnderlineChar"/>.</summary>
		Underline = 0x4000000,

		//TPM_ flags

		/// <summary>Horizontally align the menu so that the show position would be in its center.</summary>
		AlignCenterH = 0x4,

		/// <summary>Horizontally align the menu so that the show position would be at its right side.</summary>
		AlignRight = 0x8,

		/// <summary>Vertically align the menu so that the show position would be in its center.</summary>
		AlignCenterV = 0x10,

		/// <summary>Vertically align the menu so that the show position would at in its bottom.</summary>
		AlignBottom = 0x20,

		/// <summary>Show at bottom or top of <i>excludeRect</i>, not at righ/left.</summary>
		AlignRectBottomTop = 0x40,
	}

	/// <summary>
	/// Used with <see cref="popupMenu.KeyboardHook"/>.
	/// </summary>
	public enum MKHook
	{
		/// <summary>Process the key event as usually.</summary>
		Default,

		/// <summary>Close the menu.</summary>
		Close,

		/// <summary>Do nothing.</summary>
		None,
	}
}
