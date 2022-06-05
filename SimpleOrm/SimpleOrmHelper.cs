using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;

namespace SimpleOrm;

public static class SimpleOrmHelper
{
	public static IList<PropertyHierarchy> BuildHierarchy<T>(this IDataRecord reader, IReadOnlyList<object> columns)
			where T : new()
	{
		Type parentType = typeof(T);
		List<PropertyHierarchy> properties = parentType.GetProperties().BuildPropertyHierarchy(parentType, true);
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
				res.Add($"Type {property.Parent.Name} has public property {property.PropertyInfo.Name} that was not found among the query results");
			}
		}
		return res;
	}

	private static PropertyHierarchy? FindFirstNotMapped(this IList<PropertyHierarchy> properties, string name)
	{
		PropertyHierarchy? found = properties.FirstOrDefault(
				info => !info.IsMapped
				        && String.Equals(name, info.GetColumnName(), StringComparison.Ordinal));

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
			bool isBase)
	{
		var res = new List<PropertyHierarchy>();
		foreach (PropertyInfo info in propertyInfo)
		{
			PropertyHierarchy parent;
			if (info.PropertyType.IsValueType || info.PropertyType == typeof(string))
			{
				parent = new PropertyHierarchy(info, parentType, isBase, KnownTypes.Value);
				res.Add(parent);
			}
			else if (typeof(IEnumerable).IsAssignableFrom(info.PropertyType))
			{
				parent = new PropertyHierarchy(info, parentType, isBase, KnownTypes.Array);
				res.Add(parent);
				parent.Children.AddRange(
						info.PropertyType.GenericTypeArguments.First()
								.GetProperties()
								.BuildPropertyHierarchy(info.PropertyType, false));
			}
			else if (info.PropertyType.IsClass)
			{
				parent = new PropertyHierarchy(info, parentType, isBase, KnownTypes.Class);
				res.Add(parent);
				parent.Children.AddRange(
						info.PropertyType.GetProperties().BuildPropertyHierarchy(info.PropertyType, false));
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

	public static void Parse(this object element, object[] row, IList<PropertyHierarchy> properties)
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
					if (prop.ValueSet)
					{
						Console.WriteLine("Value type was about to be overriden; First result detected");
						return;
					}

					Console.WriteLine("Value");
					prop.PropertyInfo.SetValue(element, row[prop.Index]);
					prop.ValueSet = true;
					break;
				case KnownTypes.Class:
					if (prop.ValueSet)
					{
						Console.WriteLine("Class type was about to be overriden; First result detected");
						return;
					}

					Console.WriteLine("Class");
					ConstructorInfo? constructor = prop.PropertyInfo.PropertyType.GetConstructor(Array.Empty<Type>());
					if (constructor == null)
					{
						throw new Exception(
								$"Property {prop.PropertyInfo.Name} did not have a parameterless constructor");
					}
					object @class = constructor.Invoke(Array.Empty<object>());
					prop.PropertyInfo.SetValue(element, @class);
					prop.ValueSet = true;
					@class.Parse(row, prop.Children);
					break;
				case KnownTypes.Array:
					Console.WriteLine("Array");
					break;
				default: throw new ArgumentOutOfRangeException(nameof(prop), "Unexpected");
			}
		}
	}

	private static T? GetByKeys<T>(
			this IEnumerable<T> items,
			IReadOnlyList<PropertyHierarchy> baseProps,
			IReadOnlyList<object> columns) where T : new()
	{
		foreach (T item in items)
		{
			var found = false;
			for (var i = 0; i < baseProps.Count; i++)
			{
				if (baseProps[i].PropertyInfo.GetValue(item) == columns[i])
				{
					found = true;
				}
			}
			if (!found)
			{
				continue;
			}
			return item;
		}

		return default(T?);
	}
}

public class PropertyHierarchy
{
	public readonly PropertyInfo PropertyInfo;

	public readonly Type Parent;

	public PropertyHierarchy(PropertyInfo propertyInfo, Type parent, bool isBase, KnownTypes knownTypes)
	{
		PropertyInfo = propertyInfo;
		Parent = parent;
		IsBase = isBase;
		KnownTypes = knownTypes;
		IsKey = propertyInfo.GetCustomAttributesData()
				.Any(data => data.AttributeType.IsAssignableFrom(typeof(KeyAttribute)));
	}

	public readonly KnownTypes KnownTypes;

	public readonly bool IsBase;

	public bool IsMapped;

	public bool ValueSet;

	public int Index;

	public readonly List<PropertyHierarchy> Children = new();

	public readonly bool IsKey;
}

public enum KnownTypes
{
	Value,

	Class,

	Array,
}