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
//using System.Linq;

using Au.Types;
using static Au.NoClass;

namespace Au
{
	public partial struct Wnd
	{
		#region top-level or child

		//FUTURE: test when this process is not DPI-aware:
		//	Coordinate-related API on Win7 high DPI.
		//	Acc location on Win10 with different DPI of monitors.

		/// <summary>
		/// Gets visible top-level window or control from point.
		/// By default the coordinates are relative to the primary screen.
		/// </summary>
		/// <param name="x">X coordinate in screen.</param>
		/// <param name="y">Y coordinate in screen.</param>
		/// <param name="flags"></param>
		/// <param name="co">Can be used to specify screen (see <see cref="Screen_.FromObject"/>) and/or whether x y are relative to the work area.</param>
		/// <remarks>
		/// Alternatively can be used API <msdn>WindowFromPoint</msdn>, <msdn>ChildWindowFromPointEx</msdn> or <msdn>RealChildWindowFromPoint</msdn>, but all they have various limitations and are not very useful in automation scripts.
		/// This function gets non-transparent controls that are behind (in the Z order) transparent controls (group button, tab control etc); supports more control types than <msdn>RealChildWindowFromPoint</msdn>. Also does not skip disabled controls. All this is not true with flag Raw.
		/// This function is not very fast. Fastest when used flag NeedWindow. Flag Raw also makes it faster.
		/// x and y can be Coord.Reverse etc; cannot be null.
		/// </remarks>
		public static Wnd FromXY(Coord x, Coord y, WXYFlags flags = 0, CoordOptions co = null)
		{
			return FromXY(Coord.Normalize(x, y, co), flags);
		}

		/// <summary>
		/// Gets visible top-level window or control from point.
		/// More info: <see cref="FromXY(Coord, Coord, WXYFlags, CoordOptions)"/>.
		/// </summary>
		/// <param name="p">X Y in screen coordinates.</param>
		/// <param name="flags"></param>
		public static Wnd FromXY(Point p, WXYFlags flags = 0)
		{
			//TODO: rename all FromXY to FromPoint.

			bool needW = 0 != (flags & WXYFlags.NeedWindow);
			bool needC = 0 != (flags & WXYFlags.NeedControl);
			if(needW && needC) throw new ArgumentException("", "flags");
			Wnd w;

			if(needW) {
				w = Api.RealChildWindowFromPoint(Api.GetDesktopWindow(), p); //much faster than WindowFromPoint
				if(!w.HasExStyle(Native.WS_EX_TRANSPARENT | Native.WS_EX_LAYERED)) return w; //fast. Windows that have both these styles are mouse-transparent.
				return Api.WindowFromPoint(p).WndWindow; //ChildWindowFromPointEx would be faster, but less reliable

				//info:
				//WindowFromPoint is the most reliable. It skips really transparent top-level windows (TL). Unfortunately it skips disabled controls (but not TL).
				//ChildWindowFromPointEx with CWP_SKIPINVISIBLE|CWP_SKIPTRANSPARENT skips all with WS_EX_TRANSPARENT, although without WS_EX_LAYERED they aren't actually transparent.
				//RealChildWindowFromPoint does not skip transparent TL. It works like ChildWindowFromPointEx(CWP_SKIPINVISIBLE).
				//None of the above API prefers a really visible control that is under a transparent part of a sibling control. RealChildWindowFromPoint does it only for group buttons, but not for tab controls etc.
				//All API skip windows that have a hole etc in window region at that point.
				//None of the API uses WM_NCHITTEST+HTTRANSPARENT. Tested only with TL of other processes.
				//AccessibleObjectFromPoint.get_Parent in most cases gets the most correct window/control, but it is dirty, unreliable and often very slow, because sends WM_GETOBJECT etc.
				//speed:
				//RealChildWindowFromPoint is the fastest. About 5 mcs.
				//ChildWindowFromPointEx is 50% slower.
				//WindowFromPoint is 4 times slower; WndWindow does not make significantly slower.
				//AccessibleObjectFromPoint.get_Parent often is of RealChildWindowFromPoint speed, but often much slower than all others.
				//IUIAutomation.FromPoint super slow, 6-10 ms. Getting window handle from it is not easy, > 0.5 ms.
			}

			w = Api.WindowFromPoint(p);
			if(w.Is0) return w;

			if(0 != (flags & WXYFlags.Raw)) {
				if(needW) w = w.WndWindow; else if(needC && w == w.WndWindow) w = default;
				return w;
			}

			Wnd t = w.WndDirectParent; //need parent because need to call realchildwindowfrompoint on it, else for group box would go through the slow way of detecting transparen control
			if(!t.Is0) w = t;

			t = w._ChildFromXY(p, false, true);
			if(t.Is0) t = w;
			if(needC && t == w) return default;
			return t;
		}

		/// <summary>
		/// Gets visible top-level window or control from mouse cursor position.
		/// More info: <see cref="FromXY(Coord, Coord, WXYFlags, CoordOptions)"/>.
		/// </summary>
		public static Wnd FromMouse(WXYFlags flags = 0)
		{
			return FromXY(Mouse.XY, flags);
		}

		#endregion

		#region child

		/// <summary>
		/// Gets child control from point.
		/// Returns default(Wnd) if the point is not in a child control or not in the client area of this window.
		/// </summary>
		/// <param name="x">X coordinate in the client area of this window.</param>
		/// <param name="y">Y coordinate in the client area of this window.</param>
		/// <param name="directChild">Get direct child, not a child of a child and so on.</param>
		/// <param name="screenXY">x y are relative to the pimary screen, not to the client area.</param>
		/// <exception cref="WndException">This variable is invalid (window not found, closed, etc).</exception>
		/// <remarks>
		/// x y can be Coord.Reverse etc; cannot be null.
		/// </remarks>
		public Wnd ChildFromXY(Coord x, Coord y, bool directChild = false, bool screenXY = false)
		{
			ThrowIfInvalid();
			Point p = screenXY ? Coord.Normalize(x, y) : Coord.NormalizeInWindow(x, y, this);
			return _ChildFromXY(p, directChild, screenXY);
		}

		//Returns child or default(Wnd).
		Wnd _ChildFromXY(Point p, bool directChild, bool screenXY)
		{
			Wnd R = _TopChildWindowFromPointSimple(this, p, directChild, screenXY);
			if(R.Is0) return R;

			//Test whether it is a transparent control, like tab, covering other controls.
			//RealChildWindowFromPoint does it only for group button.

			if(R.HasExStyle(Native.WS_EX_MDICHILD)) return R;

			if(!screenXY) Api.ClientToScreen(this, ref p);
			g1:
			RECT r = R.Rect;
			for(Wnd t = R; ;) {
				t = Api.GetWindow(t, Api.GW_HWNDNEXT); if(t.Is0) break;
				RECT rr = t.Rect;
				if(!rr.Contains(p)) continue;
				if(!t.IsVisible) continue;
				if(rr.Width * rr.Height > r.Width * r.Height || rr == r) continue; //bigger than R, or equal

				//is R transparent?
				//Print("WM_NCHITTEST", R);
				if(R.SendTimeout(100, out var ht, Api.WM_NCHITTEST, 0, Math_.MakeUint(p.X, p.Y))) {
					if((int)ht != Api.HTTRANSPARENT) break;
				} else {
					//break;
					if(Native.GetError() != Api.ERROR_ACCESS_DENIED) break; //higher UAC level?
					if(rr.left <= r.left || rr.top <= r.top || rr.right >= r.right || rr.bottom >= r.bottom) break; //R must fully cover t, like a tab or group control
				}

				//now we know that R is transparent and there is another control behind
				if(!directChild) //that control can contain a child in that point, so we must find it
				{
					R = _TopChildWindowFromPointSimple(t, p, false, true);
					//Print(R);
					if(!R.Is0) goto g1;
				}
				R = t;
			}

			return R;
		}

		//Returns child or default(Wnd).
		static Wnd _TopChildWindowFromPointSimple(Wnd w, Point p, bool directChild, bool screenXY)
		{
			if(screenXY && !Api.ScreenToClient(w, ref p)) return default;

			for(Wnd R = default; ;) {
				Wnd t = _RealChildWindowFromPoint_RtlAware(w, p);
				if(directChild) return t;
				if(t.Is0 || !w.MapClientToClientOf(t, ref p)) return R;
				R = w = t;
			}
		}

		//Returns direct child or default(Wnd).
		static Wnd _RealChildWindowFromPoint_RtlAware(Wnd w, Point p)
		{
			if(w.HasExStyle(Native.WS_EX_LAYOUTRTL) && Api.GetClientRect(w, out var rc)) { p.X = rc.right - p.X; }
			Wnd R = Api.RealChildWindowFromPoint(w, p);
			return R == w ? default : R;
		}

		#endregion
	}
}

namespace Au.Types
{
	/// <summary>
	/// Flags for <see cref="Wnd.FromXY(Coord, Coord, WXYFlags, CoordOptions)"/> and <see cref="Wnd.FromMouse"/>.
	/// </summary>
	[Flags]
	public enum WXYFlags
	{
		/// <summary>
		/// Need top-level window. If at that point is a control, gets its top-level parent.
		/// Don't use together with NeedControl.
		/// If none of flags NeedWindow and NeedControl are specified, the function gets exactly what is at that point (control or top-level window).
		/// </summary>
		NeedWindow = 1,

		/// <summary>
		/// Need a control (child window). Returns default(Wnd) if there is no control at that point.
		/// Don't use together with NeedWindow.
		/// If none of flags NeedWindow and NeedControl are specified, the function gets exactly what is at that point (control or top-level window).
		/// </summary>
		NeedControl = 2,

		/// <summary>
		/// Just call API <msdn>WindowFromPoint</msdn>.
		/// Faster but less accurate with controls. Does not see disabled controls, does not prefer non-transparent controls.
		/// Not used with flag NeedWindow.
		/// </summary>
		Raw = 4,
	}
}