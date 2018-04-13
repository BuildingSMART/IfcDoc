// This file was automatically generated from IFCDOC at www.buildingsmart-tech.org.
// IFC content is copyright (C) 1996-2018 BuildingSMART International Ltd.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Xml.Serialization;

using BuildingSmart.IFC.IfcMeasureResource;
using BuildingSmart.IFC.IfcUtilityResource;

namespace BuildingSmart.IFC.IfcKernel
{
	[Guid("244ca500-edbb-46f5-9ab8-9560710b61a4")]
	public abstract partial class IfcPropertyDefinition : IfcRoot,
		BuildingSmart.IFC.IfcKernel.IfcDefinitionSelect
	{
		[InverseProperty("RelatedDefinitions")] 
		[MaxLength(1)]
		ISet<IfcRelDeclares> _HasContext = new HashSet<IfcRelDeclares>();
	
		[InverseProperty("RelatedObjects")] 
		ISet<IfcRelAssociates> _HasAssociations = new HashSet<IfcRelAssociates>();
	
	
		public IfcPropertyDefinition()
		{
		}
	
		public IfcPropertyDefinition(IfcGloballyUniqueId __GlobalId, IfcOwnerHistory __OwnerHistory, IfcLabel? __Name, IfcText? __Description)
			: base(__GlobalId, __OwnerHistory, __Name, __Description)
		{
		}
	
		public ISet<IfcRelDeclares> HasContext { get { return this._HasContext; } }
	
		[Description("Reference to the relationship IfcRelAssociates and thus to those externally defin" +
	    "ed concepts, like classifications, documents, or library information, which are " +
	    "associated to the property definition.")]
		public ISet<IfcRelAssociates> HasAssociations { get { return this._HasAssociations; } }
	
	
	}
	
}