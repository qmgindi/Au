using Au;
using Au.Types;
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
using System.Linq;
using System.Windows.Input;
using System.Windows;

partial class FilesModel
{
	public class FilesView : AuTreeView
	{
		public FilesView() {
			MultiSelect = true;
			AllowDrop = true;
			ImageCache = App.ImageCache;

			ItemActivated += _ItemActivated;
			ItemClick += _ItemClick;

			ItemDragStart += _ItemDragStart;

			FilesModel.NeedRedraw += v => { if (v.f != null) Redraw(v.f, v.remeasure); else Redraw(v.remeasure); };

			App.Commands.BindKeysTarget(this, "Files");
		}

		public void SetItems() {
			base.SetItems(App.Model.Root.Children(), false);
		}

		private void _ItemActivated(object sender, TVItemEventArgs e) {
			var f = e.Item as FileNode;
			if (!f.IsFolder) App.Model._SetCurrentFile(f);
		}

		private void _ItemClick(object sender, TVItemEventArgs e) {
			if (e.ModifierKeys != 0) return;
			var f = e.Item as FileNode;
			switch (e.MouseButton) {
			case MouseButton.Right:
				Dispatcher.InvokeAsync(() => App.Model._ItemRightClicked(f));
				break;
			case MouseButton.Middle:
				if (!f.IsFolder) App.Model.CloseFile(f);
				break;
			}
		}

		protected override void OnKeyDown(KeyEventArgs e) {
			base.OnKeyDown(e);
			var m = App.Model;
			switch ((e.KeyboardDevice.Modifiers, e.Key)) {
			case (0, Key.Enter): m.OpenSelected(1); break;
			case (0, Key.Delete): m.DeleteSelected(); break;
			case (ModifierKeys.Control, Key.X): m.CutCopySelected(true); break;
			case (ModifierKeys.Control, Key.C): m.CutCopySelected(false); break;
			case (ModifierKeys.Control, Key.V): m.Paste(); break;
			case (0, Key.Escape): m.Uncut(); break;
			default: return;
			}
			e.Handled = true;
		}

		public new FileNode[] SelectedItems => base.SelectedItems.Cast<FileNode>().ToArray();

		#region drag-drop

		private void _ItemDragStart(object sender, TVItemEventArgs e) {
			//if(e.Item.IsFolder && e.Item.IsExpanded) Expand(e.Index, false);
			var a = IsSelected(e.Index) ? SelectedItems : new FileNode[] { e.Item as FileNode };
			DragDrop.DoDragDrop(this, new DataObject(typeof(FileNode[]), a), DragDropEffects.Move | DragDropEffects.Copy);
		}

		protected override void OnDragOver(DragEventArgs e) {
			e.Handled = true;
			bool can = _DragDrop(e, false);
			OnDragOver2(can);
			base.OnDragOver(e);
		}

		protected override void OnDrop(DragEventArgs e) {
			e.Handled = true;
			_DragDrop(e, true);
			base.OnDrop(e);
		}

		bool _DragDrop(DragEventArgs e, bool drop) {
			bool can;
			FileNode[] nodes = null;
			if (can = e.Data.GetDataPresent(typeof(FileNode[]))) {
				nodes = e.Data.GetData(typeof(FileNode[])) as FileNode[];
				GetDropInfo(out var d);
				if (d.targetItem is FileNode target) {
					bool no = false;
					foreach (FileNode v in nodes) {
						if (d.intoFolder) {
							no = v == target;
						} else {
							no = target.IsDescendantOf(v);
						}
						if (no) break;
					}
					can = !no;
				}
			} else {
				can = e.Data.GetDataPresent(DataFormats.FileDrop);
			}

			if (can) {
				//convert multiple effects to single
				switch (e.KeyStates & (DragDropKeyStates.ControlKey | DragDropKeyStates.ShiftKey)) {
				case DragDropKeyStates.ControlKey: e.Effects &= DragDropEffects.Copy; break;
				case DragDropKeyStates.ShiftKey: e.Effects &= DragDropEffects.Move; break;
				case DragDropKeyStates.ControlKey | DragDropKeyStates.ShiftKey: e.Effects &= DragDropEffects.Link; break;
				default:
					if (e.Effects.Has(DragDropEffects.Move)) e.Effects = DragDropEffects.Move;
					else if (e.Effects.Has(DragDropEffects.Link)) e.Effects = DragDropEffects.Link;
					else if (e.Effects.Has(DragDropEffects.Copy)) e.Effects = DragDropEffects.Copy;
					else e.Effects = 0;
					break;
				}
			} else {
				e.Effects = 0;
			}
			if (e.Effects == 0) return false;

			if (drop) {
				var files = nodes == null ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
				GetDropInfo(out var d);
				var pos = d.intoFolder ? FNPosition.Inside : (d.insertAfter ? FNPosition.After : FNPosition.Before);
				App.Model._DroppedOrPasted(nodes, files, e.Effects == DragDropEffects.Copy, d.targetItem as FileNode, pos);
			}
			return true;
		}

		#endregion
	}
}