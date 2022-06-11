using System.Collections;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Reflection;
using SimpleOrm.Enums;

namespace SimpleOrm.Models;

internal class PropertyHierarchy
{
	public readonly bool IsNullable;

	public readonly SupportedTypes SupportedType;

	public readonly Type Type;

	public readonly Type? GenericType;

	public bool ValueSet;

	public readonly string? Schema;

	public readonly string? Table;

	public readonly PropertyHierarchy? Parent;

	public readonly PropertyInfo? PropertyInfo;

	public List<PropertyHierarchy> Children = new();

	public readonly string? Column;

	public readonly string? Error;

	public readonly DbColumn? DbColumn;

	public bool IsKey => DbColumn != null && (DbColumn?.IsKey ?? throw new Exception("DbColumn did not have IsKey set"));

	public int Index => DbColumn?.ColumnOrdinal ?? throw new Exception("DbColumn did not have ColumnOrdinal set");

	/// <summary>Constructor for root</summary>
	public PropertyHierarchy(Type type)
	{
		Type = type;
		SupportedType = GetSupportedType(type);
		IsNullable = CheckIfNullable(type);
		(Schema, Table) = GetTableAndSchemaName(type);
	}

	/// <summary>Constructor for props</summary>
	public PropertyHierarchy(PropertyInfo info, PropertyHierarchy parent, IEnumerable<DbColumn> dbColumns)
	{
		Parent = parent;
		PropertyInfo = info;
		Type = info.PropertyType;
		IsNullable = CheckIfNullable(Type);
		SupportedType = GetSupportedType(Type);
		if (SupportedType == SupportedTypes.Array)
		{
			GenericType = info.PropertyType.GenericTypeArguments.First();
			(Schema, Table) = GetTableAndSchemaName(GenericType);
		}
		else
		{
			(Schema, Table) = GetTableAndSchemaName(Type);
		}

		Column = GetColumnName(info);
		DbColumn? dbColumn = dbColumns.FirstOrDefault(dbColumn =>
					(parent.Schema == null || dbColumn.BaseSchemaName == parent.Schema)
					&& dbColumn.BaseTableName == parent.Table
					&& dbColumn.ColumnName == Column);

		if (dbColumn == null)
		{
			Error = $"Type {parent.Type.Name} has public property {PropertyInfo.Name} not found among query results";
		}
		else
		{
			DbColumn = dbColumn;
		}
	}

	public static bool CheckIfNullable(Type type)
	{
		bool isNullable = Nullable.GetUnderlyingType(type) != null;
		if (isNullable)
		{
			return isNullable;
		}
		const string nullableAttribute = "System.Runtime.CompilerServices.NullableAttribute";
		isNullable = type.CustomAttributes.Any(data => data.AttributeType.FullName == nullableAttribute);
		return isNullable;
	}

	public static SupportedTypes GetSupportedType(Type type)
	{
		if (type.IsValueType || typeof(string).IsAssignableFrom(type))
		{
			return SupportedTypes.Value;
		}
		if (typeof(IEnumerable).IsAssignableFrom(type))
		{
			return SupportedTypes.Array;
		}
		if (type.IsClass)
		{
			return SupportedTypes.Class;
		}
		throw new Exception("Property was not found to be of SupportedType Value, Array nor Class");
	}

	public static (string?, string) GetTableAndSchemaName(Type type)
	{
		IList<CustomAttributeData> data = type.GetCustomAttributesData();
		string tableName = type.Name;
		string? schemaName = null;
		foreach (CustomAttributeData attributeData in data)
		{
			if (!typeof(TableAttribute).IsAssignableFrom(attributeData.AttributeType))
			{
				continue;
			}
			var table = attributeData.ConstructorArguments.FirstOrDefault().Value?.ToString();
			if (table != null)
			{
				tableName = table;
			}
			var schema = attributeData.NamedArguments.FirstOrDefault().TypedValue.Value?.ToString();
			if (schema != null)
			{
				schemaName = schema;
			}
		}

		return (schemaName, tableName);
	}

	public ConstructorInfo GetConstructor()
	{
		ConstructorInfo? constructor = Type.GetConstructor(Array.Empty<Type>());
		if (constructor == null)
		{
			throw new Exception($"Class property {Type.Name} did not have a parameterless constructor");
		}
		return constructor;
	}

	public ConstructorInfo GetGenericConstructor()
	{
		ConstructorInfo? constructor = GenericType?.GetConstructor(Array.Empty<Type>());
		if (constructor == null)
		{
			throw new Exception($"Property {GenericType?.Name} did not have a parameterless constructor");
		}
		return constructor;
	}

	public void Reset(object[] prev, object[] curr)
	{
		// ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
		switch (SupportedType)
		{
			case SupportedTypes.Value when IsNewValue(prev, curr):
			case SupportedTypes.Class when Children.All(c => c.IsNewValue(prev, curr)):
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

	public void SetValue(object item, object? value)
	{
		if (Parent == null)
		{
			throw new Exception("SetValue was called on base type");
		}
		if (value == null && !IsNullable)
		{
			throw new Exception($"{PropertyInfo!.Name} is not nullable but query returned null");
		}
		PropertyInfo!.SetValue(item, value);
	}

	public object? GetValue(object item)
	{
		if (Parent == null)
		{
			throw new Exception("GetValue was called on base type");
		}
		return PropertyInfo!.GetValue(item);
	}

	public bool IsNewValue(IReadOnlyList<object> prev, IReadOnlyList<object> curr)
	{
		if (!IsKey)
		{
			return true;
		}
		int index = Index;
		return !Equals(prev[index], curr[index]);
	}

	public static string GetColumnName(PropertyInfo propertyInfo)
	{
		IList<CustomAttributeData> data = propertyInfo.GetCustomAttributesData();
		foreach (CustomAttributeData attributeData in data)
		{
			if (!typeof(ColumnAttribute).IsAssignableFrom(attributeData.AttributeType))
			{
				continue;
			}
			var name = attributeData.ConstructorArguments.First().Value?.ToString();
			if (name != null)
			{
				return name;
			}
		}

		return propertyInfo.Name;
	}
}