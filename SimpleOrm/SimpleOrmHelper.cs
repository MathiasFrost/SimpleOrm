using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;

namespace SimpleOrm;

public static class SimpleOrmHelper
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
			Type parentType)
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
				parent = new PropertyHierarchy(info, parentType, KnownTypes.Array);
				res.Add(parent);
				parent.Children.AddRange(
						info.PropertyType.GenericTypeArguments.First()
								.GetProperties()
								.BuildPropertyHierarchy(info.PropertyType));
			}
			else if (info.PropertyType.IsClass)
			{
				parent = new PropertyHierarchy(info, parentType, KnownTypes.Class);
				res.Add(parent);
				parent.Children.AddRange(info.PropertyType.GetProperties().BuildPropertyHierarchy(info.PropertyType));
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

	public static T? GetByKeys<T>(
			this IEnumerable<T> element,
			IEnumerable<PropertyHierarchy> properties,
			object[] columns)
	{
		List<PropertyHierarchy> keys = properties.Where(p => p.IsKey).ToList();
		if (!keys.Any())
		{
			throw new Exception($"Type {typeof(T).Name} needs to have at least one property with the Key attribute");
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
}

public class PropertyHierarchy
{
	public readonly PropertyInfo PropertyInfo;

	public readonly Type Parent;

	public PropertyHierarchy(PropertyInfo propertyInfo, Type parent, KnownTypes knownTypes)
	{
		PropertyInfo = propertyInfo;
		Parent = parent;
		KnownTypes = knownTypes;
		IsKey = propertyInfo.GetCustomAttributesData()
				.Any(data => data.AttributeType.IsAssignableFrom(typeof(KeyAttribute)));
	}

	public readonly KnownTypes KnownTypes;

	public bool IsMapped;

	public bool ValueSet;

	public int Index;

	public readonly List<PropertyHierarchy> Children = new();

	public readonly bool IsKey;

	public ConstructorInfo GetConstructor()
	{
		ConstructorInfo? constructor = PropertyInfo.PropertyType.GetConstructor(Array.Empty<Type>());
		if (constructor == null)
		{
			throw new Exception($"Property {PropertyInfo.Name} did not have a parameterless constructor");
		}
		return constructor;
	}

	public ConstructorInfo GetGenericConstructor()
	{
		Type type = PropertyInfo.PropertyType.GetGenericArguments().First();
		ConstructorInfo? constructor = type.GetConstructor(Array.Empty<Type>());
		if (constructor == null)
		{
			throw new Exception($"Property {PropertyInfo.Name} did not have a parameterless constructor");
		}
		return constructor;
	}

	public bool IsNewValue(IReadOnlyList<object> prev, IReadOnlyList<object> curr)
	{
		if (IsKey)
		{
			return !Equals(prev[Index], curr[Index]);
		}
		return true;
	}

	public void Reset(object[] prev, object[] curr)
	{
		switch (KnownTypes)
		{
			case KnownTypes.Value when IsNewValue(prev, curr):
			case KnownTypes.Class when Children.All(c => c.IsNewValue(prev, curr)):
				ValueSet = false;
				break;
		}
		foreach (PropertyHierarchy child in Children)
		{
			child.Reset(prev, curr);
		}
	}

	public void Reset()
	{
		ValueSet = false;
		foreach (PropertyHierarchy child in Children)
		{
			child.Reset();
		}
	}
}

public enum KnownTypes
{
	Value,

	Class,

	Array,
}