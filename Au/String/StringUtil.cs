using Au.Types;
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

namespace Au.More
{
	/// <summary>
	/// Miscellaneous rarely used string functions. Parsing etc.
	/// </summary>
	public static class StringUtil
	{
		/// <summary>
		/// Parses a function parameter that can optionally have a "***name " prefix, like "***id 100".
		/// Returns: 0 - s does not start with "***"; i+1 - s starts with "***names[i] "; -1 - s is invalid.
		/// </summary>
		/// <param name="s">Parameter. If starts with "***" and is valid, receives the 'value' part; else unchanged. Can be null.</param>
		/// <param name="names">List of supported 'name'.</param>
		/// <remarks>
		/// Used to parse parameters like <i>name</i> of <see cref="wnd.Child"/>.
		/// </remarks>
		internal static int ParseParam3Stars(ref string s, params string[] names) {
			if (s == null || !s.Starts("***")) return 0;
			for (int i = 0; i < names.Length; i++) {
				var ni = names[i];
				if (s.Length - 3 <= ni.Length || !s.Eq(3, ni)) continue;
				int j = 3 + ni.Length;
				char c = s[j]; if (c != ' ') break;
				s = s[(j + 1)..];
				return i + 1;
			}
			return -1;
		}

		/// <summary>
		/// Removes characters used to underline next character when the text is displayed in UI. Replaces two such caracters with single.
		/// </summary>
		/// <param name="s">Can be null.</param>
		/// <param name="underlineChar"></param>
		/// <remarks>
		/// Character '&amp;' (in WPF '_') is used to underline next character in displayed text of dialog controls and menu items. Two such characters are used to display single.
		/// The underline is displayed when using the keyboard with Alt key to select dialog controls and menu items.
		/// </remarks>
		[SkipLocalsInit]
		public static unsafe string RemoveUnderlineChar(string s, char underlineChar = '&') {
			if (s != null && s.Contains(underlineChar)) {
				using FastBuffer<char> b = new(s.Length);
				int j = 0; bool was = false;
				for (int i = 0; i < s.Length; i++) {
					if (s[i] == underlineChar) {
						if (i < s.Length - 1 && s[i + 1] == underlineChar) i++;
						else if (!was) { was = underlineChar == '_'; continue; } //WPF removes only first single _
					}
					b.p[j++] = s[i];
				}
				s = new string(b.p, 0, j);
			}
			return s;
		}

		/// <summary>
		/// Finds character used to underline next character when the text is displayed in UI.
		/// Returns its index, or -1 if not found.
		/// </summary>
		/// <param name="s">Can be null.</param>
		/// <param name="underlineChar"></param>
		public static int FindUnderlineChar(string s, char underlineChar = '&') {
			if (s != null) {
				for (int i = 0; i < s.Length; i++) {
					if (s[i] == underlineChar) {
						if (++i < s.Length && s[i] != underlineChar) return i;
					}
				}
			}
			return -1;
		}

		/// <summary>
		/// Converts array of command line arguments to string that can be passed to a "start process" function, for example <see cref="run.it"/>, <see cref="Process.Start"/>.
		/// Returns null if a is null or has 0 elements.
		/// </summary>
		/// <param name="a"></param>
		public static string CommandLineFromArray(string[] a) {
			if (a == null || a.Length == 0) return null;
			StringBuilder b = null;
			foreach (var v in a) {
				int esc = 0;
				if (v.NE()) esc = 1; else if (v.Contains('\"')) esc = 2; else foreach (var c in v) if (c <= ' ') { esc = 1; break; }
				if (esc == 0 && a.Length == 1) return a[0];
				if (b == null) b = new StringBuilder(); else b.Append(' ');
				if (esc == 0) b.Append(v);
				else {
					b.Append('\"');
					var s = v;
					if (esc == 2) {
						if (s.Find(@"\""") < 0) s = s.Replace(@"""", @"\""");
						else s = s.RegexReplace(@"(\\*)""", @"$1$1\""");
					}
					if (s.Ends('\\')) s = s.RegexReplace(@"(\\+)$", "$1$1");
					b.Append(s).Append('\"');
				}
			}
			return b.ToString();
		}

		/// <summary>
		/// Parses command line arguments.
		/// Calls API <msdn>CommandLineToArgvW</msdn>.
		/// Returns empty array if s is null or "".
		/// </summary>
		public static unsafe string[] CommandLineToArray(string s) {
			if (s.NE()) return Array.Empty<string>();
			char** p = Api.CommandLineToArgvW(s, out int n);
			var a = new string[n];
			for (int i = 0; i < n; i++) a[i] = new string(p[i]);
			Api.LocalFree(p);
			return a;
		}

		/// <summary>
		/// If string contains a number at startIndex, gets that number as int, also gets the string part that follows it, and returns true.
		/// For example, for string "25text" or "25 text" gets num = 25, tail = "text".
		/// Everything else is the same as with <see cref="ExtString.ToInt(string, int, out int, STIFlags)"/>.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="num">Receives the number. Receives 0 if no number.</param>
		/// <param name="tail">Receives the string part that follows the number, or "". Receives null if no number. Can be this variable.</param>
		/// <param name="startIndex">Offset in this string where to start parsing.</param>
		/// <param name="flags"></param>
		public static bool ParseIntAndString(string s, out int num, out string tail, int startIndex = 0, STIFlags flags = 0) {
			num = s.ToInt(startIndex, out int end, flags);
			if (end == 0) {
				tail = null;
				return false;
			}
			if (end < s.Length && s[end] == ' ') end++;
			tail = s[end..];
			return true;
		}

		/// <summary>
		/// Creates int[] from string containing space-separated numbers, like "4 100 -8 0x10".
		/// </summary>
		/// <param name="s">Decimal or/and hexadecimal numbers separated by single space. If null or "", returns empty array.</param>
		/// <remarks>
		/// For vice versa use <c>string.Join(" ", array)</c>.
		/// </remarks>
		public static int[] StringToIntArray(string s) {
			if (s.NE()) return Array.Empty<int>();
			int n = 1; foreach (var v in s) if (v == ' ') n++;
			var a = new int[n];
			a[0] = s.ToInt(0, STIFlags.DontSkipSpaces);
			for (int i = 0, j = 0; j < s.Length;) if (s[j++] == ' ') a[++i] = s.ToInt(j, STIFlags.DontSkipSpaces);
			return a;
		}

		/// <summary>
		/// Converts character index in string to line index and character index in that line.
		/// </summary>
		/// <param name="s"></param>
		/// <param name="index">Character index in string <i>s</i>.</param>
		/// <param name="lineIndex">Receives 0-based line index.</param>
		/// <param name="indexInLine">Receives 0-based character index in that line.</param>
		/// <exception cref="ArgumentOutOfRangeException"></exception>
		public static void LineAndColumn(string s, int index, out int lineIndex, out int indexInLine) {
			if ((uint)index > s.Length) throw new ArgumentOutOfRangeException();
			int line = 0, lineStart = 0;
			for (int i = 0; i < index; i++) {
				char c = s[i];
				if (c > '\r') continue;
				if (c != '\n') {
					if (c != '\r') continue;
					if (i < s.Length - 1 && s[i + 1] == '\n') continue;
				}

				lineStart = i + 1;
				line++;
			}
			lineIndex = line;
			indexInLine = index - lineStart;
		}

		/// <summary>
		/// Calculates the Levenshtein distance between two strings, which tells how much they are different.
		/// </summary>
		/// <remarks>
		/// It is the number of character edits (removals, inserts, replacements) that must occur to get from string s1 to string s2.
		/// Can be used to measure similarity and match approximate strings with fuzzy logic.
		/// Uses code and info from <see href="https://www.dotnetperls.com/levenshtein"/>.
		/// </remarks>
		public static int LevenshteinDistance(string s1, string s2) {
			int n = s1.Length;
			int m = s2.Length;

			// Step 1
			if (n == 0) return m;
			if (m == 0) return n;

			// Step 2
			int[,] d = new int[n + 1, m + 1];
			for (int i = 0; i <= n; d[i, 0] = i++) { }
			for (int j = 0; j <= m; d[0, j] = j++) { }

			// Step 3
			for (int i = 1; i <= n; i++) {
				//Step 4
				for (int j = 1; j <= m; j++) {
					// Step 5
					int cost = (s2[j - 1] == s1[i - 1]) ? 0 : 1;

					// Step 6
					d[i, j] = Math.Min(
						Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
						d[i - 1, j - 1] + cost);
				}
			}
			// Step 7
			return d[n, m];
		}

		/// <summary>
		/// Returns the number of characters common to the start of each string.
		/// </summary>
		public static int CommonPrefix(string s1, string s2) {
			int n = Math.Min(s1.Length, s2.Length);
			for (int i = 0; i < n; i++) {
				if (s1[i] != s2[i]) return i;
			}
			return n;
		}

		/// <summary>
		/// Returns the number of characters common to the end of each string.
		/// </summary>
		public static int CommonSuffix(string s1, string s2) {
			int len1 = s1.Length;
			int len2 = s2.Length;
			int n = Math.Min(len1, len2);
			for (int i = 1; i <= n; i++) {
				if (s1[len1 - i] != s2[len2 - i]) return i - 1;
			}
			return n;
		}
	}
}
