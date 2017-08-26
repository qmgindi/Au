﻿using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

using Catkeys;
using static Catkeys.NoClass;

[module: DefaultCharSet(CharSet.Unicode)]

namespace G.Controls
{
	internal static class _Api
	{




		//[DllImport("user32.dll")]
		//public static extern IntPtr GetWindowDC(Wnd hWnd);
		//[DllImport("user32.dll")]
		//public static extern int ReleaseDC(Wnd hWnd, IntPtr hDC);

		[DllImport("gdi32.dll")]
		public static extern IntPtr CreateSolidBrush(uint color);

	}

}