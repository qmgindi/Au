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
using System.Linq;
using System.Xml.Linq;

using static Au.NoClass;

namespace Au.Types
{
	/// <summary>
	/// Extends <see cref="XElement"/> class.
	/// </summary>
	public static class XElement_
	{
		/// <summary>
		/// Gets XML attribute value.
		/// If the attribute does not exist, returns null.
		/// If the attribute value is empty, returns "".
		/// </summary>
		public static string Attribute_(this XElement t, XName name)
		{
			return t.Attribute(name)?.Value;
		}

		/// <summary>
		/// Gets XML attribute value.
		/// If the attribute does not exist, returns defaultValue.
		/// If the attribute value is empty, returns "".
		/// </summary>
		public static string Attribute_(this XElement t, XName name, string defaultValue)
		{
			var x = t.Attribute(name);
			return x != null ? x.Value : defaultValue;
		}

		/// <summary>
		/// Gets XML attribute value.
		/// If the attribute does not exist, sets value=null and returns false.
		/// </summary>
		public static bool Attribute_(this XElement t, out string value, XName name)
		{
			value = t.Attribute(name)?.Value;
			return value != null;
		}

		/// <summary>
		/// Gets attribute value converted to int (<see cref="String_.ToInt_(string)"/>).
		/// If the attribute does not exist, returns defaultValue.
		/// If the attribute value is empty or does not begin with a valid number, returns 0.
		/// </summary>
		public static int Attribute_(this XElement t, XName name, int defaultValue)
		{
			var x = t.Attribute(name);
			return x != null ? x.Value.ToInt_() : defaultValue;
		}

		/// <summary>
		/// Gets attribute value converted to int (<see cref="String_.ToInt_(string)"/>).
		/// If the attribute does not exist, sets value=0 and returns false.
		/// If the attribute value is empty or does not begin with a valid number, sets value=0 and returns true.
		/// </summary>
		public static bool Attribute_(this XElement t, out int value, XName name)
		{
			var x = t.Attribute(name);
			if(x == null) { value = 0; return false; }
			value = x.Value.ToInt_();
			return true;
		}

		/// <summary>
		/// Gets attribute value converted to long (<see cref="String_.ToLong_(string)"/>).
		/// If the attribute does not exist, sets value=0 and returns false.
		/// If the attribute value is empty or does not begin with a valid number, sets value=0 and returns true.
		/// </summary>
		public static bool Attribute_(this XElement t, out long value, XName name)
		{
			var x = t.Attribute(name);
			if(x == null) { value = 0; return false; }
			value = x.Value.ToLong_();
			return true;
		}

		/// <summary>
		/// Gets attribute value converted to float (<see cref="String_.ToFloat_"/>).
		/// If the attribute does not exist, sets value=0F and returns false.
		/// If the attribute value is empty or is not a valid number, sets value=0F and returns true.
		/// </summary>
		public static bool Attribute_(this XElement t, out float value, XName name)
		{
			var x = t.Attribute(name);
			if(x == null) { value = 0F; return false; }
			value = x.Value.ToFloat_();
			return true;
		}

		/// <summary>
		/// Returns true if this element has the specified attribute.
		/// </summary>
		public static bool HasAttribute_(this XElement t, XName name)
		{
			return t.Attribute(name) != null;
		}

		/// <summary>
		/// Gets the first found descendant element.
		/// Returns null if not found.
		/// </summary>
		public static XElement Descendant_(this XElement t, XName name)
		{
			return t.Descendants(name).FirstOrDefault();
		}

		/// <summary>
		/// Finds the first descendant element that has the specified attribute.
		/// Returns null if not found.
		/// </summary>
		/// <param name="t"></param>
		/// <param name="name">Element name. If null, can be any name.</param>
		/// <param name="attributeName">Attribute name.</param>
		/// <param name="attributeValue">Attribute value. If null, can be any value.</param>
		/// <param name="ignoreCase">Case-insensitive attributeValue.</param>
		public static XElement Descendant_(this XElement t, XName name, XName attributeName, string attributeValue = null, bool ignoreCase = false)
		{
			foreach(var el in (name != null) ? t.Descendants(name) : t.Descendants()) {
				var a = el.Attribute(attributeName); if(a == null) continue;
				if(attributeValue != null && !a.Value.Equals_(attributeValue, ignoreCase)) continue;
				return el;
			}
			return null;

			//speed: several times faster than XPathSelectElement
		}

		/// <summary>
		/// Finds all descendant elements that have the specified attribute.
		/// Returns null if not found.
		/// </summary>
		/// <param name="t"></param>
		/// <param name="name">Element name. If null, can be any name.</param>
		/// <param name="attributeName">Attribute name.</param>
		/// <param name="attributeValue">Attribute value. If null, can be any value.</param>
		/// <param name="ignoreCase">Case-insensitive attributeValue.</param>
		public static IEnumerable<XElement> Descendants_(this XElement t, XName name, XName attributeName, string attributeValue = null, bool ignoreCase = false)
		{
			foreach(var el in (name != null) ? t.Descendants(name) : t.Descendants()) {
				var a = el.Attribute(attributeName); if(a == null) continue;
				if(attributeValue != null && !a.Value.Equals_(attributeValue, ignoreCase)) continue;
				yield return el;
			}
		}

		/// <summary>
		/// Gets the first found direct child element that has the specified attribute.
		/// Returns null if not found.
		/// </summary>
		/// <param name="t"></param>
		/// <param name="name">Element name. If null, can be any name.</param>
		/// <param name="attributeName">Attribute name.</param>
		/// <param name="attributeValue">Attribute value. If null, can be any value.</param>
		/// <param name="ignoreCase">Case-insensitive attributeValue.</param>
		public static XElement Element_(this XElement t, XName name, XName attributeName, string attributeValue = null, bool ignoreCase = false)
		{
			foreach(var el in (name != null) ? t.Elements(name) : t.Elements()) {
				var a = el.Attribute(attributeName); if(a == null) continue;
				if(attributeValue != null && !a.Value.Equals_(attributeValue, ignoreCase)) continue;
				return el;
			}
			return null;
		}

		/// <summary>
		/// Gets previous sibling element.
		/// Returns null if no element.
		/// </summary>
		public static XElement PreviousElement_(this XElement t)
		{
			for(XNode n = t.PreviousNode; n != null; n = n.PreviousNode) {
				if(n is XElement e) return e;
			}
			return null;
		}

		/// <summary>
		/// Gets next sibling element.
		/// Returns null if no element.
		/// </summary>
		public static XElement NextElement_(this XElement t)
		{
			for(XNode n = t.NextNode; n != null; n = n.NextNode) {
				if(n is XElement e) return e;
			}
			return null;
		}

		/// <summary>
		/// Loads XML file in a safer way.
		/// Uses <see cref="XElement.Load"/> and <see cref="File_.WaitIfLocked"/>.
		/// </summary>
		/// <param name="file">File. Must be full path. Can contain environment variables etc, see <see cref="Path_.ExpandEnvVar"/>.</param>
		/// <param name="options"></param>
		/// <exception cref="ArgumentException">Not full path.</exception>
		/// <exception cref="Exception">Exceptions of <see cref="XElement.Load"/>.</exception>
		public static XElement Load(string file, LoadOptions options = default)
		{
			file = Path_.LibNormalizeForNET(file);
			return File_.WaitIfLocked(() => XElement.Load(file, options));
		}

		/// <inheritdoc cref="File_.Save"/>
		/// <summary>
		/// Saves XML to a file in a safer way.
		/// Uses <see cref="XElement.Save(string, SaveOptions)"/> and <see cref="File_.Save"/>.
		/// </summary>
		/// <exception cref="Exception">Exceptions of <see cref="XElement.Save"/> and <see cref="File_.Save"/>.</exception>
		public static void Save_(this XElement t, string file, bool backup = false, SaveOptions? options = default)
		{
			File_.Save(file, temp =>
			{
				if(options.HasValue) t.Save(temp, options.GetValueOrDefault()); else t.Save(temp);
			}, backup);
		}
	}

	/// <summary>
	/// Extends <see cref="XDocument"/> class.
	/// </summary>
	public static class XDocument_
	{
		/// <summary>
		/// Loads XML file in a safer way.
		/// Uses <see cref="XDocument.Load"/> and <see cref="File_.WaitIfLocked"/>.
		/// </summary>
		/// <param name="file">File. Must be full path. Can contain environment variables etc, see <see cref="Path_.ExpandEnvVar"/>.</param>
		/// <param name="options"></param>
		/// <exception cref="ArgumentException">Not full path.</exception>
		/// <exception cref="Exception">Exceptions of <see cref="XDocument.Load"/>.</exception>
		public static XDocument Load(string file, LoadOptions options = default)
		{
			file = Path_.LibNormalizeForNET(file);
			return File_.WaitIfLocked(() => XDocument.Load(file, options));
		}

		/// <inheritdoc cref="File_.Save"/>
		/// <summary>
		/// Saves XML to a file in a safer way.
		/// Uses <see cref="XDocument.Save(string)"/> and <see cref="File_.Save"/>
		/// </summary>
		/// <exception cref="Exception">Exceptions of <see cref="XDocument.Save"/> and <see cref="File_.Save"/>.</exception>
		public static void Save_(this XDocument t, string file, bool backup = false, SaveOptions? options = default)
		{
			File_.Save(file, temp =>
			{
				if(options.HasValue) t.Save(temp, options.GetValueOrDefault()); else t.Save(temp);
			}, backup);
		}
	}
}