using System.Collections;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Reflection;
using SimpleOrm.Enums;
using SimpleOrm.Models;

namespace SimpleOrm.Helpers;

internal static class SimpleOrmHelper
{
	public static PropertyHierarchy BuildAndCheckHierarchy<T>(IReadOnlyCollection<DbColumn> dbColumns, ushort maxDepth)
	{
		var parent = new PropertyHierarchy(typeof(T));
		parent.Children = parent.Type.GetRelevantProperties().BuildHierarchy(parent, dbColumns, maxDepth);
		IList<string> errors = parent.Children.CheckHierarchy();
		if (errors.Any())
		{
			throw new Exception(String.Join('\n', errors));
		}
		return parent;
	}

	private static IEnumerable<PropertyInfo> GetRelevantProperties(this Type type)
	{
		return type.GetProperties()
					.Where(info =>
								info.SetMethod != null
								&& !info.CustomAttributes.Any(data =>
											typeof(NotMappedAttribute).IsAssignableFrom(data.AttributeType)));
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
			else if (prop.IsFaulty)
			{
				string? pName = prop.ParentName;
				string? name = prop.PropertyInfo?.Name;
				string err = $"Type '{pName}' has not nullable public property '{name}' not found among query results";
				res.Add(err);
			}
		}
		return res;
	}

	private static List<PropertyHierarchy> BuildHierarchy(this IEnumerable<PropertyInfo> propertyInfos,
	                                                      PropertyHierarchy parent,
	                                                      IReadOnlyCollection<DbColumn> dbColumns,
	                                                      ushort maxDepth,
	                                                      ushort depth = 0)
	{
		var res = new List<PropertyHierarchy>();
		foreach (PropertyInfo info in propertyInfos)
		{
			var item = new PropertyHierarchy(info, parent, dbColumns);
			IEnumerable<PropertyInfo> props;
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
					props = item.Type.GetRelevantProperties();
					item.Children = props.BuildHierarchy(item, dbColumns, maxDepth, ++depth);
					break;
				case SupportedTypes.Array:
					if (depth == maxDepth)
					{
						continue;
					}
					res.Add(item);
					props = item.GenericType!.GetRelevantProperties();
					item.Children = props.BuildHierarchy(item, dbColumns, maxDepth, ++depth);
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
					if (prop.ValueSet == ValueSetResult.NotSet)
					{
						prop.SetValue(element, row);
					}
					break;
				case SupportedTypes.Class:
					if (prop.ValueSet == ValueSetResult.NotSet)
					{
						object @class = prop.Constructor!.Invoke(Array.Empty<object>());
						prop.SetValue(element, @class);
						@class.Parse(row, prop);
					}
					break;
				case SupportedTypes.Array:
					object array;
					if (prop.ValueSet == ValueSetResult.NotSet)
					{
						array = prop.ListConstructor!.Invoke(Array.Empty<object>());
						prop.SetValue(element, array);
					}
					else
					{
						array = prop.GetValue(element)!;
					}
					object item = prop.Constructor!.Invoke(Array.Empty<object>());
					item.Parse(row, prop);
					// We don't want to add array elements that did not have their value set
					if (prop.ArrayChildrenValid())
					{
						((IList)array).Add(item);
					}
					break;
				default: throw new ArgumentOutOfRangeException(nameof(prop), "Unexpected");
			}
		}
	}

	public static T? GetByKeys<T>(this IEnumerable<T> element, PropertyHierarchy hierarchy, object[] columns)
	{
		List<PropertyHierarchy> keys = hierarchy.Children.Where(p => p.IsKey).ToList();
		IEnumerable<T> elements = from el in element
					let keyValues = from key in keys
								let value = key.GetValue(el)
								where value != null && value.Equals(columns[key.Index])
								select key
					let matches = keyValues.Count()
					where matches == keys.Count
					select el;

		foreach (T el in elements)
		{
			return el;
		}

		return default(T?);
	}

	public static string Parameterize(this string sql, object @params, Action<string>? log)
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
				case Statement.Backticks: // Might need other functionality for brackets, string and backticks
				case Statement.Brackets:
				case Statement.String:
				case Statement.None:
					res += c;
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
						valStr = '\'' + ((string)val).EscapeSingleQuotes() + '\'';
					}
					else if (typeof(DateTime).IsAssignableFrom(prop.PropertyType))
					{
						valStr = '\'' + ((DateTime)val).ToString("yyyy-MM-dd HH:mm:ss").EscapeSingleQuotes() + '\'';
					}

					// Clip ':'
					i--;
					res = res[..i];

					res += valStr;

					// Update sql
					sql = res + sql[j..];
					i += valStr!.Length - 1;

					parser.Statement = Statement.None;
					break;
				default: throw new ArgumentOutOfRangeException(nameof(parser), "Unexpected");
			}

			parser.Update(c);
		}

		log?.Invoke("Executing query: \n" + res);
		return res;
	}

	private static string EscapeSingleQuotes(this string sql) => sql.Replace("'", @"\'");
}