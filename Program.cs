// Name:        Program.cs
// Description: Command line utility entry point
// Author:      Tim Chipman
// Origination: Work performed for BuildingSmart by Constructivity.com LLC.
// Copyright:   (c) 2010 BuildingSmart International Ltd.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using IfcDoc.Schema;
using IfcDoc.Schema.VEX;
using IfcDoc.Schema.DOC;
using IfcDoc.Schema.MVD;
using IfcDoc.Schema.PSD;
using IfcDoc.Schema.SCH;
using IfcDoc.Format.HTM;

//using BuildingSmart.IFC;
using BuildingSmart.IFC.IfcKernel;
using BuildingSmart.IFC.IfcExternalReferenceResource;
using BuildingSmart.IFC.IfcMeasureResource;
using BuildingSmart.IFC.IfcPropertyResource;
using BuildingSmart.IFC.IfcUtilityResource;

using BuildingSmart.Utilities.Conversion;

#if MDB
    using IfcDoc.Format.MDB;
#endif

namespace IfcDoc
{
	class Program
	{
		[STAThread]
		static void Main(string[] args)
		{
			System.Windows.Forms.Application.EnableVisualStyles();
			System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
			System.Windows.Forms.Application.Run(new FormEdit(args));
		}

		private static void ImportVexRectangle(DocDefinition docDefinition, RECTANGLE rectangle, SCHEMATA schemata)
		{
			if (rectangle != null && schemata.settings != null && schemata.settings.page != null)
			{
				int px = (int)(rectangle.x / schemata.settings.page.width);
				int py = (int)(rectangle.y / schemata.settings.page.height);
				int page = 1 + py * schemata.settings.page.nhorizontalpages + px;
				docDefinition.DiagramNumber = page;

				docDefinition.DiagramRectangle = new DocRectangle();
				docDefinition.DiagramRectangle.X = rectangle.x;
				docDefinition.DiagramRectangle.Y = rectangle.y;
				docDefinition.DiagramRectangle.Width = rectangle.dx;
				docDefinition.DiagramRectangle.Height = rectangle.dy;
			}
		}

		private static void ImportVexLine(OBJECT_LINE_LAYOUT layout, TEXT_LAYOUT textlayout, List<DocPoint> diagramLine, DocRectangle diagramLabel)
		{
			if (layout == null)
				return;

			diagramLine.Add(new DocPoint(layout.pline.startpoint.wx, layout.pline.startpoint.wy));

			int direction = layout.pline.startdirection;
			double posx = layout.pline.startpoint.wx;
			double posy = layout.pline.startpoint.wy;

			for (int i = 0; i < layout.pline.rpoint.Count; i++)
			{
				if (diagramLabel != null && textlayout != null &&
					layout.textplacement != null &&
					layout.textplacement.npos == i)
				{
					diagramLabel.X = textlayout.x + posx;
					diagramLabel.Y = textlayout.y + posy;
					diagramLabel.Width = textlayout.width;
					diagramLabel.Height = textlayout.height;
				}

				double offset = layout.pline.rpoint[i];
				if (direction == 1)
				{
					posy += offset;
					direction = 0;
				}
				else if (direction == 0)
				{
					posx += offset;
					direction = 1;
				}
				diagramLine.Add(new DocPoint(posx, posy));
			}
		}

		/// <summary>
		/// Creates a documentation schema from a VEX schema
		/// </summary>
		/// <param name="schemata">The VEX schema to import</param>
		/// <param name="project">The documentation project where the imported schema is to be created</param>
		/// <returns>The imported documentation schema</returns>
		internal static DocSchema ImportVex(SCHEMATA schemata, DocProject docProject, bool updateDescriptions)
		{
			DocSchema docSchema = docProject.RegisterSchema(schemata.name);
			if (updateDescriptions && schemata.comment != null && schemata.comment.text != null)
			{
				docSchema.Documentation = schemata.comment.text.text;
			}
			docSchema.DiagramPagesHorz = schemata.settings.page.nhorizontalpages;
			docSchema.DiagramPagesVert = schemata.settings.page.nverticalpages;

			// remember current types for deletion if they no longer exist
			List<DocObject> existing = new List<DocObject>();
			foreach (DocType doctype in docSchema.Types)
			{
				existing.Add(doctype);
			}
			foreach (DocEntity docentity in docSchema.Entities)
			{
				existing.Add(docentity);
			}
			foreach (DocFunction docfunction in docSchema.Functions)
			{
				existing.Add(docfunction);
			}
			foreach (DocGlobalRule docrule in docSchema.GlobalRules)
			{
				existing.Add(docrule);
			}

			docSchema.PageTargets.Clear();
			docSchema.SchemaRefs.Clear();
			docSchema.Comments.Clear();

			// remember references for fixing up attributes afterwords
			Dictionary<object, DocDefinition> mapRefs = new Dictionary<object, DocDefinition>();
			Dictionary<ATTRIBUTE_DEF, DocAttribute> mapAtts = new Dictionary<ATTRIBUTE_DEF, DocAttribute>();
			//Dictionary<SELECT_DEF, DocSelectItem> mapSels = new Dictionary<SELECT_DEF, DocSelectItem>();
			Dictionary<SELECT_DEF, DocLine> mapSL = new Dictionary<SELECT_DEF, DocLine>();
			Dictionary<SUBTYPE_DEF, DocLine> mapSubs = new Dictionary<SUBTYPE_DEF, DocLine>();
			Dictionary<PAGE_REF, DocPageTarget> mapPage = new Dictionary<PAGE_REF, DocPageTarget>();

			// entities and types
			foreach (object obj in schemata.objects)
			{
				if (obj is ENTITIES)
				{
					ENTITIES ent = (ENTITIES)obj; // filter out orphaned entities having upper flags set
					if ((ent.flag & 0xFFFF0000) == 0 && (ent.interfaceto == null || ent.interfaceto.theschema == null))
					{
						// create if doesn't exist
						string name = ent.name.text;

						string super = null;
						if (ent.supertypes.Count > 0 && ent.supertypes[0].the_supertype is ENTITIES)
						{
							ENTITIES superent = (ENTITIES)ent.supertypes[0].the_supertype;
							super = superent.name.text;
						}

						DocEntity docEntity = docSchema.RegisterEntity(name);
						if (existing.Contains(docEntity))
						{
							existing.Remove(docEntity);
						}
						mapRefs.Add(obj, docEntity);

						// clear out existing if merging
						docEntity.BaseDefinition = null;

						foreach (DocSubtype docSub in docEntity.Subtypes)
						{
							docSub.Delete();
						}
						docEntity.Subtypes.Clear();

						foreach (DocUniqueRule docUniq in docEntity.UniqueRules)
						{
							docUniq.Delete();
						}
						docEntity.UniqueRules.Clear();

						foreach (DocLine docLine in docEntity.Tree)
						{
							docLine.Delete();
						}
						docEntity.Tree.Clear();

						if (updateDescriptions && ent.comment != null && ent.comment.text != null)
						{
							docEntity.Documentation = ent.comment.text.text;
						}
						if (ent.supertypes.Count > 0 && ent.supertypes[0].the_supertype is ENTITIES)
						{
							docEntity.BaseDefinition = ((ENTITIES)ent.supertypes[0].the_supertype).name.text;
						}

						docEntity.EntityFlags = ent.flag;
						if (ent.subtypes != null)
						{
							foreach (SUBTYPE_DEF sd in ent.subtypes)
							{
								// new (3.8): intermediate subtypes for diagrams
								DocLine docLine = new DocLine();
								ImportVexLine(sd.layout, null, docLine.DiagramLine, null);
								docEntity.Tree.Add(docLine);

								OBJECT od = (OBJECT)sd.the_subtype;

								// tunnel through page ref
								if (od is PAGE_REF_TO)
								{
									od = ((PAGE_REF_TO)od).pageref;
								}

								if (od is TREE)
								{
									TREE tree = (TREE)od;
									foreach (OBJECT o in tree.list)
									{
										OBJECT os = o;
										OBJECT_LINE_LAYOUT linelayout = null;

										if (o is SUBTYPE_DEF)
										{
											SUBTYPE_DEF osd = (SUBTYPE_DEF)o;
											linelayout = osd.layout;

											os = ((SUBTYPE_DEF)o).the_subtype;
										}

										if (os is PAGE_REF_TO)
										{
											os = ((PAGE_REF_TO)os).pageref;
										}

										if (os is ENTITIES)
										{
											DocSubtype docSub = new DocSubtype();
											docSub.DefinedType = ((ENTITIES)os).name.text;
											docEntity.Subtypes.Add(docSub);

											DocLine docSubline = new DocLine();
											docLine.Tree.Add(docSubline);

											if (o is SUBTYPE_DEF)
											{
												mapSubs.Add((SUBTYPE_DEF)o, docSubline);
											}

											ImportVexLine(linelayout, null, docSubline.DiagramLine, null);
										}
										else
										{
											Debug.Assert(false);
										}
									}
								}
								else if (od is ENTITIES)
								{
									DocSubtype docInt = new DocSubtype();
									docEntity.Subtypes.Add(docInt);

									docInt.DefinedType = ((ENTITIES)od).name.text;
								}
								else
								{
									Debug.Assert(false);
								}
							}
						}

						// determine EXPRESS-G page based on placement (required for generating hyperlinks)
						if (ent.layout != null)
						{
							ImportVexRectangle(docEntity, ent.layout.rectangle, schemata);
						}

						if (ent.attributes != null)
						{
							List<DocAttribute> existingattr = new List<DocAttribute>();
							foreach (DocAttribute docAttr in docEntity.Attributes)
							{
								existingattr.Add(docAttr);
							}

							// attributes are replaced, not merged (template don't apply here)                            
							foreach (ATTRIBUTE_DEF attr in ent.attributes)
							{
								if (attr.name != null)
								{
									DocAttribute docAttr = docEntity.RegisterAttribute(attr.name.text);
									mapAtts.Add(attr, docAttr);

									if (existingattr.Contains(docAttr))
									{
										existingattr.Remove(docAttr);
									}

									if (updateDescriptions && attr.comment != null && attr.comment.text != null)
									{
										docAttr.Documentation = attr.comment.text.text;
									}

									if (docAttr.DiagramLabel != null)
									{
										docAttr.DiagramLabel.Delete();
										docAttr.DiagramLabel = null;
									}

									foreach (DocPoint docPoint in docAttr.DiagramLine)
									{
										docPoint.Delete();
									}
									docAttr.DiagramLine.Clear();

									if (attr.layout != null)
									{
										if (attr.layout.pline != null)
										{
											// intermediate lines
											if (attr.layout.pline.rpoint != null)
											{
												docAttr.DiagramLabel = new DocRectangle();
												ImportVexLine(attr.layout, attr.name.layout, docAttr.DiagramLine, docAttr.DiagramLabel);
											}
										}
									}

									OBJECT def = attr.the_attribute;
									if (attr.the_attribute is PAGE_REF_TO)
									{
										PAGE_REF_TO pr = (PAGE_REF_TO)attr.the_attribute;
										def = pr.pageref;
									}

									if (def is DEFINED_TYPE)
									{
										DEFINED_TYPE dt = (DEFINED_TYPE)def;
										docAttr.DefinedType = dt.name.text;
									}
									else if (def is ENTITIES)
									{
										ENTITIES en = (ENTITIES)def;
										docAttr.DefinedType = en.name.text;
									}
									else if (def is ENUMERATIONS)
									{
										ENUMERATIONS en = (ENUMERATIONS)def;
										docAttr.DefinedType = en.name.text;
									}
									else if (def is SELECTS)
									{
										SELECTS en = (SELECTS)def;
										docAttr.DefinedType = en.name.text;
									}
									else if (def is PRIMITIVE_TYPE)
									{
										PRIMITIVE_TYPE en = (PRIMITIVE_TYPE)def;

										string length = "";
										if (en.constraints > 0)
										{
											length = " (" + en.constraints.ToString() + ")";
										}
										else if (en.constraints < 0)
										{
											int len = -en.constraints;
											length = " (" + len.ToString() + ") FIXED";
										}

										docAttr.DefinedType = en.name.text + length;
									}
									else if (def is SCHEMA_REF)
									{
										SCHEMA_REF en = (SCHEMA_REF)def;
										docAttr.DefinedType = en.name.text;
									}
									else
									{
										Debug.Assert(false);
									}

									docAttr.AttributeFlags = attr.attributeflag;

									AGGREGATES vexAggregates = attr.aggregates;
									DocAttribute docAggregate = docAttr;
									while (vexAggregates != null)
									{
										// traverse nested aggregation (e.g. IfcStructuralLoadConfiguration)
										docAggregate.AggregationType = vexAggregates.aggrtype + 1;
										docAggregate.AggregationLower = vexAggregates.lower;
										docAggregate.AggregationUpper = vexAggregates.upper;
										docAggregate.AggregationFlag = vexAggregates.flag;

										vexAggregates = vexAggregates.next;
										if (vexAggregates != null)
										{
											// inner array (e.g. IfcStructuralLoadConfiguration)
											docAggregate.AggregationAttribute = new DocAttribute();
											docAggregate = docAggregate.AggregationAttribute;
										}
									}

									docAttr.Derived = attr.is_derived;

									if (attr.user_redeclaration != null)
									{
										docAttr.Inverse = attr.user_redeclaration;
									}
									else if (attr.is_inverse is ATTRIBUTE_DEF)
									{
										ATTRIBUTE_DEF adef = (ATTRIBUTE_DEF)attr.is_inverse;
										docAttr.Inverse = adef.name.text;
									}
									else if (attr.is_inverse != null)
									{
										Debug.Assert(false);
									}
								}
							}

							foreach (DocAttribute docAttr in existingattr)
							{
								docEntity.Attributes.Remove(docAttr);
								docAttr.Delete();
							}
						}

						// unique rules
						if (ent.uniquenes != null)
						{
							// rules are replaced, not merged (template don't apply here)
							//docEntity.UniqueRules = new List<DocUniqueRule>();
							foreach (UNIQUE_RULE rule in ent.uniquenes)
							{
								DocUniqueRule docRule = new DocUniqueRule();
								docEntity.UniqueRules.Add(docRule);
								docRule.Name = rule.name;

								docRule.Items = new List<DocUniqueRuleItem>();
								foreach (ATTRIBUTE_DEF ruleitem in rule.for_attribute)
								{
									DocUniqueRuleItem item = new DocUniqueRuleItem();
									item.Name = ruleitem.name.text;
									docRule.Items.Add(item);
								}
							}
						}

						// where rules
						if (ent.wheres != null)
						{
							List<DocWhereRule> existingattr = new List<DocWhereRule>();
							foreach (DocWhereRule docWhere in docEntity.WhereRules)
							{
								existingattr.Add(docWhere);
							}

							foreach (WHERE_RULE where in ent.wheres)
							{
								DocWhereRule docWhere = docEntity.RegisterWhereRule(where.name);
								docWhere.Expression = where.rule_context;

								if (existingattr.Contains(docWhere))
								{
									existingattr.Remove(docWhere);
								}

								if (updateDescriptions && where.comment != null && where.comment.text != null)
								{
									docWhere.Documentation = where.comment.text.text;
								}
							}

							foreach (DocWhereRule exist in existingattr)
							{
								exist.Delete();
								docEntity.WhereRules.Remove(exist);
							}
						}
					}
				}
				else if (obj is ENUMERATIONS)
				{
					ENUMERATIONS ent = (ENUMERATIONS)obj;
					if (ent.interfaceto == null || ent.interfaceto.theschema == null)
					{
						if (schemata.name.Equals("IfcConstructionMgmtDomain", StringComparison.OrdinalIgnoreCase) && ent.name.text.Equals("IfcNullStyle", StringComparison.OrdinalIgnoreCase))
						{
							// hack to workaround vex bug
							Debug.Assert(true);
						}
						else
						{

							DocEnumeration docEnumeration = docSchema.RegisterType<DocEnumeration>(ent.name.text);
							if (existing.Contains(docEnumeration))
							{
								existing.Remove(docEnumeration);
							}
							mapRefs.Add(obj, docEnumeration);

							if (updateDescriptions && ent.comment != null && ent.comment.text != null)
							{
								docEnumeration.Documentation = ent.comment.text.text;
							}

							// determine EXPRESS-G page based on placement (required for generating hyperlinks)
							if (ent.typelayout != null && schemata.settings != null && schemata.settings.page != null)
							{
								ImportVexRectangle(docEnumeration, ent.typelayout.rectangle, schemata);
							}


							// enumeration values are replaced, not merged (template don't apply here)
							docEnumeration.Constants.Clear();
							foreach (string s in ent.enums)
							{
								DocConstant docConstant = new DocConstant();
								docEnumeration.Constants.Add(docConstant);
								docConstant.Name = s;
							}
						}
					}
				}
				else if (obj is DEFINED_TYPE)
				{
					DEFINED_TYPE ent = (DEFINED_TYPE)obj;

					if (ent.interfaceto == null || ent.interfaceto.theschema == null)
					{
						DocDefined docDefined = docSchema.RegisterType<DocDefined>(ent.name.text);
						if (existing.Contains(docDefined))
						{
							existing.Remove(docDefined);
						}

						mapRefs.Add(obj, docDefined);

						if (updateDescriptions && ent.comment != null && ent.comment.text != null)
						{
							docDefined.Documentation = ent.comment.text.text;
						}

						if (ent.layout != null)
						{
							ImportVexRectangle(docDefined, ent.layout.rectangle, schemata);
						}

						if (ent.defined.object_line_layout != null)
						{
							foreach (DocPoint docPoint in docDefined.DiagramLine)
							{
								docPoint.Delete();
							}
							docDefined.DiagramLine.Clear();
							ImportVexLine(ent.defined.object_line_layout, null, docDefined.DiagramLine, null);
						}

						OBJECT os = (OBJECT)ent.defined.defined;
						if (os is PAGE_REF_TO)
						{
							os = ((PAGE_REF_TO)os).pageref;
						}

						if (os is PRIMITIVE_TYPE)
						{
							PRIMITIVE_TYPE pt = (PRIMITIVE_TYPE)os;
							docDefined.DefinedType = pt.name.text;

							if (pt.constraints != 0)
							{
								docDefined.Length = pt.constraints;
							}
						}
						else if (os is DEFINED_TYPE)
						{
							DEFINED_TYPE dt = (DEFINED_TYPE)os;
							docDefined.DefinedType = dt.name.text;
						}
						else if (os is ENTITIES)
						{
							ENTITIES et = (ENTITIES)os;
							docDefined.DefinedType = et.name.text;
						}
						else
						{
							Debug.Assert(false);
						}

						// aggregation
						AGGREGATES vexAggregates = ent.defined.aggregates;
						if (vexAggregates != null)
						{
							DocAttribute docAggregate = new DocAttribute();
							docDefined.Aggregation = docAggregate;

							docAggregate.AggregationType = vexAggregates.aggrtype + 1;
							docAggregate.AggregationLower = vexAggregates.lower;
							docAggregate.AggregationUpper = vexAggregates.upper;
							docAggregate.AggregationFlag = vexAggregates.flag;
						}

						// where rules
						if (ent.whererules != null)
						{
							// rules are replaced, not merged (template don't apply here)
							foreach (DocWhereRule docWhere in docDefined.WhereRules)
							{
								docWhere.Delete();
							}
							docDefined.WhereRules.Clear();
							foreach (WHERE_RULE where in ent.whererules)
							{
								DocWhereRule docWhere = new DocWhereRule();
								docDefined.WhereRules.Add(docWhere);
								docWhere.Name = where.name;
								docWhere.Expression = where.rule_context;

								if (where.comment != null && where.comment.text != null)
								{
									docWhere.Documentation = where.comment.text.text;
								}
							}
						}

					}
				}
				else if (obj is SELECTS)
				{
					SELECTS ent = (SELECTS)obj;
					if (ent.interfaceto == null || ent.interfaceto.theschema == null)
					{
						DocSelect docSelect = docSchema.RegisterType<DocSelect>(ent.name.text);
						if (existing.Contains(docSelect))
						{
							existing.Remove(docSelect);
						}
						mapRefs.Add(obj, docSelect);

						if (updateDescriptions && ent.comment != null && ent.comment.text != null)
						{
							docSelect.Documentation = ent.comment.text.text;
						}

						// determine EXPRESS-G page based on placement (required for generating hyperlinks)
						if (ent.typelayout != null)
						{
							ImportVexRectangle(docSelect, ent.typelayout.rectangle, schemata);
						}

						docSelect.Selects.Clear();
						docSelect.Tree.Clear();
						foreach (SELECT_DEF sdef in ent.selects)
						{
							DocLine docLine = new DocLine();
							docSelect.Tree.Add(docLine);
							ImportVexLine(sdef.layout, null, docLine.DiagramLine, null);

							mapSL.Add(sdef, docLine);

							if (sdef.def is TREE)
							{
								TREE tree = (TREE)sdef.def;

								foreach (OBJECT o in tree.list)
								{
									DocSelectItem dsi = new DocSelectItem();
									docSelect.Selects.Add(dsi);

									OBJECT os = o;
									if (o is SELECT_DEF)
									{
										SELECT_DEF selectdef = (SELECT_DEF)o;

										DocLine docLineSub = new DocLine();
										docLine.Tree.Add(docLineSub);
										ImportVexLine(selectdef.layout, null, docLineSub.DiagramLine, null);

										mapSL.Add(selectdef, docLineSub);

										os = ((SELECT_DEF)o).def;
									}
									else
									{
										Debug.Assert(false);
									}

									if (os is PAGE_REF_TO)
									{
										PAGE_REF_TO pr = (PAGE_REF_TO)os;
										os = pr.pageref;
									}

									if (os is DEFINITION)
									{
										dsi.Name = ((DEFINITION)os).name.text;
									}
								}
							}
							else
							{
								OBJECT os = (OBJECT)sdef.def;

								if (os is PAGE_REF_TO)
								{
									PAGE_REF_TO pr = (PAGE_REF_TO)os;
									os = pr.pageref;
								}

								DocSelectItem dsi = new DocSelectItem();
								docSelect.Selects.Add(dsi);
								if (os is DEFINITION)
								{
									dsi.Name = ((DEFINITION)os).name.text;
								}
							}
						}
					}
				}
				else if (obj is GLOBAL_RULE)
				{
					GLOBAL_RULE func = (GLOBAL_RULE)obj;

					DocGlobalRule docFunction = docSchema.RegisterRule(func.name);
					if (existing.Contains(docFunction))
					{
						existing.Remove(docFunction);
					}

					// clear out existing if merging
					docFunction.WhereRules.Clear();

					if (updateDescriptions && func.comment != null && func.comment.text != null)
					{
						docFunction.Documentation = func.comment.text.text;
					}
					docFunction.Expression = func.rule_context;

					foreach (WHERE_RULE wr in func.where_rule)
					{
						DocWhereRule docW = new DocWhereRule();
						docW.Name = wr.name;
						docW.Expression = wr.rule_context;
						if (wr.comment != null)
						{
							docW.Documentation = wr.comment.text.text;
						}
						docFunction.WhereRules.Add(docW);
					}

					if (func.for_entities.Count == 1)
					{
						docFunction.ApplicableEntity = func.for_entities[0].ToString();
					}
				}
				else if (obj is USER_FUNCTION)
				{
					USER_FUNCTION func = (USER_FUNCTION)obj;

					DocFunction docFunction = docSchema.RegisterFunction(func.name);
					if (existing.Contains(docFunction))
					{
						existing.Remove(docFunction);
					}

					if (updateDescriptions && func.comment != null && func.comment.text != null)
					{
						docFunction.Documentation = func.comment.text.text;
					}
					docFunction.Expression = func.rule_context;

					// NOTE: While the VEX schema can represent parameters and return values, Visual Express does not implement it!
					// Rather, parameter info is also included in the 'rule_context'
					if (func.return_value != null)
					{
						docFunction.ReturnValue = func.return_value.ToString();
					}
					else
					{
						docFunction.ReturnValue = null;
					}
					docFunction.Parameters.Clear();
					if (func.parameter_list != null)
					{
						foreach (PARAMETER par in func.parameter_list)
						{
							DocParameter docParameter = new DocParameter();
							docParameter.Name = par.name;
							docParameter.DefinedType = par.parameter_type.ToString();
							docFunction.Parameters.Add(docParameter);
						}
					}
				}
				else if (obj is PRIMITIVE_TYPE)
				{
					PRIMITIVE_TYPE prim = (PRIMITIVE_TYPE)obj;

					DocPrimitive docPrimitive = new DocPrimitive();
					docPrimitive.Name = prim.name.text;
					if (prim.layout != null)
					{
						ImportVexRectangle(docPrimitive, prim.layout.rectangle, schemata);
					}

					docSchema.Primitives.Add(docPrimitive);
					mapRefs.Add(obj, docPrimitive);
				}
				else if (obj is COMMENT)
				{
					COMMENT comment = (COMMENT)obj;

					// only deal with comments that are part of EXPRESS-G layout -- ignore those referenced by definitions and old cruft left behind due to older versions of VisualE that were buggy
					if (comment.layout != null)
					{
						DocComment docComment = new DocComment();
						docComment.Documentation = comment.text.text;
						ImportVexRectangle(docComment, comment.layout.rectangle, schemata);

						docSchema.Comments.Add(docComment);
					}
				}
				else if (obj is INTERFACE_SCHEMA)
				{
					INTERFACE_SCHEMA iface = (INTERFACE_SCHEMA)obj;

					DocSchemaRef docSchemaRef = new DocSchemaRef();
					docSchema.SchemaRefs.Add(docSchemaRef);

					docSchemaRef.Name = iface.schema_name;

					foreach (object o in iface.item)
					{
						if (o is DEFINITION)
						{
							DocDefinitionRef docDefRef = new DocDefinitionRef();
							docSchemaRef.Definitions.Add(docDefRef);
							mapRefs.Add(o, docDefRef);

							docDefRef.Name = ((DEFINITION)o).name.text;

							if (o is DEFINED_TYPE)
							{
								DEFINED_TYPE dt = (DEFINED_TYPE)o;
								if (dt.layout != null)
								{
									ImportVexRectangle(docDefRef, dt.layout.rectangle, schemata);
								}
							}
							else if (o is ENTITIES)
							{
								ENTITIES ents = (ENTITIES)o;
								if (ents.layout != null) // null for IfcPolyline reference in IfcGeometricModelResource
								{
									ImportVexRectangle(docDefRef, ents.layout.rectangle, schemata);
								}

								if (ents.subtypes != null)
								{
									foreach (SUBTYPE_DEF subdef in ents.subtypes)
									{
										OBJECT_LINE_LAYOUT linelayout = subdef.layout;

										DocLine docSub = new DocLine();
										ImportVexLine(subdef.layout, null, docSub.DiagramLine, null);
										docDefRef.Tree.Add(docSub);

										if (subdef.the_subtype is TREE)
										{
											TREE tree = (TREE)subdef.the_subtype;
											foreach (object oo in tree.list)
											{
												if (oo is SUBTYPE_DEF)
												{
													SUBTYPE_DEF subsubdef = (SUBTYPE_DEF)oo;
													DocLine docSubSub = new DocLine();
													docSub.Tree.Add(docSubSub);

													ImportVexLine(subsubdef.layout, null, docSubSub.DiagramLine, null);

													mapSubs.Add(subsubdef, docSubSub);
												}
											}
										}
									}
								}
							}
							else if (o is ENUMERATIONS)
							{
								ENUMERATIONS enums = (ENUMERATIONS)o;
								if (enums.typelayout != null)
								{
									ImportVexRectangle(docDefRef, enums.typelayout.rectangle, schemata);
								}
							}
							else if (o is SELECTS)
							{
								SELECTS sels = (SELECTS)o;
								if (sels.typelayout != null)
								{
									ImportVexRectangle(docDefRef, sels.typelayout.rectangle, schemata);
								}
							}
							else if (o is SCHEMA_REF)
							{
								SCHEMA_REF sref = (SCHEMA_REF)o;
								if (sref.layout != null)
								{
									ImportVexRectangle(docDefRef, sref.layout.rectangle, schemata);
								}
							}
						}
						else if (o is USER_FUNCTION)
						{
							DocDefinitionRef docDefRef = new DocDefinitionRef();
							docSchemaRef.Definitions.Add(docDefRef);

							USER_FUNCTION uf = (USER_FUNCTION)o;
							docDefRef.Name = uf.name;
						}
					}
				}
				else if (obj is PAGE_REF)
				{
					PAGE_REF pageref = (PAGE_REF)obj;

					DocPageTarget docPageTarget = new DocPageTarget();
					docSchema.PageTargets.Add(docPageTarget);
					docPageTarget.Name = pageref.text.text;
					docPageTarget.DiagramNumber = pageref.pagenr;
					ImportVexLine(pageref.pageline.layout, null, docPageTarget.DiagramLine, null);
					ImportVexRectangle(docPageTarget, pageref.layout.rectangle, schemata);

					foreach (PAGE_REF_TO pagerefto in pageref.pagerefto)
					{
						DocPageSource docPageSource = new DocPageSource();
						docPageTarget.Sources.Add(docPageSource);

						docPageSource.DiagramNumber = pagerefto.pagenr;
						docPageSource.Name = pagerefto.text.text;
						ImportVexRectangle(docPageSource, pagerefto.layout.rectangle, schemata);

						mapRefs.Add(pagerefto, docPageSource);
					}

					mapPage.Add(pageref, docPageTarget);
				}
			}

			foreach (DocObject docobj in existing)
			{
				if (docobj is DocEntity)
				{
					docSchema.Entities.Remove((DocEntity)docobj);
				}
				else if (docobj is DocType)
				{
					docSchema.Types.Remove((DocType)docobj);
				}
				else if (docobj is DocFunction)
				{
					docSchema.Functions.Remove((DocFunction)docobj);
				}
				else if (docobj is DocGlobalRule)
				{
					docSchema.GlobalRules.Remove((DocGlobalRule)docobj);
				}

				docobj.Delete();
			}

			// now fix up attributes
			foreach (ATTRIBUTE_DEF docAtt in mapAtts.Keys)
			{
				DocAttribute docAttr = mapAtts[docAtt];
				docAttr.Definition = mapRefs[docAtt.the_attribute];
			}

			foreach (PAGE_REF page in mapPage.Keys)
			{
				DocPageTarget docPage = mapPage[page];
				docPage.Definition = mapRefs[page.pageline.pageref];
			}

			foreach (SELECT_DEF sd in mapSL.Keys)
			{
				DocLine docLine = mapSL[sd];
				if (mapRefs.ContainsKey(sd.def))
				{
					docLine.Definition = mapRefs[sd.def];
				}
			}

			foreach (SUBTYPE_DEF sd in mapSubs.Keys)
			{
				DocLine docLine = mapSubs[sd];
				if (mapRefs.ContainsKey(sd.the_subtype))
				{
					docLine.Definition = mapRefs[sd.the_subtype];
				}
			}

			foreach (object o in mapRefs.Keys)
			{
				if (o is DEFINED_TYPE)
				{
					DEFINED_TYPE def = (DEFINED_TYPE)o;
					if (def.interfaceto == null || def.interfaceto.theschema == null)
					{
						// declared within
						DocDefined docDef = (DocDefined)mapRefs[o];
						docDef.Definition = mapRefs[def.defined.defined];
					}
				}
			}

			return docSchema;
		}



		internal static void SetObject(DocObject obj, IfcRoot root)
		{
			Guid guid = GlobalId.Parse(root.GlobalId);
			if (guid != Guid.Empty)
				obj.UniqueId = guid.ToString();
			else if (obj is DocPropertySet || obj is DocQuantitySet)
				obj.Uuid = GlobalId.HashGuid(root.Name);
			obj.Name = root.Name;
		 	obj.Documentation = root.Description;

			//foreach (DocLocalization docLoc in docObject.Localization)
			//{
			//	IfcLibraryReference ifcLib = new IfcLibraryReference(new IfcURIReference(docLoc.URL), null, new IfcLabel(docLoc.Name), new IfcText(docLoc.Documentation), new IfcLanguageId(docLoc.Locale), null);
			//	IfcRelAssociatesLibrary ifcRal = new IfcRelAssociatesLibrary(NewGuid(), null, null, null, new IfcDefinitionSelect[] { ifcDefinition }, ifcLib);
			//	ifcDefinition.HasAssociations.Add(ifcRal);
			//}
		}

		internal static IfcGloballyUniqueId NewGuid()
		{
			return new IfcGloballyUniqueId(GlobalId.Format(Guid.NewGuid()));
		}

		private class ImportAggregate
		{
			private DocProject m_Project = null;
			Dictionary<string, DocProperty> propertyTemplates = new Dictionary<string, DocProperty>();
			Dictionary<string, DocQuantity> quantityTemplates = new Dictionary<string, DocQuantity>();
			Dictionary<string, DocPropertyEnumeration> propertyEnumerations = new Dictionary<string, DocPropertyEnumeration>();

			internal ImportAggregate(DocProject project)
			{
				m_Project = project;
			}

			internal string uniqueId(IfcGloballyUniqueId globalId)
			{
				Guid guid = GlobalId.Parse(globalId);
				return (guid == Guid.Empty ? Guid.NewGuid() : guid).ToString();
			}
			internal void createOrFindQuantityTemplate(IfcPropertyTemplate template, DocQuantitySet host, ref List<string> changes)
			{
				string id = uniqueId(template.GlobalId);
				DocQuantity result = null, processed = null;
				string change = "";
				if (quantityTemplates.TryGetValue(id, out processed))
				{
					if (host == null)
						return;
					change = "  PROCESSED Quantity " + template.Name;
					result = processed;
				}
				else
				{
					result = m_Project.Quantities.Where(x => string.Compare(x.UniqueId, id) == 0).FirstOrDefault();
					if (result == null)
					{
						DocQuantity existing = host.Quantities.Where(x => string.Compare(x.Name, template.Name) == 0).FirstOrDefault();
						if (existing == null)
						{
							change = "  NEW Quantity " + template.Name;
							result = new DocQuantity() { Name = template.Name, UniqueId = id, Documentation = template.Description };
							m_Project.Quantities.Add(result);
						}
						else
							result = existing;
					}
					else
					{
						change = "  EXISTING Quantity " + template.Name;
					}
				}
				if (host != null)
				{
					DocQuantity hosted = host.Quantities.Where(x => string.Compare(x.UniqueId, id) == 0).FirstOrDefault();
					if (hosted == null)
					{
						change += " ADDED";
						host.Quantities.Add(result);
					}
					else
						change += " EXISTING";
				}
				changes.Add(change);
				if (processed == null)
				{
					IfcSimplePropertyTemplate simpleTemplate = template as IfcSimplePropertyTemplate;
					if (simpleTemplate != null)
						set(simpleTemplate, result, ref changes);
					else
						changes.Add("  XX Quantity not defined as IfcSimplePropertyTemplate");
				}
				quantityTemplates[id] = result;
			}
			internal void set(IfcSimplePropertyTemplate template, DocQuantity quantity, ref List<string> changes)
			{
				if (template.TemplateType == null)
					changes.Add("  XXX " + template.Name + " has no nominated quantity template type!");
				else
				{
					DocQuantityTemplateTypeEnum templateType = DocQuantityTemplateTypeEnum.Q_AREA;
					if (Enum.TryParse<DocQuantityTemplateTypeEnum>(template.TemplateType.ToString(), out templateType))
						quantity.QuantityType = templateType;
					else
						changes.Add("  XXX " + template.Name + " has invalid quantity template type!");
				}
				if (template.AccessState != null)
				{
					DocStateEnum state = DocStateEnum.READWRITE;
					if (Enum.TryParse<DocStateEnum>(template.AccessState.ToString(), out state))
						quantity.AccessState = state;
					else
						changes.Add("  XXX " + template.Name + " has invalid Access State!");
				}
			}

			internal DocProperty createOrFindPropertyTemplate(IfcPropertyTemplate template, DocPropertySet host, ref List<string> changes)
			{
				string id = uniqueId(template.GlobalId);
				DocProperty result = null, processed = null;
				string change = "";
				if (propertyTemplates.TryGetValue(id, out processed))
				{
					if (host == null)
						return processed;
					change = "  PROCESSED Property " + template.Name;
					result = processed;
				}
				else
				{
					result = m_Project.Properties.Where(x => string.Compare(x.UniqueId, id) == 0).FirstOrDefault();
					if (result == null)
					{
						DocProperty existing = host.Properties.Where(x => string.Compare(x.Name, template.Name) == 0).FirstOrDefault();
						if (existing == null)
						{
							change = "  NEW Property " + template.Name;
							result = new DocProperty() { Name = template.Name, UniqueId = id, Documentation = template.Description };
							m_Project.Properties.Add(result);
						}
						else
							result = existing;
					}
					else
					{
						change = "  EXISTING Property " + template.Name;
					}
				}
				if (host != null)
				{
					DocProperty hosted = host.Properties.Where(x => string.Compare(x.UniqueId, id) == 0).FirstOrDefault();
					if (hosted == null)
					{
						change += " ADDED";
						host.Properties.Add(result);
					}
					else
						change += " EXISTING";
				}
				changes.Add(change);
				if (processed == null)
					set(template, result, ref changes);
				propertyTemplates[id] = result;
				return result;
			}

			internal void set(IfcPropertyTemplate template, DocProperty property, ref List<string> changes)
			{
				IfcSimplePropertyTemplate simplePropertyTemplate = template as IfcSimplePropertyTemplate;
				if (simplePropertyTemplate != null)
				{
					if (simplePropertyTemplate.TemplateType == null)
						changes.Add("  XXX " + template.Name + " has no nominated property template type!");
					else
					{
						DocPropertyTemplateTypeEnum templateType = DocPropertyTemplateTypeEnum.P_SINGLEVALUE;
						if (Enum.TryParse<DocPropertyTemplateTypeEnum>(simplePropertyTemplate.TemplateType.ToString(), out templateType))
							property.PropertyType = templateType;
						else
							changes.Add("  XXX " + template.Name + " has invalid property template type!");
					}
					if (simplePropertyTemplate.AccessState != null)
					{
						DocStateEnum state = DocStateEnum.READWRITE;
						if (Enum.TryParse<DocStateEnum>(simplePropertyTemplate.AccessState.ToString(), out state))
							property.AccessState = state;
						else
							changes.Add("  XXX " + template.Name + " has invalid Access State!");
					}


					if (!string.IsNullOrEmpty(simplePropertyTemplate.PrimaryMeasureType))
						property.PrimaryDataType = simplePropertyTemplate.PrimaryMeasureType;
					if (!string.IsNullOrEmpty(simplePropertyTemplate.SecondaryMeasureType))
						property.SecondaryDataType = simplePropertyTemplate.SecondaryMeasureType;

					IfcPropertyEnumeration enumeration = simplePropertyTemplate.Enumerators;
					if (enumeration != null)
					{
						DocPropertyEnumeration propertyEnumeration = null;
						if (propertyEnumerations.TryGetValue(propertyEnumeration.Name, out propertyEnumeration))
							property.Enumeration = propertyEnumeration;
						else
						{
							propertyEnumeration = m_Project.PropertyEnumerations.Where(x => string.Compare(x.Name, enumeration.Name, true) == 0).FirstOrDefault();
							if (propertyEnumeration == null)
							{
								changes.Add("    NEW PropertyEnumeration " + enumeration.Name);
								propertyEnumeration = new DocPropertyEnumeration() { Name = enumeration.Name };
								m_Project.PropertyEnumerations.Add(propertyEnumeration);
							}
							else
								changes.Add("    EXISTING PropertyEnumeration " + enumeration.Name);
							propertyEnumerations[propertyEnumeration.Name] = propertyEnumeration;
							foreach (IfcValue value in enumeration.EnumerationValues)
							{
								string valueString = value.ToString();
								DocPropertyConstant constant = propertyEnumeration.Constants.Where(x => string.Compare(x.Name, valueString) == 0).FirstOrDefault();
								if (constant == null)
								{
									constant = new DocPropertyConstant() { Name = valueString };
									changes.Add("      NEW PropertyConstant " + constant.Name + "ADDED");
									m_Project.PropertyConstants.Add(constant);
									propertyEnumeration.Constants.Add(constant);
								}
								else
									changes.Add("      EXISTING PropertyConstant " + constant.Name);
								//Improve to force UNSET, NOTDEFINED etc to end
							}
						}
					}
				}
				else
				{
					IfcComplexPropertyTemplate complexPropertyTemplate = template as IfcComplexPropertyTemplate;
					if (complexPropertyTemplate != null)
					{
						property.PropertyType = DocPropertyTemplateTypeEnum.COMPLEX;
						foreach (IfcPropertyTemplate propertyTemplate in complexPropertyTemplate.HasPropertyTemplates)
						{
							DocProperty nestedProperty = createOrFindPropertyTemplate(propertyTemplate, null, ref changes);

							DocProperty existingProperty = property.Elements.Where(x => string.Compare(x.UniqueId, nestedProperty.UniqueId, true) == 0).FirstOrDefault();
							if(existingProperty != null)
								changes.Add("      EXISTING Property " + nestedProperty.Name);
							else
							{
								changes.Add("      SET Property " + nestedProperty.Name);
								property.Elements.Add(nestedProperty);
							}
						}
					}
				}
			}
		}
		internal static void ImportIFC(string filePath, DocProject project, DocSchema schema)
		{
			if(schema == null)
			{
				System.Windows.Forms.MessageBox.Show("Schema wasn't nominated, import aborted!", "No Schema", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
				return;
			}
			IfcContext context = null;
			if (System.IO.Path.GetExtension(filePath) == ".ifcxml")
			{
				BuildingSmart.Serialization.Xml.XmlSerializer xmlSerializer = new BuildingSmart.Serialization.Xml.XmlSerializer(typeof(IfcContext));
				using (System.IO.FileStream streamSource = new System.IO.FileStream(filePath, System.IO.FileMode.Open))
				{
					context = xmlSerializer.ReadObject(streamSource) as IfcContext;
				}
			}
			else
			{
				BuildingSmart.Serialization.Step.StepSerializer stepSerializer = new BuildingSmart.Serialization.Step.StepSerializer(typeof(IfcContext));
				using (System.IO.FileStream streamSource = new System.IO.FileStream(filePath, System.IO.FileMode.Open))
				{
					context = stepSerializer.ReadObject(streamSource) as IfcContext;
				}
			}
			if (context == null)
			{
				System.Windows.Forms.MessageBox.Show("No valid IfcContext found, import aborted!", "No Context", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
				return;
			}
		
			List<string> changes = new List<string>();
			ImportAggregate aggregate = new ImportAggregate(project);
			foreach (IfcDefinitionSelect definition in context.Declares.SelectMany(x => x.RelatedDefinitions))
			{
				DocSchema existingSchema = null;
				IfcPropertySetTemplate propertySetTemplate = definition as IfcPropertySetTemplate;
				if (propertySetTemplate != null)
				{
					changes.Add("\r\n");
					DocVariableSet variableSet = null;
					if (propertySetTemplate.TemplateType.ToString().ToUpper().StartsWith("QTO"))
					{
						DocQuantitySet quantitySet = project.FindQuantitySet(propertySetTemplate.Name, out existingSchema);
						if (quantitySet == null)
						{
							quantitySet = new DocQuantitySet();
							SetObject(quantitySet, propertySetTemplate);
							changes.Add("NEW QuantitySet " + propertySetTemplate.Name);
							schema.QuantitySets.Add(quantitySet);
						}
						else
						{
							if (string.Compare(existingSchema.Name, schema.Name) != 0)
								changes.Add("DIFFERING SCHEMA " + propertySetTemplate.Name + " located in " + existingSchema.Name);
							changes.Add("EXISTING QuantitySet " + propertySetTemplate.Name);
							if(!string.IsNullOrEmpty(propertySetTemplate.Description) && string.Compare(propertySetTemplate.Description, quantitySet.Documentation) != 0)
							{
								quantitySet.Documentation = propertySetTemplate.Description;
								changes.Add(propertySetTemplate.Name + " revised documentation : \r\n < " + propertySetTemplate.Description + "\r\n> " + quantitySet.Documentation);
							}
						}
						variableSet = quantitySet;
						foreach (IfcPropertyTemplate template in propertySetTemplate.HasPropertyTemplates)
							aggregate.createOrFindQuantityTemplate(template, quantitySet, ref changes);
					}
					else
					{
						DocPropertySet propertySet = project.FindPropertySet(propertySetTemplate.Name, out existingSchema);
						if (propertySet == null)
						{
							propertySet = new DocPropertySet();
							SetObject(propertySet, propertySetTemplate);
							changes.Add("NEW PropertySet " + propertySetTemplate.Name);
							schema.PropertySets.Add(propertySet);
						}
						else
						{
							if (string.Compare(existingSchema.Name, schema.Name) != 0)
								changes.Add("DIFFERING SCHEMA " + propertySetTemplate.Name + " located in " + existingSchema.Name);
							changes.Add("EXISTING PropertySet " + propertySetTemplate.Name);
							if (!string.IsNullOrEmpty(propertySetTemplate.Description) && string.Compare(propertySetTemplate.Description, propertySet.Documentation) != 0)
							{
								propertySet.Documentation = propertySetTemplate.Description;
								changes.Add(propertySetTemplate.Name + " revised documentation : \r\n < " + propertySetTemplate.Description + "\r\n> " + propertySet.Documentation);
							}
						}
						variableSet = propertySet;
						foreach (IfcPropertyTemplate template in propertySetTemplate.HasPropertyTemplates)
							aggregate.createOrFindPropertyTemplate(template, propertySet, ref changes);

						if(propertySetTemplate.TemplateType != null)
							propertySet.PropertySetType = propertySetTemplate.TemplateType.Value.ToString();
					}
					if(propertySetTemplate.ApplicableEntity != null)
						variableSet.ApplicableType = propertySetTemplate.ApplicableEntity.Value.Value;
				}
				else
				{ 
					IfcPropertyTemplate propertyTemplate = definition as IfcPropertyTemplate;
					if (propertyTemplate != null)
					{
						IfcSimplePropertyTemplate simplePropertyTemplate = propertyTemplate as IfcSimplePropertyTemplate;
						if (simplePropertyTemplate != null && simplePropertyTemplate.TemplateType != null && simplePropertyTemplate.TemplateType.ToString().ToUpper()[0] == 'Q')
							aggregate.createOrFindQuantityTemplate(propertyTemplate, null, ref changes);
						else
							aggregate.createOrFindPropertyTemplate(propertyTemplate, null, ref changes);
					}
				}
			}

			string pathLog = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filePath), System.IO.Path.GetFileNameWithoutExtension(filePath)+ ".txt");
			System.IO.File.WriteAllText(pathLog, string.Join("\r\n", changes));
			System.Diagnostics.Process.Start(pathLog);
		}	



		public static DocPropertySet ImportPsd(PropertySetDef psd, DocProject docProject)
		{
			DocSchema docSchema = null;
			string schema = null;
			if (psd.Versions != null && psd.Versions.Count > 0)
			{
				schema = psd.Versions[0].schema;
				docSchema = docProject.GetSchema(schema);
			}

			if (String.IsNullOrEmpty(schema))
			{
				// guess the schema according to applicable type value
				if (psd.ApplicableTypeValue != null)
				{
					string[] parts = psd.ApplicableTypeValue.Split(new char[] { '/', '[' });
					string ent = parts[0];
					DocEntity docEntity = docProject.GetDefinition(ent) as DocEntity;
					if (docEntity != null)
					{
						docSchema = docProject.GetSchemaOfDefinition(docEntity);
						schema = docSchema.Name;
					}
				}
			}

			if (schema == null)
			{
				schema = "IfcKernel";//fallback
				docSchema = docProject.GetSchema(schema);

				if (docSchema == null)
				{
					// create default schema
					docSchema = new DocSchema();
					docSchema.Name = schema;
					docProject.Sections[4].Schemas.Add(docSchema);
				}
			}

			// find existing pset if applicable
			DocPropertySet pset = docSchema.RegisterPset(psd.Name);

			// use hashed guid
			if (pset.Uuid == Guid.Empty)
			{
				pset.Uuid = BuildingSmart.Utilities.Conversion.GlobalId.HashGuid(pset.Name);
			}

			pset.Name = psd.Name;
			if (psd.Definition != null)
			{
				pset.Documentation = psd.Definition.Trim();
			}
			if (psd.ApplicableTypeValue != null)
			{
				pset.ApplicableType = psd.ApplicableTypeValue.Replace("Type", "").Replace("[PerformanceHistory]", ""); // organize at occurrences; use pset type to determine type applicability
			}

			// for now, rely on naming convention (better to capture in pset schema eventually)
			if (psd.Name.Contains("PHistory")) // special naming convention
			{
				pset.PropertySetType = "PSET_PERFORMANCEDRIVEN";
			}
			else if (psd.Name.Contains("Occurrence"))
			{
				pset.PropertySetType = "PSET_OCCURRENCEDRIVEN";
			}
			else
			{
				pset.PropertySetType = "PSET_TYPEDRIVENOVERRIDE";
			}

			// import localized definitions
			if (psd.PsetDefinitionAliases != null)
			{
				foreach (PsetDefinitionAlias pl in psd.PsetDefinitionAliases)
				{
					pset.RegisterLocalization(pl.lang, null, pl.Value);
				}
			}

			foreach (PropertyDef subdef in psd.PropertyDefs)
			{
				DocProperty docprop = pset.RegisterProperty(subdef.Name, docProject);
				Program.ImportPsdPropertyTemplate(subdef, docprop, docProject, docSchema);
			}

			return pset;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="def">The PSD property definition to import.</param>
		/// <param name="docprop">Property to fill</param>
		/// <param name="docProject">Project, if needed to retrieve or create enumeration</param>
		/// <param name="docSchema">Schema, if needed to create enumeration</param>
		public static void ImportPsdPropertyTemplate(PropertyDef def, DocProperty docprop, DocProject docProject, DocSchema docSchema)
		{
			DocPropertyTemplateTypeEnum proptype = DocPropertyTemplateTypeEnum.P_SINGLEVALUE;
			string datatype = String.Empty;
			string elemtype = String.Empty;

			if (def.PropertyType.TypePropertyEnumeratedValue != null)
			{
				proptype = DocPropertyTemplateTypeEnum.P_ENUMERATEDVALUE;
				datatype = "IfcLabel";
				//StringBuilder sbEnum = new StringBuilder();
				//sbEnum.Append(def.PropertyType.TypePropertyEnumeratedValue.EnumList.name);
				//sbEnum.Append(":");

				DocPropertyEnumeration docEnum = docProject.FindPropertyEnumeration(def.PropertyType.TypePropertyEnumeratedValue.EnumList.name);
				if (docEnum == null)
				{
					docEnum = new DocPropertyEnumeration();
					docEnum.Name = def.PropertyType.TypePropertyEnumeratedValue.EnumList.name;
					docProject.PropertyEnumerations.Add(docEnum);

					foreach (EnumItem item in def.PropertyType.TypePropertyEnumeratedValue.EnumList.Items)
					{
						DocPropertyConstant docConstant = new DocPropertyConstant();
						docConstant.Name = item.Value;
						docEnum.Constants.Add(docConstant);

						//sbEnum.Append(item.Value);
						//sbEnum.Append(",");
					}
				}
				//sbEnum.Length--; // remove trailing ','
				//elemtype = sbEnum.ToString();
			}
			else if (def.PropertyType.TypePropertyReferenceValue != null)
			{
				proptype = DocPropertyTemplateTypeEnum.P_REFERENCEVALUE;
				datatype = def.PropertyType.TypePropertyReferenceValue.reftype;
				if (def.PropertyType.TypePropertyReferenceValue.DataType != null)
				{
					elemtype = def.PropertyType.TypePropertyReferenceValue.DataType.type; // e.g. IfcTimeSeries
				}
			}
			else if (def.PropertyType.TypePropertyBoundedValue != null)
			{
				proptype = DocPropertyTemplateTypeEnum.P_BOUNDEDVALUE;
				datatype = def.PropertyType.TypePropertyBoundedValue.DataType.type;
			}
			else if (def.PropertyType.TypePropertyListValue != null)
			{
				proptype = DocPropertyTemplateTypeEnum.P_LISTVALUE;
				datatype = def.PropertyType.TypePropertyListValue.ListValue.DataType.type;
			}
			else if (def.PropertyType.TypePropertyTableValue != null)
			{
				proptype = DocPropertyTemplateTypeEnum.P_TABLEVALUE;
				if (def.PropertyType.TypePropertyTableValue.DefiningValue != null)
				{
					datatype = def.PropertyType.TypePropertyTableValue.DefiningValue.DataType.type;
				}
				if (def.PropertyType.TypePropertyTableValue.DefinedValue != null)
				{
					elemtype = def.PropertyType.TypePropertyTableValue.DefinedValue.DataType.type;
				}
			}
			else if (def.PropertyType.TypePropertySingleValue != null)
			{
				proptype = DocPropertyTemplateTypeEnum.P_SINGLEVALUE;
				datatype = def.PropertyType.TypePropertySingleValue.DataType.type;
			}
			else if (def.PropertyType.TypeComplexProperty != null)
			{
				proptype = DocPropertyTemplateTypeEnum.COMPLEX;
				datatype = def.PropertyType.TypeComplexProperty.name;
			}

			if (!String.IsNullOrEmpty(def.IfdGuid))
			{
				try
				{
					docprop.Uuid = new Guid(def.IfdGuid);
				}
				catch
				{
				}
			}

			docprop.Name = def.Name;
			if (def.Definition != null)
			{
				docprop.Documentation = def.Definition.Trim();
			}
			docprop.PropertyType = proptype;
			docprop.PrimaryDataType = datatype;
			docprop.SecondaryDataType = elemtype.Trim();

			foreach (NameAlias namealias in def.NameAliases)
			{
				string localdesc = null;
				foreach (DefinitionAlias docalias in def.DefinitionAliases)
				{
					if (docalias.lang.Equals(namealias.lang))
					{
						localdesc = docalias.Value;
					}
				}

				docprop.RegisterLocalization(namealias.lang, namealias.Value, localdesc);
			}

			// recurse for complex properties
			if (def.PropertyType.TypeComplexProperty != null)
			{
				foreach (PropertyDef subdef in def.PropertyType.TypeComplexProperty.PropertyDefs)
				{
					DocProperty subprop = docprop.RegisterProperty(subdef.Name);
					ImportPsdPropertyTemplate(subdef, subprop, docProject, docSchema);
				}
			}
		}

#if false
        internal static void ImportMvdCardinality(DocModelRule docRule, CardinalityType cardinality)
        {
            switch (cardinality)
            {
                case CardinalityType._asSchema:
                    docRule.CardinalityMin = 0;
                    docRule.CardinalityMax = 0; // same as unitialized file
                    break;

                case CardinalityType.Zero:
                    docRule.CardinalityMin = -1; // means 0:0
                    docRule.CardinalityMax = -1;
                    break;

                case CardinalityType.ZeroToOne:
                    docRule.CardinalityMin = 0;
                    docRule.CardinalityMax = 1;
                    break;

                case CardinalityType.One:
                    docRule.CardinalityMin = 1;
                    docRule.CardinalityMax = 1;
                    break;

                case CardinalityType.OneToMany:
                    docRule.CardinalityMin = 1;
                    docRule.CardinalityMax = -1;
                    break;
            }
        }
#endif
		internal static bool CheckFilter(Array array, object target)
		{
			if (array == null)
				return true;

			foreach (Object o in array)
			{
				if (o == target)
					return true;
			}

			return false;
		}

		/// <summary>
		/// Expands list to include any inherited templates
		/// </summary>
		/// <param name="template"></param>
		/// <param name="list"></param>
		internal static void ExpandTemplateInheritance(DocTemplateDefinition template, List<DocTemplateDefinition> list)
		{
			if (template.Templates != null)
			{
				foreach (DocTemplateDefinition sub in template.Templates)
				{
					ExpandTemplateInheritance(sub, list);

					if (list.Contains(sub) && !list.Contains(template))
					{
						list.Add(template);
					}
				}
			}
		}

		internal static void ImportCnfAttribute(IfcDoc.Schema.CNF.exp_attribute exp_attribute, bool keep, Schema.CNF.boolean_or_unspecified tagless, DocEntity docEntity, DocAttribute docAttribute, DocModelView docView)
		{
			DocXsdFormatEnum xsdformat = DocXsdFormatEnum.Default;
			if (exp_attribute == Schema.CNF.exp_attribute.attribute_content)
			{
				xsdformat = DocXsdFormatEnum.Content;
			}
			else if (exp_attribute == Schema.CNF.exp_attribute.attribute_tag)
			{
				xsdformat = DocXsdFormatEnum.Attribute;
			}
			else if (exp_attribute == Schema.CNF.exp_attribute.double_tag)
			{
				xsdformat = DocXsdFormatEnum.Element;
			}
			else if (!keep)
			{
				xsdformat = DocXsdFormatEnum.Hidden;
			}
			else
			{
				xsdformat = DocXsdFormatEnum.Element;
			}

			bool? booltagless = null;
			switch (tagless)
			{
				case Schema.CNF.boolean_or_unspecified.boolean_true:
					booltagless = true;
					break;

				case Schema.CNF.boolean_or_unspecified.boolean_false:
					booltagless = false;
					break;
			}

			if (docView != null)
			{
				// configure specific model view
				DocXsdFormat docFormat = new DocXsdFormat();
				docFormat.Entity = docEntity.Name;
				docFormat.Attribute = docAttribute.Name;
				docFormat.XsdFormat = xsdformat;
				docFormat.XsdTagless = booltagless;
				docView.XsdFormats.Add(docFormat);
			}
			else
			{
				// configure default
				docAttribute.XsdFormat = xsdformat;
				docAttribute.XsdTagless = booltagless;
			}

		}

		internal static void ImportCnf(IfcDoc.Schema.CNF.configuration cnf, DocProject docProject, DocModelView docView)
		{
			Dictionary<string, DocEntity> mapEntity = new Dictionary<string, DocEntity>();
			foreach (DocSection docSection in docProject.Sections)
			{
				foreach (DocSchema docSchema in docSection.Schemas)
				{
					foreach (DocEntity docEntity in docSchema.Entities)
					{
						mapEntity.Add(docEntity.Name, docEntity);
					}
				}
			}

			foreach (IfcDoc.Schema.CNF.entity ent in cnf.entity)
			{
				DocEntity docEntity = null;
				if (mapEntity.TryGetValue(ent.select, out docEntity))
				{
					if (ent.attribute != null)
					{
						foreach (IfcDoc.Schema.CNF.attribute atr in ent.attribute)
						{
							// find attribute on entity
							foreach (DocAttribute docAttribute in docEntity.Attributes)
							{
								if (atr.select != null && atr.select.Equals(docAttribute.Name))
								{
									ImportCnfAttribute(atr.exp_attribute, atr.keep, atr.tagless, docEntity, docAttribute, docView);
								}
							}
						}
					}

					if (ent.inverse != null)
					{
						foreach (IfcDoc.Schema.CNF.inverse inv in ent.inverse)
						{
							// find attribute on entity
							foreach (DocAttribute docAttribute in docEntity.Attributes)
							{
								if (inv.select != null && inv.select.Equals(docAttribute.Name))
								{
									ImportCnfAttribute(inv.exp_attribute, true, Schema.CNF.boolean_or_unspecified.unspecified, docEntity, docAttribute, docView);
								}
							}
						}
					}

				}
			}
		}

		internal static string ImportXsdType(string xsdtype)
		{
			if (xsdtype == null)
				return null;

			switch (xsdtype)
			{
				case "xs:string":
				case "xs:anyURI":
					return "STRING";

				case "xs:decimal":
				case "xs:float":
				case "xs:double":
					return "REAL";

				case "xs:integer":
				case "xs:byte": // signed 8-bit
				case "xs:short": // signed 16-bit
				case "xs:int": // signed 32-bit
				case "xs:long": // signed 64-bit
					return "INTEGER";

				case "xs:boolean":
					return "BOOLEAN";

				case "xs:base64Binary":
				case "xs:hexBinary":
					return "BINARY";

				default:
					return xsdtype;
			}
		}

		internal static string ImportXsdAnnotation(IfcDoc.Schema.XSD.annotation annotation)
		{
			if (annotation == null)
				return null;

			StringBuilder sb = new StringBuilder();
			foreach (string s in annotation.documentation)
			{
				sb.AppendLine("<p>");
				sb.AppendLine(s);
				sb.AppendLine("</p>");
			}
			return sb.ToString();
		}

		internal static void ImportXsdElement(IfcDoc.Schema.XSD.element sub, DocEntity docEntity, bool list)
		{
			DocAttribute docAttr = new DocAttribute();
			docEntity.Attributes.Add(docAttr);
			if (!String.IsNullOrEmpty(sub.name))
			{
				docAttr.Name = sub.name;
			}
			else
			{
				docAttr.Name = sub.reftype;
			}

			if (!String.IsNullOrEmpty(sub.type))
			{
				docAttr.DefinedType = sub.type;
			}
			else
			{
				docAttr.DefinedType = sub.reftype;
			}
			// list or set??...

			if (list || sub.minOccurs != null)
			{
				if (list || sub.maxOccurs != null)
				{
					// list
					if (list || sub.maxOccurs == "unbounded")
					{
						docAttr.AggregationType = 1; // list
						if (!String.IsNullOrEmpty(sub.minOccurs))
						{
							docAttr.AggregationLower = sub.minOccurs;
						}
						else
						{
							docAttr.AggregationLower = "0";
						}
						docAttr.AggregationUpper = "?";
					}
				}
				else if (sub.minOccurs == "0")
				{
					docAttr.IsOptional = true;
				}
			}

			docAttr.Documentation = ImportXsdAnnotation(sub.annotation);
		}

		internal static void ImportXsdSimple(IfcDoc.Schema.XSD.simpleType simple, DocSchema docSchema, string name)
		{
			string thename = simple.name;
			if (simple.name == null)
			{
				thename = name;
			}

			if (simple.restriction != null && simple.restriction.enumeration.Count > 0)
			{
				DocEnumeration docEnum = new DocEnumeration();
				docSchema.Types.Add(docEnum);
				docEnum.Name = thename;
				docEnum.Documentation = ImportXsdAnnotation(simple.annotation);
				foreach (IfcDoc.Schema.XSD.enumeration en in simple.restriction.enumeration)
				{
					DocConstant docConst = new DocConstant();
					docConst.Name = en.value;
					docConst.Documentation = ImportXsdAnnotation(en.annotation);

					docEnum.Constants.Add(docConst);
				}
			}
			else
			{
				DocDefined docDef = new DocDefined();
				docDef.Name = thename;
				docDef.Documentation = ImportXsdAnnotation(simple.annotation);
				if (simple.restriction != null)
				{
					docDef.DefinedType = ImportXsdType(simple.restriction.basetype);
				}
				docSchema.Types.Add(docDef);
			}
		}

		internal static void ImportXsdAttribute(IfcDoc.Schema.XSD.attribute att, DocSchema docSchema, DocEntity docEntity)
		{
			DocAttribute docAttr = new DocAttribute();
			docEntity.Attributes.Add(docAttr);
			docAttr.Name = att.name;
			docAttr.IsOptional = (att.use == Schema.XSD.use.optional);

			if (att.simpleType != null)
			{
				string refname = docEntity.Name + "_" + att.name;
				docAttr.DefinedType = refname;
				ImportXsdSimple(att.simpleType, docSchema, refname);
			}
			else
			{
				docAttr.DefinedType = ImportXsdType(att.type);
			}
		}

		internal static void ImportXsdComplex(IfcDoc.Schema.XSD.complexType complex, DocSchema docSchema, DocEntity docEntity)
		{
			if (complex == null)
				return;

			foreach (IfcDoc.Schema.XSD.attribute att in complex.attribute)
			{
				ImportXsdAttribute(att, docSchema, docEntity);
			}

			if (complex.choice != null)
			{
				foreach (IfcDoc.Schema.XSD.element sub in complex.choice.element)
				{
					ImportXsdElement(sub, docEntity, true);
				}
			}

			if (complex.sequence != null)
			{
				foreach (IfcDoc.Schema.XSD.element sub in complex.sequence.element)
				{
					ImportXsdElement(sub, docEntity, true);
				}
			}

			if (complex.all != null)
			{
				foreach (IfcDoc.Schema.XSD.element sub in complex.all.element)
				{
					ImportXsdElement(sub, docEntity, true);
				}
			}

			if (complex.complexContent != null)
			{
				if (complex.complexContent.extension != null)
				{
					docEntity.BaseDefinition = complex.complexContent.extension.basetype;

					foreach (IfcDoc.Schema.XSD.attribute att in complex.complexContent.extension.attribute)
					{
						ImportXsdAttribute(att, docSchema, docEntity);
					}

					if (complex.complexContent.extension.choice != null)
					{
						foreach (IfcDoc.Schema.XSD.element sub in complex.complexContent.extension.choice.element)
						{
							ImportXsdElement(sub, docEntity, true);
						}
					}
				}
			}
		}

		internal static DocSchema ImportXsd(IfcDoc.Schema.XSD.schema schema, DocProject docProject)
		{
			// use resource-level section
			DocSection docSection = docProject.Sections[6]; // source schemas

			DocSchema docSchema = new DocSchema();
			docSchema.Name = schema.id;//??
			docSchema.Code = schema.id;
			docSchema.Version = schema.version;
			docSection.Schemas.Add(docSchema);

			foreach (IfcDoc.Schema.XSD.simpleType simple in schema.simpleType)
			{
				ImportXsdSimple(simple, docSchema, null);
			}

			foreach (IfcDoc.Schema.XSD.complexType complex in schema.complexType)
			{
				DocEntity docEntity = new DocEntity();
				docSchema.Entities.Add(docEntity);
				docEntity.Name = complex.name;
				docEntity.Documentation = ImportXsdAnnotation(complex.annotation);

				ImportXsdComplex(complex, docSchema, docEntity);
			}

			foreach (IfcDoc.Schema.XSD.element element in schema.element)
			{
				DocEntity docEntity = new DocEntity();
				docSchema.Entities.Add(docEntity);
				docEntity.Name = element.name;
				docEntity.Documentation = ImportXsdAnnotation(element.annotation);

				ImportXsdComplex(element.complexType, docSchema, docEntity);
			}

			return docSchema;
		}

	}

}
