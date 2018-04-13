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

using BuildingSmart.IFC.IfcGeometricConstraintResource;
using BuildingSmart.IFC.IfcKernel;
using BuildingSmart.IFC.IfcMeasureResource;
using BuildingSmart.IFC.IfcRepresentationResource;
using BuildingSmart.IFC.IfcSharedBldgElements;
using BuildingSmart.IFC.IfcStructuralAnalysisDomain;
using BuildingSmart.IFC.IfcUtilityResource;

namespace BuildingSmart.IFC.IfcProductExtension
{
	[Guid("4393710f-f1af-4927-8800-2e6b98edef41")]
	public partial class IfcGeographicElement : IfcElement
	{
		[DataMember(Order=0)] 
		[XmlAttribute]
		IfcGeographicElementTypeEnum? _PredefinedType;
	
	
		public IfcGeographicElement()
		{
		}
	
		public IfcGeographicElement(IfcGloballyUniqueId __GlobalId, IfcOwnerHistory __OwnerHistory, IfcLabel? __Name, IfcText? __Description, IfcLabel? __ObjectType, IfcObjectPlacement __ObjectPlacement, IfcProductRepresentation __Representation, IfcIdentifier? __Tag, IfcGeographicElementTypeEnum? __PredefinedType)
			: base(__GlobalId, __OwnerHistory, __Name, __Description, __ObjectType, __ObjectPlacement, __Representation, __Tag)
		{
			this._PredefinedType = __PredefinedType;
		}
	
		[Description("Predefined generic types for a geographic element that are specified in an enumer" +
	    "ation. There might be property sets defined specifically for each predefined typ" +
	    "e.")]
		public IfcGeographicElementTypeEnum? PredefinedType { get { return this._PredefinedType; } set { this._PredefinedType = value;} }
	
	
	}
	
}