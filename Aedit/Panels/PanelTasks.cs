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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Au.Util;
//using System.Linq;

class PanelTasks : DockPanel
{
	AuTreeView _tv;
	bool _updatedOnce;

	public PanelTasks() {
		_tv = new AuTreeView { Name = "Tasks_list", ImageCache = App.ImageCache };
		this.Children.Add(_tv);
	}

	public void ZUpdateList() {
		_tv.SetItems(App.Tasks.Items, _updatedOnce);
		if (!_updatedOnce) {
			_updatedOnce = true;
			FilesModel.NeedRedraw += v => { _tv.Redraw(v.remeasure); };
			_tv.ItemClick += _tv_ItemClick;
		}
	}

	private void _tv_ItemClick(object sender, TVItemEventArgs e) {
		if (e.ModifierKeys != 0 || e.ClickCount != 1) return;
		var t = e.Item as RunningTask;
		var f = t.f;
		switch (e.MouseButton) {
		case MouseButton.Left:
			App.Model.SetCurrentFile(f);
			break;
		case MouseButton.Right:
			_tv.Select(t);
			var name = f.DisplayName;
			var m = new ClassicMenu_();
			m.Add(1, "End task  " + name);
			m.Add(2, "End all  " + name);
			m.Separator();
			m.Add(3, "Close\tM-click", disable: null == Panels.Editor.ZGetOpenDocOf(f)); 
			switch (m.Show(_tv)) {
			case 1: App.Tasks.EndTask(t); break;
			case 2: App.Tasks.EndTasksOf(f); break;
			case 3: App.Model.CloseFile(f, selectOther: true); break;
			}
			break;
		case MouseButton.Middle:
			App.Model.CloseFile(f, selectOther: true);
			break;
		}
	}
}