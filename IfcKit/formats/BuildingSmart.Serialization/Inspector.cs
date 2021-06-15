// Name:        Inspector.cs
// Description: Base class for serializers
// Author:      Tim Chipman
// Origination: Work performed for BuildingSmart by Constructivity.com LLC.
// Copyright:   (c) 2017 BuildingSmart International Ltd.
// License:     http://www.buildingsmart-tech.org/legal

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Runtime.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using System.Xml.Serialization;


namespace BuildingSmart.Serialization
{
	public abstract class Inspector
	{
		Type _projtype;
		Type _roottype;
		string _schema;
		string _release;
		string _application;
		Dictionary<string, Type> _typemap = new Dictionary<string, Type>();
		Dictionary<string, Type> _abstractTypeMap = new Dictionary<string, Type>();

		Dictionary<Type, IList<KeyValuePair<string, PropertyInfo>>> _fieldmap = new Dictionary<Type, IList<KeyValuePair<string, PropertyInfo>>>(); // cached field lists in declaration order
		Dictionary<Type, IList<PropertyInfo>> _fieldinv = new Dictionary<Type, IList<PropertyInfo>>(); // cached field lists for inverses
		Dictionary<Type, IList<KeyValuePair<string, PropertyInfo>>> _fieldall = new Dictionary<Type, IList<KeyValuePair<string, PropertyInfo>>>(); // combined
		Dictionary<int, List<PropertyInfo>> _inversemap = new Dictionary<int, List<PropertyInfo>>();
		Dictionary<Type, MethodInfo> _deserializingmap = new Dictionary<Type, MethodInfo>(); // cached field lists in declaration order

		private bool _XmlAttributePriority { get; set; } = false;
		/// <summary>
		/// Creates serializer accepting all types within assembly.
		/// </summary>
		/// <param name="typeProject">Type of the root object to load</param>
		public Inspector(Type typeProject)
			: this(typeProject, null, null, null, null)
		{
		}
		public Inspector(Type typeProject, bool xmlAttributePriority)
			: this(typeProject)
		{
			_XmlAttributePriority = xmlAttributePriority;
		}
		public Inspector(Type typeProject, Type[] loadtypes)
			: this(typeProject, loadtypes, null, null, null)
		{
		}

		/// <summary>
		/// Creates serializer accepting specific types.
		/// </summary>
		/// <param name="typeProject">Type of the root object to load</param>
		/// <param name="types">Other types that may be loaded, or null for all types within assembly of typeProject.</param>
		/// <param name="schema">Schema name to use, or null to determine from assembly of typeProject.</param>
		/// <param name="release">Release name to use, or null to determine from assembly of typeProject.</param>
		/// <param name="application">Application name to use, or null to determine from executing assembly.</param>
		public Inspector(Type typeProject, Type[] types, string schema, string release, string application)
		{
			this._projtype = typeProject;

			if (typeProject == null)
				return;

			if (types == null)
			{
				types = typeProject.Assembly.GetTypes();
			}

			AssemblyName aname = typeProject.Assembly.GetName();
			string[] assemblynameparts = aname.Name.Split('.');

			// determine the schema according to the last part of the assembly name
			// e.g. BuildingSmart.IFC4X1
			if (schema != null)
			{
				this._schema = schema;
			}
			else
			{
				this._schema = assemblynameparts[assemblynameparts.Length - 1];
			}

			if (release != null)
			{
				this._release = release;
			}
			else
			{
				this._release = "final"; // assume final release unless specified otherwise
				if (aname.Version != null)//attrVersion != null)
				{
					int addendum = aname.Version.MajorRevision;
					int corrigendum = aname.Version.MinorRevision;
					{
						StringBuilder specrelease = new StringBuilder();
						if (addendum > 0)
						{
							specrelease.Append("Add");
							specrelease.Append(addendum.ToString());

							if (corrigendum > 0)
							{
								specrelease.Append("TC");
								specrelease.Append(corrigendum.ToString());
							}
						}

						if (specrelease.Length > 0)
						{
							this._release = specrelease.ToString();
						}
					}
				}
			}

			// determine the root class (IfcRoot) according to the root class of the passed in object (IfcProject)
			Type typeRoot = typeProject.BaseType;
			if (typeRoot == null || typeRoot == typeof(object))
				typeRoot = typeProject;
			else
			{
				while (typeRoot.BaseType != typeof(object))
				{
					typeRoot = typeRoot.BaseType;
				}
			}

			this._roottype = typeRoot;

			// cache types by name for deserialization
			foreach (Type t in types)
			{
				// map fields for quick loading of inverse fields
				PropertyInfo[] fields = t.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				foreach (PropertyInfo fTarget in fields)
				{
					InversePropertyAttribute[] attrs = (InversePropertyAttribute[])fTarget.GetCustomAttributes(typeof(InversePropertyAttribute), false);
					if (attrs.Length == 1)
					{
						Type typeElement;
						if (fTarget.PropertyType.IsGenericType)
						{
							// inverse set (most common)
							typeElement = fTarget.PropertyType.GetGenericArguments()[0];
						}
						else
						{
							// inverse scalar (more obscure)
							typeElement = fTarget.PropertyType;
						}

						if (typeElement.BaseType == null)
							continue;
						PropertyInfo fSource = this.GetFieldByName(typeElement, attrs[0].Property); // dictionary requires reference uniqueness
						if (fSource != null)
						{
							List<PropertyInfo> listField = null;
							if (!_inversemap.TryGetValue(fSource.MetadataToken, out listField))
							{
								listField = new List<PropertyInfo>();
								_inversemap.Add(fSource.MetadataToken, listField);
							}

							listField.Add(fTarget);
						}
					}
				}

				if (t.IsPublic)
				{
					string name = t.Name.ToUpper();
					if (t.IsAbstract)
					{
						if (!_abstractTypeMap.ContainsKey(name))
							_abstractTypeMap.Add(name, t);
					}
					else
					{
						XmlTypeAttribute xmlType = t.GetCustomAttribute<XmlTypeAttribute>();
						if(xmlType != null && !string.IsNullOrEmpty(xmlType.TypeName) && !_typemap.ContainsKey(xmlType.TypeName.ToUpper()))
						{
							_typemap.Add(xmlType.TypeName.ToUpper(), t);
						}
						else if (!_typemap.ContainsKey(name))
						{
							_typemap.Add(name, t);
						}
					}
				}
			}

			if (application != null)
			{
				this._application = application;
			}
			else
			{
				Assembly assm = Assembly.GetEntryAssembly();
				AssemblyTitleAttribute title = assm.GetCustomAttribute<AssemblyTitleAttribute>();
				if (title != null)
				{
					this._application = title.Title + " " + assm.GetName().Version.ToString();
				}
				else
				{
					this._application = assm.FullName.ToString();
				}
			}
		}

		/// <summary>
		/// Writes characters to indent line.
		/// </summary>
		/// <param name="writer"></param>
		/// <param name="indent">The number of levels to indent.</param>
		protected void WriteIndent(StreamWriter writer, int indent)
		{
			// current implementation uses tabs -- could be configurable to use spaces
			// tabs are used for efficient replacement when generating marked up example with hyperlinks

			for (int i = 0; i < indent; i++)
			{
				//writer.Write(" "); // previous setting
				writer.Write("\t");
			}
		}

		/// <summary>
		/// The schema identifier (e.g "IFC4")
		/// </summary>
		public string Schema
		{
			get
			{
				return this._schema;
			}
			set
			{
				this._schema = value;
			}
		}

		/// <summary>
		/// The release identifier (e.g. "Add2TC1")
		/// </summary>
		protected string Release
		{
			get
			{
				return this._release;
			}
		}

		public string BaseURI
		{
			get
			{
				return "http://www.buildingsmart-tech.org/ifc/" + this.Schema + "/" + this.Release;
			}
		}

		public string Preprocessor
		{
			get
			{
				return "buildingSMART IfcKit";
			}
		}

		public string Application
		{
			get
			{
				return this._application;
			}
		}

		/// <summary>
		/// The project type (IfcProject)
		/// </summary>
		protected Type ProjectType
		{
			get
			{
				return this._projtype;
			}
		}

		/// <summary>
		/// The root type of indexable objects (IfcRoot)
		/// </summary>
		protected Type RootType
		{
			get
			{
				return this._roottype;
			}
		}

		/// <summary>
		/// Returns the collection type to be instantiated for the declared field type, 
		/// e.g. ISet<IfcObject> ==> HashSet<IfcObject>, IList<IfcObject> ==> List<IfcObject>
		/// </summary>
		/// <param name="typeDeclared"></param>
		/// <returns></returns>
		protected Type GetCollectionInstanceType(Type typeDeclared)
		{
			if (!typeDeclared.IsGenericType)
				return null;

			// use instantiable collection type
			Type typeCollection = typeDeclared;
			Type typeGeneric = typeDeclared.GetGenericTypeDefinition();
			Type typeElement = typeDeclared.GetGenericArguments()[0];
			if (typeGeneric == typeof(ISet<>))
			{
				typeCollection = typeof(HashSet<>).MakeGenericType(typeElement);
			}
			else if (typeGeneric == typeof(IList<>))
			{
				typeCollection = typeof(List<>).MakeGenericType(typeElement);
			}

			return typeCollection;
		}

		/// <summary>
		/// Finds type according to name as serialized.
		/// </summary>
		/// <param name="name"></param>
		/// <returns></returns>
		protected Type GetNonAbstractTypeByName(string name)
		{
			Type type = null;
			if (this._typemap.TryGetValue(name.ToUpper(), out type))
				return type;
			return null;
		}
		protected Type GetTypeByName(string name)
		{
			Type type = GetNonAbstractTypeByName(name);
			if (type != null)
				return type;
			if(this._abstractTypeMap.TryGetValue(name.ToUpper(), out type))
				return type;
			return null;
		}

		protected Type GetType(PropertyInfo propertyInfo)
		{
			if (propertyInfo == null)
				return null;
			Type type = propertyInfo.PropertyType;
			if (type == typeof(byte[]) || type == typeof(string))
				return type;
			if (!propertyInfo.PropertyType.IsValueType && typeof(IEnumerable).IsAssignableFrom(propertyInfo.PropertyType) &&
				propertyInfo.PropertyType.IsGenericType)
			{
				return propertyInfo.PropertyType.GetGenericArguments()[0];
			}
			return type;
		}
		protected string GetTypeName(PropertyInfo propertyInfo)
		{
			Type type = GetType(propertyInfo);
			if (type == null)
				return "";
			return type.Name;
		}
		protected MethodInfo getMethod(Type type, string methodName)
		{
			if (type == null)
				return null;
			MethodInfo methodInfo = type.GetMethod(methodName);
			if (methodInfo == null)
			{
				return getMethod(type.BaseType, methodName);
			}
			return methodInfo;
		}
		protected bool isListOfType(Type candidateListType, Type type)
		{
			if (type == null || !typeof(IList).IsAssignableFrom(candidateListType))
				return false;

			if(candidateListType.IsGenericType)
			{
				Type t = candidateListType.GetGenericArguments()[0];
				if (t.IsAssignableFrom(type))
					return true;
			}
			return isListOfType(candidateListType.BaseType, type);
		}

		protected Type[] GetTypes()
		{
			return this._typemap.Values.ToArray<Type>();
		}

		/// <summary>
		/// Returns a declared field on an object.
		/// </summary>
		/// <param name="type"></param>
		/// <param name="name"></param>
		/// <returns></returns>
		protected PropertyInfo GetFieldByName(Type type, string name)
		{
			IList<KeyValuePair<string, PropertyInfo>> fields = this.GetFieldsOrdered(type);
			foreach (KeyValuePair<string, PropertyInfo> field in fields)
			{
				if (field.Value != null && field.Key.Equals(name))
					return field.Value;
			}

			return null;
		}

		protected PropertyInfo GetInverseByName(Type type, string name)
		{
			IList<PropertyInfo> fields = this.GetFieldsInverseAll(type);
			foreach (PropertyInfo field in fields)
			{
				if (field != null && field.Name.Equals(name))
					return field;
			}

			return null;
		}

		protected PropertyInfo GetFieldInverse(PropertyInfo field, Type t)
		{
			List<PropertyInfo> listFields = null;

			if (this._inversemap.TryGetValue(field.MetadataToken, out listFields))
			{
				foreach (PropertyInfo fieldTarget in listFields)
				{
					if (fieldTarget.DeclaringType.IsAssignableFrom(t))
					{
						return fieldTarget;
					}
				}
			}
			return null;
		}

		protected IList<KeyValuePair<string, PropertyInfo>> GetFieldsAll(Type type)
		{
			IList<KeyValuePair<string, PropertyInfo>> fields = null;
			if (_fieldall.TryGetValue(type, out fields))
			{
				return fields;
			}

			fields = new List<KeyValuePair<string, PropertyInfo>>();
			BuildFieldList(type, fields, true, true);
			_fieldall.Add(type, fields);

			return fields;
		}

		/// <summary>
		/// Returns all fields on an entity type in order of persistence, including inherited types.
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		protected IList<KeyValuePair<string, PropertyInfo>> GetFieldsOrdered(Type type)
		{
			IList<KeyValuePair<string, PropertyInfo>> fields = null;
			if (_fieldmap.TryGetValue(type, out fields))
			{
				return fields;
			}

			fields = new List<KeyValuePair<string, PropertyInfo>>();
			BuildFieldList(type, fields, true, false);
			_fieldmap.Add(type, fields);
			return fields;
		}

		/// <summary>
		/// Returns all inverse fields for object.
		/// </summary>
		/// <param name="type"></param>
		/// <returns></returns>
		protected IList<PropertyInfo> GetFieldsInverseAll(Type type)
		{
			IList<PropertyInfo> fields = null;
			if (_fieldinv.TryGetValue(type, out fields))
			{
				return fields;
			}

			fields = new List<PropertyInfo>();
			BuildInverseListAll(type, fields);
			_fieldinv.Add(type, fields);

			return fields;
		}

		protected void BuildInverseListAll(Type type, IList<PropertyInfo> list)
		{
			if (type.BaseType != typeof(object) && type.BaseType != typeof(Serializer))
			{
				BuildInverseListAll(type.BaseType, list);
			}

			PropertyInfo[] fields = type.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			foreach (PropertyInfo field in fields)
			{
				if (field.IsDefined(typeof(InversePropertyAttribute), false))
				{
					list.Add(field);
				}
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="type"></param>
		/// <param name="list"></param>
		/// <param name="inherit"></param>
		/// <param name="inverse"></param>
		/// <param name="platform">Specific version, or UNSET to use default (latest)</param>
		private void BuildFieldList(Type type, IList<KeyValuePair<string, PropertyInfo>> list, bool inherit, bool inverse)
		{
			if (inherit && type.BaseType != null && type.BaseType != typeof(object) && type.BaseType != typeof(Serializer))
			{
				BuildFieldList(type.BaseType, list, inherit, inverse);

				// zero-out any fields that are overridden as derived at subtype (e.g. IfcSIUnit.Dimensions)
				for (int iField = 0; iField < list.Count; iField++)
				{
					PropertyInfo field = list[iField].Value;
					if (field != null)
					{
						PropertyInfo prop = type.GetProperty(field.Name.Substring(1), BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
						if (prop != null)
						{
							list[iField] = new KeyValuePair<string, PropertyInfo>("", null); // hide derived fields
						}
					}
				}
			}

			PropertyInfo[] fields = type.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			KeyValuePair<string, PropertyInfo>[] sorted = new KeyValuePair<string, PropertyInfo>[fields.Length];
			List<KeyValuePair<string, PropertyInfo>> unorderedAttributes = new List<KeyValuePair<string, PropertyInfo>>();
			SortedList<int, KeyValuePair<string, PropertyInfo>> orderedAttributes = new SortedList<int, KeyValuePair<string, PropertyInfo>>();
			foreach (PropertyInfo field in fields)
			{
				DataMemberAttribute dataMemberAttribute = field.GetCustomAttribute<DataMemberAttribute>();
				KeyValuePair<string, PropertyInfo> pair = new KeyValuePair<string, PropertyInfo>(field.Name, field);
				if (dataMemberAttribute != null && !string.IsNullOrEmpty(dataMemberAttribute.Name))
					pair = new KeyValuePair<string, PropertyInfo>(dataMemberAttribute.Name, field);
				if (_XmlAttributePriority)
				{
					XmlIgnoreAttribute xmlIgnoreAttribute = field.GetCustomAttribute<XmlIgnoreAttribute>();
					if (xmlIgnoreAttribute != null)
						continue;
					XmlElementAttribute xmlElementAttribute = field.GetCustomAttribute<XmlElementAttribute>();
					if (xmlElementAttribute != null)
					{
						if (!string.IsNullOrEmpty(xmlElementAttribute.ElementName))
							pair = new KeyValuePair<string, PropertyInfo>(xmlElementAttribute.ElementName, pair.Value);
						if (xmlElementAttribute.Order > -1 && xmlElementAttribute.Order < fields.Length)
						{
							sorted[xmlElementAttribute.Order] = pair;
							continue;
						}
						if (dataMemberAttribute == null)
						{
							unorderedAttributes.Add(pair);
							continue;
						}
					}
					XmlAttributeAttribute xmlAttributeAttribute = field.GetCustomAttribute<XmlAttributeAttribute>();	
					if(xmlAttributeAttribute != null)
					{
						if(!string.IsNullOrEmpty( xmlAttributeAttribute.AttributeName))
							pair = new KeyValuePair<string, PropertyInfo>(xmlAttributeAttribute.AttributeName, pair.Value);
						if (dataMemberAttribute != null && dataMemberAttribute.Order >= 0)
						{
							orderedAttributes.Add(dataMemberAttribute.Order, pair);
							continue;
						}
						if (dataMemberAttribute == null)
						{
							unorderedAttributes.Add(pair);
							continue;
						}
					}
					XmlArrayAttribute xmlArrayAttribute = field.GetCustomAttribute<XmlArrayAttribute>();
					if (xmlArrayAttribute != null)
					{
						if (!string.IsNullOrEmpty(xmlArrayAttribute.ElementName))
							pair = new KeyValuePair<string, PropertyInfo>(xmlArrayAttribute.ElementName, pair.Value);
						if (xmlArrayAttribute.Order > -1 && xmlArrayAttribute.Order < fields.Length)
						{
							sorted[xmlArrayAttribute.Order] = pair;
							continue;
						}
						if (dataMemberAttribute == null)
						{
							unorderedAttributes.Add(pair);
							continue;
						}
					}
				}
				if (dataMemberAttribute != null)
				{
					if (dataMemberAttribute.Order < fields.Length)
					{
						sorted[dataMemberAttribute.Order] = pair;
					}
					else
					{
						unorderedAttributes.Add(pair);
					}
				}
			}

			foreach (KeyValuePair<string, PropertyInfo> sort in sorted)
			{
				if (sort.Value != null)
				{
					list.Add(sort);
				}
			}
			foreach (KeyValuePair<string, PropertyInfo> propertyInfo in orderedAttributes.Values)
				list.Add(propertyInfo);
			foreach (KeyValuePair<string, PropertyInfo> propertyInfo in unorderedAttributes)
				list.Add(propertyInfo);
			// now inverse -- need particular order???
			if (inverse)
			{
				//fields = type.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
				foreach (PropertyInfo field in fields)
				{
					if (field.IsDefined(typeof(InversePropertyAttribute), false))
					{
						list.Add(new KeyValuePair<string, PropertyInfo>(field.Name, field));
					}
				}
			}
		}

		protected void UpdateInverse(object instance, PropertyInfo field, object value)
		{
			if (value is string || value is Byte[] || value is ValueType)
				return;

			// set inverse link for singular references
			if (value is IEnumerable)
			{
				Type[] genericArguments = field.PropertyType.GetGenericArguments();
				if (genericArguments != null && genericArguments.Length > 0)
				{
					Type typeelem = field.PropertyType.GetGenericArguments()[0];
					if (typeelem.IsInterface || typeelem.IsClass)
					{
						// set reverse field
						IEnumerable listSource = (IEnumerable)value;
						foreach (object listItem in listSource)
						{
							if (listItem is object)
							{
								object itemSource = listItem;
								PropertyInfo fieldInverse = this.GetFieldInverse(field, itemSource.GetType());
								if (fieldInverse != null && fieldInverse.PropertyType.IsGenericType)
								{
									IEnumerable listTarget = (IEnumerable)fieldInverse.GetValue(itemSource);
									if (listTarget == null)
									{
										// must allocate list
										Type typeList = GetCollectionInstanceType(fieldInverse.PropertyType);
										listTarget = (IEnumerable)Activator.CreateInstance(typeList);
										fieldInverse.SetValue(itemSource, listTarget);
									}

									// now add to inverse set -- perf: change this to use generated code for much better perfomance
									MethodInfo methodAdd = listTarget.GetType().GetMethod("Add");
									if (methodAdd != null)
									{
										methodAdd.Invoke(listTarget, new object[] { instance });
									}
									//listTarget.Add(instance);
								}
								else if (fieldInverse != null)
								{
									// scalar inverse
									fieldInverse.SetValue(listItem, instance);
								}
							}
						}
					}
				}
			}
			else if (value != null)
			{
				PropertyInfo fieldInverse = this.GetFieldInverse(field, value.GetType());
				if (fieldInverse != null)
				{
					// set reverse field
					if (typeof(IEnumerable).IsAssignableFrom(fieldInverse.PropertyType))
					{
						IEnumerable list = (IEnumerable)fieldInverse.GetValue(value);
						if (list == null)
						{
							// must allocate list
							Type typeList = GetCollectionInstanceType(fieldInverse.PropertyType);
							list = (IEnumerable)Activator.CreateInstance(typeList);
							fieldInverse.SetValue(value, list);
						}

						// now add to inverse set
						//list.Add(instance);
						MethodInfo methodAdd = list.GetType().GetMethod("Add");
						if (methodAdd != null)
						{
							methodAdd.Invoke(list, new object[] { instance });
						}

					}
					else
					{
						fieldInverse.SetValue(value, instance);
					}
				}
			}
		}

	}
}
