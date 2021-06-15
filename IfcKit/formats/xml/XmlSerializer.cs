// Name:        XmlSerializer.cs
// Description: XML serializer
// Author:      Tim Chipman
// Origination: Work performed for BuildingSmart by Constructivity.com LLC.
// Copyright:   (c) 2017 BuildingSmart International Ltd.
// License:     http://www.buildingsmart-tech.org/legal

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Serialization;
using System.Xml;

namespace BuildingSmart.Serialization.Xml
{
	public class XmlSerializer : Serializer
	{
		protected ObjectStore _ObjectStore = new ObjectStore();
		string _NameSpace = "";
		string _SchemaLocation = "";

		public bool UseUniqueIdReferences { get { return _ObjectStore.UseUniqueIdReferences; } set { _ObjectStore.UseUniqueIdReferences = value; } }
		public string NameSpace { set { _NameSpace = value; } }
		public string SchemaLocation { set { _SchemaLocation = value; } }

		Dictionary<Type, PropertyInfo> _XmlTextMap = new Dictionary<Type, PropertyInfo>(); // cached property for innerText 
		public XmlSerializer(Type typeProject) : base(typeProject, true)
		{
		}

		public override object ReadObject(Stream stream)
		{
			if (stream == null)
				throw new ArgumentNullException("stream");

			// pull it into a memory stream so we can make multiple passes (can't assume it's a file; could be web service)
			//...MemoryStream memstream = new MemoryStream();

			Dictionary<string, object> instances = new Dictionary<string, object>();
			ReadObject(stream, out instances);

			// stash project in empty string key
			object root = null;
			if (instances.TryGetValue(String.Empty, out root))
			{
				return root;
			}

			return null; // could not find the single project object
		}

		/// <summary>
		/// Reads an object graph and provides access to instance identifiers from file.
		/// </summary>
		/// <param name="stream"></param>
		/// <param name="instances"></param>
		/// <returns></returns>
		public object ReadObject(Stream stream, out Dictionary<string, object> instances)
		{
			System.Diagnostics.Debug.WriteLine("!! Reading XML");
			instances = new Dictionary<string, object>();
			
			return ReadContent(stream, instances);
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="stream"></param>
		/// <param name="idmap"></param>
		/// <param name="parsefields">True to populate fields; False to load instances only.</param>
		private object ReadContent(Stream stream, Dictionary<string, object> instances)
		{
			QueuedObjects queuedObjects = new QueuedObjects();
			using (XmlReader reader = XmlReader.Create(stream))
			{
				while (reader.Read())
				{
					switch (reader.NodeType)
					{
						case XmlNodeType.Element:
							if (reader.Name == "ex:iso_10303_28")
							{
								//ReadIsoStep(reader, fixups, instances, inversemap);
							}
							else// if (reader.LocalName.Equals("ifcXML"))
							{
								ReadEntity(reader, instances, "", queuedObjects);
								//ReadPopulation(reader, fixups, instances, inversemap);

							}
							break;
					}
				}
			}
			object result = null;
			instances.TryGetValue("", out result);
			return result;
		}
		protected object ReadEntity(XmlReader reader, IDictionary<string, object> instances, string typename, QueuedObjects queuedObjects)
		{
			return ReadEntity(reader, null, null, instances, typename, queuedObjects, false);
		}
		private object ReadEntity(XmlReader reader, object parent, PropertyInfo propInfo, IDictionary<string, object> instances, string typename, QueuedObjects queuedObjects, bool nestedElementDefinition)
		{
			int depth = reader.Depth;
			string readerLocalName = reader.LocalName;
			while (reader.NodeType == XmlNodeType.XmlDeclaration || reader.NodeType == XmlNodeType.Whitespace || reader.NodeType == XmlNodeType.Comment || reader.NodeType == XmlNodeType.None)
				reader.Read();
			//System.Diagnostics.Debug.WriteLine(new string(' ', indent) + ">>ReadEntity: " + readerLocalName + " " + (parent == null ? "" : parent.GetType().Name + "." + (propInfo == null ? "null" : propInfo.Name)));
			if (string.IsNullOrEmpty(typename))
			{
				typename = reader.LocalName;
				if (string.IsNullOrEmpty(typename))
				{
					reader.Read();
					while (reader.NodeType == XmlNodeType.Whitespace || reader.NodeType == XmlNodeType.Comment)
						reader.Read();
					typename = reader.LocalName;
				}
			}
			if (typename.EndsWith("-wrapper"))
			{
				typename = typename.Substring(0, typename.Length - 8);
			}
			if (reader.Name == "ex:double-wrapper")
			{
				// drill in
				if (!reader.IsEmptyElement)
				{
					while (reader.Read())
					{
						switch (reader.NodeType)
						{
							case XmlNodeType.Text:
								ReadValue(reader, parent, propInfo, typeof(double));
								break;

							case XmlNodeType.EndElement:
								return null;
						}
					}
				}
			}
			if (propInfo == null && parent != null)
				propInfo = detectPropertyInfo(parent.GetType(), readerLocalName);
			string xsiType = reader.GetAttribute("xsi:type");
			if (!string.IsNullOrEmpty(xsiType))
			{
				if (xsiType.Contains(":"))
				{
					string[] parts = xsiType.Split(':');
					if (parts.Length == 2)
					{
						typename = parts[1];
					}
				}
				else
					typename = xsiType;
			}
			Type t = null;
			if (string.Compare(typename, "header", true) == 0)
				t = typeof(headerData);
			else if (!string.IsNullOrEmpty(typename))
			{
				t = GetTypeByName(typename);
				if (!string.IsNullOrEmpty(reader.LocalName) && string.Compare(reader.LocalName, typename) != 0)
				{
					string testName = reader.LocalName;
					if (testName.EndsWith("-wrapper"))
					{
						testName = testName.Substring(0, testName.Length - 8);
					}
					Type testType = GetTypeByName(testName);
					if (testType != null && (t == null || (t.IsInterface && t.IsAssignableFrom(testType)) || testType.IsSubclassOf(t)))
						t = testType;
				}
			}

			string r = reader.GetAttribute("href");
			if (string.IsNullOrEmpty(r))
			{
				PropertyInfo refProperty = t == null ? null : GetFieldByName(t, "ref");
				if(refProperty == null)
					r = reader.GetAttribute("ref");
			}
			if (!string.IsNullOrEmpty(r))
			{
				object value = null;
				if (instances.TryGetValue(r, out value))
				{
					//System.Diagnostics.Debug.WriteLine(new string(' ', indent) + "vvReadEntity: " + readerLocalName + " " + (parent == null ? "" : parent.GetType().Name + "." + propInfo.Name));
					return LoadEntityValue(parent, propInfo, value);
				}
				queuedObjects.Queue(r, parent, propInfo);
				//System.Diagnostics.Debug.WriteLine(new string(' ', indent) + "AAReadEntity: " + readerLocalName + " " + (parent == null ? "" : parent.GetType().Name + "." + propInfo.Name));
				return null;
			}
			if (t == null || t.IsValueType)
			{
				if (reader.IsEmptyElement)
				{
					if (propInfo != null && typeof(IEnumerable).IsAssignableFrom(propInfo.PropertyType))
						return null;
				}
				else
				{
					bool hasvalue = false;
					while (reader.Read())
					{
						switch (reader.NodeType)
						{
							case XmlNodeType.Text:
							case XmlNodeType.CDATA:
								if (ReadValue(reader, parent, propInfo, t))
									return null;
								hasvalue = true;
								break;

							case XmlNodeType.Element:
								ReadEntity(reader, parent, propInfo, instances, t == null ? "" : t.Name, queuedObjects, true);
								hasvalue = true;
								break;
							case XmlNodeType.EndElement:
								if (!hasvalue)
								{
									ReadValue(reader, parent, propInfo, t);
								}
								//System.Diagnostics.Debug.WriteLine(new string(' ', indent) + "##ReadEntity: " + readerLocalName + " " + reader.LocalName + " " + reader.NodeType);
								return null;
						}
					}
				}
			}
			object entity = null;
			bool useParent = false;
			if (t != null)
			{
				if (t.IsAbstract)
				{
					reader.MoveToElement();
					while (reader.Read())
					{
						switch (reader.NodeType)
						{
							case XmlNodeType.Element:
								ReadEntity(reader, parent, propInfo, instances, t.Name, queuedObjects, true);
								break;

							case XmlNodeType.Attribute:
								break;

							case XmlNodeType.EndElement:
								return null;
						}
					}
					//System.Diagnostics.Debug.WriteLine(new string(' ', indent) + "\\ReadEntity: " + readerLocalName + " " + reader.LocalName + " " + reader.NodeType);
					return null;
				}
				// map instance id if used later
				string sid = reader.GetAttribute("id");

				if (t == this.ProjectType || t.IsSubclassOf(this.ProjectType))
				{
					if (!instances.TryGetValue(String.Empty, out entity))
					{
						entity = instances[String.Empty] = Construct(t); // stash project using blank index
						if (!string.IsNullOrEmpty(sid))
							instances[sid] = entity;
					}
				}
				else if (!string.IsNullOrEmpty(sid) && !instances.TryGetValue(sid, out entity))
				{
					entity = Construct(t);
					instances[sid] = entity;
				}
				if (entity == null)
				{
					if (propInfo != null && string.Compare(readerLocalName, propInfo.Name) == 0 && reader.AttributeCount == 0 &&
						typeof(IEnumerable).IsAssignableFrom(propInfo.PropertyType) && propInfo.PropertyType.IsGenericType)
					{
						useParent = true;
						entity = parent;
					}
					else
					{
						entity = Construct(t);
					}
					if (!string.IsNullOrEmpty(sid))
						instances[sid] = entity;
				}
				if (!useParent)
				{
					if (!string.IsNullOrEmpty(sid))
					{
						queuedObjects.DeQueue(sid, entity);
					}
					// ensure all lists/sets are instantiated
					Initialize(entity, t);

					if (propInfo != null)
					{
						if (parent != null)
							this.LoadEntityValue(parent, propInfo, entity);
					}

					bool isEmpty = reader.IsEmptyElement;
					// read attribute properties
					for (int i = 0; i < reader.AttributeCount; i++)
					{
						reader.MoveToAttribute(i);
						if (!reader.LocalName.Equals("id"))
						{
							string match = reader.LocalName;
							PropertyInfo f = GetFieldByName(t, match);
							if (f != null)
								ReadValue(reader, entity, f, f.PropertyType);
						}
					}
					// now read elements or end of entity
					if (isEmpty)
					{
						//System.Diagnostics.Debug.WriteLine(new string(' ', indent) + "||ReadEntity " + readerLocalName + " " + reader.LocalName + " " + t.Name + " " + entity.ToString() + " " + reader.NodeType);
						return entity;
					}
				}
				reader.MoveToElement();
			}
			bool isNested = (t == null || reader.AttributeCount == 0) && nestedElementDefinition;
			while (reader.Read())
			{
				if (reader.NodeType == XmlNodeType.Whitespace || reader.NodeType == XmlNodeType.Comment)
					continue;
				if(reader.NodeType == XmlNodeType.CDATA)
				{
					if (t != null)
					{
						PropertyInfo textProperty = detectTextAttribute(t);
						if (textProperty != null)
						{
							LoadEntityValue(entity, textProperty, reader.Value);
						}
					}
					continue;
				}
				if(reader.NodeType == XmlNodeType.EndElement)
				{
					//System.Diagnostics.Debug.WriteLine(new string(' ', indent) + "!!ReadEntity " + readerLocalName + " " + (t == null ? "" : ": " + t.Name + ".") + reader.LocalName + " " + entity.ToString() + " " + reader.NodeType);
					return entity;
				}
				string nestedReaderLocalName = reader.LocalName;
				object localEntity = entity;
				bool nested = useParent;
				string nestedTypeName = "";
				MethodInfo listAdd = null;
				
				PropertyInfo nestedPropInfo = null;
				if (useParent)
					nestedPropInfo = propInfo;
				else if (t != null)
				{
					nestedPropInfo = detectPropertyInfo(t, nestedReaderLocalName);
					if (nestedPropInfo == null)
					{
						Type nestedType = GetTypeByName(nestedReaderLocalName);
						if(isListOfType(t, nestedType))
						{
							listAdd = getMethod(t, "Add");
							if(listAdd != null)
								nestedTypeName = nestedType.Name;
						}
					}
					else
					{
						nestedTypeName = GetTypeName(nestedPropInfo);
					}
				}
				if(listAdd == null && nestedPropInfo == null && parent != null)
				{
					nestedPropInfo = detectPropertyInfo(parent.GetType(), nestedReaderLocalName);
					if (nestedPropInfo != null)
					{
						localEntity = parent;
						useParent = true;
						nestedTypeName = GetTypeName(nestedPropInfo);
					}

				}
				switch (reader.NodeType)
				{
					case XmlNodeType.Text:
					case XmlNodeType.CDATA:
						ReadValue(reader, localEntity, nestedPropInfo, null);
						break;

					case XmlNodeType.Element:
						{
							if (isNested)
							{
			//System.Diagnostics.Debug.WriteLine(new string(' ', indent) + "  Nested "+ nestedReaderLocalName);
								if (t == null || string.Compare(nestedReaderLocalName, t.Name) == 0)
								{
									
									entity = ReadEntity(reader, parent, propInfo, instances, nestedReaderLocalName, queuedObjects, false);
			//System.Diagnostics.Debug.WriteLine(new string(' ', indent) + "<<ReadEntity: " + readerLocalName + (entity != null ? entity.GetType().Name : "null") + "." + reader.LocalName + " " + reader.NodeType);
									return entity;
								}
								else
								{
									Type localType = this.GetNonAbstractTypeByName(nestedReaderLocalName);
									if (localType != null && localType.IsSubclassOf(t))
									{
										entity = ReadEntity(reader, parent, propInfo, instances, reader.LocalName, queuedObjects, false);
			//System.Diagnostics.Debug.WriteLine(new string(' ', indent) + "<<ReadEntity: " + readerLocalName + " " + t.Name + "." + reader.LocalName + " " + reader.NodeType);
										return entity;
									}
								}
							}
							if (t == null)
								ReadEntity(reader, null, null, instances, "", queuedObjects, false);
							else
							{
								if (!nestedElementDefinition && nestedPropInfo != null)
								{
									nestedTypeName = GetTypeName(nestedPropInfo);
								}

								object obj = ReadEntity(reader, useParent ? parent : localEntity, nestedPropInfo, instances, nestedTypeName, queuedObjects, nested);
								if (obj != null && listAdd != null)
								{
									try
									{
										listAdd.Invoke(localEntity, new object[] { obj }); // perf!!
									}
									catch(Exception) { }
								}
							}
							break;
						}

					case XmlNodeType.Attribute:
						break;

						
				}
			}
			//System.Diagnostics.Debug.WriteLine(new string(' ', indent) + "<<ReadEntity: " + readerLocalName + " " + t.Name + "." + reader.LocalName  +" " + entity.ToString() + " " + reader.NodeType);
			return entity;
		}
		private bool isEnumerableToNest(Type type)
		{
			return (type != typeof(byte[]) && type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type));
		}
		private PropertyInfo detectPropertyInfo(Type type, string propertyInfoName)
		{
			PropertyInfo propertyInfo = GetFieldByName(type, propertyInfoName);

			// inverse
			if (propertyInfo == null)
				propertyInfo = GetInverseByName(type, propertyInfoName);
			return propertyInfo;
		}
		public PropertyInfo detectTextAttribute(Type type)
		{
			PropertyInfo result = null;
			if (_XmlTextMap.TryGetValue(type, out result))
				return result;
			PropertyInfo[] properties = type.GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			foreach (PropertyInfo property in properties)
			{
				XmlTextAttribute xmlTextAttribute = property.GetCustomAttribute<XmlTextAttribute>();
				if (xmlTextAttribute != null)
				{
					_XmlTextMap[type] = property;
					return property;
				}	
			}
			Type baseType = type.BaseType;
			if (baseType != typeof(object) && baseType != typeof(Serializer))
				return detectTextAttribute(baseType);
			_XmlTextMap[type] = null;
			return null;
		}

		private void LoadCollectionValue(IEnumerable list, object v)
		{
			if (list == null)
				return;

			Type typeCollection = list.GetType();

			try
			{
				MethodInfo methodAdd = typeCollection.GetMethod("Add");
				methodAdd.Invoke(list, new object[] { v }); // perf!!
			}
			catch (Exception)
			{
				// could be type that changed and is no longer compatible with schema -- try to keep going
			}
		}
		private object processReference(string sid, object parent, PropertyInfo f, IDictionary<string, object> instances, QueuedObjects queuedObjects)
		{
			if (string.IsNullOrEmpty(sid))
				return null;
			object encounteredObject = null;
			if (instances.TryGetValue(sid, out encounteredObject))
				return LoadEntityValue(parent, f, encounteredObject);
			else
			{
				queuedObjects.Queue(sid, parent, f);
				System.Diagnostics.Debug.WriteLine(":::QueuedEntity: " + sid +  " " + parent.GetType().ToString() + "." + f.Name);
			}
			return null;
		}

		/// <summary>
		/// Reads a value
		/// </summary>
		/// <param name="reader">The xml reader</param>
		/// <param name="o">The entity</param>
		/// <param name="f">The field</param>
		/// <param name="ft">Optional explicit type, or null to use field type.</param>
		private bool ReadValue(XmlReader reader, object o, PropertyInfo f, Type ft)
		{
			//bool endelement = false;

			if (ft == null)
			{
				ft = f.PropertyType;
			}

			if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(Nullable<>))
			{
				// special case for Nullable types
				ft = ft.GetGenericArguments()[0];
			}

			object v = null;
			if (ft.IsEnum)
			{
				FieldInfo enumfield = ft.GetField(reader.Value, BindingFlags.IgnoreCase | BindingFlags.Public | System.Reflection.BindingFlags.Static);
				if(enumfield == null)
                {
					FieldInfo[] fields = ft.GetFields(BindingFlags.Public | BindingFlags.Static);
					foreach(FieldInfo fieldInfo in fields)
					{
						XmlEnumAttribute xmlEnumAttribute = fieldInfo.GetCustomAttribute<XmlEnumAttribute>();
						if (xmlEnumAttribute != null && string.Compare(reader.Value, xmlEnumAttribute.Name) == 0)
						{
							enumfield = fieldInfo;
							break;
						}
					}
                }
				if (enumfield != null)
				{
					v = enumfield.GetValue(null);
				}
			}
			else if ( ft == typeof(DateTime) || ft == typeof(string) || ft == typeof(byte[]))
			{
				v = ParsePrimitive(reader.Value, ft);
			}
			else if (ft.IsValueType)
			{
				// defined type -- get the underlying field
				PropertyInfo[] fields = ft.GetProperties(BindingFlags.Instance | BindingFlags.Public); //perf: cache this
				if (fields.Length == 1)
				{
					PropertyInfo fieldValue = fields[0];
					object primval = ParsePrimitive(reader.Value, fieldValue.PropertyType);
					v = Activator.CreateInstance(ft);
					fieldValue.SetValue(v, primval);
				}
				else
				{
					object primval = ParsePrimitive(reader.Value, ft);
					LoadEntityValue(o, f, primval);
				}
			}
			else if (IsEntityCollection(ft))
			{
				// IfcCartesianPoint.Coordinates

				Type typeColl = GetCollectionInstanceType(ft);
				v = System.Activator.CreateInstance(typeColl);

				Type typeElem = ft.GetGenericArguments()[0];
				PropertyInfo propValue = typeElem.GetProperty("Value");

				if (propValue != null)
				{
					string[] elements = reader.Value.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

					IEnumerable list = (IEnumerable)v;
					foreach (string elem in elements)
					{
						object elemv = Activator.CreateInstance(typeElem);
						object primv = ParsePrimitive(elem, propValue.PropertyType);
						propValue.SetValue(elemv, primv);
						LoadCollectionValue(list, elemv);
					}
				}
			}

			LoadEntityValue(o, f, v);
			return false;
			//return endelement;
		}

		private static object ParsePrimitive(string readervalue, Type type)
		{
			object value = null;
			if (typeof(Int64) == type)
			{
				// INTEGER
				value = ParseInteger(readervalue);
			}
			else if (typeof(Int32) == type)
			{
				value = (Int32)ParseInteger(readervalue);
			}
			else if (typeof(Double) == type)
			{
				// REAL
				value = ParseReal(readervalue);
			}
			else if (typeof(Single) == type)
			{
				value = (Single)ParseReal(readervalue);
			}
			else if (typeof(Boolean) == type)
			{
				// BOOLEAN
				value = ParseBoolean(readervalue);
			}
			else if (typeof(String) == type)
			{
				// STRING
				value = Regex.Replace(readervalue, "(?<!\r)\n", "\r\n"); 
			}
			else if (typeof(Guid) == type)
			{
				Guid result = Guid.Empty;
				if (Guid.TryParse(readervalue, out result))
					return result;
			}
			else if (typeof(DateTime) == type)
			{
				DateTime dtVal;
				if (DateTime.TryParse(readervalue, out dtVal))
				{
					value = dtVal;
				}
			}
			else if (typeof(byte[]) == type)
			{
				value = ParseBinary(readervalue);
			}

			return value;
		}

		private static bool ParseBoolean(string strval)
		{
			bool iv;
			if (Boolean.TryParse(strval, out iv))
			{
				return iv;
			}

			return false;
		}

		private static Int64 ParseInteger(string strval)
		{
			long iv;
			if (Int64.TryParse(strval, out iv))
			{
				return iv;
			}

			return 0;
		}

		private static Double ParseReal(string strval)
		{
			double iv;
			if (Double.TryParse(strval, out iv))
			{
				return iv;
			}

			return 0.0;
		}

		/// <summary>
		/// Writes an object graph to a stream formatted xml.
		/// </summary>
		/// <param name="stream">The stream to write.</param>
		/// <param name="root">The root object to write</param>
		public override void WriteObject(Stream stream, object root)
		{
			if (stream == null)
				throw new ArgumentNullException("stream");

			if (root == null)
				throw new ArgumentNullException("root");

			// pass 1: (first time ever encountering for serialization) -- determine which entities require IDs -- use a null stream
			writeFirstPassForIds(root, new HashSet<string>());
			// pass 2: write to file -- clear save map; retain ID map
			writeRootObject(stream, root, new HashSet<string>(), false);
		}
		internal protected void writeFirstPassForIds(object root, HashSet<string> propertiesToIgnore)
		{
			int indent = 0;
			StreamWriter writer = new StreamWriter(Stream.Null);
			Queue<object> queue = new Queue<object>();
			queue.Enqueue(root);
			while (queue.Count > 0)
			{
				object ent = queue.Dequeue();
				if (!_ObjectStore.isSerialized(ent))
				{
					this.WriteEntity(writer, ref indent, ent, propertiesToIgnore, queue, true, "", "");
				}
			}
			// pass 2: write to file -- clear save map; retain ID map
		}
		internal protected void writeObject(Stream stream, object root, HashSet<string> propertiesToIgnore)
		{
			writeRootObject(stream, root, propertiesToIgnore, false);
		}
		

		protected virtual void WriteHeader(StreamWriter writer)
		{
			writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
		}

		protected virtual void WriteFooter(StreamWriter writer)
		{
		}

		protected virtual void WriteRootDelimeter(StreamWriter writer)
		{
		}

		protected virtual void WriteCollectionStart(StreamWriter writer, ref int indent)
		{
		}

		protected virtual void WriteCollectionDelimiter(StreamWriter writer, int indent)
		{
		}

		protected virtual void WriteCollectionEnd(StreamWriter writer, ref int indent)
		{
		}

		protected virtual void WriteEntityStart(StreamWriter writer, ref int indent)
		{
		}

		protected virtual void WriteEntityEnd(StreamWriter writer, ref int indent)
		{
		}

		private void writeRootObject(Stream stream, object root, HashSet<string> propertiesToIgnore, bool isIdPass)
		{
			int indent = 0;
			StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false));

			this.WriteHeader(writer);

			Type t = root.GetType();
			string typeName = TypeSerializeName(t);

			
			this.WriteStartElementEntity(writer, ref indent, typeName);
			this.WriteStartAttribute(writer, indent, "xmlns:xsi");
			writer.Write("http://www.w3.org/2001/XMLSchema-instance");
			this.WriteEndAttribute(writer);
			this.WriteAttributeDelimiter(writer);
			if(!string.IsNullOrEmpty(_NameSpace))
			{
				this.WriteStartAttribute(writer, indent, "xmlns");
				writer.Write(_NameSpace);
				this.WriteEndAttribute(writer);
				this.WriteAttributeDelimiter(writer);
			}
			if (!string.IsNullOrEmpty(_SchemaLocation))
			{
				this.WriteStartAttribute(writer, indent, "xsi:schemaLocation");
				writer.Write(_SchemaLocation);
				this.WriteEndAttribute(writer);
				this.WriteAttributeDelimiter(writer);
			}
			Queue<object> queue = new Queue<object>();
			bool closeelem = this.WriteEntityAttributes(writer, ref indent, root, propertiesToIgnore, queue, isIdPass);
			if (!closeelem)
			{
				if (queue.Count == 0)
				{
					this.WriteCloseElementAttribute(writer, ref indent);
					this.WriteFooter(writer);
					writer.Flush();
					return;
				}
				this.WriteOpenElement(writer);
			}
			indent = 1;
			while (queue.Count > 0)
			{
				// insert delimeter after first root object
				this.WriteRootDelimeter(writer);

				object ent = queue.Dequeue();
				if (!_ObjectStore.isSerialized(ent))
				{
					this.WriteEntity(writer, ref indent, ent, propertiesToIgnore, queue, isIdPass, "", "");
				}
			}
			this.WriteEndElementEntity(writer, ref indent, typeName);
			this.WriteFooter(writer);
			writer.Flush();
		}
		private void WriteEntity(StreamWriter writer, ref int indent, object o, HashSet<string> propertiesToIgnore, Queue<object> queue, bool isIdPass, string elementName, string elementTypeName)
		{
			// sanity check
			if (indent > 100)
			{
				return;
			}

			if (o == null)
				return;

			Type t = o.GetType();
			string typeName = TypeSerializeName(t);
			string name = string.IsNullOrEmpty(elementName) ? typeName : elementName;
			this.WriteStartElementEntity(writer, ref indent, name);
			if (!isIdPass)
			{
				if (string.IsNullOrEmpty(elementTypeName))
				{
					if (string.Compare(typeName, name) != 0)
					{
						WriteType(writer, indent, typeName);
					}
				}
				else
				{
					if (string.Compare(name, elementTypeName) != 0 || string.Compare(name, typeName) != 0)
					{
						WriteType(writer, indent, typeName);
					}
				}
			}
			bool close = this.WriteEntityAttributes(writer, ref indent, o, propertiesToIgnore, queue, isIdPass);

			if (close)
			{
				this.WriteEndElementEntity(writer, ref indent, name);
			}
			else
			{
				this.WriteCloseElementEntity(writer, ref indent);
			}
		}

		/// <summary>
		/// Terminates the opening tag, to allow for sub-elements to be written
		/// </summary>
		protected virtual void WriteOpenElement(StreamWriter writer) { WriteOpenElement(writer, true); }
		protected virtual void WriteOpenElement(StreamWriter writer, bool newLine)
		{
			// end opening tag
			if (newLine)
				writer.WriteLine(">");
			else
				writer.Write(">");
		}

		/// <summary>
		/// Terminates the opening tag, with no subelements
		/// </summary>
		protected virtual void WriteCloseElementEntity(StreamWriter writer, ref int indent)
		{
			writer.WriteLine(" />");
			indent--;
		}

		protected virtual void WriteCloseElementAttribute(StreamWriter writer, ref int indent)
		{
			this.WriteCloseElementEntity(writer, ref indent);
		}

		protected virtual void WriteStartElementEntity(StreamWriter writer, ref int indent, string name)
		{
			this.WriteIndent(writer, indent);
			writer.Write("<" + name);
			indent++;
		}

		protected virtual void WriteStartElementAttribute(StreamWriter writer, ref int indent, string name)
		{
			this.WriteStartElementEntity(writer, ref indent, name);
		}

		protected virtual void WriteEndElementEntity(StreamWriter writer, ref int indent, string name)
		{
			indent--;

			this.WriteIndent(writer, indent);
			this.WriteEndElementEntity(writer, name);
		}
		protected void WriteEndElementEntity(StreamWriter writer, string name)
		{
			writer.Write("</");
			writer.Write(name);
			writer.WriteLine(">");
		}

		protected virtual void WriteEndElementAttribute(StreamWriter writer, ref int indent, string name)
		{
			WriteEndElementEntity(writer, ref indent, name);
		}

		protected virtual void WriteIdentifier(StreamWriter writer, int indent, string id)
		{
			// record id, and continue to write out all attributes (works properly on second pass)
			writer.Write(" id=\"");
			writer.Write(id);
			writer.Write("\"");
		}

		protected virtual void WriteReference(StreamWriter writer, int indent, string id)
		{
			writer.Write(" xsi:nil=\"true\" href=\"");
			writer.Write(id);
			writer.Write("\"");
		}

		protected virtual void WriteType(StreamWriter writer, int indent, string type)
		{
			writer.Write(" xsi:type=\"");
			writer.Write(type);
			writer.Write("\"");
		}

		protected virtual void WriteTypedValue(StreamWriter writer, ref int indent, string type, string encodedvalue)
		{
			this.WriteIndent(writer, indent);
			writer.WriteLine("<" + type + "-wrapper>" + encodedvalue + "</" + type + "-wrapper>");
		}

		protected virtual void WriteStartAttribute(StreamWriter writer, int indent, string name)
		{
			writer.Write(" ");
			writer.Write(name);
			writer.Write("=\"");
		}

		protected virtual void WriteEndAttribute(StreamWriter writer)
		{
			writer.Write("\"");
		}

		protected virtual void WriteAttributeDelimiter(StreamWriter writer)
		{
		}

		protected virtual void WriteAttributeTerminator(StreamWriter writer)
		{
		}

		

		protected static bool IsValueCollection(Type t)
		{
			return t.IsGenericType &&
				typeof(IEnumerable).IsAssignableFrom(t.GetGenericTypeDefinition()) &&
				t.GetGenericArguments()[0].IsValueType;
		}

		
		/// <summary>
		/// Returns true if any elements written (requiring closing tag); or false if not
		/// </summary>
		/// <param name="o"></param>
		/// <returns></returns>
		private bool WriteEntityAttributes(StreamWriter writer, ref int indent, object o, HashSet<string> propertiesToIgnore, Queue<object> queue, bool isIdPass)
		{
			Type t = o.GetType(), stringType = typeof(String);


			// give it an ID if needed (first pass)
			// mark as saved
			if (isIdPass)
			{
				int count = _ObjectStore.LogObject(o);
				if(count > 1)
					return false;
			}
			else
			{
				string id = _ObjectStore.SerializedId(o);
				if (!string.IsNullOrEmpty(id))
				{
					this.WriteReference(writer, indent, id);
					return false;
				}
				id = _ObjectStore.ReferenceId(o);
				if (!string.IsNullOrEmpty(id))
					this.WriteIdentifier(writer, indent, id);
			}

			bool previousattribute = false;

			// write fields as attributes
			IList<KeyValuePair<string, PropertyInfo>> fields = this.GetFieldsOrdered(t);
			List<Tuple<KeyValuePair<string, PropertyInfo>, DataMemberAttribute, object>> elementFields = new List<Tuple<KeyValuePair<string, PropertyInfo>, DataMemberAttribute, object>>();
			foreach (KeyValuePair<string, PropertyInfo> pair in fields)
			{
				if (pair.Value != null) // derived fields are null
				{
					if (propertiesToIgnore.Contains(pair.Key))
						continue;
					PropertyInfo f = pair.Value;
					string elementName = "";
					DocXsdFormatEnum? xsdformat = this.GetXsdFormat(f, ref elementName);
					if (xsdformat == DocXsdFormatEnum.Hidden)
						continue;

					Type ft = f.PropertyType, valueType = null;
					DataMemberAttribute dataMemberAttribute = null;
					object value = GetSerializeValue(o, f, out dataMemberAttribute, out valueType);
					if (value == null)
					{
						if(dataMemberAttribute != null && dataMemberAttribute.IsRequired)
						{
							if (ft == stringType)
							{
								if (previousattribute)
								{
									this.WriteAttributeDelimiter(writer);
								}

								previousattribute = true;
								this.WriteStartAttribute(writer, indent, pair.Key);
								this.WriteEndAttribute(writer);
							}

						}
						continue;
					}
					if (dataMemberAttribute != null && (xsdformat == null || xsdformat == DocXsdFormatEnum.Attribute))
					{
						// direct field
						bool isvaluelist = IsValueCollection(ft);
						bool isvaluelistlist = ft.IsGenericType && // e.g. IfcTriangulatedFaceSet.Normals
							typeof(System.Collections.IEnumerable).IsAssignableFrom(ft.GetGenericTypeDefinition()) &&
							IsValueCollection(ft.GetGenericArguments()[0]);

						if (isvaluelistlist || isvaluelist || ft.IsValueType || ft == stringType)
						{
							if (ft == stringType && string.IsNullOrEmpty(value.ToString()) && !dataMemberAttribute.IsRequired)
								continue;

							if (previousattribute)
							{
								this.WriteAttributeDelimiter(writer);
							}

							previousattribute = true;
							this.WriteStartAttribute(writer, indent, pair.Key);

							if (isvaluelistlist)
							{
								ft = ft.GetGenericArguments()[0].GetGenericArguments()[0];
								PropertyInfo fieldValue = ft.GetProperty("Value");
								if (fieldValue != null)
								{
									System.Collections.IList list = (System.Collections.IList)value;
									for (int i = 0; i < list.Count; i++)
									{
										System.Collections.IList listInner = (System.Collections.IList)list[i];
										for (int j = 0; j < listInner.Count; j++)
										{
											if (i > 0 || j > 0)
											{
												writer.Write(" ");
											}

											object elem = listInner[j];
											if (elem != null) // should never be null, but be safe
											{
												elem = fieldValue.GetValue(elem);
												string encodedvalue = System.Security.SecurityElement.Escape(elem.ToString());
												writer.Write(encodedvalue);
											}
										}
									}
								}
								else
								{
									System.Diagnostics.Debug.WriteLine("XXX Error serializing ValueListlist" + o.ToString());
								}
							}
							else if (isvaluelist)
							{
								ft = ft.GetGenericArguments()[0];
								PropertyInfo fieldValue = ft.GetProperty("Value");

								IEnumerable list = (IEnumerable) value;
								int i = 0;
								foreach (object e in list)
								{
									if (i > 0)
									{
										writer.Write(" ");
									}

									if (e != null) // should never be null, but be safe
									{
										object elem = e;
										if (fieldValue != null)
										{
											elem = fieldValue.GetValue(e);
										}

										if (elem is byte[])
										{
											// IfcPixelTexture.Pixels
											writer.Write(SerializeBytes((byte[])elem));
										}
										else
										{
											string encodedvalue = System.Security.SecurityElement.Escape(elem.ToString());
											writer.Write(encodedvalue);
										}
									}

									i++;
								}

							}
							else
							{
								if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(Nullable<>))
								{
									// special case for Nullable types
									ft = ft.GetGenericArguments()[0];
								}

								Type typewrap = null;
								while (ft.IsValueType && !ft.IsPrimitive)
								{
									PropertyInfo fieldValue = ft.GetProperty("Value");
									if (fieldValue != null)
									{
										value = fieldValue.GetValue(value);
										if (typewrap == null)
										{
											typewrap = ft;
										}
										ft = fieldValue.PropertyType;
									}
									else
									{
										break;
									}
								}

								if (ft.IsEnum)
								{
									FieldInfo fieldInfo = ft.GetField(value.ToString());
									if (fieldInfo != null)
									{
										XmlEnumAttribute xmlEnumAttribute = fieldInfo.GetCustomAttribute<XmlEnumAttribute>();
										if (xmlEnumAttribute != null)
											value = xmlEnumAttribute.Name;
										else
											value = value.ToString().ToLowerInvariant();
									}
									else
										value = value.ToString().ToLowerInvariant();
								}
								else if(ft == typeof(bool))
								{
									value = value.ToString().ToLowerInvariant();
								}

								if (value is IList)
								{
									// IfcCompoundPlaneAngleMeasure
									IList list = (IList)value;
									for (int i = 0; i < list.Count; i++)
									{
										if (i > 0)
										{
											writer.Write(" ");
										}

										object elem = list[i];
										if (elem != null) // should never be null, but be safe
										{
											string encodedvalue = System.Security.SecurityElement.Escape(elem.ToString());
											writer.Write(encodedvalue);
										}
									}
								}
								else if (value != null)
								{
									string encodedvalue = System.Security.SecurityElement.Escape(value.ToString());
									writer.Write(encodedvalue);
								}
							}

							this.WriteEndAttribute(writer);
						}
						else
						{
							elementFields.Add(new Tuple<KeyValuePair<string, PropertyInfo>, DataMemberAttribute, object>(pair, dataMemberAttribute, value));
						}
					}
					else
					{
						elementFields.Add(new Tuple<KeyValuePair<string, PropertyInfo>, DataMemberAttribute, object>(pair, dataMemberAttribute, value));
					}
				}
			}

			bool open = false;
			if (elementFields.Count > 0)
			{
				// write direct object references and lists
				foreach (Tuple<KeyValuePair<string, PropertyInfo>, DataMemberAttribute, object> tuple in elementFields) // derived attributes are null
				{
					PropertyInfo f = tuple.Item1.Value;
					Type ft = f.PropertyType;
					bool isvaluelist = IsValueCollection(ft);
					bool isvaluelistlist = ft.IsGenericType && // e.g. IfcTriangulatedFaceSet.Normals
						typeof(IEnumerable).IsAssignableFrom(ft.GetGenericTypeDefinition()) &&
						IsValueCollection(ft.GetGenericArguments()[0]);
					DataMemberAttribute dataMemberAttribute = tuple.Item2;
					object value = tuple.Item3;
					string elementName = f.Name;
					DocXsdFormatEnum? format = GetXsdFormat(f, ref elementName);
					if (format == DocXsdFormatEnum.Element)
					{
						if(ft == typeof(string))
						{
							if (!open)
							{
								WriteOpenElement(writer);
								open = true;
							}
							string str = System.Security.SecurityElement.Escape((string)value);
							WriteStartElementEntity(writer, ref indent, elementName);
							WriteOpenElement(writer, false);
							writer.Write(str);
							indent--;
							WriteEndElementEntity(writer, elementName);

							continue;
						}
						if (ft == typeof(byte[]))
						{
							if (!open)
							{
								WriteOpenElement(writer);
								open = true;
							}
							WriteStartElementEntity(writer, ref indent, elementName);
							WriteOpenElement(writer, false);
							string strval = SerializeBytes((byte[])value);
							writer.Write(strval);
							indent--;
							WriteEndElementEntity(writer, elementName);
							continue;
						}
						bool showit = true; //...check: always include tag if Attribute (even if zero); hide if Element 
						IEnumerable ienumerable = value as IEnumerable;
						string fieldName = tuple.Item1.Key, fieldTypeName = TypeSerializeName(ft);
						if (ienumerable == null)
						{
							if (!ft.IsValueType && !isvaluelist && !isvaluelistlist)
							{

								if (!open)
								{
									WriteOpenElement(writer);
									open = true;
								}
								WriteEntity(writer, ref indent, value, propertiesToIgnore, queue, isIdPass, fieldName, fieldTypeName);
								continue;

							}
						}
						// for collection is must be non-zero (e.g. IfcProject.IsNestedBy)
						else // what about IfcProject.RepresentationContexts if empty? include???
						{
							showit = false;
							XmlTypeAttribute xmlTypeAttribute = ft.GetCustomAttribute<XmlTypeAttribute>();
							if (xmlTypeAttribute == null)
							{
								foreach (object check in ienumerable)
								{
									showit = true; // has at least one element
									break;
								}
							}
							else
							{
								if (!open)
								{
									WriteOpenElement(writer);
									open = true;
								}
								WriteEntity(writer, ref indent, value, propertiesToIgnore, queue, isIdPass, fieldName, fieldTypeName);
								continue;
							}
							if (showit)
							{
								if (!open)
								{
									WriteOpenElement(writer);
									open = true;
								}

								foreach (object subObj in ienumerable)
								{
									WriteEntity(writer, ref indent, subObj, propertiesToIgnore, queue, isIdPass, elementName, "");
								}
							}
						}
					}
					else if (dataMemberAttribute != null)
					{
						// hide fields where inverse attribute used instead
						if (!ft.IsValueType && !isvaluelist && !isvaluelistlist)
						{
							if (value != null)
							{
								IEnumerable ienumerable = value as IEnumerable;
								if (ienumerable == null)
								{
									string fieldName = tuple.Item1.Key, fieldTypeName = TypeSerializeName(ft);
									if (string.Compare(fieldName, fieldTypeName) == 0)
									{
										if (!open)
										{
											WriteOpenElement(writer);
											open = true;
										}
										WriteEntity(writer, ref indent, value, propertiesToIgnore, queue, isIdPass, fieldName, fieldTypeName);
										continue;
									}

								}
								bool showit = true;

								if (!f.IsDefined(typeof(System.ComponentModel.DataAnnotations.RequiredAttribute), false) && ienumerable != null)
								{
									showit = false;
									foreach (object sub in ienumerable)
									{
										showit = true;
										break;
									}
								}

								if (showit)
								{
									if (!open)
									{
										WriteOpenElement(writer);
										open = true;
									}

									if (previousattribute)
									{
										this.WriteAttributeDelimiter(writer);
									}
									previousattribute = true;

									WriteAttribute(writer, ref indent, o, new HashSet<string>(), f, queue, isIdPass);
								}
							}
						}
					}
					else
					{
					    // inverse
						// record it for downstream serialization
						if (value is IEnumerable)
						{
							IEnumerable invlist = (IEnumerable)value;
							foreach (object invobj in invlist)
							{
								if (!_ObjectStore.isSerialized(invobj))
									queue.Enqueue(invobj);
							}
						}
					}
				}
			}
			IEnumerable enumerable = o as IEnumerable;
			if(enumerable != null)
			{
				if(!open)
				{
					WriteOpenElement(writer);
					open = true;
				}
				foreach (object obj in enumerable)
					WriteEntity(writer, ref indent, obj, propertiesToIgnore, queue, isIdPass, "", "");
			}
			else if (!isIdPass)
			{
				PropertyInfo property = detectTextAttribute(t);
				if(property != null)
				{
					object obj = property.GetValue(o);
					if (!open)
					{
						WriteOpenElement(writer,false);
						open = true;
					}
					writer.WriteLine(obj.ToString().TrimEnd());
				}
			}
			if (!open)
			{
				this.WriteAttributeTerminator(writer);
				return false;
			}
			return open;
		}

		private void WriteAttribute(StreamWriter writer, ref int indent, object o, HashSet<string> propertiesToIgnore, PropertyInfo f, Queue<object> queue, bool isIdPass)
		{
			object v = f.GetValue(o);
			if (v == null)
				return;
			string memberName = PropertySerializeName(f);
			Type objectType = o.GetType();
			string typeName = TypeSerializeName(o.GetType());
			if(string.Compare(memberName, typeName) == 0)
			{
				WriteEntity(writer, ref indent, v, propertiesToIgnore, queue, isIdPass, memberName, typeName);
				return;
			}
			this.WriteStartElementAttribute(writer, ref indent, memberName);

			int zeroIndent = 0;
			Type ft = f.PropertyType;
			PropertyInfo fieldValue = null;
			if (ft.IsValueType)
			{
				if (ft == typeof(DateTime)) // header datetime
				{
					this.WriteOpenElement(writer, false);
					DateTime datetime = (DateTime)v;
					string datetimeiso8601 = datetime.ToString("s", System.Globalization.CultureInfo.InvariantCulture);
					writer.Write(datetimeiso8601);
					indent--; 
					WriteEndElementAttribute(writer, ref zeroIndent, f.Name);
					return;
				}
				fieldValue = ft.GetProperty("Value"); // if it exists for value type
			}
			else if (ft == typeof(string))
			{
				this.WriteOpenElement(writer, false);
				string strval = System.Security.SecurityElement.Escape((string)v);
				writer.Write(strval);
				indent--;
				WriteEndElementAttribute(writer, ref zeroIndent, f.Name);
				return;
			}
			else if (ft == typeof(byte[]))
			{
				this.WriteOpenElement(writer, false);
				string strval = SerializeBytes((byte[])v); 
				writer.Write(strval);
				indent--;
				WriteEndElementAttribute(writer, ref zeroIndent, f.Name);
				return;
			}
			DocXsdFormatEnum? format = GetXsdFormat(f);
			if (format == null || format != DocXsdFormatEnum.Attribute || f.Name.Equals("InnerCoordIndices")) //hackhack -- need to resolve...
			{
				this.WriteOpenElement(writer);
			}

			if (IsEntityCollection(ft))
			{
				IEnumerable list = (IEnumerable)v;

				// for nested lists, flatten; e.g. IfcBSplineSurfaceWithKnots.ControlPointList
				Type[] genericTypes = ft.GetGenericArguments();
				if (genericTypes != null && genericTypes.Length > 0 && typeof(IEnumerable).IsAssignableFrom(genericTypes[0]))
				{
					// special case
					if (f.Name.Equals("InnerCoordIndices")) //hack
					{
						foreach (System.Collections.IEnumerable innerlist in list)
						{
							string entname = "Seq-IfcPositiveInteger-wrapper"; // hack
							this.WriteStartElementEntity(writer, ref indent, entname);
							this.WriteOpenElement(writer);
							foreach (object e in innerlist)
							{
								object ev = e.GetType().GetField("Value").GetValue(e);

								writer.Write(ev.ToString());
								writer.Write(" ");
							}
							writer.WriteLine();
							this.WriteEndElementEntity(writer, ref indent, entname);
						}
						WriteEndElementAttribute(writer, ref indent, f.Name);
						return;
					}

					ArrayList flatlist = new ArrayList();
					foreach (IEnumerable innerlist in list)
					{
						foreach (object e in innerlist)
						{
							flatlist.Add(e);
						}
					}

					list = flatlist;
				}

				// required if stated or if populated.....

				foreach (object e in list)
				{
					// if collection is non-zero and contains entity instances
					if (e != null && !e.GetType().IsValueType && !(e is string) && !(e is System.Collections.IEnumerable))
					{
						this.WriteCollectionStart(writer, ref indent);
					}
					break;
				}

				bool needdelim = false;
				foreach (object e in list)
				{
					if (e != null) // could be null if buggy file -- not matching schema
					{
						if (e is IEnumerable)
						{
							IEnumerable listInner = (IEnumerable)e;
							foreach (object oinner in listInner)//j = 0; j < listInner.Count; j++)
							{
								object oi = oinner;//listInner[j];

								Type et = oi.GetType();
								while (et.IsValueType && !et.IsPrimitive)
								{
									PropertyInfo fieldColValue = et.GetProperty("Value");
									if (fieldColValue != null)
									{
										oi = fieldColValue.GetValue(oi);
										et = fieldColValue.PropertyType;
									}
									else
									{
										break;
									}
								}

								// write each value in sequence with spaces delimiting
								string sval = oi.ToString();
								writer.Write(sval);
								writer.Write(" ");
							}
						}
						else if (!e.GetType().IsValueType && !(e is string)) // presumes an entity
						{
							if (needdelim)
							{
								this.WriteCollectionDelimiter(writer, indent);
							}

							if (format != null && format == DocXsdFormatEnum.Attribute)
							{
								// only one item, e.g. StyledByItem\IfcStyledItem
								this.WriteEntityStart(writer, ref indent);
								bool closeelem = this.WriteEntityAttributes(writer, ref indent, e, propertiesToIgnore, queue, isIdPass);
								if (!closeelem)
								{
									this.WriteCloseElementAttribute(writer, ref indent);
									/////?????return;//TWC:20180624
								}
								else
								{
									this.WriteEntityEnd(writer, ref indent);
								}
								break; // if more items, skip them -- buggy input data; no way to encode
							}
							else
							{
								this.WriteEntity(writer, ref indent, e, propertiesToIgnore, queue, isIdPass, "", "");
							}

							needdelim = true;
						}
						else
						{
							// if flat-list (e.g. structural load Locations) or list of strings (e.g. IfcPostalAddress.AddressLines), must wrap
							this.WriteValueWrapper(writer, ref indent, e);
						}
					}
				}

				foreach (object e in list)
				{
					if (e != null && !e.GetType().IsValueType && !(e is string))
					{
						this.WriteCollectionEnd(writer, ref indent);
					}
					break;
				}
			} // otherwise if not collection...
			else if (ft.IsInterface && v is ValueType)
			{
				this.WriteValueWrapper(writer, ref indent, v);
			}
			else if (fieldValue != null) // must be IfcBinary -- but not DateTime or other raw primitives
			{
				v = fieldValue.GetValue(v);
				if (v is byte[])
				{
					this.WriteOpenElement(writer);

					// binary data type - we don't support anything other than 8-bit aligned, though IFC doesn't either so no point in supporting extraBits
					byte[] bytes = (byte[])v;

					StringBuilder sb = new StringBuilder(bytes.Length * 2);
					for (int i = 0; i < bytes.Length; i++)
					{
						byte b = bytes[i];
						sb.Append(HexChars[b / 0x10]);
						sb.Append(HexChars[b % 0x10]);
					}
					v = sb.ToString();
					writer.WriteLine(v);
				}
			}
			else
			{
				if (format != null && format == DocXsdFormatEnum.Attribute)
				{
					this.WriteEntityStart(writer, ref indent);

					Type vt = v.GetType();
					if (ft != vt)
					{
						this.WriteType(writer, indent, vt.Name);
					}
					bool closeelem = this.WriteEntityAttributes(writer, ref indent, v, new HashSet<string>(), queue, isIdPass);

					if (!closeelem)
					{
						this.WriteCloseElementEntity(writer, ref indent);
						return;
					}

					this.WriteEntityEnd(writer, ref indent);
				}
				else
				{
					// if rooted, then check if we need to use reference; otherwise embed
					this.WriteEntity(writer, ref indent, v, new HashSet<string>(), queue, isIdPass, "", "");
				}
			}

			WriteEndElementAttribute(writer, ref indent, memberName);
		}

		private void WriteValueWrapper(StreamWriter writer, ref int indent, object v)
		{
			Type vt = v.GetType();
			PropertyInfo fieldValue = vt.GetProperty("Value");
			while (fieldValue != null)
			{
				v = fieldValue.GetValue(v);
				if (v != null)
				{
					Type wt = v.GetType();
					if (wt.IsEnum || wt == typeof(bool))
					{
						v = v.ToString().ToLowerInvariant();
					}

					fieldValue = wt.GetProperty("Value");
				}
				else
				{
					fieldValue = null;
				}
			}

			string encodedvalue = String.Empty;
			if (v is IEnumerable && !(v is string))
			{
				// IfcIndexedPolyCurve.Segments
				IEnumerable list = (IEnumerable)v;
				StringBuilder sb = new StringBuilder();
				foreach (object o in list)
				{
					if (sb.Length > 0)
					{
						sb.Append(" ");
					}

					PropertyInfo fieldValueInner = o.GetType().GetProperty("Value");
					if (fieldValueInner != null)
					{
						//...todo: recurse for multiple levels of indirection, e.g. 
						object vInner = fieldValueInner.GetValue(o);
						sb.Append(vInner.ToString());
					}
					else
					{
						sb.Append(o.ToString());
					}
				}

				encodedvalue = sb.ToString();
			}
			else if (v != null)
			{
				encodedvalue = System.Security.SecurityElement.Escape(v.ToString());
			}

			this.WriteTypedValue(writer, ref indent, vt.Name, encodedvalue);
		}

		protected DocXsdFormatEnum? GetXsdFormat(PropertyInfo propertyInfo)
		{
			string elementName = "";
			return GetXsdFormat(propertyInfo, ref elementName);
		}
		protected DocXsdFormatEnum? GetXsdFormat(PropertyInfo field, ref string elementName)
		{
			// direct fields marked ignore are ignored
			if (field.IsDefined(typeof(XmlIgnoreAttribute)))
				return DocXsdFormatEnum.Hidden;

			if (field.IsDefined(typeof(XmlAttributeAttribute)))
				return null;

			XmlElementAttribute attrElement = field.GetCustomAttribute<XmlElementAttribute>();
			if (attrElement != null)
			{
                if (!String.IsNullOrEmpty(attrElement.ElementName))
                    elementName = attrElement.ElementName;
				//{
					return DocXsdFormatEnum.Element; // tag according to attribute AND element name
				//}
				//else
				//{
				//	return DocXsdFormatEnum.Attribute; // tag according to attribute name
				//}
			}

			// inverse fields not marked with XmlElement are ignored
			if (attrElement == null && field.IsDefined(typeof(InversePropertyAttribute)))
				return DocXsdFormatEnum.Hidden;

			return null; //?
		}

		protected enum DocXsdFormatEnum
		{
			Hidden = 1,//IfcDoc.Schema.CNF.exp_attribute.no_tag,    // for direct attribute, don't include as inverse is defined instead
			Attribute = 2,//IfcDoc.Schema.CNF.exp_attribute.attribute_tag, // represent as attribute
			Element = 3,//IfcDoc.Schema.CNF.exp_attribute.double_tag,   // represent as element
		}

		protected internal class QueuedObjects
		{
			private Dictionary<string, QueuedObject> queued = new Dictionary<string, QueuedObject>();

			internal void Queue(string sid, object o, PropertyInfo propertyInfo)
			{
				QueuedObject queuedObject = null;
				if (!queued.TryGetValue(sid, out queuedObject))
					queuedObject = queued[sid] = new QueuedObject();
				queuedObject.Queue(o, propertyInfo);
			}
			internal void DeQueue(string sid, object value)
			{
				QueuedObject queuedObject = null;
				if(queued.TryGetValue(sid, out queuedObject))
				{
					queuedObject.Dequeue(value);
					queued.Remove(sid);
				}
			}
		}
		protected internal class QueuedObject
		{
			private List<Tuple<object,PropertyInfo>> attributes = new List<Tuple<object,PropertyInfo>>();

			internal void Queue(object o, PropertyInfo propertyInfo)
			{
				if (propertyInfo == null)
					return;
				attributes.Add(new Tuple<object, PropertyInfo>(o, propertyInfo));
			}
			
			internal void Dequeue(object value)
			{
				foreach (Tuple<object, PropertyInfo> tuple in attributes)
				{
					object obj = tuple.Item1;
					PropertyInfo propertyInfo = tuple.Item2;
					if(IsEntityCollection(propertyInfo.PropertyType))
					{
						IEnumerable list = propertyInfo.GetValue(obj) as IEnumerable;
						Type typeCollection = list.GetType();
						MethodInfo methodAdd = typeCollection.GetMethod("Add");
						if (methodAdd == null)
						{
							throw new Exception("Unsupported collection type " + typeCollection.Name);
						}
						methodAdd.Invoke(list, new object[] { value });
					}
					else
						propertyInfo.SetValue(obj, value);
				}
			}
		}

		protected internal class ObjectStore
		{
			private class Item
			{
				internal string Id = "";
				internal int Count = 1;
				internal bool Serialized = false;

				internal Item(object obj, bool useUniqueIdReferences, long index)
				{
					Id = UniqueId(obj, useUniqueIdReferences, index);
				}
				public override string ToString()
				{
					return Id + " " + Count + " " + Serialized;
				}

				private string validId(string str)
				{
					string result = Regex.Replace(str.Trim(), @"\s+", "_");
					result = Regex.Replace(result, @"[^0-9a-zA-Z_]+", string.Empty);
					char c = result[0];
					return ((char.IsDigit(c) || c == '$' || c == '-' || c == '.') ? "x" : "") + result;
				}

				private string UniqueId(object o, bool useUniqueIdReferences, long index)
				{
					if (useUniqueIdReferences)
					{
						Type ot = o.GetType();
						PropertyInfo propertyInfo = ot.GetProperty("id", typeof(string));

						if (propertyInfo != null)
						{
							object obj = propertyInfo.GetValue(o);
							if (obj != null)
							{
								string str = obj.ToString();
								if (!string.IsNullOrEmpty(str))
								{
									try
									{
										return validId(str);
									} catch(Exception) { }
								}
							}
						}

						propertyInfo = ot.GetProperty("GlobalId");
						if (propertyInfo != null)
						{
							object obj = propertyInfo.GetValue(o);
							if (obj != null)
							{
								string globalId = obj.ToString();
								if (!string.IsNullOrEmpty(globalId))
								{
									PropertyInfo propertyInfoName = ot.GetProperty("Name");
									if (propertyInfoName != null)
									{
										obj = propertyInfoName.GetValue(o);
										if (obj != null)
										{
											string name = obj.ToString();
											if (!string.IsNullOrEmpty(name))
												return validId(name + "_" + globalId);
										}
									}
									return validId(globalId);
								}
							}
						}
					}
					return "i" + index;
				}
			}
			internal bool UseUniqueIdReferences { get; set; } = true;
			private ObjectIDGenerator IdGenerator = new ObjectIDGenerator();
			private Dictionary<long,Item> Items = new Dictionary<long, Item>() { };

			internal string SerializedId(object obj)
			{
				bool firstTime = true;
				long index = IdGenerator.GetId(obj, out firstTime);
				if (firstTime)
					return "";

				Item item = null;
				if (Items.TryGetValue(index, out item))
				{
					if (item.Count > 1)
					{
						if (item.Serialized)
							return item.Id;
					}
				}
				return null;
			}
			internal string ReferenceId(object obj)
			{
				bool firstTime = true;
				long index = IdGenerator.GetId(obj, out firstTime);
				if (firstTime)
					return "";

				Item item = null;
				if (Items.TryGetValue(index, out item))
				{
					item.Serialized = true;
					if (item.Count > 1)
						return item.Id;
				}
				return null;
			}
			internal int LogObject(object obj)
			{
				bool firstTime = true;
				long index = IdGenerator.GetId(obj, out firstTime);
				if (firstTime)
					Items[index] = new Item(obj, UseUniqueIdReferences, index);
				else
				{
					Item item = null;
					if (Items.TryGetValue(index, out item))
					{
						item.Count++;
						return item.Count;
					}
					else
						Items[index] = new Item(obj, UseUniqueIdReferences, index);
				}
				return 1;
			}
			internal bool isSerialized(object obj)
			{
				bool firstTime = true;
				long index = IdGenerator.GetId(obj, out firstTime);
				Item item = null;
				if (Items.TryGetValue(index, out item))
				{
					return item.Serialized;
				}
				return false;
			}

			internal void MarkSerialized(object obj)
			{
				bool firstTime = true;
				long index = IdGenerator.GetId(obj, out firstTime);
				Item item = null;
				if (Items.TryGetValue(index, out item))
					item.Serialized = true;
				else
					Items[index] = new Item(obj, UseUniqueIdReferences, index) { Serialized = true, Count = 2 };
			}
			internal void UnMarkSerialized(object obj)
			{
				bool firstTime = true;
				long index = IdGenerator.GetId(obj, out firstTime);
				Item item = null;
				if (Items.TryGetValue(index, out item))
					item.Serialized = false;
				else
					Items[index] = new Item(obj, UseUniqueIdReferences, index) { Serialized = false, Count = 2 };
			}
		}


		public List<object> ExtractObjects(object o, Type baseType)
		{
			ObjectIDGenerator idGenerator = new ObjectIDGenerator();
			List<object> result = new List<object>();
			extractObjects(o, idGenerator, baseType, result);
			return result;
		}

		private void extractObjects(object o, ObjectIDGenerator idGenerator, Type baseType, List<object> objects)
		{
			bool firstTime = false;
			idGenerator.GetId(o, out firstTime);
			if (firstTime)
			{
				Type objectType = o.GetType();
				if(objectType == baseType || objectType.IsSubclassOf(baseType))
					objects.Add(o);
				IEnumerable<PropertyInfo> fields = GetFieldsOrdered(o.GetType()).Select(x=>x.Value);
				foreach(PropertyInfo field in fields)
				{
					if (field == null)
						continue;
					object val = field.GetValue(o);
					if (val == null)
						continue;

					Type t = val.GetType();
					if (typeof(IEnumerable).IsAssignableFrom(t) && t.IsGenericType)
					{
						IEnumerable enumerable = (IEnumerable) val;
						foreach (object obj in enumerable)
							extractObjects(obj, idGenerator, baseType, objects);
					}
					else if(!t.IsValueType)
						extractObjects(val, idGenerator, baseType, objects);
				}
			}
		}
	}

}


