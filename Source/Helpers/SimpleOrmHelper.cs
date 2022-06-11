using System.Collections;
using System.Data.Common;
using System.Reflection;
using SimpleOrm.Enums;
using SimpleOrm.Models;

namespace SimpleOrm.Helpers;

internal static class SimpleOrmHelper
{
	public static PropertyHierarchy BuildAndCheckHierarchy<T>(IReadOnlyCollection<DbColumn> dbColumns, ushort maxDepth)
				where T : new()
	{
		var parent = new PropertyHierarchy(typeof(T));
		parent.Children = parent.Type.GetProperties().BuildHierarchy(parent, dbColumns, maxDepth);
		IList<string> errors = parent.Children.CheckHierarchy();
		if (errors.Any())
		{
			throw new Exception(String.Join('\n', errors));
		}
		return parent;
	}

	private static List<string> CheckHierarchy(this List<PropertyHierarchy> properties)
	{
		var res = new List<string>();
		foreach (PropertyHierarchy prop in properties)
		{
			if (prop.Children.Any())
			{
				res.AddRange(prop.Children.CheckHierarchy());
			}
			else if (prop.Error != null)
			{
				res.Add(prop.Error);
			}
		}
		return res;
	}

	private static List<PropertyHierarchy> BuildHierarchy(this IEnumerable<PropertyInfo> propertyInfo,
	                                                      PropertyHierarchy parent,
	                                                      IReadOnlyCollection<DbColumn> dbColumns,
	                                                      ushort maxDepth,
	                                                      ushort depth = 0)
	{
		var res = new List<PropertyHierarchy>();
		foreach (PropertyInfo info in propertyInfo)
		{
			var item = new PropertyHierarchy(info, parent, dbColumns);
			PropertyInfo[] props;
			switch (item.SupportedType)
			{
				case SupportedTypes.Value:
					res.Add(item);
					break;
				case SupportedTypes.Class:
					if (depth == maxDepth)
					{
						continue;
					}
					res.Add(item);
					props = item.Type.GetProperties();
					item.Children = props.BuildHierarchy(item, dbColumns, ++depth, maxDepth);
					break;
				case SupportedTypes.Array:
					if (depth == maxDepth)
					{
						continue;
					}
					res.Add(item);
					props = item.GenericType!.GetProperties();
					item.Children = props.BuildHierarchy(item, dbColumns, ++depth, maxDepth);
					break;
				default: throw new ArgumentOutOfRangeException(nameof(item), "Unexpected");
			}
		}

		return res;
	}

	public static void Parse(this object element, object[] row, PropertyHierarchy hierarchy)
	{
		if (element == null)
		{
			throw new NullReferenceException();
		}

		foreach (PropertyHierarchy prop in hierarchy.Children)
		{
			switch (prop.SupportedType)
			{
				case SupportedTypes.Value:
					if (!prop.ValueSet)
					{
						prop.SetValue(element, row[prop.Index]);
						prop.ValueSet = true;
					}
					break;
				case SupportedTypes.Class:
					if (!prop.ValueSet)
					{
						object @class = prop.GetConstructor().Invoke(Array.Empty<object>());
						prop.SetValue(element, @class);
						@class.Parse(row, prop);
						prop.ValueSet = true;
					}
					break;
				case SupportedTypes.Array:
					object array;
					if (!prop.ValueSet)
					{
						array = prop.GetConstructor().Invoke(Array.Empty<object>());
						prop.SetValue(element, array);
						prop.ValueSet = true;
					}
					else
					{
						array = prop.GetValue(element)!;
					}
					object item = prop.GetGenericConstructor().Invoke(Array.Empty<object>());
					((IList)array).Add(item);
					item.Parse(row, prop);
					break;
				default: throw new ArgumentOutOfRangeException(nameof(prop), "Unexpected");
			}
		}
	}

	public static T? GetByKeys<T>(this List<T> element, PropertyHierarchy hierarchy, object[] columns)
	{
		List<PropertyHierarchy> keys = hierarchy.Children.Where(p => p.IsKey).ToList();
		foreach (T el in element)
		{
			int matches = (from key in keys
						let value = key.GetValue(el)
						where value != null && value.Equals(columns[key.Index])
						select key).Count();

			if (matches == keys.Count)
			{
				return el;
			}
		}

		return default(T?);
	}

	public static string Parameterize(this string sql, object @params)
	{
		Dictionary<string, PropertyInfo> props = @params.GetType().GetProperties().ToDictionary(info => info.Name);
		var res = "";
		var parser = new SqlParser();
		for (var i = 0; i < sql.Length; i++)
		{
			char c = sql[i];
			parser.WillEscape = false;
			switch (parser.Statement)
			{
				case Statement.None:
					res += c;
					break;
				case Statement.String:
					res += $"\\{c}";
					break;
				case Statement.Parameter:
					int j = i + 1;
					for (; j < sql.Length; j++)
					{
						if (!Char.IsLetterOrDigit(sql[j]))
						{
							break;
						}
					}

					string name = sql[i..j];
					PropertyInfo prop = props[name];
					object val = prop.GetValue(@params) ?? "null";

					var valStr = String.Empty;
					if (prop.PropertyType.IsValueType)
					{
						valStr = val.ToString();
					}
					else if (typeof(string).IsAssignableFrom(prop.PropertyType))
					{
						valStr = ((string)val).EscapeSingleQuotes();
					}
					else if (typeof(DateTime).IsAssignableFrom(prop.PropertyType))
					{
						valStr = ((DateTime)val).ToString("yyyy-MM-dd HH:mm:ss").EscapeSingleQuotes();
					}

					i--;
					res = res[..i];
					i = ++j;

					res += '\'' + valStr + '\'';
					parser.Statement = Statement.None;
					break;
				case Statement.Backticks:
					res += c;
					break;
				case Statement.Brackets: break;
				default: throw new ArgumentOutOfRangeException(nameof(parser), "Unexpected");
			}

			parser.Update(c);
		}

		return res;
	}

	private static string EscapeSingleQuotes(this string sql) => sql.Replace("'", @"\'");
}