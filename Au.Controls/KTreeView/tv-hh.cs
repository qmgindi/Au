using Au.Types;
using Au.More;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Diagnostics;

namespace Au.Controls
{
	public unsafe partial class KTreeView : HwndHost
	{
		const string c_winClassName = "KTreeView";
		wnd _w;
		bool _hasHwnd;

		public wnd Hwnd => _w;

		static KTreeView() {
			WndUtil.RegisterWindowClass(c_winClassName);
		}

		protected override HandleRef BuildWindowCore(HandleRef hwndParent) {
			var wParent = (wnd)hwndParent.Handle;
#if true
			WndUtil.CreateWindow(_wndProc = _WndProc, false, c_winClassName, Name, WS.CHILD | WS.CLIPCHILDREN, 0, 0, 0, 10, 10, wParent);
#else //the below code works, but not on Win7. The above code works if WS.DISABLED or WM_NCHITTEST returns HTTRANSPARENT. WPF can remove WS.DISABLED.
			_w=WndUtil.CreateWindow(_wndProc = _WndProc, false, c_winClassName, null, WS.CHILD|WS.CLIPCHILDREN, WSE.TRANSPARENT|WSE.LAYERED, 0, 0, 10, 10, wParent);
	//		_w.SetTransparency(true, 255);
			Api.SetLayeredWindowAttributes(_w, 0, 0, 0);
#endif

			return new HandleRef(this, _w.Handle);
		}

		protected override void DestroyWindowCore(HandleRef hwnd) {
			Api.DestroyWindow(_w);
		}

		WNDPROC _wndProc;
		nint _WndProc(wnd w, int msg, nint wParam, nint lParam) {
			//var pmo = new PrintMsgOptions(Api.WM_NCHITTEST, Api.WM_SETCURSOR, Api.WM_MOUSEMOVE, Api.WM_NCMOUSEMOVE, 0x10c1);
			//if (WndUtil.PrintMsg(out string s, _w, msg, wParam, lParam, pmo)) print.it("<><c green>" + s + "<>");

			if (_vscroll.WndProc(w, msg, wParam, lParam) || _hscroll.WndProc(w, msg, wParam, lParam)) return default;

			switch (msg) {
			case Api.WM_NCCREATE:
				_w = w;
				_hasHwnd = true;
				_SetDpiAndItemSize(More.Dpi.OfWindow(_w));
				break;
			case Api.WM_NCDESTROY:
				_w = default;
				_hasHwnd = false;
				_acc?.Dispose(); _acc = null;
				break;
			case Api.WM_PAINT:
				using (var bp = new BufferedPaint(w, true)) _Render(bp.DC, bp.UpdateRect);
				return default;
			case Api.WM_SHOWWINDOW when wParam == 1:
				if (_ensureVisibleIndex > 0) EnsureVisible(_ensureVisibleIndex);
				break;
			case Api.WM_SIZE:
				_width = Math2.LoWord(lParam);
				_height = Math2.HiWord(lParam);
				_Measure();
				break;
			case Api.WM_SYSCOMMAND when ((int)wParam & 0xfff0) is Api.SC_VSCROLL or Api.SC_HSCROLL: //note: Windows bug: swapped SC_VSCROLL and SC_HSCROLL
				try {
					_inScrollbarScroll = true;
					return Api.DefWindowProc(w, msg, wParam, lParam);
				}
				finally {
					_ScrollEnded();
					_inScrollbarScroll = false;
				}
			}

			var R = Api.DefWindowProc(w, msg, wParam, lParam);

			switch (msg) {
			case Api.WM_NCHITTEST:
				if (R == Api.HTCLIENT) R = Api.HTTRANSPARENT; //workaround for focus problems and closing parent Popup on click
				break;
			}

			return R;
		}

		protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) {
			if (msg == Api.WM_GETOBJECT) { //not in _WndProc, because WPF steals it if passed to base.WndProc
				handled = true;
				return (_acc ??= new _Accessible(this)).WmGetobject(wParam, lParam);
			}
			return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
		}

		//protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => null; //removes unused object from MSAA tree, but then no UIA
		_Accessible _acc;

		protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e) {
			//print.it(e.Property);
			if (_lePopup != null && e.Property.Name == "IsVisible" && e.NewValue is bool y && !y) EndEditLabel(true);
			base.OnPropertyChanged(e);
		}
	}
}
