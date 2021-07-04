using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Microsoft.CSharp;

using IfcDoc.Schema;
using IfcDoc.Format.CSC;
using IfcDoc.Schema.DOC;
using BuildingSmart.Serialization.Step;
using IfcDoc.Format.XML;
using IfcDoc.Schema.SVG;
using IfcDoc.Documentation;

namespace IfcDoc
{
	[Flags]
	public enum FolderStorageOptions
	{
		None = 0,

		Schemas = 1,
		Exchanges = 2,
		Examples = 4,
		Localization = 8,
	}

	public static class FolderStorage
	{
		/// <summary>
		/// Loads a .NET DLL
		/// </summary>
		/// <param name="project"></param>
		/// <param name="path"></param>
		public static void LoadLibrary(DocProject project, string path)
		{
			Assembly assem = Assembly.LoadFile(path);
			LoadAssembly(project, assem);
		}

		/// <summary>
		/// Loads all content from a folder hierarchy (overlaying anything already existing)
		/// </summary>
		/// <param name="project"></param>
		/// <param name="path"></param>
		public static void LoadFolder(DocProject project, string path)
		{
			// get all files within folder hierarchy
			string pathSchema = path + @"\schemas";
			IEnumerable<string> en = System.IO.Directory.EnumerateFiles(pathSchema, "*.cs", System.IO.SearchOption.AllDirectories);
			List<string> list = new List<string>();
			foreach (string s in en)
			{
				list.Add(s);
			}
			string[] files = list.ToArray();

			Dictionary<string, string> options = new Dictionary<string, string> { { "CompilerVersion", "v4.0" } };
			Microsoft.CSharp.CSharpCodeProvider prov = new Microsoft.CSharp.CSharpCodeProvider(options);
			System.CodeDom.Compiler.CompilerParameters parms = new System.CodeDom.Compiler.CompilerParameters();
			parms.GenerateInMemory = true;
			parms.GenerateExecutable = false;
			parms.ReferencedAssemblies.Add("System.dll");
			parms.ReferencedAssemblies.Add("System.Core.dll");
			parms.ReferencedAssemblies.Add("System.ComponentModel.dll");
			parms.ReferencedAssemblies.Add("System.ComponentModel.DataAnnotations.dll");
			parms.ReferencedAssemblies.Add("System.Data.dll");
			parms.ReferencedAssemblies.Add("System.Runtime.Serialization.dll");
			parms.ReferencedAssemblies.Add("System.Xml.dll");

			System.CodeDom.Compiler.CompilerResults results = prov.CompileAssemblyFromFile(parms, files);
			System.Reflection.Assembly assem = results.CompiledAssembly;

			LoadAssembly(project, assem);

			// EXPRESS rules (eventually in C#, though .exp file snippets for now)
			en = System.IO.Directory.EnumerateFiles(pathSchema, "*.exp", System.IO.SearchOption.AllDirectories);
			foreach (string file in en)
			{
				string name = Path.GetFileNameWithoutExtension(file);
				string expr = null;
				using (StreamReader readExpr = new StreamReader(file, Encoding.UTF8))
				{
					if (name.Contains('-'))
					{
						// where rule
						expr = readExpr.ReadToEnd();
					}
					else
					{
						// function: skip first and last lines
						readExpr.ReadLine();

						StringBuilder sbExpr = new StringBuilder();
						while (!readExpr.EndOfStream)
						{
							string line = readExpr.ReadLine();
							if (!readExpr.EndOfStream)
							{
								sbExpr.AppendLine(line);
							}
						}

						expr = sbExpr.ToString();
					}
				}

				if (name.Contains('-'))
				{
					// where rule 
					string[] parts = name.Split('-');
					if (parts.Length == 2)
					{
						DocWhereRule docWhere = new DocWhereRule();
						docWhere.Name = parts[1];
						docWhere.Expression = expr;

						DocDefinition docDef = project.GetDefinition(parts[0]);
						if (docDef is DocEntity)
						{
							DocEntity docEnt = (DocEntity)docDef;
							docEnt.WhereRules.Add(docWhere);
						}
						else if (docDef is DocDefined)
						{
							DocDefined docEnt = (DocDefined)docDef;
							docEnt.WhereRules.Add(docWhere);
						}
						else if (docDef == null)
						{
							//... global rule...
						}
					}
				}
				else
				{
					// function
					string schema = Path.GetDirectoryName(file);
					schema = Path.GetDirectoryName(schema);
					schema = Path.GetFileName(schema);
					DocSchema docSchema = project.GetSchema(schema);
					if (docSchema != null)
					{
						DocFunction docFunction = new DocFunction();
						docSchema.Functions.Add(docFunction);
						docFunction.Name = name;
						docFunction.Expression = expr;
					}
				}
			}

			// now, hook up html documentation
			en = System.IO.Directory.EnumerateFiles(pathSchema, "*.htm", System.IO.SearchOption.AllDirectories);
			foreach (string file in en)
			{
				string name = Path.GetFileNameWithoutExtension(file);
				DocObject docObj = null;
				if (name == "schema")
				{
					string schema = Path.GetDirectoryName(file);
					schema = Path.GetFileName(schema);
					docObj = project.GetSchema(schema);
				}
				else if (name.Contains('-'))
				{
					// where rule 
					string[] parts = name.Split('-');
					if (parts.Length == 2)
					{
						DocDefinition docDef = project.GetDefinition(parts[0]);
						if (docDef is DocEntity)
						{
							DocEntity docEnt = (DocEntity)docDef;
							foreach (DocWhereRule docWhereRule in docEnt.WhereRules)
							{
								if (docWhereRule.Name.Equals(parts[1]))
								{
									docObj = docWhereRule;
									break;
								}
							}
						}
						else if (docDef is DocDefined)
						{
							DocDefined docEnt = (DocDefined)docDef;
							foreach (DocWhereRule docWhereRule in docEnt.WhereRules)
							{
								if (docWhereRule.Name.Equals(parts[1]))
								{
									docObj = docWhereRule;
									break;
								}
							}
						}
					}
				}
				else
				{
					docObj = project.GetDefinition(name);

					if (docObj == null)
					{
						docObj = project.GetFunction(name);
					}

				}

				if (docObj != null)
				{
					using (StreamReader readHtml = new StreamReader(file, Encoding.UTF8))
					{
						docObj.Documentation = readHtml.ReadToEnd();
					}
				}
			}

			// load schema diagrams
			en = System.IO.Directory.EnumerateFiles(pathSchema, "*.svg", System.IO.SearchOption.AllDirectories);
			foreach (string file in en)
			{
				string schema = Path.GetDirectoryName(file);
				schema = Path.GetFileName(schema);

				DocSchema docSchema = project.GetSchema(schema);
				if (docSchema != null)
				{
					using (IfcDoc.Schema.SVG.SchemaSVG schemaSVG = new IfcDoc.Schema.SVG.SchemaSVG(file, docSchema, project, DiagramFormat.UML))
					{
						using (FormatXML format = new FormatXML(file, typeof(svg), "http://www.w3.org/2000/svg"))
						{
							format.Load();
							svg s = format.Instance as svg;
							schemaSVG.Load(s);
						}
					}
				}
			}

			// psets, qsets
			//...

			// exchanges
			en = System.IO.Directory.EnumerateFiles(path, "*.mvdxml", System.IO.SearchOption.AllDirectories);
			foreach (string file in en)
			{
				IfcDoc.Format.MVDXML.FormatMVDXML.Load(project, file);
			}

			// examples
			string pathExamples = path + @"\examples";
			if (Directory.Exists(pathExamples))
			{
				en = System.IO.Directory.EnumerateFiles(pathExamples, "*.htm", SearchOption.TopDirectoryOnly);
				foreach (string file in en)
				{
					DocExample docExample = new DocExample();
					docExample.Name = Path.GetFileNameWithoutExtension(file);
					project.Examples.Add(docExample);

					using (StreamReader reader = new StreamReader(file))
					{
						docExample.Documentation = reader.ReadToEnd();
					}

					string dirpath = file.Substring(0, file.Length - 4);
					if (Directory.Exists(dirpath))
					{
						IEnumerable<string> suben = System.IO.Directory.EnumerateFiles(dirpath, "*.ifc", SearchOption.TopDirectoryOnly);
						foreach (string ex in suben)
						{
							DocExample docEx = new DocExample();
							docEx.Name = Path.GetFileNameWithoutExtension(ex);
							docExample.Examples.Add(docEx);

							// read the content of the file
							using (FileStream fs = new FileStream(ex, FileMode.Open, FileAccess.Read))
							{
								docEx.File = new byte[fs.Length];
								fs.Read(docEx.File, 0, docEx.File.Length);
							}

							// read documentation
							string exdoc = ex.Substring(0, ex.Length - 4) + ".htm";
							if (File.Exists(exdoc))
							{
								using (StreamReader reader = new StreamReader(exdoc))
								{
									docEx.Documentation = reader.ReadToEnd();
								}
							}

						}
					}
				}
			}

			// localization
			en = System.IO.Directory.EnumerateFiles(path, "*.txt", System.IO.SearchOption.AllDirectories);
			foreach (string file in en)
			{
				using (FormatCSV format = new FormatCSV(file))
				{
					try
					{
						format.Instance = project;
						format.Load();
					}
					catch
					{
					}
				}
			}
		}

		public static void LoadAssembly(DocProject project, Assembly assem)
		{
			// look through classes of assembly
			foreach (Type t in assem.GetTypes())
			{
				if (t.Namespace != null)
				{
					string[] namespaceparts = t.Namespace.Split('.');
					string schema = namespaceparts[namespaceparts.Length - 1];
					DocSection docSection = null;
					if (t.Namespace.EndsWith("Resource"))
					{
						docSection = project.Sections[7];
					}
					else if (t.Namespace.EndsWith("Domain"))
					{
						docSection = project.Sections[6];
					}
					else if (t.Namespace.Contains("Shared"))
					{
						docSection = project.Sections[5];
					}
					else
					{
						docSection = project.Sections[4]; // kernel, extensions
					}

					// find schema
					DocSchema docSchema = null;
					foreach (DocSchema docEachSchema in docSection.Schemas)
					{
						if (docEachSchema.Name.Equals(schema))
						{
							docSchema = docEachSchema;
							break;
						}
					}

					if (docSchema == null)
					{
						docSchema = new DocSchema();
						docSchema.Name = schema;
						docSection.Schemas.Add(docSchema);
						docSection.SortSection();
					}

					DocDefinition docDef = null;
					if (t.IsEnum)
					{
						DocEnumeration docEnum = new DocEnumeration();
						docSchema.Types.Add(docEnum);
						docDef = docEnum;

						System.Reflection.FieldInfo[] fields = t.GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
						foreach (System.Reflection.FieldInfo field in fields)
						{
							DocConstant docConst = new DocConstant();
							docEnum.Constants.Add(docConst);
							docConst.Name = field.Name;

							DescriptionAttribute[] attrs = (DescriptionAttribute[])field.GetCustomAttributes(typeof(DescriptionAttribute), false);
							if (attrs.Length == 1)
							{
								docConst.Documentation = attrs[0].Description;
							}
						}
					}
					else if (t.IsValueType)
					{
						PropertyInfo[] fields = t.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
						if (fields.Length > 0)
						{
							Type typeField = fields[0].PropertyType;

							DocDefined docDefined = new DocDefined();
							docSchema.Types.Add(docDefined);
							docDef = docDefined;
							docDefined.DefinedType = FormatCSC.GetExpressType(typeField);

							if (typeField.IsGenericType)
							{
								Type typeGeneric = typeField.GetGenericTypeDefinition();
								typeField = typeField.GetGenericArguments()[0];
								if (typeGeneric == typeof(ISet<>) ||
									typeGeneric == typeof(HashSet<>))
								{
									docDefined.Aggregation = new DocAttribute();
									docDefined.Aggregation.AggregationType = (int)DocAggregationEnum.SET;
								}
								else if (typeGeneric == typeof(IList<>) ||
									typeGeneric == typeof(List<>))
								{
									docDefined.Aggregation = new DocAttribute();
									docDefined.Aggregation.AggregationType = (int)DocAggregationEnum.LIST;
								}
							}

							MaxLengthAttribute mxa = (MaxLengthAttribute)fields[0].GetCustomAttribute(typeof(MaxLengthAttribute));
							if (mxa != null)
							{
								docDefined.Length = mxa.Length;
							}
						}
					}
					else if (t.IsInterface)
					{
						DocSelect docSelect = new DocSelect();
						docSchema.Types.Add(docSelect);
						docDef = docSelect;
					}
					else if (t.IsClass)
					{
						DocEntity docEntity = new DocEntity();
						docSchema.Entities.Add(docEntity);
						docDef = docEntity;

						if (t.BaseType != null)
						{
							if (t.BaseType != typeof(object) && t.BaseType.Name != "SEntity") // back-compat for reflecting on IfcDoc types to generate Express
							{
								docEntity.BaseDefinition = t.BaseType.Name;
							}
						}

						docEntity.IsAbstract = t.IsAbstract;

						Dictionary<int, DocAttribute> attrsDirect = new Dictionary<int, DocAttribute>();
						List<DocAttribute> attrsInverse = new List<DocAttribute>();
						PropertyInfo[] fields = t.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | BindingFlags.DeclaredOnly);
						foreach (PropertyInfo field in fields)
						{
							DocAttribute docAttr = new DocAttribute();
							docAttr.Name = field.Name;

							Type typeField = field.PropertyType;
							if (typeField.IsGenericType)
							{
								Type typeGeneric = typeField.GetGenericTypeDefinition();
								typeField = typeField.GetGenericArguments()[0];
								if (typeGeneric == typeof(Nullable<>))
								{
									docAttr.IsOptional = true;
								}
								else if (typeGeneric == typeof(ISet<>) ||
									typeGeneric == typeof(HashSet<>))
								{
									docAttr.AggregationType = (int)DocAggregationEnum.SET;
								}
								else if (typeGeneric == typeof(IList<>) ||
									typeGeneric == typeof(List<>))
								{
									docAttr.AggregationType = (int)DocAggregationEnum.LIST;
								}
							}

							// primitives
							docAttr.DefinedType = FormatCSC.GetExpressType(typeField);

							MinLengthAttribute mla = (MinLengthAttribute)field.GetCustomAttribute(typeof(MinLengthAttribute));
							if (mla != null)
							{
								docAttr.AggregationLower = mla.Length.ToString();
							}

							MaxLengthAttribute mxa = (MaxLengthAttribute)field.GetCustomAttribute(typeof(MaxLengthAttribute));
							if (mxa != null)
							{
								docAttr.AggregationUpper = mxa.Length.ToString();
							}

							DescriptionAttribute da = (DescriptionAttribute)field.GetCustomAttribute(typeof(DescriptionAttribute));
							if (da != null)
							{
								docAttr.Documentation = da.Description;
							}

							DataMemberAttribute dma = (DataMemberAttribute)field.GetCustomAttribute(typeof(DataMemberAttribute));
							if (dma != null)
							{
								attrsDirect.Add(dma.Order, docAttr);

								RequiredAttribute rqa = (RequiredAttribute)field.GetCustomAttribute(typeof(RequiredAttribute));
								if (rqa == null)
								{
									docAttr.IsOptional = true;
								}

								CustomValidationAttribute cva = (CustomValidationAttribute)field.GetCustomAttribute(typeof(CustomValidationAttribute));
								if (cva != null)
								{
									docAttr.IsUnique = true;
								}
							}
							else
							{
								InversePropertyAttribute ipa = (InversePropertyAttribute)field.GetCustomAttribute(typeof(InversePropertyAttribute));
								if (ipa != null)
								{
									docAttr.Inverse = ipa.Property;
									attrsInverse.Add(docAttr);
								}
							}

							// xml
							XmlIgnoreAttribute xia = (XmlIgnoreAttribute)field.GetCustomAttribute(typeof(XmlIgnoreAttribute));
							if (xia != null)
							{
								docAttr.XsdFormat = DocXsdFormatEnum.Hidden;
							}
							else
							{
								XmlElementAttribute xea = (XmlElementAttribute)field.GetCustomAttribute(typeof(XmlElementAttribute));
								if (xea != null)
								{
									if (!String.IsNullOrEmpty(xea.ElementName))
									{
										docAttr.XsdFormat = DocXsdFormatEnum.Element;
									}
									else
									{
										docAttr.XsdFormat = DocXsdFormatEnum.Attribute;
									}
								}
							}
						}

						foreach (DocAttribute docAttr in attrsDirect.Values)
						{
							docEntity.Attributes.Add(docAttr);
						}

						foreach (DocAttribute docAttr in attrsInverse)
						{
							docEntity.Attributes.Add(docAttr);
						}

						// get derived attributes based on properties
#if false
                        PropertyInfo[] props = t.GetProperties(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public);
                        foreach (PropertyInfo prop in props)
                        {
                            // if no backing field, then derived
                            FieldInfo field = t.GetField("_" + prop.Name, BindingFlags.NonPublic | BindingFlags.Instance);
                            if (field == null)
                            {
                                DocAttribute docDerived = new DocAttribute();
                                docDerived.Name = prop.Name;
                                docEntity.Attributes.Add(docDerived);
                            }
                        }
#endif
					}

					if (docDef != null)
					{
						docDef.Name = t.Name;
						docDef.Uuid = t.GUID;
					}

					docSchema.SortTypes();
					docSchema.SortEntities();
				}
			}

			// pass 2: hook up selects
			foreach (Type t in assem.GetTypes())
			{
				Type[] typeInterfaces = t.GetInterfaces();

				Type[] typeInherit = null;
				if (t.BaseType != null)
				{
					typeInherit = t.BaseType.GetInterfaces();
				}

				if (typeInterfaces.Length > 0)
				{
					foreach (Type typeI in typeInterfaces)
					{
						if (typeInherit == null || !typeInherit.Contains<Type>(typeI))
						{
							DocSelect docSelect = project.GetDefinition(typeI.Name) as DocSelect;
							if (docSelect != null)
							{
								DocSelectItem docItem = new DocSelectItem();
								docItem.Name = t.Name;
								docSelect.Selects.Add(docItem);
							}
						}
					}
				}
			}


		}

		/// <summary>
		/// Saves all content to folder hierarchy
		/// </summary>
		/// <param name="project"></param>
		/// <param name="path"></param>
		public static void Save(DocProject project, string path, Dictionary<string, DocObject> mapEntity, FolderStorageOptions options)
		{
			bool bExportSchema = ((options & FolderStorageOptions.Schemas) != 0);
			bool bExportExchanges = ((options & FolderStorageOptions.Exchanges) != 0);
			bool bExportExamples = ((options & FolderStorageOptions.Examples) != 0);
			bool bExportLocalize = ((options & FolderStorageOptions.Localization) != 0);

			Compiler compiler = new Compiler(project, null, null, false);
			System.Reflection.Emit.AssemblyBuilder assembly = compiler.Assembly;

			// -exchanges (or mvd?)
			//  {exchange}.mvdxml - definition
			//  {exchange}.cs     - C# partial classes for capturing exchange --- later
			//  templates.mvdxml - shared templates
			// -figures -- manually added
			// -formats
			//  -json
			//  -step
			//  -ttl
			//  -xml
			// -samples
			//  {sample}.ifcxml - ifcxml is native format for easier browsing, comparing, and validating
			//  {sample}.htm    - documentation for example
			//  {sample}.png    - preview image of example
			//  {sample} - subdirectory if children
			// -schemas
			//  {version}
			//   {schema}
			//    {class}.cs   - definition in C#
			//    {class}.htm  - documentation in HTML
			//    schema.cs    - functions and 
			//    schema.htm   - documentation of schema in HTML
			//    schema.svg   - diagram of schema in SVG
			//    templates.ifcxml - property and quantity templates
			//   localization
			//   {locale}.txt    - localized definitions
			//  ifc.csproj

			if (bExportSchema)
			{
				string pathClasses = path + @"\schemas\" + project.GetSchemaIdentifier();
				System.IO.Directory.CreateDirectory(pathClasses);
				FormatCSC.GenerateCode(project, pathClasses, mapEntity, DocCodeEnum.All);

				// generate ifcxml for templates
				DocumentationISO.DoExport(project, null, pathClasses + @"\templates.ifcxml", null, null, DocDefinitionScopeEnum.Default, null, mapEntity);

				// XSD configuration // not needed -- can re-read from C# classes
				//DocumentationISO.DoExport(project, null, pathClasses + @"\xsdconfig.xml", null, null, DocDefinitionScopeEnum.Default, null, mapEntity);
			}

			if (bExportExchanges)
			{
				string pathExchanges = path + @"\exchanges";
				System.IO.Directory.CreateDirectory(pathExchanges);
				foreach (DocModelView docView in project.ModelViews)
				{
					string pathView = pathExchanges + @"\" + DocumentationISO.MakeLinkName(docView);
					DocumentationISO.DoExport(project, null, pathView + ".mvdxml", new DocModelView[] { docView }, null, DocDefinitionScopeEnum.Default, null, mapEntity);

					//... future: once it works flawlessly...FormatCSC.GenerateExchange(project, docView, pathView, mapEntity);
				}
			}

			if (bExportExamples)
			{
				// compile schema into assembly
				Type typeProject = Compiler.CompileProject(project);

				string pathSamples = path + @"\examples";
				string pathSamplesWeb = "examples";
				System.IO.Directory.CreateDirectory(pathSamples);
				using (StreamWriter writerIndex = new StreamWriter(Stream.Null))//...pathSamples + @"\index.htm"))
				{
					writerIndex.WriteLine("<html><body>");

					writerIndex.WriteLine("<table>");
					foreach (DocExample docExam in project.Examples)
					{
						// generate ifcxml for each sample
						ExportExample(pathSamples, pathSamplesWeb, typeProject, docExam, writerIndex);
					}
					writerIndex.WriteLine("</table>");

					writerIndex.WriteLine("</body></html>");
				}
			}

			// terms, abbreviations, references, bibliography, ...
#if false
            string pathTerms = path + @"\terms";
            System.IO.Directory.CreateDirectory(pathSamples);
            foreach (DocTerm docTerm in this.m_project.Terms)
            {

            }
#endif

			// localization
			SortedList<string, string> listLocale = new SortedList<string, string>();
			foreach (DocObject eachobj in mapEntity.Values)
			{
				if (eachobj.Localization != null)
				{
					foreach (DocLocalization doclocal in eachobj.Localization)
					{
						// only deal with languages, not regions
						if (doclocal.Locale != null && doclocal.Locale.Length >= 2)
						{
							string language = doclocal.Locale.Substring(0, 2);

							if (!listLocale.ContainsKey(language))
							{
								listLocale.Add(language, doclocal.Locale);
							}
						}
					}
				}
			}

			if (bExportLocalize)
			{
				string pathLocalize = path + @"\localize";
				System.IO.Directory.CreateDirectory(pathLocalize);
				foreach (string locale in listLocale.Keys)
				{
					string pathLocale = path + @"\localize\" + locale + ".txt";
					using (FormatCSV format = new FormatCSV(pathLocale))
					{
						format.Instance = project;
						format.Locales = new string[] { locale };
						format.Scope = DocDefinitionScopeEnum.Default;
						format.Save();
					}
				}
			}
		}


		/// <summary>
		/// 
		/// </summary>
		/// <param name="path">Path of parent</param>
		/// <param name="docExample"></param>
		private static void ExportExample(string path, string webpath, Type typeProject, DocExample docExample, StreamWriter writerIndex)
		{
			string pathExample = path + @"\" + DocumentationISO.MakeLinkName(docExample);
			string pathExampleWeb = webpath + "/" + DocumentationISO.MakeLinkName(docExample);

			// load SPF from internal content
			if (docExample.File != null)
			{
				try
				{
					using (MemoryStream streamSource = new MemoryStream(docExample.File))
					{
						StepSerializer serSource = new StepSerializer(typeProject);
						object project = serSource.ReadObject(streamSource);

						// write original IFC file as-is (including comments)  -- or could be normalized using StepSerializer
						using (FileStream streamIFC = new FileStream(pathExample + ".ifc", FileMode.Create))
						{
							streamIFC.Write(docExample.File, 0, docExample.File.Length);
						}

#if false
                        using (FileStream streamXML = new FileStream(pathExample + ".ifcxml", FileMode.Create))
                        {
                            BuildingSmart.Serialization.Xml.XmlSerializer streamTarget = new BuildingSmart.Serialization.Xml.XmlSerializer(typeProject);
                            streamTarget.WriteObject(streamXML, project);
                        }

                        using (FileStream streamJSN = new FileStream(pathExample + ".json", FileMode.Create))
                        {
                            BuildingSmart.Serialization.Json.JsonSerializer streamTarget = new BuildingSmart.Serialization.Json.JsonSerializer(typeProject);
                            streamTarget.WriteObject(streamJSN, project);
                        }

                        using (FileStream streamTTL = new FileStream(pathExample + ".ttl", FileMode.Create))
                        {
                            BuildingSmart.Serialization.Turtle.TurtleSerializer streamTarget = new BuildingSmart.Serialization.Turtle.TurtleSerializer(typeProject);
                            streamTarget.WriteObject(streamTTL, project);
                        }
#endif
					}
				}
				catch (Exception xx)
				{
					System.Diagnostics.Debug.WriteLine(xx.Message);
					return;
				}

				writerIndex.WriteLine("<tr><td><a href=\"./" + webpath + ".htm\">" + docExample.Name + "</a></td>");
				WriteExampleIndexFormat(writerIndex, pathExampleWeb, "ifc");
				WriteExampleIndexFormat(writerIndex, pathExampleWeb, "ifcxml");
				WriteExampleIndexFormat(writerIndex, pathExampleWeb, "json");
				WriteExampleIndexFormat(writerIndex, pathExampleWeb, "ttl");

				// github link
				writerIndex.WriteLine("<td><a href=\"https://github.com/BuildingSMART/IfcDoc/blob/master/IfcKit/" + pathExampleWeb + ".ifc\"><img src=\"github.png\" width=\"32\" height=\"32\" /></a></td>");

				writerIndex.WriteLine("</tr>");
			}
			else
			{
				// write header row
				writerIndex.WriteLine("<tr><th colspan=\"6\"><a href=\"./" + pathExampleWeb + ".htm\">" + docExample.Name + "</a></th></tr>");
			}

			if (!String.IsNullOrEmpty(docExample.Documentation))
			{
				string filehtml = pathExample + ".htm";
				using (StreamWriter writerHtml = new StreamWriter(filehtml, false, Encoding.UTF8))
				{
					writerHtml.Write(docExample.Documentation);
				}
			}

			if (docExample.Examples.Count > 0)
			{
				System.IO.Directory.CreateDirectory(pathExample);

				// recurse
				foreach (DocExample docSub in docExample.Examples)
				{
					ExportExample(pathExample, pathExampleWeb, typeProject, docSub, writerIndex);
				}
			}

		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="writer">writer to index html</param>
		/// <param name="relpath">relative path</param>
		/// <param name="ext">file extension (not including dot)</param>
		private static void WriteExampleIndexFormat(StreamWriter writer, string relpath, string ext)
		{
			writer.Write("<td><a href=\"./" + relpath + "." + ext + "\"><img width=\"32\" height=\"32\" src=\"" + ext + ".png\" /></a></td>");
		}

	}
}
