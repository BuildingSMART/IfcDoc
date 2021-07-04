using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using IfcDoc.Format.XML;
using IfcDoc.Schema.DOC;


namespace IfcDoc.Format.CNF
{
	public class FormatCNF : FormatXML
	{
		public FormatCNF(string file, Type type) : base(file, type, null, null)
		{
		}

		public FormatCNF(string file, Type type, string defaultnamespace)
			: base(file, type, defaultnamespace, null)
		{
		}

		public FormatCNF(string file, Type type, string defaultnamespace, XmlSerializerNamespaces prefixes) : base(file, type, defaultnamespace, prefixes)
		{
			string dirpath = System.IO.Path.GetDirectoryName(file);
			if (!Directory.Exists(dirpath))
			{
				Directory.CreateDirectory(dirpath);
			}
		}

		public FormatCNF(Stream stream, Type type, string defaultnamespace, XmlSerializerNamespaces prefixes) : base(stream, type, defaultnamespace, prefixes)
		{
		}

		public void Save(DocProject docProject, DocModelView[] docViews, Dictionary<DocObject, bool> included)
		{
			Schema.CNF.configuration config = new Schema.CNF.configuration();
			base.Instance = config;
			ExportCnf(config, docProject, docViews, included);
			base.Save();
		}

		internal static void ExportCnf(IfcDoc.Schema.CNF.configuration cnf, DocProject docProject, DocModelView[] docViews, Dictionary<DocObject, bool> included)
		{
			// configure general settings

			/*
			  <cnf:option inheritance="true" exp-type="unspecified" concrete-attribute="attribute-content" entity-attribute="double-tag" tagless="unspecified" naming-convention="preserve-case" generate-keys="false"/>
			  <cnf:schema targetNamespace="http://www.buildingsmart-tech.org/ifcXML/IFC4/final" embed-schema-items="true" elementFormDefault="qualified" attributeFormDefault="unqualified">
				<cnf:namespace prefix="ifc" alias="http://www.buildingsmart-tech.org/ifcXML/IFC4/final"/>
			  </cnf:schema>
			  <cnf:uosElement name="ifcXML"/>
			  <cnf:type select="NUMBER" map="xs:double"/>
			  <cnf:type select="BINARY" map="xs:hexBinary"/>  
			  <cnf:type select="IfcStrippedOptional" keep="false"/>
			*/
			cnf.id = docProject.GetSchemaIdentifier();

			IfcDoc.Schema.CNF.option opt = new Schema.CNF.option();
			opt.inheritance = true;
			opt.exp_type = Schema.CNF.exp_type.unspecified;
			opt.concrete_attribute = Schema.CNF.exp_attribute_global.attribute_content;
			opt.entity_attribute = Schema.CNF.exp_attribute_global.double_tag;
			opt.tagless = Schema.CNF.boolean_or_unspecified.unspecified;
			opt.naming_convention = Schema.CNF.naming_convention.preserve_case;
			opt.generate_keys = false;
			cnf.option.Add(opt);

			IfcDoc.Schema.CNF.schema sch = new IfcDoc.Schema.CNF.schema();
			sch.targetNamespace = "http://www.buildingsmart-tech.org/ifcXML/IFC4/final"; //... make parameter...
			sch.embed_schema_items = true;
			sch.elementFormDefault = Schema.CNF.qual.qualified;
			sch.attributeFormDefault = Schema.CNF.qual.unqualified;

			IfcDoc.Schema.CNF._namespace ns = new Schema.CNF._namespace();
			ns.prefix = "ifc";
			ns.alias = "http://www.buildingsmart-tech.org/ifcXML/IFC4/final";
			sch._namespace = ns;

			cnf.schema.Add(sch);

			IfcDoc.Schema.CNF.uosElement uos = new Schema.CNF.uosElement();
			uos.name = "ifc:ifcXML"; // added ifc: prefix for config fix
			cnf.uosElement.Add(uos);

			IfcDoc.Schema.CNF.type typeNumber = new Schema.CNF.type();
			typeNumber.select = "NUMBER";
			typeNumber.map = "xs:double";
			cnf.type.Add(typeNumber);

			IfcDoc.Schema.CNF.type typeBinary = new Schema.CNF.type();
			typeBinary.select = "BINARY";
			typeBinary.map = "xs:hexBinary";
			cnf.type.Add(typeBinary);

			IfcDoc.Schema.CNF.type typeStripped = new Schema.CNF.type();
			typeStripped.select = "IfcStrippedOptional";
			typeStripped.keep = false;
			cnf.type.Add(typeStripped);

			SortedDictionary<string, IfcDoc.Schema.CNF.entity> mapEntity = new SortedDictionary<string, Schema.CNF.entity>();

			// export default configuration -- also export for Common Use Definitions (base view defined as itself to include everything)
			//if (docViews == null || docViews.Length == 0 || (docViews.Length == 1 && docViews[0].BaseView == docViews[0].Uuid.ToString()))
			{
				foreach (DocSection docSection in docProject.Sections)
				{
					foreach (DocSchema docSchema in docSection.Schemas)
					{
						foreach (DocEntity docEntity in docSchema.Entities)
						{
							if (docEntity.Name.Equals("IfcObjectDefinition"))
							{
								docEntity.ToString();
							}

							bool include = true; //... check if included in graph?
							if (included != null && !included.ContainsKey(docEntity))
							{
								include = false;
							}

							if (include)
							{
								foreach (DocAttribute docAttr in docEntity.Attributes)
								{
									if (docAttr.XsdFormat != DocXsdFormatEnum.Default || docAttr.XsdTagless == true)
									{
										IfcDoc.Schema.CNF.entity ent = null;
										if (!mapEntity.TryGetValue(docEntity.Name, out ent))
										{
											ent = new Schema.CNF.entity();
											ent.select = docEntity.Name;
											mapEntity.Add(docEntity.Name, ent);
										}

										ExportCnfAttribute(ent, docAttr, docAttr.XsdFormat, docAttr.XsdTagless);
									}
								}
							}
						}
					}
				}
			}

			// export view-specific configuration
			if (docViews != null)
			{
				foreach (DocModelView docView in docViews)
				{
					foreach (DocXsdFormat format in docView.XsdFormats)
					{
						DocEntity docEntity = docProject.GetDefinition(format.Entity) as DocEntity;
						if (docEntity != null)
						{
							DocAttribute docAttr = null;
							foreach (DocAttribute docEachAttr in docEntity.Attributes)
							{
								if (docEachAttr.Name != null && docEachAttr.Name.Equals(format.Attribute))
								{
									docAttr = docEachAttr;
									break;
								}
							}

							if (docAttr != null)
							{
								IfcDoc.Schema.CNF.entity ent = null;
								if (!mapEntity.TryGetValue(docEntity.Name, out ent))
								{
									ent = new Schema.CNF.entity();
									mapEntity.Add(docEntity.Name, ent);
								}

								ExportCnfAttribute(ent, docAttr, format.XsdFormat, format.XsdTagless);
							}
						}
					}
				}
			}

			// add at end, such that sorted
			foreach (IfcDoc.Schema.CNF.entity ent in mapEntity.Values)
			{
				cnf.entity.Add(ent);
			}
		}

		internal static void ExportCnfAttribute(IfcDoc.Schema.CNF.entity ent, DocAttribute docAttr, DocXsdFormatEnum xsdformat, bool? tagless)
		{
			Schema.CNF.exp_attribute exp = Schema.CNF.exp_attribute.unspecified;
			bool keep = true;
			switch (xsdformat)
			{
				case DocXsdFormatEnum.Content:
					exp = Schema.CNF.exp_attribute.attribute_content;
					break;

				case DocXsdFormatEnum.Attribute:
					exp = Schema.CNF.exp_attribute.attribute_tag;
					break;

				case DocXsdFormatEnum.Element:
					exp = Schema.CNF.exp_attribute.double_tag;
					break;

				case DocXsdFormatEnum.Hidden:
					keep = false;
					break;

				default:
					break;
			}

			if (!String.IsNullOrEmpty(docAttr.Inverse))
			{
				if (keep)
				{
					IfcDoc.Schema.CNF.inverse inv = new Schema.CNF.inverse();
					inv.select = docAttr.Name;
					inv.exp_attribute = exp;
					ent.inverse.Add(inv);
				}
			}
			else
			{
				IfcDoc.Schema.CNF.attribute att = new Schema.CNF.attribute();
				att.select = docAttr.Name;
				att.exp_attribute = exp;
				att.keep = keep;

				if (tagless != null)
				{
					if (tagless == true)
					{
						att.tagless = Schema.CNF.boolean_or_unspecified.boolean_true;
					}
					else
					{
						att.tagless = Schema.CNF.boolean_or_unspecified.boolean_false;
					}
				}
				ent.attribute.Add(att);
			}

		}
	}
}
