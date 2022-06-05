using System.ComponentModel.DataAnnotations;
using System.Reflection;
using SimpleOrm.Enums;

namespace SimpleOrm.Models;

internal sealed class PropertyHierarchy
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