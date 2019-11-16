//#define TRACE_TEMP_RANGES

using System;
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
using System.Drawing;
//using System.Linq;

using Au;
using Au.Types;
using static Au.AStatic;
using Au.Controls;
using static Au.Controls.Sci;
using DiffMatchPatch;

partial class SciCode : AuScintilla
{
	public readonly FileNode ZFile;
	SciText.FileLoaderSaver _fls;

	//margins. Initially 0-4. We can add more with SCI_SETMARGINS.
	public const int c_marginFold = 0;
	const int c_marginLineNumbers = 1;
	const int c_marginMarkers = 2; //breakpoints etc

	//markers. We can use 0-24. Folding 25-31.
	const int c_markerBookmark = 0;
	const int c_markerBreakpoint = 1;
	const int c_markerStepNext = 2;
	public const int c_markerUnderline = 24;

	//indicators. We can use 8-31. Lexers use 0-7.
	const int c_indicFind = 8;

	internal SciCode(FileNode file, SciText.FileLoaderSaver fls)
	{
		//_edit = edit;
		ZFile = file;
		_fls = fls;

		this.Dock = DockStyle.Fill;
		this.Name = "Code_text";
		this.AccessibleRole = AccessibleRole.Document;
		this.AllowDrop = true;

		ZInitImagesStyle = ZImagesStyle.AnyString;
		if(fls.IsBinary) ZInitReadOnlyAlways = true;
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);

		int dpi = Au.Util.ADpi.BaseDPI;

		Call(SCI_SETMODEVENTMASK, (int)(MOD.SC_MOD_INSERTTEXT | MOD.SC_MOD_DELETETEXT /*| MOD.SC_MOD_INSERTCHECK*/));
		Call(SCI_SETMARGINTYPEN, c_marginLineNumbers, SC_MARGIN_NUMBER);
		Z.MarginWidth(c_marginLineNumbers, 40 * dpi / 96);

		Z.StyleFont(STYLE_DEFAULT, "Consolas", 9); //like default in VS
		Z.StyleClearAll();

		if(ZFile.IsCodeFile) {
			//C# interprets Unicode newline characters NEL, LS and PS as newlines. Visual Studio too.
			//	Scintilla and C++ lexer support it, but by default it is disabled.
			//	If disabled, line numbers in errors/warnings/stacktraces may be incorrect.
			//	Ascii VT and FF are not interpreted as newlines by C# and Scintilla.
			//	Not tested, maybe this must be set for each document in the control.
			//	Scintilla controls without C++ lexer don't support it.
			//		But if we temporarily set C++ lexer for <code>, newlines are displayed in whole text.
			//	Somehow this disables <fold> tag, therefore now not used for output etc.
			Call(SCI_SETLINEENDTYPESALLOWED, 1);

			Call(SCI_SETMOUSEDWELLTIME, 500);

			CiStyling.InitSciDoc(this);

			//Call(SCI_ASSIGNCMDKEY, 3 << 16 | 'C', SCI_COPY); //Ctrl+Shift+C = raw copy
		} else {
			Call(SCI_SETLEXER, (int)LexLanguage.SCLEX_NULL);
		}

		//Call(SCI_SETXCARETPOLICY, CARET_SLOP | CARET_EVEN, 20); //does not work

		//Call(SCI_SETVIEWWS, 1); Call(SCI_SETWHITESPACEFORE, 1, 0xcccccc);
	}

	internal void ZInit(byte[] text, bool newFile)
	{
		if(!IsHandleCreated) CreateHandle();
		_fls.SetText(Z, text);
		if(newFile) _openState = 1;
	}

	//protected override void Dispose(bool disposing)
	//{
	//	AOutput.QM2.Write($"Dispose disposing={disposing} IsHandleCreated={IsHandleCreated} Visible={Visible}");
	//	base.Dispose(disposing);
	//}

	protected unsafe override void ZOnSciNotify(ref SCNotification n)
	{
		//switch(n.nmhdr.code) {
		//case NOTIF.SCN_PAINTED:
		////case NOTIF.SCN_UPDATEUI:
		//case NOTIF.SCN_FOCUSIN:
		//case NOTIF.SCN_FOCUSOUT:
		//case NOTIF.SCN_DWELLSTART:
		//case NOTIF.SCN_DWELLEND:
		//case NOTIF.SCN_NEEDSHOWN:
		//	break;
		//case NOTIF.SCN_MODIFIED:
		//	Print(n.nmhdr.code, n.modificationType);
		//	break;
		//default:
		//	Print(n.nmhdr.code);
		//	break;
		//}

		switch(n.nmhdr.code) {
		case NOTIF.SCN_SAVEPOINTLEFT:
			Program.Model.Save.TextLater();
			break;
		case NOTIF.SCN_SAVEPOINTREACHED:
			//never mind: we should cancel the 'save text later'
			break;
		case NOTIF.SCN_STYLENEEDED:
			CodeInfo.SciStyleNeeded(this, n.position);
			break;
		case NOTIF.SCN_MODIFIED:
			//Print("SCN_MODIFIED", n.modificationType, n.position, n.FinalPosition, Z.CurrentPos, n.Text);
			//Print(n.modificationType);
			//if(n.modificationType.Has(MOD.SC_PERFORMED_USER | MOD.SC_MOD_BEFOREINSERT)) {
			//	Print($"'{n.Text}'");
			//	if(n.length == 2 && n.textUTF8!=null && n.textUTF8[0]=='\r' && n.textUTF8[1] == '\n') {
			//		Call(SCI_BEGINUNDOACTION); Call(SCI_ENDUNDOACTION);
			//	}
			//}
			if(n.modificationType.HasAny(MOD.SC_MOD_INSERTTEXT | MOD.SC_MOD_DELETETEXT)) {
				_modified = true;
				_TempRangeOnModifiedOrPosChanged(n.modificationType, n.position, n.length);
				CodeInfo.SciModified(this, n);
				Panels.Find.ZUpdateQuickResults(true);
				//} else if(n.modificationType.Has(MOD.SC_MOD_INSERTCHECK)) {
				//	//Print(n.Text);
				//	//if(n.length==1 && n.textUTF8[0] == ')') {
				//	//	Call(Sci.SCI_SETOVERTYPE, _testOvertype = true);

				//	//}
			}
			break;
		case NOTIF.SCN_CHARADDED:
			//Print($"SCN_CHARADDED  {n.ch}  '{(char)n.ch}'");
			if(n.ch == '\n' /*|| n.ch == ';'*/) { //split scintilla Undo
				Call(SCI_BEGINUNDOACTION); Call(SCI_ENDUNDOACTION);
			}
			if(n.ch != '\r' && n.ch <= 0xffff) { //on Enter we receive notifications for '\r' and '\n'
				CodeInfo.SciCharAdded(this, (char)n.ch);
			}
			break;
		case NOTIF.SCN_UPDATEUI:
			//Print((uint)n.updated, _modified);
			if(0 != (n.updated & 1)) {
				if(_modified) _modified = false; else n.updated &= ~1; //ignore duplicate notification after adding or removing ()[]{}. Also the first after opening.
			}
			if(0 != (n.updated & 3)) { //text/styling (1), selection/click (2)
				_TempRangeOnModifiedOrPosChanged(0, 0, 0);
				Panels.Editor.ZUpdateUI_Cmd();
				CodeInfo.SciUpdateUI(this, modified: 0 != (n.updated & 1));
			} else if(0 != (n.updated & 12)) { //scrolled
				if(ZFile.IsCodeFile) CodeInfo.Cancel();
			}
			break;
		case NOTIF.SCN_DWELLSTART:
			CodeInfo.SciMouseDwellStarted(this, n.position);
			break;
		//case NOTIF.SCN_DWELLEND:
		//	CodeInfo.SciMouseDwellEnded(this);
		//	break;
		case NOTIF.SCN_MARGINCLICK:
			if(ZFile.IsCodeFile) {
				CodeInfo.Cancel();
				if(n.margin == c_marginFold) {
					_FoldingOnMarginClick(null, n.position);
				}

				//SHOULDDO: when clicked selbar to select a fold header line, should select all hidden lines. Like in VS.
			}
			break;
		}

		base.ZOnSciNotify(ref n);
	}
	bool _modified;

	protected override void WndProc(ref Message m)
	{
		//var w = (AWnd)m.HWnd;
		//Print(m);
		switch(m.Msg) {
		case Api.WM_SETFOCUS:
			if(!_noModelEnsureCurrentSelected) Program.Model?.EnsureCurrentSelected();
			break;
		case Api.WM_CHAR:
			int c = (int)m.WParam;
			if(c < 32) {
				if(!(c == 9 || c == 10 || c == 13)) return;
			} else {
				if(CodeInfo.SciBeforeCharAdded(this, (char)c)) return;
			}
			break;
		case Api.WM_KEYDOWN:
			if((KKey)m.WParam == KKey.Insert) return;
			break;
		case Api.WM_RBUTTONDOWN:
			//prevent changing selection when right-clicked margin if selection start is in that line
			POINT p = (AMath.LoShort(m.LParam), AMath.HiShort(m.LParam));
			if(Z.MarginFromPoint(p, false) >= 0) {
				var k = Z.LineStartEndFromPos(false, Z.PosFromXY(false, p, false));
				var cp = Z.SelectionStart8;
				if(cp >= k.start && cp <= k.end) return;
			}
			break;
		case Api.WM_CONTEXTMENU:
			bool kbd = (int)m.LParam == -1;
			int margin = kbd ? -1 : Z.MarginFromPoint((AMath.LoShort(m.LParam), AMath.HiShort(m.LParam)), true);
			switch(margin) {
			case -1:
				Strips.ddEdit.ZShowAsContextMenu(kbd);
				break;
			case c_marginLineNumbers:
			case c_marginMarkers:
				ZCommentLines(null);
				break;
				//case c_marginFold:
				//	break;
			}
			return;
		}

		base.WndProc(ref m);

		switch(m.Msg) {
		//case Api.WM_MOUSEMOVE:
		//	CodeInfo.SciMouseMoved(this, AMath.LoShort(m.LParam), AMath.HiShort(m.LParam));
		//	break;
		case Api.WM_KILLFOCUS:
			CodeInfo.SciKillFocus(this);
			break;
		case Api.WM_LBUTTONUP:
			if(ModifierKeys == Keys.Control) CiGoTo.GoToSymbolFromPos(onCtrlClick: true);
			break;
		}
	}
	bool _noModelEnsureCurrentSelected;

	protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
	{
		switch(keyData) {
		case Keys.Control | Keys.C:
			ZCopyModified(onlyInfo: true);
			break;
		case Keys.Control | Keys.V:
			if(ZPasteModified()) return true;
			break;
		default:
			if(CodeInfo.SciCmdKey(this, keyData)) return true;
			switch(keyData) {
			case Keys.Enter:
				Call(SCI_BEGINUNDOACTION); Call(SCI_ENDUNDOACTION);
				break;
			}
			break;
		}
		return base.ProcessCmdKey(ref msg, keyData);
	}

	public bool ZIsUnsaved {
		get => _isUnsaved || 0 != Call(SCI_GETMODIFY);
		set {
			if(_isUnsaved = value) Program.Model.Save.TextLater(1);
		}
	}
	bool _isUnsaved;

	internal bool ZSaveText()
	{
		if(ZIsUnsaved) {
			//AOutput.QM2.Write("saving");
			ZFile.UnCacheText();
			if(!Program.Model.TryFileOperation(() => _fls.Save(Z, ZFile.FilePath, tempDirectory: ZFile.IsLink ? null : ZFile.Model.TempDirectory))) return false;
			//info: with tempDirectory less noise for FileSystemWatcher
			_isUnsaved = false;
			Call(SCI_SETSAVEPOINT);
		}
		return true;
	}

	public void ZFileModifiedExternally()
	{
		var text = ZFile.GetText(saved: true); if(text == this.Text) return;
		ZReplaceTextGently(text);
		Call(SCI_SETSAVEPOINT);
		if(this == Panels.Editor.ZActiveDoc) Print($"<>Info: file {ZFile.Name} has been modified by another program and therefore reloaded in editor. You can Undo.");
	}
	//public void ZFileModifiedExternally()
	//{
	//	var text = ZFile.GetText(saved: true); if(text == this.Text) return;
	//	if(this == Panels.Editor.ActiveDoc && AWnd.Active.IsOfThisProcess) {
	//		IsUnsaved = true;
	//		Print($"<>Info: the active editor file {ZFile.SciLink} has been modified by another program. The modified file will be replaced with editor text.");
	//		return;
	//	}
	//	ReplaceTextGently(text);
	//	Call(SCI_SETSAVEPOINT);
	//}

	#region drag drop

	enum _DD_DataType { None, Text, Files, Shell, Link, Script };
	_DD_DataType _drag;

	protected override void OnDragEnter(DragEventArgs e)
	{
		var d = e.Data;
		//foreach(var v in d.GetFormats()) Print(v, d.GetData(v, false)?.GetType()); Print("--");
		_drag = 0;
		if(d.GetDataPresent("Aga.Controls.Tree.TreeNodeAdv[]", false)) _drag = _DD_DataType.Script;
		else if(d.GetDataPresent("FileDrop", false)) _drag = _DD_DataType.Files;
		else if(d.GetDataPresent("Shell IDList Array", false)) _drag = _DD_DataType.Shell;
		else if(d.GetDataPresent("UnicodeText", false))
			_drag = d.GetDataPresent("FileGroupDescriptorW", false) ? _DD_DataType.Link : _DD_DataType.Text;
		e.Effect = _DD_GetEffect(e);
		base.OnDragEnter(e);
	}

	protected override void OnDragOver(DragEventArgs e)
	{
		if((e.Effect = _DD_GetEffect(e)) != 0) _DD_Over(e);
		base.OnDragOver(e);
	}

	protected override void OnDragDrop(DragEventArgs e)
	{
		if((e.Effect = _DD_GetEffect(e)) != 0) _DD_Drop(e);
		_drag = 0;
		base.OnDragDrop(e);
	}

	protected override void OnDragLeave(EventArgs e)
	{
		if(_drag != 0) {
			_drag = 0;
			Call(SCI_DRAGDROP, 3);
		}
		base.OnDragLeave(e);
	}

	Point _DD_GetDropPos(DragEventArgs e, out int pos)
	{
		var p = this.PointToClient(new Point(e.X, e.Y));
		if(_drag != _DD_DataType.Text) { //if files etc, drop as lines, not anywhere
			pos = Call(SCI_POSITIONFROMPOINT, p.X, p.Y);
			pos = Z.LineStartFromPos(false, pos);
			p.X = Call(SCI_POINTXFROMPOSITION, 0, pos);
			p.Y = Call(SCI_POINTYFROMPOSITION, 0, pos);
		} else pos = 0;
		return p;
	}

	unsafe void _DD_Over(DragEventArgs e)
	{
		var p = _DD_GetDropPos(e, out _);
		var z = new Sci_DragDropData { x = p.X, y = p.Y };
		Call(SCI_DRAGDROP, 1, &z);

		//FUTURE: auto-scroll
	}

	unsafe void _DD_Drop(DragEventArgs e)
	{
		var xy = _DD_GetDropPos(e, out int pos);
		string s = null; StringBuilder t = null;
		int endOfMeta = 0; bool inMeta = false; string menuVar = null;

		if(_drag != _DD_DataType.Text) {
			t = new StringBuilder();
			if(ZFile.IsCodeFile) {
				var text = this.Text;
				endOfMeta = Au.Compiler.MetaComments.FindMetaComments(text);
				if(pos < endOfMeta) inMeta = true;
				else if(pos > endOfMeta) text.RegexMatch(@"\b(\w+)\s*=\s*new\s+Au(?:Menu|Toolbar)", 1, out menuVar, 0, endOfMeta..pos);
			}
		}

		var d = e.Data;
		switch(_drag) {
		case _DD_DataType.Text:
			s = d.GetData("UnicodeText", false) as string;
			break;
		case _DD_DataType.Files:
			if(d.GetData("FileDrop", false) is string[] paths) {
				foreach(var path in paths) {
					bool isLnk = path.Ends(".lnk", true);
					if(isLnk) t.Append("//");
					var name = APath.GetFileName(path, true);
					_AppendFile(path, name);
					if(isLnk) {
						try {
							var g = AShortcutFile.Open(path);
							string target = g.TargetAnyType, args = null;
							if(target.Starts("::")) {
								using var pidl = APidl.FromString(target);
								name = pidl.ToShellString(Native.SIGDN.NORMALDISPLAY);
							} else {
								args = g.Arguments;
								if(!target.Ends(".exe", true) || name.Find("Shortcut") >= 0)
									name = APath.GetFileName(target, true);
							}
							_AppendFile(target, name, args);
						}
						catch(AuException) { break; }
					}
				}
				s = t.ToString();
			}
			break;
		case _DD_DataType.Shell:
			_DD_GetShell(d, out var shells, out var names);
			if(shells != null) {
				for(int i = 0; i < shells.Length; i++) {
					_AppendFile(shells[i], names[i]);
				}
				s = t.ToString();
			}
			break;
		case _DD_DataType.Link:
			_DD_GetLink(d, out s, out var s2);
			if(s != null) {
				_AppendFile(s, s2);
				s = t.ToString();
			}
			break;
		case _DD_DataType.Script:
			var nodes = d.GetData("Aga.Controls.Tree.TreeNodeAdv[]", false) as Aga.Controls.Tree.TreeNodeAdv[];
			if(nodes != null) {
				foreach(var tn in nodes) {
					var fn = tn.Tag as FileNode;
					_AppendFile(fn.ItemPath, fn.Name, null, fn);
				}
				s = t.ToString();
			}
			break;
		}

		if(!Empty(s)) {
			var z = new Sci_DragDropData { x = xy.X, y = xy.Y };
			var b = Au.Util.AConvert.ToUtf8(s);
			fixed(byte* bp = b) {
				z.text = bp;
				z.len = b.Length - 1;
				if(_drag != _DD_DataType.Text || 0 == (e.Effect & DragDropEffects.Move)) z.copy = 1;
				Call(SCI_DRAGDROP, 2, &z);
			}
			if(!Focused && ((AWnd)(FindForm())).IsActive) { //note: don't activate window; let the drag source do it, eg Explorer activates on drag-enter.
				_noModelEnsureCurrentSelected = true; //don't scroll treeview to currentfile
				Focus();
				_noModelEnsureCurrentSelected = false;
			}
		} else {
			Call(SCI_DRAGDROP, 3);
		}

		void _AppendFile(string path, string name, string args = null, FileNode fn = null)
		{
			if(!ZFile.IsCodeFile) {
				t.Append(path);
			} else if(inMeta) {
				string opt = null;
				switch(_drag) {
				case _DD_DataType.Files:
					opt = AFile.ExistsAsDirectory(path) ? "outputPath " : "r ";
					break;
				case _DD_DataType.Script:
					if(fn.IsFolder) {
						if(fn.IsProjectFolder(out fn)) { opt = "pr "; path = fn.ItemPath; }
					} else if(!fn.IsScript) {
						if(!fn.IsCodeFile) opt = "resource ";
						else if(fn.FindProject(out _, out var fMain) && fn == fMain) opt = "pr ";
						else opt = "c ";
					}
					break;
				}
				if(opt == null) return;
				//make relative path
				var p2 = ZFile.ItemPath; int i = p2.LastIndexOf('\\') + 1;
				if(0 == string.CompareOrdinal(path, 0, p2, 0, i)) path = path.Substring(i);

				t.Append(opt).Append(path).Append(';');
			} else {
				name = name.Escape();
				if(menuVar != null) t.Append(menuVar).Append("[\"").Append(name).Append("\"] =o=> ");
				bool isFN = fn != null;
				if(isFN && !fn.IsCodeFile) {
					t.Append("//").Append(path);
				} else {
					t.Append(isFN ? "ATask.Run(@\"" : "AExec.Run(@\"").Append(path);
					if(!Empty(args)) t.Append("\", \"").Append(args.Escape());
					t.Append("\");");
					if(menuVar == null && !isFN && (path.Starts("::") || path.Find(name, true) < 0)) t.Append(" //").Append(name);
					//FUTURE: add unexpanded path version
				}
			}
			t.AppendLine();
		}
	}

	DragDropEffects _DD_GetEffect(DragEventArgs e)
	{
		if(_drag == 0) return 0;
		if(Z.IsReadonly) return 0;
		var ae = e.AllowedEffect;
		DragDropEffects r = 0;
		switch(e.KeyState & (4 | 8 | 32)) { case 0: r = DragDropEffects.Move; break; case 8: r = DragDropEffects.Copy; break; default: return 0; }
		if(_drag == _DD_DataType.Text) return 0 != (ae & r) ? r : ae;
		if(0 != (ae & DragDropEffects.Link)) r = DragDropEffects.Link;
		else if(0 != (ae & DragDropEffects.Copy)) r = DragDropEffects.Copy;
		else r = ae;
		return r;
	}

	static unsafe void _DD_GetShell(IDataObject d, out string[] shells, out string[] names)
	{
		shells = names = null;
		var b = _DD_GetByteArray(d, "Shell IDList Array"); if(b == null) return;
		fixed(byte* p = b) {
			int* pi = (int*)p;
			int n = *pi++; if(n < 1) return;
			shells = new string[n]; names = new string[n];
			IntPtr pidlFolder = (IntPtr)(p + *pi++);
			for(int i = 0; i < n; i++) {
				using(var pidl = new APidl(pidlFolder, (IntPtr)(p + pi[i]))) {
					shells[i] = pidl.ToString();
					names[i] = pidl.ToShellString(Native.SIGDN.NORMALDISPLAY);
				}
			}
		}
	}

	static unsafe void _DD_GetLink(IDataObject d, out string url, out string text)
	{
		url = text = null;
		var b = _DD_GetByteArray(d, "FileGroupDescriptorW"); if(b == null) return;
		fixed(byte* p = b) { //FILEGROUPDESCRIPTORW
			if(*(int*)p != 1) return; //count of FILEDESCRIPTORW
			var s = new string((char*)(p + 76));
			if(!s.Ends(".url", true)) return;
			url = d.GetData("UnicodeText", false) as string;
			if(url != null) text = s.RemoveSuffix(4);
		}
	}

	static byte[] _DD_GetByteArray(IDataObject d, string format)
	{
		switch(d.GetData(format, false)) {
		case byte[] b: return b; //when d is created from data transferred from non-admin process to this admin process by UacDragDrop
		case MemoryStream m: return m.ToArray(); //original .NET DataObject. Probably this process is non-admin.
		}
		return null;
	}

	#endregion

	#region copy paste

	public void ZCopyModified(bool onlyInfo = false)
	{
		int i1 = Z.SelectionStart8, i2 = Z.SelectionEnd8, textLen = Len8;
		if(textLen == 0) return;
		bool isFragment = (i2 != i1 && !(i1 == 0 && i2 == textLen)) || !ZFile.IsCodeFile;
		if(onlyInfo) {
			if(isFragment || s_infoCopy) return; s_infoCopy = true;
			Print("Info: To copy C# code for pasting in the forum, use menu Edit -> Forum Copy. Then simply paste there; don't use the Code button.");
			return;
		}

		if(!isFragment) { i1 = 0; i2 = textLen; }
		string s = Z.RangeText(false, i1, i2);
		bool isScript = ZFile.IsScript;
		var b = new StringBuilder("[code2]");
		if(isFragment) {
			b.Append(s);
		} else {
			var name = ZFile.Name; if(name.RegexIsMatch(@"(?i)^(Script|Class)\d*\.cs")) name = null;
			var sType = isScript ? "script" : "class";
			//APerf.First();
			if(isScript && _RxScriptHeader.Match(s, out var m) && s.Find("\n// using //", m.Index) < 0 && s.Find("\n// main //", m.Index) < 0) {
				//APerf.NW();
				bool hasM12 = m[1].Length > 0 || m[2].Length > 0;
				int i = m.EndIndex;
				if(name == null && m.Index == 0 && !hasM12) { //if standard script named like "ScriptN.cs", copy as fragment
					while(i < s.Length && (s[i] == '\r' || s[i] == '\n')) i++;
				} else {
					//Start with prefix '//- type "name"'.
					b.AppendFormat("//- {0} \"{1}\"{2}", sType, name, s[0] == '/' ? " " : "\r\n")
						.Append(s, 0, m.Index);
					b.Append("//-{");
					if(hasM12) { //If there is something above or below standard usings, replace standard codes with '// using //' and '// main //'.
						b.Append(s, m[1].Index, m[1].Length).Append("\r\n// using //")
							.Append(s, m[2].Index, m[2].Length).Append("\r\n// main //");
					}
				}
				b.Append(s, i, s.Length - i);
			} else { //raw. Start with prefix '//~ type "name"'.
				b.AppendFormat("//~ {0} \"{1}\"\r\n{2}", sType, name, s);
			}
		}
		b.AppendLine("[/code2]");
#if TEST_COPYPASTE
			_Print(s, true);
#endif
		s = b.ToString();
#if TEST_COPYPASTE
			_Print(s);
#endif
		new AClipboardData().AddText(s).SetClipboard();
#if TEST_COPYPASTE
			PasteModified();
#endif
	}
	static bool s_infoCopy;

	public bool ZPasteModified()
	{
		var s = AClipboardData.GetText();
		if(s == null) return false;
		if(s.Like("[code2]*[/code2]\r\n")) s = s[7..^10];

		if(!s.RegexMatch(@"^//[\-~] (script|class) ""(.*?)""( |\R)", out var m)) return false;
		bool isClass = s[4] == 'c';
		int i = m.EndIndex;
		if(s[2] == '~') { //raw
			s = s.Substring(i);
		} else {
			Debug.Assert(!isClass);
			if(!s.RegexMatch(@"[ \n]//-\{", 0, out RXGroup m1)) return false;
			int j = m1.EndIndex;
			var b = new StringBuilder();
			b.Append(s, i, j - i);
			if(s.RegexMatch(@"(?ms)(.*?)^// using //\R(.*?)^// main //$", out var k, range: j..)) {
				b.Append(s, j, k[1].Length).AppendLine(c_usings).Append(s, k[2].Index, k[2].Length);
				i = k.EndIndex;
			} else {
				b.AppendLine().AppendLine(c_usings);
				i = j;
			}
			b.Append(c_scriptMain).Append(s, i, s.Length - i);
			s = b.ToString();
		}
#if TEST_COPYPASTE
			_Print(s); return false;
#endif
		var name = m[2].Length > 0 ? m[2].Value : (isClass ? "Class1.cs" : "Script1.cs");

		string buttons = ZFile.FileType != (isClass ? EFileType.Class : EFileType.Script)
			? "1 Create new file|Cancel"
			: "1 Create new file|2 Replace all text|3 Paste|Cancel";
		switch(ADialog.Show("Import C# file text from clipboard", "Source file: " + name, buttons, DFlags.CommandLinks, owner: this)) {
		case 0: break; //Cancel
		case 1: //Create new file
			Program.Model.NewItem(isClass ? "Class.cs" : "Script.cs", name, text: new EdNewFileText(true, s));
			break;
		case 2: //Replace all text
			Z.SetText(s);
			break;
		case 3: //Paste
			Z.ReplaceSel(s);
			break;
		} //rejected: option to rename this file

		return true;
	}

#if DEBUG && TEST_COPYPASTE
		void _Print(string s, bool first = false)
		{
			if(first) AOutput.Clear();
			//Print("<><code>" + s + "</code>\r\n<Z 0xc0e0c0><>");
			Print("<><code>" + s + "</code>");
			Print("<><Z 0xc0e0c0><>");
		}
#endif

	#endregion

	#region script header

	const string c_usings = "using Au; using Au.Types; using static Au.AStatic; using System; using System.Collections.Generic;";
	const string c_scriptMain = "class Script :AScript { [STAThread] static void Main(string[] a) => new Script(a); Script(string[] args) { //-}}}";

	static ARegex _RxScriptHeader => s_rxScript ??= new ARegex(@"(?sm)//-\{(.*?)\R\Q" + c_usings + @"\E$(.*?)\R\Q" + c_scriptMain + @"\E$");
	static ARegex s_rxScript;

	///// <summary>
	///// Finds script header //-{...//-} using regular expression.
	///// </summary>
	///// <param name="s">Script text.</param>
	///// <param name="m">
	///// Group 1 is "" or text between //-{ and c_usings. Includes the starting newline but not the ending newline.
	///// Group 2 is "" or text between c_usings and c_scriptMain. Includes the starting newline but not the ending newline.
	///// </param>
	//public static bool FindScriptHeader(string s, out RXMatch m) => _RxScript.Match(s, out m);

	//bool _IsScriptHeaderFolded()
	//{
	//	if(!ZFile.IsScript) return false;
	//	string s = Text;
	//	if(!_RxScriptHeader.Match(s, out var m)) return false;
	//	int line = Z.LineIndexFromPos(m.Index, utf16: true);
	//	return 0 == Call(SCI_GETFOLDEXPANDED, line);
	//}

	#endregion

	#region indicators

	bool _indicFindInited;
	bool _findHilited;

	public void ZHiliteFind(List<POINT> a)
	{
		if(_findHilited) {
			_findHilited = false;
			Z.IndicatorClear(c_indicFind);
		}
		if(a == null || a.Count == 0) return;
		_findHilited = true;

		if(!_indicFindInited) {
			_indicFindInited = true;
			Call(SCI_INDICSETSTYLE, c_indicFind, INDIC_STRAIGHTBOX);
			Call(SCI_INDICSETFORE, c_indicFind, 0x00ffff); Call(SCI_INDICSETALPHA, c_indicFind, 95); //0xa0ffff if white background
			Call(SCI_INDICSETUNDER, c_indicFind, 1); //draw before text
		}

		foreach(var v in a) Z.IndicatorAdd(true, c_indicFind, v.x, v.x + v.y);
	}

	#endregion

	#region text replacements etc

	/// <summary>
	/// Replaces text without losing markers, expanding folded code, etc.
	/// </summary>
	public void ZReplaceTextGently(string s)
	{
		int len = s.Lenn(); if(len == 0) goto gRaw;
		string old = Text;
		if(len > 1_000_000 || old.Length > 1_000_000 || old.Length == 0) goto gRaw;
		var dmp = new diff_match_patch();
		var a = dmp.diff_main(old, s, true); //the slowest part. Timeout 1 s; then a valid but smaller.
		if(a.Count > 1000) goto gRaw;
		dmp.diff_cleanupEfficiency(a);
		Call(SCI_BEGINUNDOACTION);
		for(int i = a.Count - 1, j = old.Length; i >= 0; i--) {
			var d = a[i];
			if(d.operation == Operation.INSERT) {
				Z.InsertText(true, j, d.text);
			} else {
				j -= d.text.Length;
				if(d.operation == Operation.DELETE) Z.DeleteRange(true, j, j + d.text.Length);
			}
		}
		Call(SCI_ENDUNDOACTION);
		return;
		gRaw:
		this.Text = s;
	}

	/// <summary>
	/// Comments (adds //) or uncomments (removes //) selected lines or current line.
	/// </summary>
	/// <param name="comment">Comment (true), uncomment (false) or toggle (null).</param>
	public void ZCommentLines(bool? comment)
	{
		if(Z.IsReadonly) return;
		Z.GetSelectionLines(out var x);
		var s = x.text;
		if(s.Length == 0) return;
		bool wasSelection = x.selEnd > x.selStart;
		bool caretAtEnd = wasSelection && Z.CurrentPos8 == x.linesEnd;
		int fromEnd = Len8 - x.linesEnd;
		bool com = comment ?? !s.RegexIsMatch("^[ \t]*//(?!/[^/])");
		s = com ? s.RegexReplace(@"(?m)^", "//") : s.RegexReplace(@"(?m)^([ \t]*)//", "$1");
		Z.ReplaceRange(false, x.linesStart, x.linesEnd, s);
		if(wasSelection) {
			int i = x.linesStart, j = Len8 - fromEnd;
			Call(SCI_SETSEL, caretAtEnd ? i : j, caretAtEnd ? j : i);
		}
	}

	#endregion

	#region temp ranges

	[Flags]
	public enum ZTempRangeFlags
	{
		/// <summary>
		/// Call onLeave etc when current position != current end of range.
		/// </summary>
		LeaveIfPosNotAtEndOfRange = 1,

		/// <summary>
		/// Call onLeave etc when range text modified.
		/// </summary>
		LeaveIfRangeTextModified = 2,

		/// <summary>
		/// Don't add new range if already exists a range with same current from, to, owner and flags. Then returns that range.
		/// </summary>
		NoDuplicate = 4,
	}

	public interface ITempRange
	{
		/// <summary>
		/// Removes this range from the collection of ranges of the document.
		/// Optional. Temp ranges are automatically removed sooner or later.
		/// Does nothing if already removed.
		/// </summary>
		void Remove();

		/// <summary>
		/// Gets current start and end positions of this range added with <see cref="ZTempRanges_Add"/>.
		/// Returns false if the range is removed; then sets from = to = -1.
		/// </summary>
		bool GetCurrentFromTo(out int from, out int to, bool utf8 = false);

		/// <summary>
		/// Gets current start position of this range added with <see cref="ZTempRanges_Add"/>. UTF-16.
		/// Returns -1 if the range is removed.
		/// </summary>
		int CurrentFrom { get; }

		/// <summary>
		/// Gets current end position of this range added with <see cref="ZTempRanges_Add"/>. UTF-16.
		/// Returns -1 if the range is removed.
		/// </summary>
		int CurrentTo { get; }

		object Owner { get; }

		/// <summary>
		/// Any data. Not used by temp range functions.
		/// </summary>
		object OwnerData { get; set; }
	}

	class _TempRange : ITempRange
	{
		SciCode _doc;
		readonly object _owner;
		readonly int _fromUtf16;
		internal readonly int from;
		internal int to;
		internal readonly Action onLeave;
		readonly ZTempRangeFlags _flags;

		internal _TempRange(SciCode doc, object owner, int fromUtf16, int fromUtf8, int toUtf8, Action onLeave, ZTempRangeFlags flags)
		{
			_doc = doc;
			_owner = owner;
			_fromUtf16 = fromUtf16;
			from = fromUtf8;
			to = toUtf8;
			this.onLeave = onLeave;
			_flags = flags;
		}

		public void Remove()
		{
			_TraceTempRange("remove", _owner);
			if(_doc != null) {
				_doc._tempRanges.Remove(this);
				_doc = null;
			}
		}

		internal void Leaved() => _doc = null;

		public bool GetCurrentFromTo(out int from, out int to, bool utf8 = false)
		{
			if(_doc == null) { from = to = -1; return false; }
			if(utf8) {
				from = this.from;
				to = this.to;
			} else {
				from = _fromUtf16;
				to = CurrentTo;
			}
			return true;
		}

		public int CurrentFrom => _doc != null ? _fromUtf16 : -1;

		public int CurrentTo => _doc?.Pos16(to) ?? -1;

		public object Owner => _owner;

		public object OwnerData { get; set; }

		internal bool MustLeave(int pos, int pos2, int modLen)
		{
			return pos < from || pos2 > to
				|| (0 != (_flags & ZTempRangeFlags.LeaveIfPosNotAtEndOfRange) && pos2 != to)
				|| (0 != (_flags & ZTempRangeFlags.LeaveIfRangeTextModified) && modLen != 0);
		}

		internal bool Contains(int pos, object owner, bool endPosition)
			=> (endPosition ? (pos == to) : (pos >= from || pos <= to)) && (owner == null || ReferenceEquals(owner, _owner));

		internal bool Equals(int from2, int to2, object owner2, ZTempRangeFlags flags2)
		{
			if(from2 != from || to2 != to || flags2 != _flags
				//|| onLeave2 != onLeave //delegate always different if captured variables
				//|| !ReferenceEquals(onLeave2?.Method, onLeave2?.Method) //can be used but slow. Also tested Target, always different.
				) return false;
			return ReferenceEquals(owner2, _owner);
		}

		public override string ToString() => $"({CurrentFrom}, {CurrentTo}), owner={_owner}";
	}

	List<_TempRange> _tempRanges = new List<_TempRange>();

	/// <summary>
	/// Marks a temporary working range of text and later notifies when it is leaved.
	/// Will automatically update range bounds when editing text inside it.
	/// Supports many ranges, possibly overlapping.
	/// The returned object can be used to get range info or remove it.
	/// Used mostly for code info, eg to cancel the completion list or signature help.
	/// </summary>
	/// <param name="owner">Owner of the range. See also <see cref="ITempRange.OwnerData"/>.</param>
	/// <param name="from">Start of range, UTF-16.</param>
	/// <param name="to">End of range, UTF-16. Can be = from.</param>
	/// <param name="onLeave">
	/// Called when current position changed and is outside this range (before from or after to) or text modified outside it. Then also forgets the range.
	/// Called after removing the range.
	/// If leaved several ranges, called in LIFO order.
	/// Can be null.
	/// </param>
	/// <param name="flags"></param>
	public ITempRange ZTempRanges_Add(object owner, int from, int to, Action onLeave = null, ZTempRangeFlags flags = 0)
	{
		int fromUtf16 = from;
		Z.NormalizeRange(true, ref from, ref to);
		Debug.Assert(Z.CurrentPos8 >= from && (flags.Has(ZTempRangeFlags.LeaveIfPosNotAtEndOfRange) ? Z.CurrentPos8 == to : Z.CurrentPos8 <= to));

		if(flags.Has(ZTempRangeFlags.NoDuplicate)) {
			for(int i = _tempRanges.Count - 1; i >= 0; i--) {
				var t = _tempRanges[i];
				if(t.Equals(from, to, owner, flags)) return t;
			}
		}

		_TraceTempRange("ADD", owner);
		var r = new _TempRange(this, owner, fromUtf16, from, to, onLeave, flags);
		_tempRanges.Add(r);
		return r;
	}

	/// <summary>
	/// Gets ranges containing the specified position and optionally of the specified owner, in LIFO order.
	/// It's safe to remove the retrieved ranges while enumerating.
	/// </summary>
	/// <param name="position"></param>
	/// <param name="owner">If not null, returns only ranges where ReferenceEquals(owner, range.owner).</param>
	/// <param name="endPosition">position must be at the end of the range.</param>
	/// <param name="utf8"></param>
	public IEnumerable<ITempRange> ZTempRanges_Enum(int position, object owner = null, bool endPosition = false, bool utf8 = false)
	{
		if(!utf8) position = Pos8(position);
		for(int i = _tempRanges.Count - 1; i >= 0; i--) {
			var r = _tempRanges[i];
			if(r.Contains(position, owner, endPosition)) yield return r;
		}
	}

	/// <summary>
	/// Gets ranges of the specified owner, in LIFO order.
	/// It's safe to remove the retrieved ranges while enumerating.
	/// </summary>
	/// <param name="owner">Returns only ranges where ReferenceEquals(owner, range.owner).</param>
	public IEnumerable<ITempRange> ZTempRanges_Enum(object owner)
	{
		for(int i = _tempRanges.Count - 1; i >= 0; i--) {
			var r = _tempRanges[i];
			if(ReferenceEquals(owner, r.Owner)) yield return r;
		}
	}

	void _TempRangeOnModifiedOrPosChanged(MOD mod, int pos, int len)
	{
		if(_tempRanges.Count == 0) return;
		if(mod == 0) pos = Z.CurrentPos8;
		int pos2 = pos;
		if(mod.Has(MOD.SC_MOD_DELETETEXT)) { pos2 += len; len = -len; }
		for(int i = _tempRanges.Count - 1; i >= 0; i--) {
			var r = _tempRanges[i];
			if(r.MustLeave(pos, pos2, len)) {
				_TraceTempRange("leave", r.Owner);
				_tempRanges.RemoveAt(i);
				r.Leaved();
				r.onLeave?.Invoke();
			} else {
				r.to += len;
				Debug.Assert(r.to >= r.from);
			}
		}
	}

	[Conditional("TRACE_TEMP_RANGES")]
	static void _TraceTempRange(string action, object owner) => Print(action, owner);

	#endregion
}