using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

using HtmlAgilityPack;

using IfcDoc.Schema;
using IfcDoc.Schema.DOC;
using BuildingSmart.Serialization.Step;
using BuildingSmart.Serialization.Xml;


namespace IfcDoc
{
	public static class IfcDocUtils
	{
		public static void SaveProjectFolderRepostiory(string path, DocProject project)
		{
			XmlFolderSerializer folderSerializer = new XmlFolderSerializer(typeof(DocProject));
			folderSerializer.AddFilePrefix(typeof(DocDefinition), "Ifc");
			folderSerializer.AddFilePrefix(typeof(DocPropertyEnumeration), "PEnum_");
			folderSerializer.WriteObject(path, project);
		}
		public static void SaveProject(DocProject project, string filePath)
		{
			project.SortProject();
			DocConstant notdefined = project.Constants.Where(x => string.Compare(x.Name, "NOTDEFINED", true) == 0).Where(x => string.Compare(x.Documentation, "Undefined type.", true) == 0).FirstOrDefault();
			DocConstant userdefined = project.Constants.Where(x => string.Compare(x.Name, "USERDEFINED", true) == 0).Where(x => string.Compare(x.Documentation, "User_defined type.", true) == 0).FirstOrDefault();

			foreach(DocSection section in project.Sections)
			{
				foreach(DocSchema schema in section.Schemas)
				{
					foreach(DocEnumeration enumeration in schema.Types.OfType<DocEnumeration>())
					{
						for(int counter = 0; counter < enumeration.Constants.Count; counter++)
						{
							DocConstant constant = enumeration.Constants[counter];
							if(notdefined != null && string.Compare(notdefined.UniqueId, constant.UniqueId) != 0 && string.Compare(notdefined.Name, constant.Name) == 0 && string.Compare(notdefined.Documentation, constant.Documentation) == 0)
							{
								project.Constants.Remove(constant);
								enumeration.Constants[counter] = notdefined;
							}
							else if(userdefined != null && string.Compare(userdefined.UniqueId, constant.UniqueId) != 0 && string.Compare(userdefined.Name, constant.Name) == 0 && string.Compare(userdefined.Documentation, constant.Documentation) == 0)
							{
								project.Constants.Remove(constant);
								enumeration.Constants[counter] = notdefined;
							}
						}
					}
				}
			}
			foreach(DocTemplateDefinition templateDefinition in project.Templates)
			{
				templateDefinition.setRuleIds();	
			}

			DocPropertyConstant other = project.PropertyConstants.Where(x => string.Compare(x.Name, "OTHER", true) == 0).FirstOrDefault();
			DocPropertyConstant none = project.PropertyConstants.Where(x => string.Compare(x.Name, "NONE", true) == 0).FirstOrDefault();
			DocPropertyConstant notknown = project.PropertyConstants.Where(x => string.Compare(x.Name, "NOTKNOWN", true) == 0).FirstOrDefault();
			DocPropertyConstant unset = project.PropertyConstants.Where(x => string.Compare(x.Name, "UNSET", true) == 0).FirstOrDefault();
			DocPropertyConstant notdefinedProperty = project.PropertyConstants.Where(x => string.Compare(x.Name, "NOTDEFINED", true) == 0).FirstOrDefault();
			DocPropertyConstant userdefinedProperty = project.PropertyConstants.Where(x => string.Compare(x.Name, "USERDEFINED", true) == 0).FirstOrDefault();
			
			foreach(DocPropertyEnumeration enumeration in project.PropertyEnumerations)
			{
				for(int counter = 0; counter < enumeration.Constants.Count; counter++)
				{
					DocPropertyConstant constant = enumeration.Constants[counter];
					if (other != null && string.Compare(other.UniqueId, constant.UniqueId) != 0 && string.Compare(other.Name, constant.Name) == 0)
					{
						project.PropertyConstants.Remove(constant);
						enumeration.Constants[counter] = other;
					}
					else if (none != null && string.Compare(none.UniqueId, constant.UniqueId) != 0 && string.Compare(none.Name, constant.Name) == 0)
					{
						project.PropertyConstants.Remove(constant);
						enumeration.Constants[counter] = none;
					}
					else if (notknown != null && string.Compare(notknown.UniqueId, constant.UniqueId) != 0 && string.Compare(notknown.Name, constant.Name) == 0)
					{
						project.PropertyConstants.Remove(constant);
						enumeration.Constants[counter] = notknown;
					}
					else if (unset != null && string.Compare(unset.UniqueId, constant.UniqueId) != 0 && string.Compare(unset.Name, constant.Name) == 0)
					{
						project.PropertyConstants.Remove(constant);
						enumeration.Constants[counter] = unset;
					}
					else if (notdefined != null && string.Compare(notdefinedProperty.UniqueId, constant.UniqueId) != 0 && string.Compare(notdefinedProperty.Name, constant.Name) == 0)
					{
						project.PropertyConstants.Remove(constant);
						enumeration.Constants[counter] = notdefinedProperty;
					}
					else if (userdefinedProperty != null && string.Compare(userdefinedProperty.UniqueId, constant.UniqueId) != 0 && string.Compare(userdefinedProperty.Name, constant.Name) == 0)
					{
						project.PropertyConstants.Remove(constant);
						enumeration.Constants[counter] = userdefinedProperty;
					}
				}
			}
			string ext = System.IO.Path.GetExtension(filePath).ToLower();
			switch (ext)
			{
				case ".ifcdoc":
					using (FileStream streamDoc = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite))
					{
						StepSerializer formatDoc = new StepSerializer(typeof(DocProject), SchemaDOC.Types, "IFCDOC_12_0", "IfcDoc 12.0", "BuildingSmart IFC Documentation Generator");
						formatDoc.WriteObject(streamDoc, project); // ... specify header...IFCDOC_11_8
					}
					break;

#if MDB
                        case ".mdb":
                            using (FormatMDB format = new FormatMDB(this.m_file, SchemaDOC.Types, this.m_instances))
                            {
                                format.Save();
                            }
                            break;
#endif
				case ".ifcdocxml":
					using (FileStream streamDoc = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite))
					{
						XmlSerializer formatDoc = new XmlSerializer(typeof(DocProject));

						formatDoc.WriteObject(streamDoc, project); // ... specify header...IFCDOC_11_8
					}
					break;
			}
		}
		public static DocProject LoadFile(string filePath)
		{
			List<object> instances = new List<object>();
			return LoadFile(filePath, out instances);
		}
		internal static void ReviseImport(List<object> instances, double schemaVersion)
		{
			foreach (object o in instances)
			{
				if (o is DocSchema)
				{
					DocSchema docSchema = (DocSchema)o;

					docSchema.initialize();
					// renumber page references
					foreach (DocPageTarget docTarget in docSchema.PageTargets)
					{
						if (docTarget.Definition != null) // fix it up -- NULL bug from older .ifcdoc files
						{
							int page = docSchema.GetDefinitionPageNumber(docTarget);
							int item = docSchema.GetPageTargetItemNumber(docTarget);
							docTarget.Name = page + "," + item + " " + docTarget.Definition.Name;

							foreach (DocPageSource docSource in docTarget.Sources)
							{
								docSource.Name = docTarget.Name;
							}
						}
					}
				}
				else if (o is DocExchangeDefinition)
				{
					// files before V4.9 had Description field; no longer needed so use regular Documentation field again.
					DocExchangeDefinition docexchange = (DocExchangeDefinition)o;
					if (docexchange._Description != null)
					{
						docexchange.Documentation = docexchange._Description;
						docexchange._Description = null;
					}
				}
				else if (o is DocTemplateDefinition)
				{
					// files before V5.0 had Description field; no longer needed so use regular Documentation field again.
					DocTemplateDefinition doctemplate = (DocTemplateDefinition)o;
					if (doctemplate._Description != null)
					{
						doctemplate.Documentation = doctemplate._Description;
						doctemplate._Description = null;
					}

					foreach (DocModelRule rule in doctemplate.Rules)
					{
						rule.setId(doctemplate.UniqueId);
					}
				}
				else if (o is DocConceptRoot)
				{
					// V12.0: ensure template is defined
					DocConceptRoot docConcRoot = (DocConceptRoot)o;
					if (docConcRoot.ApplicableTemplate == null && docConcRoot.ApplicableEntity != null)
					{
						docConcRoot.ApplicableTemplate = new DocTemplateDefinition();
						docConcRoot.ApplicableTemplate.Type = docConcRoot.ApplicableEntity.Name;
					}
				}
				else if (o is DocTemplateUsage)
				{
					// V12.0: ensure template is defined
					DocTemplateUsage docUsage = (DocTemplateUsage)o;
					if (docUsage.Definition == null)
					{
						docUsage.Definition = new DocTemplateDefinition();
					}
				}
				else if (o is DocLocalization)
				{
					DocLocalization localization = o as DocLocalization;
					if (!string.IsNullOrEmpty(localization.Name))
						localization.Name = localization.Name.Trim();
				}
				// ensure all objects have valid guid
				DocObject docObject = o as DocObject;
				if (docObject != null)
				{
					if (docObject.Uuid == Guid.Empty)
					{
						docObject.Uuid = Guid.NewGuid();
					}
					if (!string.IsNullOrEmpty(docObject.Documentation))
						docObject.Documentation = docObject.Documentation.Trim();

					if (schemaVersion < 12.1)
					{
						DocChangeSet docChangeSet = docObject as DocChangeSet;
						if (docChangeSet != null)
						{
							docChangeSet.ChangesEntities.RemoveAll(isUnchanged);
							docChangeSet.ChangesProperties.RemoveAll(isUnchanged);
							docChangeSet.ChangesQuantities.RemoveAll(isUnchanged);
							docChangeSet.ChangesViews.RemoveAll(isUnchanged);

						}
						else
						{
							if (schemaVersion < 12)
							{
								DocEntity entity = docObject as DocEntity;
								if (entity != null)
								{
									entity.ClearDefaultMember();
								}
							}
						}
					}
				}

				IDocTreeHost treeHost = o as IDocTreeHost;
				if (treeHost != null && treeHost.Tree == null)
				{
					treeHost.InitializeTree();
				}
			}
		}
		public static DocProject LoadFile(string filePath, out List<object> instances)
		{ 
			instances = new List<object>();
			string ext = System.IO.Path.GetExtension(filePath).ToLower();
			string schema = "";
			DocProject project = null;
			switch (ext)
			{
				case ".ifcdoc":
					using (FileStream streamDoc = new FileStream(filePath, FileMode.Open, FileAccess.Read))
					{
						Dictionary<long, object> dictionaryInstances = null;
						StepSerializer formatDoc = new StepSerializer(typeof(DocProject), SchemaDOC.Types);
						project = (DocProject)formatDoc.ReadObject(streamDoc, out dictionaryInstances);
						instances.AddRange(dictionaryInstances.Values);
						schema = formatDoc.Schema;
					}
					break;
				case ".ifcdocxml":
					using (FileStream streamDoc = new FileStream(filePath, FileMode.Open, FileAccess.Read))
					{
						Dictionary<string, object> dictionaryInstances = null;
						XmlSerializer formatDoc = new XmlSerializer(typeof(DocProject));
						project = (DocProject)formatDoc.ReadObject(streamDoc, out dictionaryInstances);
						if(project != null)
						instances.AddRange(formatDoc.ExtractObjects(project, typeof(SEntity)));
					}
					break;
				default:
					MessageBox.Show("Unsupported file type " + ext, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
					break;
#if MDB
                    case ".mdb":
                        using (FormatMDB format = new FormatMDB(this.m_file, SchemaDOC.Types, this.m_instances))
                        {
                            format.Load();
                        }
                        break;
#endif
			}
			if (project == null)
				return null;

			double schemaVersion = 0;
			if (!string.IsNullOrEmpty(schema))
			{
				string[] fields = schema.Split("_".ToCharArray());
				int i = 0;
				if (fields.Length > 1)
				{
					if (int.TryParse(fields[1], out i))
						schemaVersion = i;
					if (fields.Length > 2 && int.TryParse(fields[2], out i))
						schemaVersion += i / 10.0;
				}
			}
			List<SEntity> listDelete = new List<SEntity>();

			ReviseImport(instances, schemaVersion);

			if (project == null)
				return null;

			if (schemaVersion > 0)
			{
				if (schemaVersion < 12.1)
				{
					Dictionary<string, DocPropertyEnumeration> encounteredPropertyEnumerations = new Dictionary<string, DocPropertyEnumeration>();
					foreach (DocSchema docSchema in project.Sections.SelectMany(x => x.Schemas))
						extractListingsV12_1(project, docSchema, encounteredPropertyEnumerations);
				}
				if (schemaVersion < 12.2)
				{
					foreach (IDocumentation obj in instances.OfType<IDocumentation>().Where(x=> !string.IsNullOrEmpty(x.Documentation)))
					{
						obj.Documentation = convertToMarkdown(obj.Documentation);
						DocObject docObject = obj as DocObject;
						if (docObject != null && docObject.Localization.Count > 0)
						{	
							List<DocLocalization> localizations = docObject.Localization.ToList();
							docObject.Localization.Clear();
							foreach(DocLocalization localization in localizations)
								docObject.RegisterLocalization(new DocLocalization(localization));
						}
					}
				}
			}
			foreach (DocModelView docModelView in project.ModelViews)
			{
				// sort alphabetically (V11.3+)
				docModelView.SortConceptRoots();
			}

			// upgrade to Publications (V9.6)
			if (project.Annotations.Count == 4)
			{
				project.Publications.Clear();

				DocAnnotation docCover = project.Annotations[0];
				DocAnnotation docContents = project.Annotations[1];
				DocAnnotation docForeword = project.Annotations[2];
				DocAnnotation docIntro = project.Annotations[3];

				DocPublication docPub = new DocPublication();
				docPub.Name = "Default";
				docPub.Documentation = docCover.Documentation;
				docPub.Owner = docCover.Owner;
				docPub.Author = docCover.Author;
				docPub.Code = docCover.Code;
				docPub.Copyright = docCover.Copyright;
				docPub.Status = docCover.Status;
				docPub.Version = docCover.Version;

				docPub.Annotations.Add(docForeword);
				docPub.Annotations.Add(docIntro);

				project.Publications.Add(docPub);

				docCover.Delete();
				docContents.Delete();
				project.Annotations.Clear();
			}
			project.SortProject();
			return project;
		}
		private static void upgradeSetRuleEntityUuid(DocTemplateDefinition template, string path)
		{
			string nestedPath = path + template.UniqueId;
			foreach (DocTemplateDefinition templateDefinition in template.Templates)
				upgradeSetRuleEntityUuid(templateDefinition, nestedPath);
			foreach (DocModelRule rule in template.Rules)
				upgradeSetRuleEntityUuid(rule, nestedPath);
		}
		private static void upgradeSetRuleEntityUuid(DocModelRule rule, string path)
		{
			string nestedPath = path + rule.Name;
			foreach (DocModelRule modelRule in rule.Rules)
				upgradeSetRuleEntityUuid(modelRule, nestedPath);
			DocModelRuleEntity entityRule = rule as DocModelRuleEntity;
			if (entityRule != null)
				entityRule.UniqueId = BuildingSmart.Utilities.Conversion.GlobalId.HashGuid(path).ToString();

		}
			
		private static bool isUnchanged(DocChangeAction docChangeAction)
		{
			docChangeAction.Changes.RemoveAll(isUnchanged);
			if (docChangeAction.Changes.Count ==  0 && docChangeAction.Action == DocChangeActionEnum.NOCHANGE && !docChangeAction.ImpactXML && !docChangeAction.ImpactSPF)
				return true;
			return false;
		}
		private static void extractListingsV12_1(DocProject project, DocSchema schema, Dictionary<string, DocPropertyEnumeration> encounteredPropertyEnumerations)
		{
			if (schema.PropertyEnumerations != null)
			{
				foreach (DocPropertyEnumeration enumeration in schema.PropertyEnumerations)
				{
					if (encounteredPropertyEnumerations.ContainsKey(enumeration.Name))
						continue;
					project.PropertyEnumerations.Add(enumeration);
					encounteredPropertyEnumerations[enumeration.Name] = enumeration;
					foreach (DocPropertyConstant constant in enumeration.Constants)
					{
						constant.Name = constant.Name.Trim();
						if (!project.PropertyConstants.Contains(constant))
							project.PropertyConstants.Add(constant);
					}
				}
			}
			foreach (DocType t in schema.Types)
			{
				DocEnumeration enumeration = t as DocEnumeration;
				if (enumeration != null)
				{
					foreach (DocConstant constant in enumeration.Constants)
					{
						if (!project.Constants.Contains(constant)) 
							project.Constants.Add(constant);
					}
				}
			}
			foreach (DocProperty property in schema.PropertySets.SelectMany(x=>x.Properties))
				extractListings(project, property, encounteredPropertyEnumerations); //listings
		
			foreach (DocQuantity quantity in schema.QuantitySets.SelectMany(x => x.Quantities))
				project.Quantities.Add(quantity);
		}

		private static void extractListings(DocProject project, DocProperty property, Dictionary<string, DocPropertyEnumeration> encounteredPropertyEnumerations)
		{
			project.Properties.Add(property);

			if (string.IsNullOrEmpty(property.SecondaryDataType))
				property.SecondaryDataType = null;
			else if (property.PropertyType == DocPropertyTemplateTypeEnum.P_ENUMERATEDVALUE && property.Enumeration == null)
			{
				string[] fields = property.SecondaryDataType.Split(":".ToCharArray());
				if(fields.Length == 1)
				{
					string name = fields[0];
					foreach(DocPropertyEnumeration docEnumeration in project.PropertyEnumerations)
					{
						if(string.Compare(name, docEnumeration.Name) == 0)
						{
							property.Enumeration = docEnumeration;
							property.SecondaryDataType = null;
							break;
						}
					}
				}
				else if (fields.Length == 2)
				{
					string name = fields[0];
					DocPropertyEnumeration propertyEnumeration = null;
					if (encounteredPropertyEnumerations.TryGetValue(name, out propertyEnumeration))
					{
						property.SecondaryDataType = null;
						property.Enumeration = propertyEnumeration;
					}
					else
					{
						foreach (DocPropertyEnumeration docEnumeration in project.PropertyEnumerations)
						{
							if (string.Compare(name, docEnumeration.Name) == 0)
							{
								property.Enumeration = docEnumeration;
								property.SecondaryDataType = null;
								break;
							}
						}
						if (property.Enumeration == null)
						{
							property.Enumeration = new DocPropertyEnumeration() { Name = name };
							project.PropertyEnumerations.Add(property.Enumeration = property.Enumeration);
							encounteredPropertyEnumerations[name] = property.Enumeration;
							foreach (string str in fields[1].Split(",".ToCharArray()))
							{
								string constantName = str.Trim();
								DocPropertyConstant constant = null;
								foreach (DocPropertyConstant docConstant in project.PropertyConstants)
								{
									if (string.Compare(docConstant.Name, constantName) == 0)
									{
										constant = docConstant;
										break;
									}
								}
								if (constant == null)
								{
									constant = new DocPropertyConstant() { Name = constantName };
									project.PropertyConstants.Add(constant);
								}
								property.Enumeration.Constants.Add(constant);
							}
							property.SecondaryDataType = null;
						}
					}
				}
			}

			foreach (DocProperty element in property.Elements)
				extractListings(project, element, encounteredPropertyEnumerations);
		}


		private static string openingTag(HtmlNode node)
		{
			string result = "<" + node.Name;
			foreach (HtmlAttribute attribute in node.Attributes)
				result += " " + attribute.Name + "=\"" + attribute.Value;
			return result + ">";
		}
		public static string convertToMarkdown(string html)
		{
			string documentation = Regex.Replace(html, @"<li>&quot;\s+", "<li>&quot;").Replace("<br/>\r\n", "<br/>").Replace("blackquote", "blockquote");
			documentation = documentation.Replace("<u>Definition from IAI</u>:", "");
			documentation = documentation.Replace("<U>Definition from IAI</U>:", "");
			documentation = documentation.Replace("<u>Definition from IAI:</u>", "");
			documentation = documentation.Replace("<U>Definition from IAI:</U>", "");
			documentation = documentation.Replace("<i>Definition from IAI</i>:", "");
			documentation = documentation.Replace("<u><b>Definition from IAI</b></u>:", "");
			documentation = documentation.Replace("<b><u>Definition from IAI</u></b>:", "");
			documentation = documentation.Replace("Definition from IAI:", "");

			int pTagIndex = documentation.IndexOf("<p"), pTagTerminateIndex = documentation.IndexOf("</p>");
			if (pTagTerminateIndex > 0 && (pTagIndex < 0 || pTagIndex > pTagTerminateIndex))
				documentation = "<p>" + documentation;
			documentation = DocumentationISO.UpdateNumbering(documentation, new List<DocumentationISO.ContentRef>(), new List<DocumentationISO.ContentRef>(), null);
			HtmlAgilityPack.HtmlDocument doc = new HtmlAgilityPack.HtmlDocument();
			doc.LoadHtml(documentation);
			HtmlNode node = doc.DocumentNode;
			string result = convertToMarkdown(node, new ConvertMarkdownOptions()).Trim();

			if(result.EndsWith("___"))
				result = result.Substring(0, result.Length - 3).TrimEnd();
			return result;
		}
		private static string convertToMarkdown(HtmlNode node, ConvertMarkdownOptions options)
		{
			ConvertMarkdownOptions childOptions = new ConvertMarkdownOptions(options);
			if (node.NodeType == HtmlNodeType.Text)
			{
				string text = node.InnerText.Replace("&quot;", "\"");
				if (options.m_CollapseLines && !options.m_NestsCode)
				{
					Regex regex = new Regex("[ ]{2,}", RegexOptions.None);
					text = regex.Replace(Regex.Replace(text, @"\n|\r|\t", " "), " ");
				}
				string escaped = "";
				foreach (char c in text)
				{
					if (c == '*' || c == '^' || c == '~')// || c == '\\' || c == '`' || c == '_' || c == '{' || c == '}' || c == '[' || c == ']' || c == '(' || c == ')' || c == '#' || c == '+' || c == '-' || c == '!' || c == '.')
						escaped += '\\';
					escaped += c;
				}
				return escaped;
			}

			if (node.NodeType == HtmlNodeType.Comment)
				return "";

			string result = "", suffix = "", prefix = "";
			if (node.NodeType == HtmlNodeType.Document)
			{
				HtmlNode firstChild = node.FirstChild;
				if (firstChild != null && string.Compare(firstChild.Name, "tr", true) == 0)
				{
					return node.OuterHtml;
				}
			}
			bool trimChildStart = false, quoteChild = false;
			bool isBlockQuote = false, isParagraph = false;
			if (node.NodeType == HtmlNodeType.Element)
			{
				if (node.HasAttributes)
				{
					string _class = "", id = "";
					foreach (HtmlAttribute attribute in node.Attributes)
					{
						if (string.Compare(attribute.Name, "Class", true) == 0 && !node.InnerText.ToLower().TrimStart().StartsWith(attribute.Value.ToLower()))
							_class = attribute.Value;
						else if (string.Compare(attribute.Name, "id", true) == 0)
							id = attribute.Value;
					}
					if (!string.IsNullOrEmpty(id))
						result += "{#" + id + (string.IsNullOrEmpty(_class) ? "}\r\n" : " ." + _class + "}\r\n");
					else if (!string.IsNullOrEmpty(_class))
						result += "{ ." + _class + "}\r\n";

				}
				string name = node.Name;

				if (string.IsNullOrEmpty(name))
					return node.OuterHtml;

				if (string.Compare(name, "a", true) == 0)
				{
					result = "";
					string href = "", target = "", _class = "", id = "";
					foreach (HtmlAttribute attribute in node.Attributes)
					{
						name = attribute.Name;
						if (string.Compare(name, "href", true) == 0)
							href = attribute.Value;
						else if (string.Compare(name, "target", true) == 0)
							target = attribute.Value;
						else if (string.Compare(name, "class", true) == 0)
							_class = attribute.Value;
						else if (string.Compare(name, "id", true) == 0)
							id = attribute.Value;
						else
						{
							suffix = "";
						}
					}
					if (!string.IsNullOrEmpty(href))
					{
						prefix += "[";
						suffix = "](" + href + ")";
					}
					if (!string.IsNullOrEmpty(_class) || !string.IsNullOrEmpty(id) || !string.IsNullOrEmpty(target))
						suffix += "{" + (string.IsNullOrEmpty(id) ? "" : "#" + id) + (string.IsNullOrEmpty(_class) ? "" : " ." + _class) + (string.IsNullOrEmpty(target) ? "" : " target=\"" + target + "\"") + "}";
				}
				else if (string.Compare(name, "b", true) == 0 || string.Compare(name, "strong", true) == 0 || string.Compare(name, "u", true) == 0)
				{
					result += "**";
					suffix = "**";
				}
				else if (string.Compare(name, "blockquote", true) == 0)
				{
					isBlockQuote = true;
					if (childOptions.m_BlockQuoteLevel == 0)
						suffix = "\r\n\r\n";
					childOptions.m_BlockQuoteLevel += 1;
				}
				else if (string.Compare(name, "big", true) == 0)
					return node.OuterHtml;
				else if (string.Compare(name, "br", true) == 0)
				{
					if (options.m_RetainBreakReturns)
						return node.OuterHtml;
					suffix = "  \r\n" + blockQuote(options.m_BlockQuoteLevel);
				}
				else if (string.Compare(name, "code", true) == 0)
				{
					prefix += "\r\n" + blockQuote(options.m_BlockQuoteLevel) + "```\r\n";
					suffix = "\r\n" + blockQuote(options.m_BlockQuoteLevel) + "```\r\n";
					childOptions.m_NestsCode = true;
					quoteChild = true;
				}
				else if (string.Compare(name, "div", true) == 0)
					return node.OuterHtml;
				else if (string.Compare(name, "em", true) == 0 || string.Compare(name, "i", true) == 0)
				{
					if(node.ChildNodes.Count == 1 && node.FirstChild.NodeType == HtmlNodeType.Text && node.FirstChild.InnerText[0] == '.')
						return result  + "._" + convertToMarkdown(node.FirstChild,childOptions).Substring(1) + "_";
					prefix += "_";
					suffix = "_";
				}
				else if (string.Compare(name, "epm-html", true) == 0)
				{
					//ignore legacy bespoke tag
				}
				else if (string.Compare(name, "font", true) == 0)
				{
				//	System.Diagnostics.Debug.WriteLine("Font :" + node.OuterHtml);
					return node.OuterHtml;
				}
				else if (name.Length == 2 && name[0] == 'h' && char.IsDigit(name[1]))
				{
					int count = 0;
					if (int.TryParse(name.Substring(1), out count))
					{
						prefix = new String('#', count) + " ";
						suffix = "\r\n";
					}
				}
				else if (string.Compare(name, "hr", true) == 0)
					return "___" + "\r\n";
				else if (string.Compare(name, "img", true) == 0)
				{
					string alt = "Image", source = "";
					foreach (HtmlAttribute attribute in node.Attributes)
					{
						if (string.Compare(attribute.Name, "src", true) == 0)
							source = attribute.Value;
						else if (string.Compare(attribute.Name, "alt", true) == 0)
							alt = attribute.Value;
						else
						{
							suffix = "";
						}
					}
					if (!string.IsNullOrEmpty(source))
						return "![" + alt + "](" + source + ")";
				}
				else if (string.Compare(name, "li", true) == 0 || string.Compare(name, "td", true) == 0 || string.Compare(name, "th", true) == 0  || string.Compare(name, "thead",true) == 0)
				{
					//handled in parent
				}
				else if (string.Compare(name, "ol", true) == 0)
					return result + prefix + convertListToMarkdown(node, new ConvertMarkdownOptions(childOptions) { m_CollapseLines = true }, true, 0) + suffix;
				else if (string.Compare(name, "noscript", true) == 0)	
					return node.OuterHtml;
				else if (string.Compare(name, "p", true) == 0)
				{
					isParagraph = true;
					suffix = "\r\n" + blockQuote(options.m_BlockQuoteLevel) + "\r\n";
				}
				else if (string.Compare(name, "pre", true) == 0)
					return node.OuterHtml;
				else if (string.Compare(name, "small", true) == 0)
					return node.OuterHtml;
				else if (string.Compare(name, "span", true) == 0)
				{
					if (node.InnerText.StartsWith("NOTE", StringComparison.CurrentCultureIgnoreCase))
					{
						isBlockQuote = true;
						if (childOptions.m_BlockQuoteLevel == 0)
							suffix = "\r\n\r\n";
						childOptions.m_BlockQuoteLevel += 1;
					}
					else
						return node.OuterHtml;
				}
				else if (string.Compare(name, "strike", true) == 0)
				{
					prefix += "~~";
					suffix += "~~";
				}
				else if (string.Compare(name, "sub", true) == 0)
				{
					prefix += "~";
					suffix += "~";
				}
				else if (string.Compare(name, "sup", true) == 0 || string.Compare(name, "super", true) == 0)
				{
					prefix += "^";
					suffix += "^";
				}
				else if (string.Compare(name, "table", true) == 0)
					return result + prefix + convertTable(node, options) + suffix;
				else if (string.Compare(name, "ul", true) == 0)
					return result + prefix + convertListToMarkdown(node, new ConvertMarkdownOptions(childOptions) { m_CollapseLines = true }, false, 0) + suffix;
				else
					return node.OuterHtml;
			}


			if (node.ChildNodes.Count == 0)
				return result + (isBlockQuote ? blockQuote(childOptions.m_BlockQuoteLevel) : "") + node.InnerText + suffix;

			string inner = "";
			bool trimStart = !childOptions.m_NestsCode, trimEnd = trimStart;
			if(string.Compare(node.FirstChild.Name,"code",true) == 0)
				trimStart = false;
			if(string.Compare(node.LastChild.Name,"code",true) == 0 || string.Compare(node.LastChild.Name,"blockquote",true) == 0)
				trimEnd = false;

			List<HtmlNode> dataNodes = new List<HtmlNode>();
			foreach (HtmlNode n in node.ChildNodes)
			{
				if (string.Compare(n.Name, "br", true) == 0)
				{
					if (childOptions.m_RetainBreakReturns)
						inner += n.OuterHtml;
					else
					{
						inner += "  \r\n" + blockQuote(childOptions.m_BlockQuoteLevel);
						trimChildStart = true;
					}
				}
				else
				{
					ConvertMarkdownOptions nestedOptions = childOptions;
					if ((isParagraph || isBlockQuote) && n.NodeType == HtmlNodeType.Text)
						nestedOptions = new ConvertMarkdownOptions(childOptions) { m_CollapseLines = true };
					string str = convertToMarkdown(n, nestedOptions);
					if (!string.IsNullOrWhiteSpace(str))
					{
						bool childBlockQuote = string.Compare(n.Name, "blockquote",true) == 0;
						if(childBlockQuote)
						{
							if (!string.IsNullOrEmpty(inner) && !inner.EndsWith("\r\n"))
								inner += "\r\n";
						}
						dataNodes.Add(n);
						if (quoteChild && str[0] != '>')
							inner += blockQuote(childOptions.m_BlockQuoteLevel);
						if (trimChildStart)
							inner += str.TrimStart();
						else
							inner += str;
						quoteChild = false;
						trimChildStart = false;
						if (n.NextSibling != null && (string.Compare(n.Name, "code", true) == 0 || childBlockQuote || string.Compare(n.Name, "p", true) == 0))
						{
							quoteChild = true;
						}
					}
				}
			}
			if (isBlockQuote && dataNodes.Count > 0)
			{
				if (string.Compare(dataNodes[0].Name, "blockquote", true) == 0)
					result += blockQuote(childOptions.m_BlockQuoteLevel) + "\r\n";
				if (string.Compare(dataNodes[dataNodes.Count - 1].Name, "blockquote", true) == 0)
				{
					suffix += blockQuote(childOptions.m_BlockQuoteLevel) + "\r\n";
					trimEnd = false;
				}
			}
			if (quoteChild)
				trimEnd = false;
				
			if (trimStart)
			{
				if (trimEnd)
					inner = inner.Trim();
				else
					inner = inner.TrimStart();
			}
			else if (trimEnd)
				inner = inner.TrimEnd();
			if(string.IsNullOrEmpty(inner))
				return result + suffix;

			if (isBlockQuote && inner[0] != '>')
			{
				string line = result + blockQuote(childOptions.m_BlockQuoteLevel) + prefix + inner + suffix;
				if(options.m_BlockQuoteLevel > 0)
					return line.TrimEnd() + "\r\n";
				return line;
			}
			return result + prefix + inner + suffix;
		}
		private static string convertTable(HtmlNode node, ConvertMarkdownOptions options)
		{
			List<HtmlNode> rows = new List<HtmlNode>();
			foreach (HtmlNode htmlNode in childNodes(node))
			{
				if (string.Compare(htmlNode.Name, "tr", true) == 0)
					rows.Add(htmlNode);
				else
				{
					if (htmlNode.FirstChild != null)
					{
						List<HtmlNode> nestednodes = childNodes(htmlNode);
						if (string.Compare(htmlNode.Name, "thead", true) == 0)
						{
							if (nestednodes.Count == 1 && string.Compare(nestednodes[0].Name, "tr", true) == 0)
								rows.Add(nestednodes[0]);
						}
						else if (string.Compare(htmlNode.Name, "tbody", true) == 0)
						{
							foreach(HtmlNode n in nestednodes)
							{
								if (string.Compare(n.Name, "tr", true) == 0)
									rows.Add(n);	
							}
						}
					}
				}
			}
			
			if(rows.Count == 2)
			{
				HtmlNode row1 = rows[0], row2 = rows[1];
				List<HtmlNode> tableData1 = new List<HtmlNode>(), tableData2 = new List<HtmlNode>();
				foreach(HtmlNode htmlNode in row1.ChildNodes)
				{
					if (string.Compare(htmlNode.Name, "td") == 0 && !string.IsNullOrEmpty(htmlNode.InnerHtml.Trim()))
						tableData1.Add(htmlNode);
				}
				foreach(HtmlNode htmlNode in row2.ChildNodes)
				{
					if (string.Compare(htmlNode.Name, "td") == 0 && !string.IsNullOrEmpty(htmlNode.InnerHtml.Trim()))
						tableData2.Add(htmlNode);
				}
				
				if(tableData1.Count == 1 && tableData2.Count == 1)
				{
					HtmlNode data1 = tableData1[0], data2 = tableData2[0];
					HtmlNode image = null;
					foreach(HtmlNode htmlNode in tableData1[0].ChildNodes)
					{
						if (string.Compare(htmlNode.Name, "img", true) == 0 && image == null)
							image = htmlNode;
						else if (string.IsNullOrEmpty(htmlNode.OuterHtml.Trim()))
							continue;
						else
						{
							image = null;
							break;
						}
					}
					if (image != null)
					{
						List<HtmlNode> removed = Format.HTM.FormatHTM.RemoveParagraphs(tableData2[0]);

						string titleString = "";
						foreach (HtmlNode htmlNode in removed)
							titleString += convertToMarkdown(htmlNode, new ConvertMarkdownOptions(options) { m_BlockQuoteLevel = 0, m_CollapseLines = true });
						if (image != null)
						{
							string alt = "", src = "";
							foreach (HtmlAttribute attribute in image.Attributes)
							{
								if (string.Compare(attribute.Name, "alt", true) == 0)
									alt = attribute.Value;
								else if (string.Compare(attribute.Name, "src", true) == 0)
									src = attribute.Value;
							}
							if (!string.IsNullOrEmpty(src))
								return (string.IsNullOrEmpty(alt) ? "!(" : "![\"" + alt + "\"](") + src + (string.IsNullOrEmpty(titleString) ? ")\r\n\r\n" : " \"" + titleString + "\")\r\n\r\n");
						}
					}
				}
			}

			if (rows.Count > 1)
			{
				List<string> headers = new List<string>();
				foreach (HtmlNode child in rows[0].ChildNodes)
				{
					if (isEmpty(child))
						continue;
					if(string.Compare(child.Name, "th", true) != 0 && string.Compare(child.Name,"thead",true) != 0)
					{
						headers.Clear();
						break;
					}
					string str = convertToMarkdown(child, new ConvertMarkdownOptions(options) { m_BlockQuoteLevel = 0, m_CollapseLines = true, m_RetainBreakReturns = true });
					if(str.Contains("\r\n"))
					{
						headers.Clear();
						break;
					}
					headers.Add(str);
				}
				if (headers.Count > 0)
				{
					string result = blockQuote(options.m_BlockQuoteLevel) + (headers.Count == 1 ? headers[0] + " |" : string.Join(" | ", headers))+ "\r\n";
					result += blockQuote(options.m_BlockQuoteLevel) + string.Join(" | ", headers.Select(x=>new string('-', x.Length))) + "\r\n";

					foreach(HtmlNode row in rows.Skip(1))
					{
						List<string> rowData = new List<string>();
						foreach (HtmlNode data in row.ChildNodes)
						{
							if(isEmpty(data))
								continue;
							if (string.Compare(data.Name, "td", true) != 0)
							{
								result = null;
								break;
							}
							string str = convertToMarkdown(data, new ConvertMarkdownOptions(options) { m_BlockQuoteLevel = 0, m_CollapseLines = true, m_RetainBreakReturns = true });
							if (str.Contains("\r\n"))
							{
								result = null;
								break;
							}
							rowData.Add(str);
						}
						if (string.IsNullOrEmpty(result))
							break;
						result += blockQuote(options.m_BlockQuoteLevel) + (rowData.Count == 1 ? rowData[0] + " |" : string.Join(" | ", rowData)) + "\r\n";

					}
					if (!string.IsNullOrEmpty(result))
						return result + "\r\n\r\n";
				}
			}

			return blockQuote(options.m_BlockQuoteLevel) + node.OuterHtml + "\r\n\r\n";
		}
	
		private static string convertListToMarkdown(HtmlNode node, ConvertMarkdownOptions options, bool isOrdered, int indentLevel)
		{
			string result = "";
			int count = 1;
			foreach (HtmlNode hn in node.ChildNodes)
			{
				if (string.Compare(hn.Name, "ol", true) == 0)
				{
					string nestedList = convertListToMarkdown(hn, options, true, indentLevel + 1);
					if (nestedList.StartsWith("<"))
						return node.OuterHtml + "\r\n";
					result += nestedList;
				}
				else if (string.Compare(hn.Name, "ul", true) == 0)
				{
					string nestedList = convertListToMarkdown(hn, options, false, indentLevel + 1);
					if (nestedList.StartsWith("<"))
						return node.OuterHtml + "\r\n";
					result += nestedList;
				}
				else if (string.Compare(hn.Name, "li", true) == 0)
				{
					if (hn.HasChildNodes)
					{
						bool newItem = true, setItem = false;
						foreach (HtmlNode htmlNode in hn.ChildNodes)
						{
							if (string.Compare(htmlNode.Name, "ol", true) == 0)
							{
								string nestedList = convertListToMarkdown(htmlNode, options, true, indentLevel + 1);
								if (nestedList.StartsWith("<"))
									return node.OuterHtml + "\r\n";
								setItem = false;
								result += "\r\n" + nestedList.TrimEnd();
							}
							else if (string.Compare(htmlNode.Name, "ul", true) == 0)
							{
								string nestedList = convertListToMarkdown(htmlNode, options, false, indentLevel + 1);
								if (nestedList.StartsWith("<"))
									return node.OuterHtml + "\r\n";
								setItem = false;
								result += "\r\n" + nestedList.TrimEnd();
							}
							else if (string.Compare(htmlNode.Name, "blockquote", true) == 0)
							{
								if (isOrdered)
									return node.OuterHtml + "\r\n";
								string str = convertToMarkdown(htmlNode, new ConvertMarkdownOptions(options) { m_BlockQuoteLevel = indentLevel + 1 }).TrimEnd('\r','\n');
								if (str.Contains("\r\n"))
									return node.OuterHtml + "\r\n";
								result += "\r\n" + str.TrimEnd('\r','\n');
								setItem = false;
							}
							else
							{
								string str = convertToMarkdown(htmlNode, new ConvertMarkdownOptions(options) { m_BlockQuoteLevel = 0 }).Trim('\r','\n');
								if (str.Contains("\r\n"))
									return node.OuterHtml + "\r\n";
								if(newItem)
								{
									result += blockQuote(options.m_BlockQuoteLevel) + (indentLevel > 0 ? new string(' ', indentLevel * 4) : "") + (isOrdered ? count + ". " : "* ");
									count++;
									newItem = false;
								}
								result += str;
								setItem = true;
							}
						}
						if(setItem)
							result += "\r\n";
					}
					else
					{
						string str = convertToMarkdown(hn, new ConvertMarkdownOptions(options) { m_BlockQuoteLevel = 0 }).Trim();
						if (!string.IsNullOrEmpty(str))
						{
							if (str.Contains("\r\n"))
								return node.OuterHtml + "\r\n";
							result += blockQuote(options.m_BlockQuoteLevel) + (indentLevel > 0 ? new string(' ', indentLevel * 4) : "") + (isOrdered ? count + ". " : "* ") + str + "\r\n";
							count++;
						}
					}
				}
			}
			return result + "\r\n";
		}

		private static string blockQuote(int level)
		{
			return level == 0 ? "" : new String('>', level) + " ";
		}
		private static bool isEmpty(HtmlNode node)
		{
			return node.NodeType == HtmlNodeType.Text && string.IsNullOrEmpty(node.InnerHtml.Trim());
		}
		private static List<HtmlNode> childNodes(HtmlNode node)
		{
			return node.ChildNodes.Where(x => !isEmpty(x)).ToList();
		}
		private class ConvertMarkdownOptions
		{
			internal bool m_NestsCode = false;
			internal bool m_CollapseLines = false;
			internal int m_BlockQuoteLevel = 0;
			internal bool m_RetainBreakReturns = false;

			internal ConvertMarkdownOptions() { }
			internal ConvertMarkdownOptions(ConvertMarkdownOptions config)
			{
				m_NestsCode = config.m_NestsCode;
				m_CollapseLines = config.m_CollapseLines;
				m_BlockQuoteLevel = config.m_BlockQuoteLevel;
				m_RetainBreakReturns = config.m_RetainBreakReturns;
			}

		}
	}
}
