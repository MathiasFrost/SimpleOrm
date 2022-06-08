using System.Collections;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.SqlTypes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SimpleOrm.Enums;
using SimpleOrm.Models;

namespace SimpleOrm.Helpers;

internal static class SimpleOrmHelper
{
	public static List<PropertyHierarchy> BuildHierarchy<T>(this IDataRecord reader, IReadOnlyList<object> columns)
				where T : new()
	{
		Type parentType = typeof(T);
		List<PropertyHierarchy> properties = parentType.GetProperties().BuildPropertyHierarchy(parentType);
		for (var i = 0; i < reader.FieldCount; i++)
		{
			string name = reader.GetName(i);
			Type actual = columns[i].GetType();
			PropertyHierarchy? truth = properties.FindFirstNotMapped(name);

			if (truth == null)
			{
				throw new Exception($"{name} was not found as a public property of type {parentType.Name}");
			}
			if (!actual.IsAssignableFrom(truth.PropertyInfo.PropertyType))
			{
				throw new Exception($"{actual.Name} is not assignable from {truth.PropertyInfo.PropertyType.Name}");
			}

			truth.IsMapped = true;
			truth.Index = i;
		}
		IList<string> errors = properties.CheckHierarchy();
		if (errors.Any())
		{
			throw new Exception(String.Join('\n', errors));
		}
		return properties;
	}

	private static IList<string> CheckHierarchy(this IEnumerable<PropertyHierarchy> properties)
	{
		var res = new List<string>();
		foreach (PropertyHierarchy property in properties)
		{
			if (property.Children.Any())
			{
				res.AddRange(property.Children.CheckHierarchy());
			}
			else if (!property.IsMapped)
			{
				res.Add(
							$"Type {property.Parent.Name} has public property {property.PropertyInfo.Name} that was not found among the query results");
			}
		}
		return res;
	}

	private static PropertyHierarchy? FindFirstNotMapped(this IList<PropertyHierarchy> properties, string name)
	{
		PropertyHierarchy? found = properties.FirstOrDefault(
					info => !info.IsMapped && String.Equals(name, info.GetColumnName(), StringComparison.Ordinal));

		if (found != null)
		{
			return found;
		}
		foreach (PropertyHierarchy propertyHierarchy in properties)
		{
			found = propertyHierarchy.Children.FindFirstNotMapped(name);
			if (found != null)
			{
				return found;
			}
		}
		return null;
	}

	private static List<PropertyHierarchy> BuildPropertyHierarchy(
				this IEnumerable<PropertyInfo> propertyInfo,
				Type parentType,
				ushort depth = 0,
				ushort maxDepth = 20)
	{
		var res = new List<PropertyHierarchy>();
		foreach (PropertyInfo info in propertyInfo)
		{
			PropertyHierarchy parent;
			if (info.PropertyType.IsValueType || info.PropertyType == typeof(string))
			{
				parent = new PropertyHierarchy(info, parentType, KnownTypes.Value);
				res.Add(parent);
			}
			else if (typeof(IEnumerable).IsAssignableFrom(info.PropertyType))
			{
				if (depth == maxDepth)
				{
					continue;
				}
				parent = new PropertyHierarchy(info, parentType, KnownTypes.Array);
				res.Add(parent);
				PropertyInfo[] props = info.PropertyType.GenericTypeArguments.First().GetProperties();
				parent.Children.AddRange(props.BuildPropertyHierarchy(info.PropertyType, ++depth, maxDepth));
			}
			else if (info.PropertyType.IsClass)
			{
				if (depth == maxDepth)
				{
					continue;
				}
				parent = new PropertyHierarchy(info, parentType, KnownTypes.Class);
				res.Add(parent);
				PropertyInfo[] props = info.PropertyType.GetProperties();
				parent.Children.AddRange(props.BuildPropertyHierarchy(info.PropertyType, ++depth, maxDepth));
			}
		}

		return res;
	}

	private static string GetColumnName(this PropertyHierarchy property)
	{
		IList<CustomAttributeData> data = property.PropertyInfo.GetCustomAttributesData();
		foreach (CustomAttributeData attributeData in data)
		{
			if (!attributeData.AttributeType.IsAssignableFrom(typeof(ColumnAttribute)))
			{
				continue;
			}
			var name = attributeData.ConstructorArguments.First().Value?.ToString();
			if (name != null)
			{
				return name;
			}
		}

		return property.PropertyInfo.Name;
	}

	public static void Parse(this object element, object[] row, List<PropertyHierarchy> properties)
	{
		if (element == null)
		{
			throw new NullReferenceException();
		}

		foreach (PropertyHierarchy prop in properties)
		{
			switch (prop.KnownTypes)
			{
				case KnownTypes.Value:
					if (!prop.ValueSet)
					{
						prop.PropertyInfo.SetValue(element, row[prop.Index]);
						prop.ValueSet = true;
					}
					break;
				case KnownTypes.Class:
					if (!prop.ValueSet)
					{
						object @class = prop.GetConstructor().Invoke(Array.Empty<object>());
						prop.PropertyInfo.SetValue(element, @class);
						@class.Parse(row, prop.Children);
						prop.ValueSet = true;
					}
					break;
				case KnownTypes.Array:
					object array;
					if (!prop.ValueSet)
					{
						array = prop.GetConstructor().Invoke(Array.Empty<object>());
						prop.PropertyInfo.SetValue(element, array);
						prop.ValueSet = true;
					}
					else
					{
						array = prop.PropertyInfo.GetValue(element)!;
					}
					object item = prop.GetGenericConstructor().Invoke(Array.Empty<object>());
					((IList)array).Add(item);
					item.Parse(row, prop.Children);
					break;
				default: throw new ArgumentOutOfRangeException(nameof(prop), "Unexpected");
			}
		}
	}

	public static T? GetByKeys<T>(this IEnumerable<T> element, List<PropertyHierarchy> properties, object[] columns)
	{
		List<PropertyHierarchy> keys = properties.Where(p => p.IsKey).ToList();
		if (!keys.Any())
		{
			keys = properties;
		}
		foreach (T el in element)
		{
			int matches = (from key in keys
						let value = key.PropertyInfo.GetValue(el)
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
		PropertyInfo[] props = @params.GetType().GetProperties();
		foreach (PropertyInfo prop in props)
		{
			var parser = new SqlParser();
			var res = "";
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
						object val = prop.GetValue(@params) ?? "null";
						var valStr = String.Empty;
						if (prop.PropertyType.IsValueType)
						{
							valStr = val.ToString();
						}
						else if (prop.PropertyType.IsAssignableFrom(typeof(string)))
						{
							valStr = ((string)val).EscapeSingleQuotes();
						}
						else if (prop.PropertyType.IsAssignableFrom(typeof(DateTime)))
						{
							valStr = ((DateTime)val).ToString("yyyy-MM-dd HH:mm:ss").EscapeSingleQuotes();
						}
						res += valStr;
						Match match = Regex.Match(sql[i..], @"\b");
						i += match.Index;
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
		}

		return sql;
	}

	private static string EscapeSingleQuotes(this string sql) => sql.Replace("'", @"\'");
}