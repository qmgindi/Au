//#define NO_COMPL_CORR_SIGN

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
using System.Linq;

using Au;
using Au.Types;
using Au.More;
using Au.Compiler;
using Au.Controls;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Windows.Input;

//TODO: support <inheritdoc>. VS supports it partially (shows only summary, no parameters/remarks/exceptions).
//TODO: don't show black/unfolded text before Roslyn is ready. Then user can't work anyway, and waits until colored/folded, and the black text is annoying.

static class CodeInfo
{
	internal static readonly CiCompletion _compl = new();
	internal static readonly CiSignature _signature = new();
	internal static readonly CiAutocorrect _correct = new();
	internal static readonly CiQuickInfo _quickInfo = new();
	internal static readonly CiStyling _styling = new();
	internal static readonly CiErrors _diag = new();
	internal static readonly CiTools _tools = new();
	internal static readonly CiFavorite _favorite = new();

	static Solution _solution;
	static ProjectId _projectId;
	static DocumentId _documentId;
	static Document _document;
	static MetaComments _meta;
	static string _metaText;
	static bool _isWarm;
	static bool _isUI;
	static RECT _sciRect;

	public static void UiLoaded() {
		//warm up
		//Task.Delay(100).ContinueWith(_1 => {
		Task.Run(() => {
			//var p1 = perf.local();
			try {
				var code = @"//.
using Au; using Au.Types; using System; using System.Collections.Generic;
class Script { static void Main(string[] a) => new Script(a); Script(string[] args) {
print.it(""t"" + 'c' + 1);
}}";

				var refs = new MetaReferences().Refs;
				int position = code.IndexOf('}');
				ProjectId projectId = ProjectId.CreateNewId();
				DocumentId documentId = DocumentId.CreateNewId(projectId);
				using var ws = new AdhocWorkspace();
				var sol = ws.CurrentSolution
					.AddProject(projectId, "p", "p", LanguageNames.CSharp)
					.AddMetadataReferences(projectId, refs)
					.AddDocument(documentId, "f.cs", code);
				var document = sol.GetDocument(documentId);
				//p1.Next();
				//_ = document.GetSemanticModelAsync().Result;
				//p1.Next();
				App.Wmain.Dispatcher.InvokeAsync(() => {
					//perf.next('w');
					_isWarm = true;
					ReadyForStyling?.Invoke();
					Panels.Editor.ZActiveDocChanged += Stop;
					App.Timer025sWhenVisible += _Timer025sWhenVisible;
				});
				//p1.Next();
				//1000.ms();
				//p1.Next();
				//Compiler.Warmup(document); //don't need. Later fast enough. Now just uses more memory and CPU at startup.
				//p1.NW('w');
				//perf.nw();

				//EdUtil.MinimizeProcessPhysicalMemory(500); //with this later significantly slower
			}
			catch (Exception ex) {
				Debug_.Print(ex);
			}
		});
	}

	/// <summary>
	/// Code styling and folding already can work after program starts.
	/// </summary>
	public static bool IsReadyForStyling => _isWarm;

	/// <summary>
	/// When code styling and folding already can work after program starts.
	/// Runs in main thread.
	/// </summary>
	public static event Action ReadyForStyling;

	static bool _CanWork(SciCode doc) {
		if (!_isWarm) return false;
		if (doc == null) return false;
		if (!doc.ZFile.IsCodeFile) return false;
		if (doc != Panels.Editor.ZActiveDoc) { _Uncache(); return false; } //maybe changed an inactive file that participates in current compilation //FUTURE: what if isn't open?
		return true;
	}

	static void _Uncache() {
		//print.it("_Uncache");
		CurrentWorkspace = null;
		_solution = null;
		_projectId = null;
		_documentId = null;
		_document = null;
		_meta = null;
		_metaText = null;
	}

	public static void Stop() {
		Cancel();
		_Uncache();
	}

	public static void Cancel() {
		HideTextPopupAndTempWindows();
		_compl.Cancel();
		_signature.Cancel();
	}

	/// <summary>
	/// Called when files added, deleted, moved, copied, imported.
	/// Eg need to update styling when a meta c file became [un]available or when project folder structure changed.
	/// </summary>
	public static void FilesChanged() {
		Stop();
		_styling.Update();
	}

	public static void SciKillFocus(SciCode doc) {
		if (!_CanWork(doc)) return;
#if DEBUG
		if (Debugger.IsAttached) return;
#endif
		//hide code info windows, except when a code info window is focused. Code info window names start with "Ci.".
		var aw = wnd.thisThread.active;
		if (aw.Is0) Stop(); else if (!(KPopup.FromHwnd(aw) is KPopup p && p.Name.Starts("Ci."))) Cancel();
	}

	public static bool SciCmdKey(SciCode doc, KKey key, ModifierKeys mod) {
#if NO_COMPL_CORR_SIGN
		return false;
#endif
		if (!_CanWork(doc)) return false;
		switch ((key, mod)) {
		case (KKey.Space, ModifierKeys.Control):
			ShowCompletionList(doc);
			return true;
		case (KKey.Space, ModifierKeys.Control | ModifierKeys.Shift):
			ShowSignature(doc);
			return true;
		case (KKey.Escape, 0):
		case (KKey.Down, 0):
		case (KKey.Up, 0):
		case (KKey.PageDown, 0):
		case (KKey.PageUp, 0):
		case (KKey.Home, 0):
		case (KKey.End, 0):
			if ((HideTextPopup() || _tools.HideTempWindows()) && key == KKey.Escape) return true;
			//never mind: on Esc, if several popups, should hide the top popup.
			//	We instead hide less-priority popups when showing a popup, so that Escape will hide the correct popup in most cases.
			return _compl.OnCmdKey_SelectOrHide(key) || _signature.OnCmdKey(key);
		case (KKey.Tab, 0):
		case (KKey.Enter, 0):
			return _compl.OnCmdKey_Commit(doc, key) != CiComplResult.None || _correct.SciBeforeKey(doc, key, mod);
		case (KKey.Enter, ModifierKeys.Shift):
		case (KKey.Enter, ModifierKeys.Control):
		case (KKey.OemSemicolon, ModifierKeys.Control):
			var complResult = _compl.OnCmdKey_Commit(doc, key);
			//if(complResult == CiComplResult.Complex && mod==0) return true;
			return _correct.SciBeforeKey(doc, key, mod) | (complResult != CiComplResult.None);
		case (KKey.Back, 0):
		case (KKey.Delete, 0):
			return _correct.SciBeforeKey(doc, key, mod);
		}
		return false;
	}

	public static bool SciBeforeCharAdded(SciCode doc, char ch) {
#if NO_COMPL_CORR_SIGN
		return false;
#endif
		if (!_CanWork(doc)) return false;

		if (_correct.SciBeforeCharAdded(doc, ch, out var b)) {
			if (b == null) return true;

			if (_compl.IsVisibleUI) {
				int diff = b.newPosUtf8 - b.oldPosUtf8;
				_compl.SciCharAdding_Commit(doc, ch);
				b.newPosUtf8 = doc.zCurrentPos8 + diff;
			}

			doc.zCurrentPos8 = b.newPosUtf8;
			if (!b.dontSuppress) return true;
		} else if (_compl.IsVisibleUI) {
			if (CiComplResult.Complex == _compl.SciCharAdding_Commit(doc, ch)) return true;
		}
		return false;
	}

	public static void SciModified(SciCode doc, in Sci.SCNotification n) {
		if (!_CanWork(doc)) return;
		_document = null;
		_compl.SciModified(doc, in n);
		_styling.SciModified(doc, in n);
		_diag.SciModified(doc, in n);
	}

	public static void SciCharAdded(SciCode doc, char ch) {
#if NO_COMPL_CORR_SIGN
		return;
#endif
		if (!_CanWork(doc)) return;

		using var c = new CharContext(doc, ch);
		_correct.SciCharAdded(c); //sync adds or removes ')' etc if need.
		if (!c.ignoreChar) {
			_compl.SciCharAdded_ShowList(c); //async gets completions and shows popup list. If already showing, filters/selects items.
			_signature.SciCharAdded(c.doc, c.ch); //async shows signature help. Faster than _compl.
		}

		//Example: user types 'wri('.
		//	When typed 'w', _compl.SciCharAdded_ShowList shows popup list (async).
		//	While typing 'ri', _compl.SciModified in the list selects Write.
		//	When typed '(':
		//		_compl.SciCharAdded_Commit replaces 'wri(' with 'Write('. Caret is after '('.
		//		_correct adds ')'. Caret is still after '('.
		//		_signature shows signature help.
		//	If then user types 'tr)':
		//		_compl on 't' shows popup list and on ')' replaces 'tr)' with 'true)'.
		//		_correct deletes the ')' it added before.
		//		_signature not called because discardChar==true. To hide signature help are used temp ranges.
		//	Finally we have 'Write(true)', and caret is after it, and no double '))'.
		//	If instead types 'tr;':
		//		_correct on ';' moves caret after ')', and finally we have 'Write(true);', and caret after ';'.
	}

	public static void SciUpdateUI(SciCode doc, int updated) {
#if NO_COMPL_CORR_SIGN
		return;
#endif
		//print.it("SciUpdateUI", modified, _tempNoAutoComplete);
		if (!_CanWork(doc)) return;

		if (0 != (updated & 3)) { //text (1), selection/click (2)
			_compl.SciUpdateUI(doc);
			_signature.SciPositionChanged(doc);
		} else if (0 != (updated & 12)) { //scrolled
			Cancel();
			if (0 != (updated & 4)) { //vertically
									  //_styling.Timer250msWhenVisibleAndWarm(doc); //rejected. Uses much CPU. The 250 ms timer is OK.
			}
		}
	}

	public static void ShowCompletionList(SciCode doc) {
		if (!_CanWork(doc)) return;
		_compl.ShowList();
	}

	public static void ShowSignature(SciCode doc = null) {
		doc ??= Panels.Editor.ZActiveDoc;
		if (!_CanWork(doc)) return;
		_signature.ShowSignature(doc);
	}

	/// <summary>
	/// Shows or hides quick info or/and error info.
	/// </summary>
	public static async void SciMouseDwellStarted(SciCode doc, int pos8) {
		if (!_CanWork(doc) || pos8 < 0) return;

		var pi = Panels.Info; if (!pi.IsVisible) pi = null;

		var text0 = doc.zText;
		int pos16 = doc.zPos16(pos8);
		var diag = _diag.GetPopupTextAt(doc, pos8, pos16, out var onLinkClick);
		var quick = await _quickInfo.GetTextAt(pos16);
		if(doc != Panels.Editor.ZActiveDoc || (object)text0 != doc.zText) return; //changed while awaiting

		if (quick == null) pi?.ZSetAboutInfo();
		if (diag == null && quick == null) {
			HideTextPopup();
		} else {
			if (quick != null && pi != null) {
				pi.ZSetText(quick);
				if (diag == null) return;
				quick = null;
			}
			var text = diag ?? quick;
			if (quick != null && diag != null) {
				text.Blocks.Add(new System.Windows.Documents.BlockUIContainer(new System.Windows.Controls.Separator { Margin = new(4) }));
				text.Blocks.Add(quick);
			}
			_ShowTextPopup(doc, pos16, text, onLinkClick);
		}
	}

	public static void SciMouseDwellEnded(SciCode doc) {
		if (!_CanWork(doc)) return;
		//_diag.SciMouseDwellEnded(doc);
	}

	//public static void SciMouseMoved(SciCode doc, int x, int y)
	//{
	//	if(!_CanWork(doc)) return;
	//	_quickInfo.SciMouseMoved(x, y);
	//}

	/// <summary>
	/// Call this before pasting or inserting text when may need special processing, eg auto-inserting 'using' directives.
	/// </summary>
	public static void Pasting(SciCode doc) {
		if (!_CanWork(doc)) return;
		_diag.Pasting(doc);
	}

	public class Context
	{
		public Document document { get; private set; }
		public readonly SciCode sciDoc;
		public readonly string code;
		public readonly (int start, int end) meta;
		public int pos16;
		public readonly bool isCodeFile;

		/// <summary>
		/// Initializes all fields except document.
		/// For sciDoc uses Panels.Editor.ZActiveDoc.
		/// </summary>
		public Context(int pos) {
			Debug.Assert(Thread.CurrentThread.ManagedThreadId == 1);

			document = null;
			sciDoc = Panels.Editor.ZActiveDoc;
			code = sciDoc.zText;
			if (pos == -1) pos = sciDoc.zCurrentPos16; else if (pos == -2) pos = sciDoc.zSelectionStart16;
			pos16 = pos;
			if (isCodeFile = sciDoc.ZFile.IsCodeFile) meta = MetaComments.FindMetaComments(code);
		}

		/// <summary>
		/// Initializes the document field.
		/// Creates or updates Solution if need.
		/// Returns false if fails, unlikely.
		/// </summary>
		public bool GetDocument() {
			if (_document != null) {
				document = _document;
				return true;
			}

			if (_solution != null && !code.Eq(meta.start..meta.end, _metaText)) {
				_Uncache();
				_styling.Update();
			}
			if (_solution == null) _metaText = code[meta.start..meta.end];

			try {
				if (_solution == null) {
					_CreateSolution(sciDoc.ZFile);
				} else {
					_solution = _solution.WithDocumentText(_documentId, SourceText.From(code, Encoding.UTF8));
				}
			}
			catch (Exception ex) {
				Debug_.Print(ex);
				return false;
			}

			document = _document = _solution.GetDocument(_documentId);
			if (document == null) return false;

			_ModifySource();

			return true;
		}

		//public bool GetDocumentAndSyntaxRoot(out SyntaxNode root)
		//{
		//	if(!GetDocument()) { root = null; return false; }
		//	root = document.GetSyntaxRootAsync().Result;
		//	return true;
		//}

		//public bool GetDocumentAndFindNode(out SyntaxNode node)
		//{
		//	if(!GetDocument()) { node = null; return false; }
		//	node = document.GetSyntaxRootAsync().Result.FindToken(position).Parent;
		//	return true;
		//}

		//TODO
		void _ModifySource() {
			//var r = document.GetSyntaxRootAsync().Result as CompilationUnitSyntax;

			//print.it("Externs:", r.Externs);
			//print.it("Usings:", r.Usings);
			//print.it("Members:", r.Members);
			//print.it("AttributeLists:", r.AttributeLists);

			//var st = SyntaxFactory.ParseCompilationUnit("using Microsoft.Win32;\n");
			//root = root.AddUsings(st.ChildNodes().Cast<UsingDirectiveSyntax>().ToArray());
			//_document = document = document.WithSyntaxRoot(root);

			//return;

			//SyntaxNode lastUsing = null;

			//foreach (var v in root.ChildNodes()) {
			//	switch (v) {
			//	case ExternAliasDirectiveSyntax ea:
			//		lastUsing = ea;
			//		break;
			//	case UsingDirectiveSyntax u:
			//		lastUsing = u;
			//		if(u.Alias == null && u.StaticKeyword.RawKind == 0) {
			//			var span = u.Name.Span;
			//			var s = code[span.Start..span.End];
			//			//print.it(s);

			//		}
			//		//perf.first();
			//		//perf.next();
			//		//perf.nw();
			//		break;
			//	//case AttributeListSyntax als when als.Target.Identifier.Text is "module" or "assembly":
			//	//	break;
			//	//case GlobalStatementSyntax:
			//	//	if (start < 0) start = v.FullSpan.Start;
			//	//	break;
			//	default:
			//		//CiUtil.PrintNode(v);
			//		//end = v.FullSpan.Start;
			//		goto g1;
			//	}
			//}
			//g1:;
			//if (lastUsing != null) {
			//	var st = SyntaxFactory.ParseCompilationUnit("using Microsoft.Win32;\n");
			//	root = root.InsertNodesAfter(lastUsing, st.ChildNodes());
			//	_document = document = document.WithSyntaxRoot(root);
			//}
		}
	}

	/// <summary>
	/// Creates new Context and calls its GetDocument.
	/// Returns false if: 1. Not a code file; 2. position is in meta comments (unless metaToo==true); 3. Fails to create/update Solution (unlikely). Then r.document is null.
	/// If returns true, r.document is Document for Panels.Editor.ActiveDoc. If need, parses meta, creates Project, Solution, etc.
	/// Always sets other r fields.
	/// </summary>
	/// <param name="position">If -1, gets current position. If -2, gets selection start.</param>
	/// <param name="metaToo">Don't return false if position is in meta comments.</param>
	public static bool GetContextAndDocument(out Context r, int position = -1, bool metaToo = false) {
		if (!GetContextWithoutDocument(out r, position, metaToo)) return false;
		return r.GetDocument();
	}

	/// <summary>
	/// Creates new Context with document=null. Even if returns false.
	/// Returns false if: 1. Not a code file; 2. position is in meta comments (unless metaToo==true).
	/// </summary>
	/// <param name="position">If -1, gets current position. If -2, gets selection start.</param>
	/// <param name="metaToo">Don't return false if position is in meta comments.</param>
	public static bool GetContextWithoutDocument(out Context r, int position = -1, bool metaToo = false) {
		r = new Context(position);
		if (!r.isCodeFile) return false;
		if (!metaToo && r.pos16 < r.meta.end && r.pos16 > r.meta.start) return false;
		return true;
	}

	/// <summary>
	/// Calls <see cref="GetContextAndDocument"/>, gets its syntax root and finds node.
	/// </summary>
	/// <param name="position">If -1, gets current position. If -2, gets selection start.</param>
	/// <param name="metaToo">Don't return false if position is in meta comments.</param>
	public static bool GetDocumentAndFindNode(out Context r, out SyntaxNode node, int position = -1, bool metaToo = false) {
		if (!GetContextAndDocument(out r, position, metaToo)) { node = null; return false; }
		node = r.document.GetSyntaxRootAsync().Result.FindToken(r.pos16).Parent;
		return true;
	}

	public static Workspace CurrentWorkspace { get; private set; }

	public static MetaComments Meta => _meta;

	static void _CreateSolution(FileNode f) {
		_diag.ClearMetaErrors();
		Au.Compiler.InternalsVisible.Clear();
		CurrentWorkspace = new AdhocWorkspace();
		_solution = CurrentWorkspace.CurrentSolution;
		_projectId = _AddProject(f, true);

		static ProjectId _AddProject(FileNode f, bool isMain) {
			var f0 = f;
			if (f.FindProject(out var projFolder, out var projMain)) f = projMain;

			var m = new MetaComments();
			m.Parse(f, projFolder, EMPFlags.ForCodeInfo);
			if (isMain) _meta = m;
			if (m.TestInternal is string[] testInternal) Au.Compiler.InternalsVisible.Add(f.Name, testInternal);

			var projectId = ProjectId.CreateNewId();
			var adi = new List<DocumentInfo>();
			foreach (var f1 in m.CodeFiles) {
				var docId = DocumentId.CreateNewId(projectId);
				var tav = TextAndVersion.Create(SourceText.From(f1.code, Encoding.UTF8), VersionStamp.Default, f1.f.FilePath);
				adi.Add(DocumentInfo.Create(docId, f1.f.Name, null, SourceCodeKind.Regular, TextLoader.From(tav), f1.f.ItemPath));
				if (f1.f == f0 && isMain) {
					_documentId = docId;
				}
			}

			var pi = ProjectInfo.Create(projectId, VersionStamp.Default, f.Name, f.Name, LanguageNames.CSharp, null, null,
				m.CreateCompilationOptions(),
				m.CreateParseOptions(),
				adi,
				m.ProjectReferences?.Select(f1 => new ProjectReference(_AddProject(f1, false))),
				m.References.Refs);

			_solution = _solution.AddProject(pi);
			//info: does not add to CurrentWorkspace.CurrentSolution. Now _solution != CurrentWorkspace.CurrentSolution. Even after Workspace.ApplyChanges.

			return projectId;
		}
	}

	private static void _Timer025sWhenVisible() {
		var doc = Panels.Editor.ZActiveDoc;
		if (doc == null || !doc.ZFile.IsCodeFile) return;

		//cancel if changed the screen rectangle of the document window
		if (_compl.IsVisibleUI || _signature.IsVisibleUI || _tpVisible) {
			var r = Panels.Editor.ZActiveDoc.Hwnd().Rect;
			if (!_isUI) {
				_isUI = true;
				_sciRect = r;
			} else if (r != _sciRect) { //moved/resized top-level window or eg moved some splitter
				_isUI = false;
				Cancel();
			}
		} else if (_isUI) {
			_isUI = false;
		}

		_styling.Timer250msWhenVisibleAndWarm(doc);
	}

	static CiPopupText _textPopup;
	static bool _tpVisible;

	static void _ShowTextPopup(SciCode doc, int pos16, System.Windows.Documents.Section text, Action<CiPopupText, string> onLinkClick = null) {
		_textPopup ??= new CiPopupText(CiPopupText.UsedBy.Info, onHiddenOrDestroyed: (_, _) => _tpVisible = false);
		_textPopup.Text = text;
		_textPopup.OnLinkClick = onLinkClick;
		_textPopup.Show(doc, pos16, hideIfOutside: true);
		_tpVisible = true;
	}

	//CONSIDER: option to show tooltip: below mouse (like now), above mouse, top/bottom (which is farther), maybe above Output etc.
	//	This test version shows above Output.
	//static void _ShowTextPopup(SciCode doc, int pos16, System.Windows.Documents.Section text, Action<CiPopupText, string> onLinkClick = null) {
	//	_textPopup ??= new CiPopupText(CiPopupText.UsedBy.Info, onHiddenOrDestroyed: (_, _) => _tpVisible = false);
	//	_textPopup.Text = text;
	//	_textPopup.OnLinkClick = onLinkClick;
	//	if (keys.isScrollLock && Panels.Output.IsVisible) {
	//		var r = Panels.Output.RectInScreen();
	//		_textPopup.Show(doc, r, null);
	//	} else {
	//		_textPopup.Show(doc, pos16, hideIfOutside: true);
	//	}
	//	_tpVisible = true;
	//}

	internal static bool HideTextPopup() {
		if (_tpVisible) { _textPopup.Hide(); return true; }
		return false;
	}

	internal static void HideTextPopupAndTempWindows() {
		HideTextPopup();
		_tools.HideTempWindows();
	}

	public class CharContext : IDisposable
	{
		public readonly SciCode doc;
		public char ch;
		public bool ignoreChar;
		//bool _undoStarted;

		public CharContext(SciCode doc, char ch) {
			this.doc = doc;
			this.ch = ch;
		}

		//public void BeginUndoAction()
		//{
		//	if(!_undoStarted) {
		//		_undoStarted = true;
		//		doc.Call(Sci.SCI_BEGINUNDOACTION);
		//	}
		//}

		public void Dispose() {
			//if(_undoStarted) {
			//	_undoStarted = false;
			//	doc.Call(Sci.SCI_ENDUNDOACTION);
			//}
		}
	}

	//public static void Test()
	//{

	//}
}
