using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

using IfcDoc.Schema.DOC;
using IfcDoc.Schema.SCH;
using IfcDoc.Format.XML;

namespace IfcDoc.Format.SCH
{
	public class FormatSCH : FormatXML
	{
		public FormatSCH(string file, Type type) : base(file, type, null, null)
		{
		}

		public FormatSCH(string file, Type type, string defaultnamespace)
			: base(file, type, defaultnamespace, null)
		{
		}
		
		public FormatSCH(string file, Type type, string defaultnamespace, XmlSerializerNamespaces prefixes) : base(file, type, defaultnamespace, prefixes)
		{
			string dirpath = System.IO.Path.GetDirectoryName(file);
			if (!Directory.Exists(dirpath))
			{
				Directory.CreateDirectory(dirpath);
			}
		}

		public FormatSCH(Stream stream, Type type, string defaultnamespace, XmlSerializerNamespaces prefixes) : base(stream, type, defaultnamespace, prefixes)
		{
		}

		public void Save(DocProject docProject, Dictionary<DocObject, bool> included)
		{
			Schema.SCH.schema sch = new Schema.SCH.schema();
			base.Instance = sch;
			ExportSch(sch, docProject, included);
			base.Save();
		}

		private void ExportSch(IfcDoc.Schema.SCH.schema schema, DocProject docProject, Dictionary<DocObject, bool> included)
		{
			Dictionary<DocExchangeDefinition, phase> mapPhase = new Dictionary<DocExchangeDefinition, phase>();

			foreach (DocModelView docModel in docProject.ModelViews)
			{
				if (included == null || included.ContainsKey(docModel))
				{
					foreach (DocExchangeDefinition docExchange in docModel.Exchanges)
					{
						phase ph = new phase();
						schema.Phases.Add(ph);
						ph.id = docExchange.Name.ToLower().Replace(" ", "-");

						mapPhase.Add(docExchange, ph);
					}

					foreach (DocConceptRoot docRoot in docModel.ConceptRoots)
					{
						foreach (DocTemplateUsage docConcept in docRoot.Concepts)
						{
							pattern pat = new pattern();
							schema.Patterns.Add(pat);

							pat.id = docRoot.ApplicableEntity.Name.ToLower() + "-" + docConcept.Definition.Name.ToLower().Replace(" ", "-");// docConcept.Uuid.ToString();
							pat.name = docConcept.Definition.Name;
							pat.p = docConcept.Documentation;

							foreach (DocExchangeItem docExchangeUsage in docConcept.Exchanges)
							{
								if (docExchangeUsage.Applicability == DocExchangeApplicabilityEnum.Export && docExchangeUsage.Requirement == DocExchangeRequirementEnum.Mandatory)
								{
									phase ph = mapPhase[docExchangeUsage.Exchange];
									active a = new active();
									a.pattern = pat.id;
									ph.Actives.Add(a);
								}
							}

							// recurse through template rules

							List<DocModelRule> listParamRules = new List<DocModelRule>();
							if (docConcept.Definition.Rules != null)
							{
								foreach (DocModelRule docRule in docConcept.Definition.Rules)
								{
									docRule.BuildParameterList(listParamRules);
								}
							}


							List<DocModelRule[]> listPaths = new List<DocModelRule[]>();
							foreach (DocModelRule docRule in listParamRules)
							{
								DocModelRule[] rulepath = docConcept.Definition.BuildRulePath(docRule);
								listPaths.Add(rulepath);
							}


							if (docConcept.Items.Count > 0)
							{
								foreach (DocTemplateItem docItem in docConcept.Items)
								{
									rule r = new rule();
									pat.Rules.Add(r);

									r.context = "//" + docRoot.ApplicableEntity.Name;

									//TODO: detect constraining parameter and generate XPath...
									for (int iRule = 0; iRule < listParamRules.Count; iRule++)// (DocModelRule docRule in listParamRules)
									{
										DocModelRule docRule = listParamRules[iRule];
										if (docRule.IsCondition())
										{
											DocModelRule[] docPath = listPaths[iRule];

											StringBuilder sbContext = new StringBuilder();
											sbContext.Append("[@");
											for (int iPart = 0; iPart < docPath.Length; iPart++)
											{
												sbContext.Append(docPath[iPart].Name);
												sbContext.Append("/");
											}

											sbContext.Append(" = ");
											string cond = docItem.GetParameterValue(docRule.Identification);
											sbContext.Append(cond);

											sbContext.Append("]");

											r.context += sbContext.ToString();
										}
									}

									for (int iRule = 0; iRule < listParamRules.Count; iRule++)// (DocModelRule docRule in listParamRules)
									{
										DocModelRule docRule = listParamRules[iRule];

										if (!docRule.IsCondition())
										{
											string value = docItem.GetParameterValue(docRule.Identification);
											if (value != null)
											{
												DocModelRule[] docPath = listPaths[iRule];

												StringBuilder sbContext = new StringBuilder();
												for (int iPart = 0; iPart < docPath.Length; iPart++)
												{
													sbContext.Append(docPath[iPart].Name);
													sbContext.Append("/");
												}

												sbContext.Append(" = '");
												sbContext.Append(value);
												sbContext.Append("'");

												assert a = new assert();
												a.test = sbContext.ToString();
												r.Asserts.Add(a);
											}
										}
									}
								}
							}
							else
							{
								// recurse through each rule
								List<DocModelRule> pathRule = new List<DocModelRule>();
								foreach (DocModelRule docModelRule in docConcept.Definition.Rules)
								{
									pathRule.Add(docModelRule);

									rule r = new rule();
									r.context = "//" + docRoot.ApplicableEntity;
									pat.Rules.Add(r);

									ExportSchRule(r, pathRule);

									pathRule.Remove(docModelRule);
								}
							}
						}

					}
				}


			}
		}

		private static void ExportSchRule(rule r, List<DocModelRule> pathRule)
		{
			assert a = new assert();
			r.Asserts.Add(a);

			StringBuilder sb = new StringBuilder();
			foreach (DocModelRule docRule in pathRule)
			{
				sb.Append(docRule.Name);
				sb.Append("/");
			}
			a.test = sb.ToString();

			// recurse
			DocModelRule docParent = pathRule[pathRule.Count - 1];
			if (docParent.Rules != null)
			{
				foreach (DocModelRule docSub in docParent.Rules)
				{
					pathRule.Add(docSub);

					ExportSchRule(r, pathRule);

					pathRule.Remove(docSub);
				}
			}
		}
	}
}
