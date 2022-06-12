using System.Collections;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Common;
using System.Reflection;
using SimpleOrm.Enums;

namespace SimpleOrm.Models;

internal class PropertyHierarchy
{
	private readonly DbColumn? _dbColumn;

	private readonly bool _isNullable;

	private readonly PropertyHierarchy? _parent;

	private readonly string? _schema;

	private readonly string? _table;

	public readonly ConstructorInfo? Constructor;

	public readonly Type? GenericType;

	public readonly ConstructorInfo? ListConstructor;

	public readonly PropertyInfo? PropertyInfo;

	public readonly SupportedTypes SupportedType;

	public readonly Type Type;

	public List<PropertyHierarchy> Children = new();

	public ValueSetResult ValueSet = ValueSetResult.NotSet;

	/// <summary>Constructor for root</summary>
	public PropertyHierarchy(Type type)
	{
		Type = type;
		_isNullable = false;
		SupportedType = GetSupportedType(type);
		(_schema, _table) = GetTableAndSchemaName(type);
		Constructor = Type.GetConstructor(Array.Empty<Type>());
	}

	/// <summary>Constructor for props</summary>
	public PropertyHierarchy(PropertyInfo info, PropertyHierarchy parent, IEnumerable<DbColumn> dbColumns)
	{
		_parent = parent;
		PropertyInfo = info;
		Type = info.PropertyType;
		SupportedType = GetSupportedType(Type);
		_isNullable = CheckIfNullable(info, parent);
		if (SupportedType == SupportedTypes.Array)
		{
			GenericType = info.PropertyType.GenericTypeArguments.First();
			(_schema, _table) = GetTableAndSchemaName(GenericType);
			Constructor = GenericType.GetConstructor(Array.Empty<Type>());
			ListConstructor = GetListConstructor(GenericType);
		}
		else
		{
			(_schema, _table) = GetTableAndSchemaName(Type);
			Constructor = Type.GetConstructor(Array.Empty<Type>());
		}

		ColumnName = GetColumnName(info);
		_dbColumn = dbColumns.FirstOrDefault(dbColumn =>
					(parent._schema == null || dbColumn.BaseSchemaName == parent._schema)
					&& dbColumn.BaseTableName == parent._table
					&& dbColumn.ColumnName == ColumnName);
	}

	public bool ColumnNotFound => _dbColumn == null && !_isNullable;

	public string? ParentName => _parent?.SupportedType switch
	{
				SupportedTypes.Array => _parent.GenericType?.Name,
				SupportedTypes.Class => _parent.Type.Name,
				_ => null,
	};

	public string TableName => (_parent?._schema == null ? "" : _parent._schema + '.')
				+ (_parent?._table == null
							? ""
							: _parent._table.Contains('.')
										? $"[{_table}]"
										: _parent._table);

	public string? ColumnName { get; }

	public bool IsKey =>
				_dbColumn != null && (_dbColumn?.IsKey ?? throw new Exception("DbColumn did not have IsKey set"));

	public int Index => _dbColumn?.ColumnOrdinal ?? throw new Exception("DbColumn did not have ColumnOrdinal set");

	private static bool CheckIfNullable(PropertyInfo info, PropertyHierarchy? parent = null)
	{
		// Make nullable infectious
		if (parent is { _isNullable: true })
		{
			return true;
		}

		bool isNullable = Nullable.GetUnderlyingType(info.PropertyType) != null;
		if (isNullable)
		{
			return isNullable;
		}

		const string nullableAttribute = "System.Runtime.CompilerServices.NullableAttribute";
		isNullable = info.CustomAttributes.Any(data => data.AttributeType.FullName == nullableAttribute);
		return isNullable;
	}

	private static SupportedTypes GetSupportedType(Type type)
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

	private static (string?, string) GetTableAndSchemaName(MemberInfo type)
	{
		string tableName = type.Name;
		string? schemaName = null;
		foreach (CustomAttributeData attributeData in type.CustomAttributes)
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

	private static ConstructorInfo GetListConstructor(Type type)
	{
		Type listType = typeof(List<>).MakeGenericType(type);
		ConstructorInfo? constructor = listType.GetConstructor(Array.Empty<Type>());
		if (constructor == null)
		{
			throw new Exception("Somehow could not get List constructor");
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
				ValueSet = ValueSetResult.NotSet;
				break;
		}
		foreach (PropertyHierarchy child in Children)
		{
			child.Reset(prev, curr);
		}
	}

	public void Reset()
	{
		ValueSet = ValueSetResult.NotSet;
		foreach (PropertyHierarchy child in Children)
		{
			child.Reset();
		}
	}

	public bool ArrayChildrenValid()
	{
		// If all is nullable, we good.
		if (Children.All(child => child._isNullable))
		{
			return true;
		}

		// If all was set to null, do not add
		if (Children.All(child => child.ValueSet == ValueSetResult.SetNull))
		{
			return false;
		}
		
		// If a property of an array prop is not nullable and was set to null, the element is not valid
		IEnumerable<string> errors = from child in Children
					where !child._isNullable && child.ValueSet == ValueSetResult.SetNull
					select $"Entity '{child.ParentName}' has not nullable public property '{child.PropertyInfo?.Name}'"
								+ $", but value from column '{child.ColumnName}' from table '{child.TableName}' was null";

		foreach (string err in errors)
		{
			throw new Exception(err);
		}

		return true;
	}

	public void SetValue(object item, object[] columns)
	{
		object value = _dbColumn == null ? DBNull.Value : columns[Index];
		if (value is DBNull)
		{
			if (!_isNullable && _parent?.SupportedType != SupportedTypes.Array)
			{
				string err = $"Entity '{ParentName}' has not nullable public property '{PropertyInfo?.Name}', but "
							+ $"value from column '{ColumnName}' from table '{TableName}' was null";

				throw new Exception(err);
			}
			PropertyInfo!.SetValue(item, null);
			ValueSet = ValueSetResult.SetNull;
		}
		else
		{
			PropertyInfo!.SetValue(item, value);
			ValueSet = ValueSetResult.Set;
		}
	}

	public void SetValue(object item, object? value)
	{
		if (Children.All(hierarchy => hierarchy._dbColumn == null) && !_isNullable)
		{
			string err = $"Entity '{ParentName}' has not nullable public property '{PropertyInfo?.Name}', but "
						+ $"values from  column from table '{TableName}' was all null";

			throw new Exception(err);
		}
		if (value == null)
		{
			PropertyInfo!.SetValue(item, null);
			ValueSet = ValueSetResult.SetNull;
		}
		else
		{
			PropertyInfo!.SetValue(item, value);
			ValueSet = ValueSetResult.Set;
		}
	}

	public object? GetValue(object item) => PropertyInfo?.GetValue(item);

	private bool IsNewValue(IReadOnlyList<object> prev, IReadOnlyList<object> curr)
	{
		if (!IsKey)
		{
			return true;
		}
		int index = Index;
		return !Equals(prev[index], curr[index]);
	}

	private static string GetColumnName(MemberInfo propertyInfo)
	{
		foreach (CustomAttributeData attributeData in propertyInfo.CustomAttributes)
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