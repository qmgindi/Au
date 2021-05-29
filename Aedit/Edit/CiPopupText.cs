using Au;
using Au.Types;
using Au.Util;
using Au.Controls;
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
using System.Windows;
using System.Windows.Controls;

class CiPopupText
{
	public enum UsedBy { PopupList, Signature, Info }

	KPopup _w;
	FlowDocumentControl _c;
	EventHandler _onHiddenOrDestroyed;
	UsedBy _usedBy;
	System.Windows.Documents.Section _section;
	bool _updateText;

	public CiPopupText(UsedBy usedy, EventHandler onHiddenOrDestroyed = null) {
		_usedBy = usedy;
		_onHiddenOrDestroyed = onHiddenOrDestroyed;
	}

	/// <summary>
	/// The top-level popup window.
	/// </summary>
	public KPopup PopupWindow => _CreateOrGet();

	/// <summary>
	/// Called when clicked a link with href prefix "^".
	/// </summary>
	public Action<CiPopupText, string> OnLinkClick { get; set; }

	/// <summary>
	/// Text to show. Set before calling Show.
	/// </summary>
	public System.Windows.Documents.Section Text {
		get => _section;
		set {
			if (value != _section) {
				_section = value;
				if (IsVisible) _SetText(); else _updateText = true;
				//SHOULDDO: if visible, probably does not auto-size. But maybe now not used, because never noticed incorrect size.
			}
		}
	}

	KPopup _CreateOrGet() {
		if (_w == null) {
			bool ubInfo = _usedBy == UsedBy.Info;
			_w = new KPopup(ubInfo ? WS.POPUP | WS.BORDER : WS.POPUP | WS.THICKFRAME, shadow: ubInfo, sizeToContent: ubInfo ? SizeToContent.Height : default) {
				Name = "Ci.Info",
				WindowName = _usedBy switch { UsedBy.PopupList => "Au completion item info", UsedBy.Signature => "Au parameters info", _ => "Au quick info" },
				CloseHides = true
			};
			bool ubList = _usedBy == UsedBy.PopupList;
			_w.Size = (ubList ? 450 : 600, ubList ? 360 : 300);
			//rejected: save size in app settings (if not info). Popup list too.

			_c = CiText.CreateControl();
			_w.Content = _c;

			if (_onHiddenOrDestroyed != null) _w.Hidden += _onHiddenOrDestroyed;
			_c.LinkClicked += (sender, s) => {
				if (s.Starts('#')) return; //anchor
				if (s.Starts('^')) {
					OnLinkClick?.Invoke(this, s);
				} else if (s.Starts('|')) { //go to symbol source file/position or web page
					CiGoTo.LinkGoTo(s);
				} else {
					ARun.RunSafe(s);
				}
			};

			//never mind: on mouse up: if(IsVisible && !c.zSelectedText.NE()) _w.Hwnd.ActivateL(); //the user may want Ctrl+C
			//	but no mouse up events, even PreviewX.
			//	can use context menu instead.
		}
		return _w;
	}

	/// <summary>
	/// Shows by anchorRect, owned by ownerControl.
	/// </summary>
	/// <param name="ownerControl"></param>
	/// <param name="anchorRect">Rectangle in screen coord. The popup window will be by it but not in it (in it if side = null).</param>
	/// <param name="align"></param>
	/// <param name="hideIfOutside">Hide when mouse moved outside anchorRect unless is in the popup window.</param>
	public void Show(SciCode ownerControl, RECT anchorRect, Dock? side, bool hideIfOutside = false) {
		_CreateOrGet();
		if (_updateText) _SetText();
		_w.ShowByRect(ownerControl, side, anchorRect);

		if (hideIfOutside) {
			ATimer.Every(100, t => {
				if (IsVisible) {
					var p = AMouse.XY;
					if (anchorRect.Contains(p) || _w.Hwnd.Rect.Contains(p) || _c.IsMouseCaptureWithin) return;
					Hide();
				}
				t.Stop();
			});
		}
	}

	public void Show(SciCode ownerControl, int pos16, bool hideIfOutside = false, bool above = false) {
		var r = CiUtil.GetCaretRectFromPos(ownerControl, pos16, inScreen: true);
		r.Inflate(50, 0);
		Show(ownerControl, r, above ? Dock.Top : Dock.Bottom, hideIfOutside);
	}

	public bool Hide() {
		if (!IsVisible) return false;
		//AOutput.Write(new StackTrace());
		_w.Close();
		_section = null;
		_SetText();
		return true;
	}

	public bool IsVisible => _w?.IsVisible ?? false;

	void _SetText() {
		_updateText = false;
		_c?.Clear();
		if (_section != null) _c.Document.Blocks.Add(_section);
	}
}
