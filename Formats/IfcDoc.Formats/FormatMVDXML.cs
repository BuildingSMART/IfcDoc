using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using IfcDoc.Schema.DOC;
using IfcDoc.Schema.MVD;

namespace IfcDoc.Format.MVDXML
{
	public class FormatMVDXML
	{
		public static void Load(DocProject docProject, string filename)
		{
			for (int iNS = 0; iNS < mvdXML.Namespaces.Length; iNS++)
			{
				string xmlns = mvdXML.Namespaces[iNS];
				mvdXML mvd = null;
				BuildingSmart.Serialization.Xml.XmlSerializer xmlSerializer = new BuildingSmart.Serialization.Xml.XmlSerializer(typeof(mvdXML));
				using (System.IO.FileStream streamSource = new System.IO.FileStream(filename, System.IO.FileMode.Open))
				{
					mvd = xmlSerializer.ReadObject(streamSource) as mvdXML;
				}
				if (mvd != null)
				{
					try
					{
						ImportMvd(mvd, docProject, filename);
						break;
					}
					catch (InvalidOperationException xx)
					{
						// keep going until successful

						if (iNS == mvdXML.Namespaces.Length - 1)
						{
							throw new InvalidOperationException("The file is not of a supported format (mvdXML 1.0 or mvdXML 1.1).");
						}
					}
					catch (Exception xx)
					{
						xmlns = null;
					}
				}
			}
		}


		public static void ImportMvd(mvdXML mvd, DocProject docProject, string filepath)
		{
			List<DocTemplateDefinition> listPrivateTemplateRoots = new List<DocTemplateDefinition>();

			if (mvd.Templates != null)
			{
				Dictionary<EntityRule, DocModelRuleEntity> fixups = new Dictionary<EntityRule, DocModelRuleEntity>();
				foreach (ConceptTemplate mvdTemplate in mvd.Templates)
				{
					DocTemplateDefinition docDef = docProject.GetTemplate(mvdTemplate.Uuid);
					if (docDef == null)
					{
						docDef = new DocTemplateDefinition();
						docProject.Templates.Add(docDef);
					}

					ImportMvdTemplate(mvdTemplate, docDef, fixups);

					if (mvdTemplate.Name.StartsWith("_"))
					{
						// hidden -- treat as private template
						listPrivateTemplateRoots.Add(docDef);
					}
				}

				foreach (EntityRule er in fixups.Keys)
				{
					DocModelRuleEntity docEntityRule = fixups[er];
					if (er.References != null)
					{
						foreach (TemplateRef tr in er.References.Template)
						{
							DocTemplateDefinition dtd = docProject.GetTemplate(tr.Ref);
							if (dtd != null)
							{
								docEntityRule.References.Add(dtd);
							}
						}
					}
				}
			}

			if (mvd.Views != null)
			{
				foreach (ModelView mvdView in mvd.Views)
				{
					DocModelView docView = docProject.GetView(mvdView.Uuid);
					if (docView == null)
					{
						docView = new DocModelView();
						docProject.ModelViews.Add(docView);
					}

					ImportMvdView(mvdView, docView, docProject, filepath);
				}
			}

			foreach (DocTemplateDefinition docTemplatePrivate in listPrivateTemplateRoots)
			{
				docProject.Templates.Remove(docTemplatePrivate);
			}
		}

		private static void ImportMvdView(ModelView mvdView, DocModelView docView, DocProject docProject, string filepath)
		{
			ImportMvdObject(mvdView, docView);

			docView.BaseView = mvdView.BaseView;
			docView.Exchanges.Clear();
			Dictionary<Guid, ExchangeRequirement> mapExchange = new Dictionary<Guid, ExchangeRequirement>();
			foreach (ExchangeRequirement mvdExchange in mvdView.ExchangeRequirements)
			{
				mapExchange.Add(mvdExchange.Uuid, mvdExchange);

				DocExchangeDefinition docExchange = new DocExchangeDefinition();
				ImportMvdObject(mvdExchange, docExchange);
				docView.Exchanges.Add(docExchange);

				docExchange.Applicability = (DocExchangeApplicabilityEnum)mvdExchange.Applicability;

				// attempt to find icons if exists -- remove extention
				try
				{
					string iconpath = filepath.Substring(0, filepath.Length - 7) + @"\mvd-" + docExchange.Name.ToLower().Replace(' ', '-') + ".png";
					if (System.IO.File.Exists(iconpath))
					{
						docExchange.Icon = System.IO.File.ReadAllBytes(iconpath);
					}
				}
				catch
				{

				}
			}

			foreach (ConceptRoot mvdRoot in mvdView.Roots)
			{
				// find the entity
				DocEntity docEntity = LookupEntity(docProject, mvdRoot.ApplicableRootEntity);
				if (docEntity != null)
				{
					DocConceptRoot docConceptRoot = docView.GetConceptRoot(mvdRoot.Uuid);
					if (docConceptRoot == null)
					{
						docConceptRoot = new DocConceptRoot();
						docView.ConceptRoots.Add(docConceptRoot);
					}

					ImportMvdObject(mvdRoot, docConceptRoot);
					docConceptRoot.ApplicableEntity = docEntity;

					if (mvdRoot.Applicability != null)
					{
						docConceptRoot.ApplicableTemplate = docProject.GetTemplate(mvdRoot.Applicability.Template.Ref);
						if (mvdRoot.Applicability.TemplateRules != null)
						{
							docConceptRoot.ApplicableOperator = (DocTemplateOperator)Enum.Parse(typeof(TemplateOperator), mvdRoot.Applicability.TemplateRules.Operator.ToString());
							foreach (TemplateRule r in mvdRoot.Applicability.TemplateRules.OfType<TemplateRule>())
							{
								DocTemplateItem docItem = ImportMvdItem(r, docProject, mapExchange);
								docConceptRoot.ApplicableItems.Add(docItem);
							}
						}
					}

					docConceptRoot.Concepts.Clear();
					if (mvdRoot.Concepts != null)
					{
						foreach (Concept mvdNode in mvdRoot.Concepts)
						{
							DocTemplateUsage docUse = new DocTemplateUsage();
							docConceptRoot.Concepts.Add(docUse);
							ImportMvdConcept(mvdNode, docUse, docProject, mapExchange);
						}
					}
				}
				else
				{
					//TODO: log error
				}
			}

			// subviews
			if (mvdView.Views != null)
			{
				foreach (ModelView mvdSubView in mvdView.Views)
				{
					DocModelView docSubView = new DocModelView();
					docView.ModelViews.Add(docView);
					ImportMvdView(mvdSubView, docSubView, docProject, filepath);
				}
			}
		}

		private static void ImportMvdRequirement(ConceptRequirement mvdReq, DocExchangeItem docReq, DocProject docProject)
		{
			// TODO: support inner views!!!
			foreach (DocModelView docModel in docProject.ModelViews)
			{
				foreach (DocExchangeDefinition docAnno in docModel.Exchanges)
				{
					if (docAnno.Uuid.Equals(mvdReq.ExchangeRequirement))
					{
						docReq.Exchange = docAnno;
						break;
					}
				}
			}

			docReq.Applicability = (DocExchangeApplicabilityEnum)mvdReq.Applicability;

			switch (mvdReq.Requirement)
			{
				case RequirementEnum.Mandatory:
					docReq.Requirement = DocExchangeRequirementEnum.Mandatory;
					break;

				case RequirementEnum.Recommended:
					docReq.Requirement = DocExchangeRequirementEnum.Optional;
					break;

				case RequirementEnum.NotRelevant:
					docReq.Requirement = DocExchangeRequirementEnum.NotRelevant;
					break;

				case RequirementEnum.NotRecommended:
					docReq.Requirement = DocExchangeRequirementEnum.NotRecommended;
					break;

				case RequirementEnum.Excluded:
					docReq.Requirement = DocExchangeRequirementEnum.Excluded;
					break;
			}
		}

		private static void ImportMvdConcept(Concept mvdNode, DocTemplateUsage docUse, DocProject docProject, Dictionary<Guid, ExchangeRequirement> mapExchange)
		{
			ImportMvdObject(mvdNode, docUse);

			if (mvdNode.Template != null)
			{
				DocTemplateDefinition docTemplateDef = docProject.GetTemplate(mvdNode.Template.Ref);
				if (docTemplateDef != null)
				{
					docUse.Definition = docTemplateDef;
				}
			}

			docUse.Override = mvdNode.Override;

			// exchange requirements
			foreach (ConceptRequirement mvdReq in mvdNode.Requirements)
			{
				ExchangeRequirement mvdExchange = null;
				if (mapExchange.TryGetValue(mvdReq.ExchangeRequirement, out mvdExchange))
				{
					DocExchangeItem docReq = new DocExchangeItem();
					docUse.Exchanges.Add(docReq);
					ImportMvdRequirement(mvdReq, docReq, docProject);
				}
			}

			// rules as template items
			if (mvdNode.TemplateRules != null)
			{
				docUse.Operator = (DocTemplateOperator)Enum.Parse(typeof(DocTemplateOperator), mvdNode.TemplateRules.Operator.ToString());
				foreach (TemplateRule rule in mvdNode.TemplateRules.OfType<TemplateRule>())
				{
					DocTemplateItem docItem = ImportMvdItem(rule, docProject, mapExchange);
					docUse.Items.Add(docItem);
				}
			}
		}


		private static DocTemplateItem ImportMvdItem(TemplateRule ruleItem, DocProject docProject, Dictionary<Guid, ExchangeRequirement> mapExchange)
		{
			DocTemplateItem docItem = new DocTemplateItem();
			docItem.Documentation = ruleItem.Description;
			docItem.RuleInstanceID = ruleItem.RuleID;
			docItem.ParseParameterExpressions(ruleItem.Parameters); // convert from mvdXML

			// V11.6
			if (ruleItem is TemplateItem)
			{
				TemplateItem rule = (TemplateItem)ruleItem;
				//if (rule.Requirements != null)
				//{
				//    foreach (ConceptRequirement mvdReq in rule.Requirements)
				//    {
				//        ExchangeRequirement mvdExchange = null;
				//        if (mapExchange.TryGetValue(mvdReq.ExchangeRequirement, out mvdExchange))
				//        {
				//            DocExchangeItem docReq = new DocExchangeItem();
				//            docItem.Exchanges.Add(docReq);
				//            ImportMvdRequirement(mvdReq, docReq, docProject);
				//        }
				//    }
				//}

				if (rule.References != null)
				{
					foreach (Concept con in rule.References)
					{
						DocTemplateUsage docInner = new DocTemplateUsage();
						docItem.Concepts.Add(docInner);
						ImportMvdConcept(con, docInner, docProject, mapExchange);
					}
				}

				//docItem.Order = rule.Order;
				//switch (rule.Usage)
				//{
				//    case TemplateRuleUsage.System:
				//        docItem.Calculated = true;
				//        break;

				//    case TemplateRuleUsage.Calculation:
				//        docItem.Calculated = true;
				//        docItem.Optional = true;
				//        break;

				//    case TemplateRuleUsage.Reference:
				//        docItem.Reference = true;
				//        break;

				//    case TemplateRuleUsage.Key:
				//        docItem.Key = true;
				//        break;

				//    case TemplateRuleUsage.Optional:
				//        docItem.Optional = true;
				//        break;

				//    case TemplateRuleUsage.Required:
				//        // default
				//        break;
				//}
			}

			return docItem;
		}

		private static void ImportMvdTemplate(ConceptTemplate mvdTemplate, DocTemplateDefinition docDef, Dictionary<EntityRule, DocModelRuleEntity> fixups)
		{
			ImportMvdObject(mvdTemplate, docDef);
			docDef.Type = mvdTemplate.ApplicableEntity;

			docDef.Rules.Clear();
			if (mvdTemplate.Rules != null)
			{
				foreach (AttributeRule mvdRule in mvdTemplate.Rules)
				{
					DocModelRule docRule = ImportMvdRule(mvdRule, fixups);
					docDef.Rules.Add(docRule);
					docRule.ParentRule = null;
				}
			}

			// recurse through subtemplates
			if (mvdTemplate.SubTemplates != null)
			{
				foreach (ConceptTemplate mvdSub in mvdTemplate.SubTemplates)
				{
					DocTemplateDefinition docSub = docDef.GetTemplate(mvdSub.Uuid);
					if (docSub == null)
					{
						docSub = new DocTemplateDefinition();
						docDef.Templates.Add(docSub);
					}
					ImportMvdTemplate(mvdSub, docSub, fixups);
				}
			}
		}

				internal static DocModelRule ImportMvdRule(AttributeRule mvdRule, Dictionary<EntityRule, DocModelRuleEntity> fixups)
		{
			DocModelRuleAttribute docRule = new DocModelRuleAttribute();
			docRule.Name = mvdRule.AttributeName;
			docRule.Description = mvdRule.Description;
			docRule.Identification = mvdRule.RuleID;
			//ImportMvdCardinality(docRule, mvdRule.Cardinality);

			if (mvdRule.EntityRules != null)
			{
				foreach (EntityRule mvdEntityRule in mvdRule.EntityRules)
				{
					DocModelRuleEntity docRuleEntity = new DocModelRuleEntity();
					docRuleEntity.Name = mvdEntityRule.EntityName;
					docRuleEntity.Description = mvdEntityRule.Description;
					docRuleEntity.Identification = mvdEntityRule.RuleID;
					//ImportMvdCardinality(docRule, mvdRule.Cardinality);
					docRule.Rules.Add(docRuleEntity);
					docRuleEntity.ParentRule = docRule;
					if (mvdEntityRule.References != null)
					{
						docRuleEntity.Prefix = mvdEntityRule.References.IdPrefix;
					}

					if (mvdEntityRule.AttributeRules != null)
					{
						foreach (AttributeRule mvdAttributeRule in mvdEntityRule.AttributeRules)
						{
							DocModelRule docRuleAttribute = ImportMvdRule(mvdAttributeRule, fixups);
							docRuleEntity.Rules.Add(docRuleAttribute);
							docRuleAttribute.ParentRule = docRuleEntity;
						}
					}

					if (mvdEntityRule.Constraints != null)
					{
						foreach (Constraint mvdConstraint in mvdEntityRule.Constraints)
						{
							DocModelRuleConstraint docRuleConstraint = new DocModelRuleConstraint();
							docRuleConstraint.Description = mvdConstraint.Expression;
							docRuleEntity.Rules.Add(docRuleConstraint);
							docRuleConstraint.ParentRule = docRuleEntity;
						}
					}

					if (mvdEntityRule.References != null)
					{
						// add it later, as referenced templates may not necessarily be loaded yet
						fixups.Add(mvdEntityRule, docRuleEntity);
					}
				}
			}

			if (mvdRule.Constraints != null)
			{
				foreach (Constraint mvdConstraint in mvdRule.Constraints)
				{
					DocModelRuleConstraint docRuleConstraint = new DocModelRuleConstraint();
					docRuleConstraint.Description = mvdConstraint.Expression;
					docRule.Rules.Add(docRuleConstraint);
					docRuleConstraint.ParentRule = docRule;
				}
			}

			return docRule;
		}


		/// <summary>
		/// Helper function to populate attributes
		/// </summary>
		/// <param name="mvd"></param>
		/// <param name="doc"></param>
		private static void ImportMvdObject(Element mvd, DocObject doc)
		{
			doc.Name = mvd.Name;
			doc.Uuid = mvd.Uuid;
			doc.Version = mvd.Version;
			doc.Owner = mvd.Owner;
			doc.Status = mvd.Status.ToString();
			doc.Copyright = mvd.Copyright;
			doc.Code = mvd.Code;
			doc.Author = mvd.Author;

			if (mvd.Definitions != null)
			{
				foreach (Definition def in mvd.Definitions)
				{
					if (def != null && def.Body != null)
					{
						if (!String.IsNullOrEmpty(def.Body.Lang))
						{
							// mvdXML1.1 now uses this
							DocLocalization loc = new DocLocalization();
							doc.Localization.Add(loc);
							loc.Name = def.Tags;
							loc.Locale = def.Body.Lang;
							loc.Documentation = def.Body.Content;
							loc.Category = DocCategoryEnum.Definition;
						}
						else if (def.Body != null)
						{
							// base definition
							doc.Documentation = def.Body.Content;
						}

						if (def.Links != null)
						{
							// older exports use this
							foreach (Link link in def.Links)
							{
								DocLocalization loc = doc.GetLocalization(link.Lang);
								if (loc == null)
								{
									loc = new DocLocalization();
									doc.Localization.Add(loc);

									try
									{
										loc.Category = (DocCategoryEnum)(CategoryEnum)Enum.Parse(typeof(CategoryEnum), link.Category.ToString());
									}
									catch
									{

									}
									loc.Locale = link.Lang;
									loc.URL = link.Href;
								}

								loc.Name = link.Title;
								if (loc.Documentation == null)
								{
									loc.Documentation = link.Content;
								}
							}
						}
					}
				}
			}
		}

		private static DocEntity LookupEntity(DocProject project, string name)
		{
			// inefficient, but keeps code organized

			foreach (DocSection section in project.Sections)
			{
				foreach (DocSchema schema in section.Schemas)
				{
					foreach (DocEntity entity in schema.Entities)
					{
						if (entity.Name.Equals(name))
						{
							return entity;
						}
					}
				}
			}

			return null;
		}

		// each list is optional- if specified then must be followed; if null, then no filter applies (all included)
		public static void ExportMvd(
			mvdXML mvd,
			DocProject docProject,
			string version,
			Dictionary<string, DocObject> map,
			Dictionary<DocObject, bool> included)
		{
			mvd.Uuid = Guid.NewGuid(); // changes every time saved
			mvd.Name = "mvd";

			// export all referenced shared templates
			foreach (DocTemplateDefinition docTemplateDef in docProject.Templates)
			{
				if (included == null || included.ContainsKey(docTemplateDef))
				{
					ConceptTemplate mvdConceptTemplate = new ConceptTemplate();
					mvd.Templates.Add(mvdConceptTemplate);
					ExportMvdTemplate(mvdConceptTemplate, docTemplateDef, included, true);
				}
			}

			// export all non-shared templates
			//...
			foreach (DocModelView docModelView in docProject.ModelViews)
			{
				if (included == null || included.ContainsKey(docModelView))
				{
					ModelView mvdModelView = new ModelView();
					mvd.Views.Add(mvdModelView);

					List<DocTemplateDefinition> listPrivateTemplates = new List<DocTemplateDefinition>();
					ExportMvdView(mvdModelView, docModelView, docProject, version, map, included, listPrivateTemplates);

					if (listPrivateTemplates.Count > 0)
					{
						ConceptTemplate mvdViewTemplate = new ConceptTemplate();
						mvdViewTemplate.Name = "_" + docModelView.Name; // underscore indicates hidden
						mvdViewTemplate.Uuid = docModelView.Uuid;
						mvdViewTemplate.SubTemplates = new List<ConceptTemplate>();
						mvd.Templates.Add(mvdViewTemplate);

						foreach (DocTemplateDefinition docTemp in listPrivateTemplates)
						{
							ConceptTemplate mvdConceptTemplate = new ConceptTemplate();
							ExportMvdTemplate(mvdConceptTemplate, docTemp, included, true);
							mvdViewTemplate.SubTemplates.Add(mvdConceptTemplate);
						}
					}
				}

			}
		}

		internal static void ExportMvdView(
			ModelView mvdModelView,
			DocModelView docModelView,
			DocProject docProject,
			string version,
			Dictionary<string, DocObject> map,
			Dictionary<DocObject, bool> included,
			List<DocTemplateDefinition> listPrivateTemplates)
		{
			ExportMvdObject(mvdModelView, docModelView, true);
			mvdModelView.ApplicableSchema = docProject.GetSchemaIdentifier();
			mvdModelView.BaseView = docModelView.BaseView;

			foreach (DocExchangeDefinition docExchangeDef in docModelView.Exchanges)
			{
				ExchangeRequirement mvdExchangeDef = new ExchangeRequirement();
				mvdModelView.ExchangeRequirements.Add(mvdExchangeDef);
				ExportMvdObject(mvdExchangeDef, docExchangeDef, true);
				switch (docExchangeDef.Applicability)
				{
					case DocExchangeApplicabilityEnum.Export:
						mvdExchangeDef.Applicability = ApplicabilityEnum.Export;
						break;

					case DocExchangeApplicabilityEnum.Import:
						mvdExchangeDef.Applicability = ApplicabilityEnum.Import;
						break;
				}
			}

			// export template usages for model view
			foreach (DocConceptRoot docRoot in docModelView.ConceptRoots)
			{
				if (docRoot.ApplicableEntity != null)
				{
					// check if entity contains any concept roots
					ConceptRoot mvdConceptRoot = new ConceptRoot();
					mvdModelView.Roots.Add(mvdConceptRoot);

					ExportMvdConceptRoot(mvdConceptRoot, docRoot, docProject, version, map, true, listPrivateTemplates);
				}
			}

			// V12: export nested model views
			if (version == mvdXML.NamespaceV12)
			{
				if (docModelView.ModelViews.Count > 0)
				{
					mvdModelView.Views = new List<ModelView>();
				}
				foreach (DocModelView docSubView in docModelView.ModelViews)
				{
					ModelView mvdSubView = new ModelView();
					mvdModelView.Views.Add(mvdSubView);
					ExportMvdView(mvdSubView, docSubView, docProject, version, map, included, listPrivateTemplates);
				}
			}
		}

		public static void ExportMvdTemplate(ConceptTemplate mvdTemplate, DocTemplateDefinition docTemplateDef, Dictionary<DocObject, bool> included, bool documentation)
		{
			ExportMvdObject(mvdTemplate, docTemplateDef, documentation);
			mvdTemplate.Name = docTemplateDef.Name;
			if (string.IsNullOrEmpty(mvdTemplate.Name))
				mvdTemplate.Name = "NOTDEFINED";
			mvdTemplate.ApplicableEntity = docTemplateDef.Type;

			if (docTemplateDef.Rules != null && docTemplateDef.Rules.Count > 0)
			{
				mvdTemplate.Rules = new List<AttributeRule>();

				foreach (DocModelRule docRule in docTemplateDef.Rules)
				{
					AttributeRule mvdAttr = new AttributeRule();
					mvdTemplate.Rules.Add(mvdAttr);
					ExportMvdRule(mvdAttr, docRule, docTemplateDef);
				}
			}

			// recurse through sub-templates
			if (docTemplateDef.Templates != null && docTemplateDef.Templates.Count > 0)
			{

				foreach (DocTemplateDefinition docSub in docTemplateDef.Templates)
				{
					if (included == null || included.ContainsKey(docSub))
					{
						if (mvdTemplate.SubTemplates == null)
						{
							mvdTemplate.SubTemplates = new List<ConceptTemplate>();
						}

						ConceptTemplate mvdSub = new ConceptTemplate();
						mvdTemplate.SubTemplates.Add(mvdSub);
						ExportMvdTemplate(mvdSub, docSub, included, documentation);
					}
				}
			}
		}

		/// <summary>
		/// Helper function to populate attributes
		/// </summary>
		/// <param name="mvd"></param>
		/// <param name="doc"></param>
		private static void ExportMvdObject(Element mvd, DocObject doc, bool documentation)
		{
			mvd.Name = EnsureValidString(doc.Name);
			mvd.Uuid = doc.Uuid;
			mvd.Version = EnsureValidString(doc.Version);
			mvd.Owner = EnsureValidString(doc.Owner);
			StatusEnum status = StatusEnum.Sample;
			if (Enum.TryParse<StatusEnum>(doc.Status, out status))
				mvd.Status = status;
			mvd.Copyright = EnsureValidString(doc.Copyright);
			mvd.Code = EnsureValidString(doc.Code);
			mvd.Author = EnsureValidString(doc.Author);

			if (documentation && doc.Documentation != null)
			{
				Definition mvdDef = new Definition();
				mvdDef.Body = new Body();
				mvdDef.Body.Content = doc.Documentation;

				mvd.Definitions = new List<Definition>();
				mvd.Definitions.Add(mvdDef);

				if (doc.Localization != null && doc.Localization.Count > 0)
				{
					foreach (DocLocalization docLocal in doc.Localization)
					{
						Definition mvdLocalDef = new Definition();

						//mvdLocalDef.Lang = docLocal.Locale;
						//mvdLocalDef.Tags = docLocal.Name;
						Body mvdBody = new Body();
						mvdLocalDef.Body = mvdBody;
						mvdBody.Lang = docLocal.Locale;
						mvdBody.Content = docLocal.Documentation;

						Link mvdLink = new Link();
						mvdLocalDef.Links = new List<Link>();
						mvdLocalDef.Links.Add(mvdLink);
						if (!String.IsNullOrEmpty(docLocal.Name))
						{
							mvdLink.Title = docLocal.Name;
						}
						if (!String.IsNullOrEmpty(docLocal.Locale))
						{
							mvdLink.Lang = docLocal.Locale;
						}
						mvdLink.Href = String.Empty; // must not be null for valid mvdXML
						if (!String.IsNullOrEmpty(docLocal.URL))
						{
							mvdLink.Href = docLocal.URL;
						}
						mvdLink.Category = (CategoryEnum)(int)docLocal.Category;

						mvd.Definitions.Add(mvdLocalDef);
					}

#if false // old
                    mvdDef.Links = new List<Link>();
                    foreach (DocLocalization docLocal in doc.Localization)
                    {
                        Link mvdLink = new Link();
                        mvdDef.Links.Add(mvdLink);
                        mvdLink.Title = docLocal.Name;
                        // old -- now use above mvdLink.Content = docLocal.Documentation;
                        mvdLink.Lang = docLocal.Locale;
                        mvdLink.Href = docLocal.URL;
                        mvdLink.Category = (CategoryEnum)(int)docLocal.Category;
                    }
                
#endif
				}
			}
		}

		public static void ExportMvdConceptRoot(
			ConceptRoot mvdConceptRoot,
			DocConceptRoot docRoot,
			DocProject docProject,
			string version,
			Dictionary<string, DocObject> map,
			bool documentation,
			List<DocTemplateDefinition> listPrivateTemplates)
		{
			ExportMvdObject(mvdConceptRoot, docRoot, documentation);

			if (String.IsNullOrEmpty(mvdConceptRoot.Name))
			{
				mvdConceptRoot.Name = docRoot.ApplicableEntity.Name;
			}

			mvdConceptRoot.ApplicableRootEntity = docRoot.ApplicableEntity.Name;
			if (docRoot.ApplicableTemplate != null)
			{
				mvdConceptRoot.Applicability = new ApplicabilityRules();
				mvdConceptRoot.Applicability.Template = new TemplateRef();
				mvdConceptRoot.Applicability.Template.Ref = docRoot.ApplicableTemplate.Uuid;
				mvdConceptRoot.Applicability.TemplateRules = new TemplateRules();

				mvdConceptRoot.Applicability.TemplateRules.Operator = (TemplateOperator)Enum.Parse(typeof(TemplateOperator), docRoot.ApplicableOperator.ToString());
				foreach (DocTemplateItem docItem in docRoot.ApplicableItems)
				{
					TemplateRule rule = ExportMvdItem(docItem, docRoot.ApplicableTemplate, docProject, null, map);
					mvdConceptRoot.Applicability.TemplateRules.Add(rule);
				}

				if (docProject.GetTemplate(docRoot.ApplicableTemplate.Uuid) == null)
				{
					listPrivateTemplates.Add(docRoot.ApplicableTemplate);
				}
			}

			if (docRoot.Concepts.Count > 0)
			{
				mvdConceptRoot.Concepts = new List<Concept>();
				foreach (DocTemplateUsage docTemplateUsage in docRoot.Concepts)
				{
					Concept mvdConceptLeaf = new Concept();
					mvdConceptRoot.Concepts.Add(mvdConceptLeaf);
					ExportMvdConcept(mvdConceptLeaf, docTemplateUsage, docProject, version, map, documentation, listPrivateTemplates);
				}
			}
		}

		internal static void ExportMvdConcept(
			Concept mvdConceptLeaf,
			DocTemplateUsage docTemplateUsage,
			DocProject docProject,
			string version,
			Dictionary<string, DocObject> map,
			bool documentation,
			List<DocTemplateDefinition> listPrivateTemplates)
		{
			ExportMvdObject(mvdConceptLeaf, docTemplateUsage, documentation);

			if (docTemplateUsage.Definition != null)
			{
				mvdConceptLeaf.Template = new TemplateRef();
				mvdConceptLeaf.Template.Ref = docTemplateUsage.Definition.Uuid;

				if (docProject.GetTemplate(docTemplateUsage.Definition.Uuid) == null)
				{
					listPrivateTemplates.Add(docTemplateUsage.Definition);
				}
			}

			mvdConceptLeaf.Override = docTemplateUsage.Override;

			if (String.IsNullOrEmpty(mvdConceptLeaf.Name))
			{
				mvdConceptLeaf.Name = docTemplateUsage.Definition.Name;
			}

			// requirements
			foreach (DocExchangeItem docExchangeRef in docTemplateUsage.Exchanges)
			{
				if (docExchangeRef.Exchange != null)
				{
					ConceptRequirement mvdRequirement = new ConceptRequirement();

					if (mvdConceptLeaf.Requirements == null)
					{
						mvdConceptLeaf.Requirements = new List<ConceptRequirement>();
					}
					mvdConceptLeaf.Requirements.Add(mvdRequirement);
					ExportMvdRequirement(mvdRequirement, docExchangeRef);
				}
			}

			// rules         
			if (docTemplateUsage.Items.Count > 0)
			{
				mvdConceptLeaf.TemplateRules = new TemplateRules();
				mvdConceptLeaf.TemplateRules.Operator = (TemplateOperator)Enum.Parse(typeof(TemplateOperator), docTemplateUsage.Operator.ToString());
				foreach (DocTemplateItem docRule in docTemplateUsage.Items)
				{
					TemplateRule mvdTemplateRule = ExportMvdItem(docRule, docTemplateUsage.Definition, docProject, version, map);
					mvdConceptLeaf.TemplateRules.Add(mvdTemplateRule);

					// using proposed mvdXML schema
					if (mvdTemplateRule is TemplateItem)
					{
						TemplateItem ruleV12 = (TemplateItem)mvdTemplateRule;
						ruleV12.References = new List<Concept>();
						foreach (DocTemplateUsage docInner in docRule.Concepts)
						{
							Concept mvdInner = new Concept();
							ruleV12.References.Add(mvdInner);
							ExportMvdConcept(mvdInner, docInner, docProject, version, map, documentation, listPrivateTemplates);
						}
					}
				}
			}
		}

		private static void ExportMvdRule(AttributeRule mvdRule, DocModelRule docRule, DocTemplateDefinition docTemplate)
		{
			if (!String.IsNullOrEmpty(docRule.Identification))
			{
				mvdRule.RuleID = docRule.Identification.Replace(' ', '_');
			}
			mvdRule.Description = docRule.Description;
			mvdRule.AttributeName = docRule.Name;

			foreach (DocModelRule docRuleEntity in docRule.Rules)
			{
				if (docRuleEntity is DocModelRuleEntity)
				{
					if (mvdRule.EntityRules == null)
					{
						mvdRule.EntityRules = new List<EntityRule>();
					}

					EntityRule mvdRuleEntity = new EntityRule();
					mvdRule.EntityRules.Add(mvdRuleEntity);
					if (!String.IsNullOrEmpty(docRuleEntity.Identification))
					{
						mvdRuleEntity.RuleID = docRuleEntity.Identification.Replace(' ', '_');
					}
					mvdRuleEntity.Description = docRuleEntity.Description;
					mvdRuleEntity.EntityName = docRuleEntity.Name;

					// references
					DocModelRuleEntity dme = (DocModelRuleEntity)docRuleEntity;
					if (dme.References.Count > 0)
					{
						mvdRuleEntity.References = new References();
						mvdRuleEntity.References.IdPrefix = dme.Prefix;
						mvdRuleEntity.References.Template = new List<TemplateRef>();
						foreach (DocTemplateDefinition dtd in dme.References)
						{
							TemplateRef tr = new TemplateRef();
							tr.Ref = dtd.Uuid;
							mvdRuleEntity.References.Template.Add(tr);

							break; // only one reference template can be exported
						}
					}

					foreach (DocModelRule docRuleAttribute in docRuleEntity.Rules)
					{
						if (docRuleAttribute is DocModelRuleAttribute)
						{
							if (mvdRuleEntity.AttributeRules == null)
							{
								mvdRuleEntity.AttributeRules = new List<AttributeRule>();
							}

							AttributeRule mvdRuleAttribute = new AttributeRule();
							mvdRuleEntity.AttributeRules.Add(mvdRuleAttribute);
							ExportMvdRule(mvdRuleAttribute, docRuleAttribute, docTemplate);
						}
						else if (docRuleAttribute is DocModelRuleConstraint)
						{
							DocModelRuleConstraint mrc = (DocModelRuleConstraint)docRuleAttribute;
							string expr = mrc.FormatExpression(docTemplate);
							if (docTemplate.Name == "Element Decomposition Precast")
							{
								string e = expr;
								DocOpExpression expression = mrc.Expression;
								//expr = expression.ToString();
							}

							// replace with attribute name
							if (expr != null)
							{
								int bracket = expr.IndexOf('[');
								if (bracket > 0)
								{
									if (mvdRuleEntity.Constraints == null)
									{
										mvdRuleEntity.Constraints = new List<Constraint>();
									}

									Constraint mvdConstraint = new Constraint();
									mvdRuleEntity.Constraints.Add(mvdConstraint);

									if (expr.StartsWith("("))
									{
										//string toDelete = expr.Substring(1, (bracket - 1));
										//expr = expr.Substring(1).Replace(toDelete, "");
										//mvdConstraint.Expression = docRule.Identification + expr.Remove(expr.Length - 1);
										//mvdConstraint.Expression = expr.Replace("(", "").Replace(")", "");
										mvdConstraint.Expression = expr;
									}
									else
									{
										mvdConstraint.Expression = docRule.Identification + expr.Substring(bracket);
									}
								}
							}
						}
					}

				}
				else if (docRuleEntity is DocModelRuleConstraint)
				{
					if (mvdRule.Constraints == null)
					{
						mvdRule.Constraints = new List<Constraint>();
					}

					Constraint mvdConstraint = new Constraint();
					mvdRule.Constraints.Add(mvdConstraint);
					mvdConstraint.Expression = docRuleEntity.Description;
				}
			}
		}

		internal static void ExportMvdRequirement(ConceptRequirement mvdRequirement, DocExchangeItem docExchangeRef)
		{
			switch (docExchangeRef.Applicability)
			{
				case DocExchangeApplicabilityEnum.Export:
					mvdRequirement.Applicability = ApplicabilityEnum.Export;
					break;

				case DocExchangeApplicabilityEnum.Import:
					mvdRequirement.Applicability = ApplicabilityEnum.Import;
					break;
			}

			switch (docExchangeRef.Requirement)
			{
				case DocExchangeRequirementEnum.Excluded:
					mvdRequirement.Requirement = RequirementEnum.Excluded;
					break;

				case DocExchangeRequirementEnum.Mandatory:
					mvdRequirement.Requirement = RequirementEnum.Mandatory;
					break;

				case DocExchangeRequirementEnum.NotRelevant:
					mvdRequirement.Requirement = RequirementEnum.NotRelevant;
					break;

				case DocExchangeRequirementEnum.Optional:
					mvdRequirement.Requirement = RequirementEnum.Recommended;
					break;

				default:
					mvdRequirement.Requirement = RequirementEnum.NotRelevant;
					break;
			}

			mvdRequirement.ExchangeRequirement = docExchangeRef.Exchange.Uuid;
		}

		internal static TemplateRule ExportMvdItem(
			DocTemplateItem docItem,
			DocTemplateDefinition docTemplate,
			DocProject docProject,
			string version,
			Dictionary<string, DocObject> map)
		{
			TemplateRule mvdRule;

			if (version == mvdXML.NamespaceV12)
			{
				TemplateItem mvdTemplateRule = new TemplateItem();
				mvdRule = mvdTemplateRule;
				//if (docItem.Calculated)
				//{
				//    if (docItem.Optional)
				//    {
				//        mvdTemplateRule.Usage = TemplateRuleUsage.Calculation;
				//    }
				//    else
				//    {
				//        mvdTemplateRule.Usage = TemplateRuleUsage.System;
				//    }
				//}
				//else if (docItem.Key)
				//{
				//    mvdTemplateRule.Usage = TemplateRuleUsage.Key;
				//}
				//else if (docItem.Reference)
				//{
				//    mvdTemplateRule.Usage = TemplateRuleUsage.Reference;
				//}
				//else if (docItem.Optional)
				//{
				//    mvdTemplateRule.Usage = TemplateRuleUsage.Optional;
				//}

				//mvdTemplateRule.Order = docItem.Order;

				// requirements -- not yet captured in user interface
				if (docItem.Exchanges.Count > 0)
				{
					//mvdTemplateRule.Requirements = new List<ConceptRequirement>();
					foreach (DocExchangeItem docExchangeItem in docItem.Exchanges)
					{
						ConceptRequirement mvdRequirement = new ConceptRequirement();
						//mvdTemplateRule.Requirements.Add(mvdRequirement);
						ExportMvdRequirement(mvdRequirement, docExchangeItem);
					}
				}
			}
			else
			{
				mvdRule = new TemplateRule();
			}

			mvdRule.Description = docItem.Documentation;
			//if (String.IsNullOrEmpty(docItem.RuleParameters))
			//{
			mvdRule.Parameters = docItem.FormatParameterExpressions(docTemplate, docProject, map); // was RuleParameters;
																								   //}
																								   //else
																								   //{
																								   //mvdRule.Parameters = docItem.RuleParameters;
																								   //}


			return mvdRule;
		}

		private static string EnsureValidString(string value)
		{
			if (String.IsNullOrEmpty(value))
			{
				return null;
			}

			return value;
		}
	}
}
