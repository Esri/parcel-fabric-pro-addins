/*
   Copyright 2025 Esri

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       https://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

   See the License for the specific language governing permissions and
   limitations under the License.

*/
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ArcGIS.Core.Data;
using ArcGIS.Core.Data.Exceptions;
using ArcGIS.Core.Data.PluginDatastore;
using ArcGIS.Core.Geometry;
using Microsoft.VisualBasic.FileIO;
using LandXMLPluginDataSource.Helpers;
using System.Collections;
using ArcGIS.Core.SystemCore;
using ArcGIS.Core.Data.DDL;
using System.Runtime.CompilerServices;
using System.Xml;
using System.Windows.Controls;
using ArcGIS.Core.Data.Analyst3D;
using ArcGIS.Core.Internal.CIM;
using System.Diagnostics.Metrics;
using Tesseract;
using static System.Runtime.InteropServices.Marshalling.IIUnknownCacheStrategy;
using static ArcGIS.Core.Data.NetworkDiagrams.AngleDirectedDiagramLayoutParameters;
using System.IO;
using System.Runtime.InteropServices;
using RBush;
using ArcGIS.Core.Internal.Geometry;

namespace LandXMLPluginDataSource
{
  /// <summary>
  /// (Custom) interface the sample uses to extract row information from the
  /// plugin table
  /// </summary>
  internal interface IPluginRowProvider
  {
    PluginRow FindRow(long oid, IEnumerable<string> columnFilter, ArcGIS.Core.Geometry.SpatialReference sr);
  }

  /// <summary>
  /// Implements a plugin table.
  /// </summary>
  /// <remarks>The plugin table appears as an ArcGIS.Core.Data.Table or FeatureClass to
  /// .NET clients (add-ins) and as an ITable or IFeatureClass to native clients (i.e. Pro)
  /// </remarks>
  public class ProPluginTableTemplate : PluginTableTemplate, IDisposable, IPluginRowProvider
  {
    private string _path;
    private string _fileName;
    private string _tableName;

    private DataTable _table;
    private RBush.RBush<RBushGeometry> _rtree;
    private RBush.RBush<RBushPolyline> _rtreeLines;
    private RBush.RBush<RBushPolygon> _rtreePolygons;
    private RBush.RBush<RBushPolygon> _rtreePlans;

    private RBush.Envelope _extent;
    private ArcGIS.Core.Geometry.Envelope _gisExtent;
    private ArcGIS.Core.Geometry.SpatialReference _sr;
    private bool _hasZ = false;

    internal ProPluginTableTemplate(string path, string filename, string tablename, ArcGIS.Core.Geometry.SpatialReference sr = null)
    {
      _path = path;
      _fileName = filename;
      _tableName = tablename;
      _rtree = new RBush.RBush<RBushGeometry>();
      _rtreeLines = new RBush.RBush<RBushPolyline>();
      _rtreePolygons = new RBush.RBush<RBushPolygon>();
      _rtreePlans = new RBush.RBush<RBushPolygon>();
      _sr = sr ?? SpatialReferences.WGS84;
      Open();
    }



    /// <summary>
    /// Get the name of the table
    /// </summary>
    /// <returns>Table name</returns>
    public override string GetName() => _tableName;


    /// <summary>
    /// Gets whether native row count is supported
    /// </summary>
    /// <remarks>Return true if your table can get the row count without having
    /// to enumerate through all the rows (and count them)....which will be
    /// the default behavior if you return false</remarks>
    /// <returns>True or false</returns>
    public override bool IsNativeRowCountSupported() => true;

    /// <summary>
    /// Gets the native row count (if IsNativeRowCountSupported is true)
    /// </summary>
    /// <returns>The row count</returns>
    //public override long GetNativeRowCount() => _rtree?.Count ?? _table.Rows.Count;

    public override long GetNativeRowCount()
    {    
      if (_tableName.EndsWith("_POINTS"))
        return _rtree?.Count ?? _table.Rows.Count;
      
      if (_tableName.EndsWith("_LINES"))
        return _rtreeLines?.Count ?? _table.Rows.Count;

      if (_tableName.EndsWith("_PARCELS"))
        return _rtreePolygons?.Count ?? _table.Rows.Count;

      if (_tableName.EndsWith("_PLANS"))
        return _rtreePlans?.Count ?? _table.Rows.Count;

      return 0;
    }

    /// <summary>
    /// Search the underlying plugin table using the input QueryFilter
    /// </summary>
    /// <param name="queryFilter"></param>
    /// <remarks>If the PluginDatasourceTemplate.IsQueryLanguageSupported returns
    /// false, the WhereClause will always be empty.<br/>
    /// The QueryFilter is never null (even if the client passed in null to the "outside"
    /// table or feature class).<br/>
    /// A FID set in the ObjectIDs collection of the query filter, if present, acts as
    /// the "super" set - or constraint - from which all selections should be made. 
    /// In other words, if the FID set contains ids {1,5,6,10} then a WhereClause
    /// on the query filter can only select from {1,5,6,10} and not from any other
    /// records.</remarks>
    /// <returns><see cref="PluginCursorTemplate"/></returns>
    public override PluginCursorTemplate Search(ArcGIS.Core.Data.QueryFilter queryFilter) =>
                                                  this.SearchInternal(queryFilter);

    /// <summary>
    /// Search the underlying plugin table using the input SpatialQueryFilter
    /// </summary>
    /// <remarks>A SpatialQueryFilter cann only be used by clients if the plugin
    /// table returns a GeometryType other than Unknown from GetShapeType().</remarks>
    /// <param name="spatialQueryFilter"></param>
    /// <returns><see cref="PluginCursorTemplate"/></returns>
    public override PluginCursorTemplate Search(SpatialQueryFilter spatialQueryFilter) =>
                                                  this.SearchInternal(spatialQueryFilter);
    /// <summary>
    /// Gets the supported GeometryType if there is one, otherwise Unknown
    /// </summary>
    /// <remarks>Plugins returning a geometry type get a FeatureClass (which is also a Table) wrapper 
    /// and can be used as data sources for layers. Plugins returning a geometry type of Unknown
    /// get a Table wrapper and can be used as data sources for StandAloneTables only.</remarks>
    /// <returns></returns>
    public override GeometryType GetShapeType()
    {
      //Note: empty tables treated as non-geometry
       if (_tableName.EndsWith("_POINTS"))
           return _table.Columns.Contains("SHAPE") ? GeometryType.Point : GeometryType.Unknown;
       else if(_tableName.EndsWith("_LINES"))
           return _table.Columns.Contains("SHAPE") ? GeometryType.Polyline: GeometryType.Unknown;
       else if (_tableName.EndsWith("_PARCELS"))
           return _table.Columns.Contains("SHAPE") ? GeometryType.Polygon : GeometryType.Unknown;
      else if (_tableName.EndsWith("_PLANS"))
        return _table.Columns.Contains("SHAPE") ? GeometryType.Polygon : GeometryType.Unknown;
      else return GeometryType.Unknown;
    }

    /// <summary>
    /// Get the extent for the dataset (if it has one)
    /// </summary>
    /// <remarks>Ideally, your plugin table should return an extent even if it is
    /// empty</remarks>
    /// <returns><see cref="Envelope"/></returns>
    public override ArcGIS.Core.Geometry.Envelope GetExtent()
    {
      if (this.GetShapeType() != GeometryType.Unknown)
      {
        if (_gisExtent == null)
        {
          _gisExtent = _extent.ToEsriEnvelope(_sr, _hasZ);
        }
      }
      return _gisExtent;
    }

    /// <summary>
    /// Get the collection of fields accessible on the plugin table
    /// </summary>
    /// <remarks>The order of returned columns in any rows must match the
    /// order of the fields specified from GetFields()</remarks>
    /// <returns><see cref="IReadOnlyList{PluginField}"/></returns>
    public override IReadOnlyList<PluginField> GetFields()
    {
      var pluginFields = new List<PluginField>();

      if (_tableName.EndsWith("_POINTS"))
      {
        foreach (var col in _table.Columns.Cast<DataColumn>())
        {
          var fieldType = ArcGIS.Core.Data.FieldType.String;
          //special handling for OBJECTID and SHAPE
          if (col.ColumnName == "OBJECTID")
          {
            fieldType = ArcGIS.Core.Data.FieldType.OID;
          }
          else if (col.ColumnName == "SHAPE")
          {
            fieldType = ArcGIS.Core.Data.FieldType.Geometry;
          }
          else if (col.ColumnName.ToLower().Trim() == "x" || col.ColumnName.ToLower().Trim() == "y")
          {
            // columns: X or Y
            fieldType = ArcGIS.Core.Data.FieldType.Double;
          }
          else if (col.ColumnName.ToUpper() == "OID")
          {
            // Long datatype
            fieldType = ArcGIS.Core.Data.FieldType.Integer;
          }
          else if (col.ColumnName.ToUpper().EndsWith("DATE"))
          {
            // DateTime datatype
            fieldType = ArcGIS.Core.Data.FieldType.Date;
          }


          pluginFields.Add(new PluginField()
          {
            Name = col.ColumnName,
            AliasName = col.Caption,
            FieldType = fieldType
          });
        }
      }
      else if (_tableName.EndsWith("_LINES"))
      {
        foreach (var col in _table.Columns.Cast<DataColumn>())
        {
          var fieldType = ArcGIS.Core.Data.FieldType.String;
          //special handling for OBJECTID and SHAPE
          if (col.ColumnName == "OBJECTID")
          {
            fieldType = ArcGIS.Core.Data.FieldType.OID;
          }
          else if (col.ColumnName == "SHAPE")
          {
            fieldType = ArcGIS.Core.Data.FieldType.Geometry;
          }
          else if (col.ColumnName.ToLower().Trim() == "direction" || col.ColumnName.ToLower().Trim() == "distance"
             || col.ColumnName.ToLower().Trim() == "radius" || col.ColumnName.ToLower().Trim() == "arclength")
          {
            // columns: DIRECTION, DISTANCE, RADIUS or ARCLENGTH
            fieldType = ArcGIS.Core.Data.FieldType.Double;
          }
          else if (col.ColumnName.ToUpper().EndsWith("DATE"))
          {
            // DateTime datatype
            fieldType = ArcGIS.Core.Data.FieldType.Date;
          }
          pluginFields.Add(new PluginField()
          {
            Name = col.ColumnName,
            AliasName = col.Caption,
            FieldType = fieldType
          });
        }

      }


      else if (_tableName.EndsWith("_PARCELS"))
      {
        foreach (var col in _table.Columns.Cast<DataColumn>())
        {
          var fieldType = ArcGIS.Core.Data.FieldType.String;
          //special handling for OBJECTID and SHAPE
          if (col.ColumnName == "OBJECTID")
          {
            fieldType = ArcGIS.Core.Data.FieldType.OID;
          }
          else if (col.ColumnName == "SHAPE")
          {
            fieldType = ArcGIS.Core.Data.FieldType.Geometry;
          }
          else if (col.ColumnName.ToLower().Trim() == "shape_area")
          {
            // columns: DIRECTION, DISTANCE, RADIUS or ARCLENGTH
            fieldType = ArcGIS.Core.Data.FieldType.Double;
          }
          else if (col.ColumnName.ToUpper().EndsWith("DATE"))
          {
            // DateTime datatype
            fieldType = ArcGIS.Core.Data.FieldType.Date;
          }
          pluginFields.Add(new PluginField()
          {
            Name = col.ColumnName,
            AliasName = col.Caption,
            FieldType = fieldType
          });
        }

      }

      else if (_tableName.EndsWith("_PLANS"))
      {
        foreach (var col in _table.Columns.Cast<DataColumn>())
        {
          var fieldType = ArcGIS.Core.Data.FieldType.String;
          //special handling for OBJECTID and SHAPE
          if (col.ColumnName == "OBJECTID")
          {
            fieldType = ArcGIS.Core.Data.FieldType.OID;
          }
          else if (col.ColumnName == "SHAPE")
          {
            fieldType = ArcGIS.Core.Data.FieldType.Geometry;
          }
          else if (col.ColumnName.ToLower().Trim() == "distancefactor" || col.ColumnName.ToLower().Trim() == "shape_area")
          {
            // columns: DIRECTION, DISTANCE, RADIUS or ARCLENGTH
            fieldType = ArcGIS.Core.Data.FieldType.Double;
          }
          else if (col.ColumnName.ToUpper().EndsWith("DATE"))
          {
            // DateTime datatype
            fieldType = ArcGIS.Core.Data.FieldType.Date;
          }
          pluginFields.Add(new PluginField()
          {
            Name = col.ColumnName,
            AliasName = col.Caption,
            FieldType = fieldType
          });
        }

      }

      return pluginFields;
    }


    #region IPluginRowProvider

    /// <summary>
    /// Custom interface specific to the way the sample is implemented.
    /// </summary>
    public PluginRow FindRow(long oid, IEnumerable<string> columnFilter, ArcGIS.Core.Geometry.SpatialReference srout)
    {
      ArcGIS.Core.Geometry.Geometry shape = null;

      List<object> values = new List<object>();
      var row = _table.Rows.Find(oid);
      //The order of the columns in the returned rows ~must~ match
      //GetFields. If a column is filtered out, an empty placeholder must
      //still be provided even though the actual value is skipped
      var columnNames = this.GetFields().Select(col => col.Name.ToUpper()).ToList();

      foreach (var colName in columnNames)
      {
        if (columnFilter.Contains(colName))
        {
          //special handling for shape
          if (colName == "SHAPE")
          {
            var shpBuffer = row["SHAPE"] as Byte[];
            // Get the geometry from the shape buffer
            var geometry = 
              GeometryEngine.Instance.ImportFromEsriShape(EsriShapeImportFlags.EsriShapeImportDefaults,
                  shpBuffer,_sr);
            if (geometry != null)
            {
              // Get the geometry type
              if (geometry.GeometryType == GeometryType.Point)
                shape = MapPointBuilderEx.FromEsriShape(shpBuffer, _sr);
              else if (geometry.GeometryType == GeometryType.Polyline)
                shape = PolylineBuilderEx.FromEsriShape(shpBuffer, _sr);
              else if (geometry.GeometryType == GeometryType.Polygon)
                shape = PolygonBuilderEx.FromEsriShape(shpBuffer, _sr);
            }
            if (srout != null)
            {
              if (!srout.Equals(_sr))
                shape = GeometryEngine.Instance.Project(shape, srout);
            }
            values.Add(shape);
          }
          else
          {
            values.Add(row[colName]);
          }
        }
        else
        {
          values.Add(System.DBNull.Value);//place holder
        }
      }
      return new PluginRow() { Values = values };
    }

    #endregion IPluginRowProvider

    #region Private

    /// <summary>
    /// Implementation of reading a csv which is specific to the way this sample
    /// is implemented. Your mileage may vary. Change to suit your purposes as
    /// needed.
    /// </summary>
    private void Open()
    {
      var lstDoubleFields = new List<int>();
      var lstLongFields = new List<int>();
      var lstDateFields = new List<int>();

      var sourceLandXMLFile = System.IO.Path.Combine(_path, _fileName);
      //Read the LandXML, build measurement network
      
      XmlDocument xmlDoc = new XmlDocument(); // Create an XML document object
      xmlDoc.Load(sourceLandXMLFile); // Load the XML document from the specified file

      if (!MeasurementNetworkFromLandXML(xmlDoc, out Hashtable measurementNetwork, 
        out Hashtable coordinateList, out Hashtable measurementSequences, 
        out Hashtable measurementSequenceInfo, out Hashtable pointAttributeLookup,
        out Hashtable measurementAttributeLookup, out Hashtable parcelAttributeLookup, 
        out Hashtable surveyPlanAttributeLookup, 
        out Dictionary<string, List<string[]>> ParcelCircArcRadiusTracker, out int totalLinesRead))
        return;

      //get all attribute field names from the points ----
      var dictPointAttribute =
        HashtableToDictionary<string, Dictionary<string, string>>(pointAttributeLookup);

      List<string> pointExtendedFldNames = new();
      foreach (var pointAttribute in dictPointAttribute.Values)
      {
        foreach (var fldName in pointAttribute.Keys)
        {
          if (!pointExtendedFldNames.Contains(fldName))
            pointExtendedFldNames.Add(fldName);
        }
      }

      //get all attribute field names from the measurements ----
      var dictMeasurementAttribute = 
        HashtableToDictionary<string, Dictionary<string, string>>(measurementAttributeLookup);

      List<string> measurementExtendedFldNames = new();
      foreach (var measurementAttribute in dictMeasurementAttribute.Values)
      {
        foreach (var fldName in measurementAttribute.Keys)
        {
          if (!measurementExtendedFldNames.Contains(fldName))
            measurementExtendedFldNames.Add(fldName);
        }
      }

      //get all attribute field names from the parcels ----
      var dictParcelAttribute =
        HashtableToDictionary<string, Dictionary<string, string>>(parcelAttributeLookup);

      List<string> parcelExtendedFldNames = new();
      foreach (var parcelAttribute in dictParcelAttribute.Values)
      {
        foreach (var fldName in parcelAttribute.Keys)
        {
          if (!parcelExtendedFldNames.Contains(fldName.ToUpper()))
            parcelExtendedFldNames.Add(fldName.ToUpper());
        }
      }


      //get all attribute field names from the survey plan ----
      var dictSurveyPlanAttribute =
        HashtableToDictionary<string, Dictionary<string, string>>(surveyPlanAttributeLookup);

      List<string> surveyPlanExtendedFldNames = new();
      foreach (var surveyPlanAttribute in dictSurveyPlanAttribute.Values)
      {
        foreach (var fldName in surveyPlanAttribute.Keys)
        {
          if (!surveyPlanExtendedFldNames.Contains(fldName.ToUpper()))
            surveyPlanExtendedFldNames.Add(fldName.ToUpper());
        }
      }

      //Initialize our data table
      _table = new DataTable();
      //dataTable.PrimaryKey = new DataColumn("OBJECTID", typeof(long));

      var oid = new DataColumn("OBJECTID", typeof(long))
      {
        AutoIncrement = true,
        AutoIncrementSeed = 1
      };
      _table.Columns.Add(oid);
      _table.PrimaryKey = new DataColumn[] { oid };

      if (_tableName.EndsWith("_POINTS"))
      {
        //column headings
        var fieldIdx = 0;
        List<string> fieldNames = new(["X", "Y", "DESC", "STATE", "NAME", "PNTSURV", "OID"]);

        fieldNames.AddRange(pointExtendedFldNames);
        fieldNames = fieldNames.Distinct().ToList();

        foreach (var field in fieldNames)
        {
          var field_name = field.Replace(' ', '_').ToUpper();
          if (field_name.ToLower().Trim() == "x" || field_name.ToLower().Trim() == "y")
          {
            // field name is X or Y
            _table.Columns.Add(new DataColumn(field_name, typeof(double)));
            lstDoubleFields.Add(fieldIdx);
          }
          else if (field_name.ToUpper()=="OID")
          {
            _table.Columns.Add(new DataColumn(field_name, typeof(long)));
            lstLongFields.Add(fieldIdx);
          }
          else if (field_name.EndsWith("DATE"))
          {
            _table.Columns.Add(new DataColumn(field_name, typeof(DateTime)));
            lstDateFields.Add(fieldIdx);
          }
          else _table.Columns.Add(new DataColumn(field_name, typeof(string)));
          fieldIdx++;
        }

        //For spatial data...
        //Domain to verify coordinates (2D)
        var sr_extent = new RBush.Envelope(
          MinX: _sr.Domain.XMin,
          MinY: _sr.Domain.YMin,
          MaxX: _sr.Domain.XMax,
          MaxY: _sr.Domain.YMax
        );

        //default to the Spatial Reference domain
        _extent = sr_extent;

        //add a shape column
        _table.Columns.Add(new DataColumn("SHAPE", typeof(System.Byte[])));

        foreach (Tuple<double, double, double, Dictionary<string, string>> ptNode in coordinateList.Values)
        {
          //add fields on-the-fly as string type, based on row's dictionary
          Dictionary<string, string> Fields = ptNode.Item4;
          foreach (string fldName in Fields.Keys)
          {
            if (!_table.Columns.Contains(fldName.ToUpper()))
              _table.Columns.Add(new DataColumn(fldName.ToUpper(), typeof(System.String)));
          }

          //load the datatable
          var row = _table.NewRow();
          //Tuple<double, double, double, Dictionary<string, string>> thisPointNode = ptCoord;
          //new(X, Y, Z, pointAttributes); //x,y,z,attributes, Northing(Y), Easting(X), 
          //CoordinateList.Add(pointName, thisPointNode);

          double x = ptNode.Item1; // ptCoord;
          double y = ptNode.Item2; // ptCoord;
          double z = 0.0; // ptCoord;

          //ensure the coordinate is within bounds
          var coord = new Coordinate3D(x, y, z);

          if (!sr_extent.ContainsCoordinate2D(coord))
            throw new GeodatabaseFeatureException(
              "The feature falls outside the defined spatial reference");

          //store it
          row["SHAPE"] = coord.ToMapPoint().ToEsriShape();
          row["X"] = x;
          row["Y"] = y;

          var dictAttr = ptNode.Item4;

          if (dictAttr.TryGetValue("STATE", out string value))
            row["STATE"] = value;

          if (dictAttr.TryGetValue("NAME", out value))
            row["NAME"] = value;

          if (dictAttr.TryGetValue("PNTSURV", out value))
            row["PNTSURV"] = value;

          if (dictAttr.TryGetValue("DESC", out value))
            row["DESC"] = value;

          if (ptNode.Item4.TryGetValue("OID", out string val))
          {
            if (long.TryParse(val, out long lOid))
              row["OID"] = lOid;
          }

          //add the extended attribute values
          if (pointAttributeLookup.ContainsKey(ptNode.Item4["NAME"]))
          {
            var dict = pointAttributeLookup[ptNode.Item4["NAME"]] as Dictionary<string, string>;
            foreach (var item in dict)
            {
              //only write new value is there it's currently a NULL
              if(row[item.Key]==DBNull.Value)
                row[item.Key] = item.Value;
            }
          }

          //add it to the index
          var rbushCoord = new RBushGeometry(coord, (long)row["OBJECTID"]);
          _rtree.Insert(rbushCoord);

          //update max and min for use in the extent
          if (_rtree.Count == 1)
          {
            //first record
            _extent = rbushCoord.Envelope;
          }
          else
          {
            _extent = rbushCoord.Envelope.Union2D(_extent);
          }

          _table.Rows.Add(row);

        }
      }

      else if (_tableName.EndsWith("_LINES"))
      {
        //column headings
        var fieldIdx = 0;
        List<string> fieldNames = new(["DIRECTION", "DISTANCE", "RADIUS", "ARCLENGTH", "PLANNAME", "SHAPE_LENGTH"]);

        measurementExtendedFldNames = measurementExtendedFldNames.ConvertAll(s => s.ToUpper());
        fieldNames.AddRange(measurementExtendedFldNames);
        fieldNames = fieldNames.Distinct().ToList();

        foreach (var field in fieldNames)
        {
          var field_name = field.Replace(' ', '_').ToUpper();
          if (field_name.ToLower().Trim() == "direction" || field_name.ToLower().Trim() == "distance"
            || field_name.ToLower().Trim() == "radius" || field_name.ToLower().Trim() == "arclength"
            || field_name.ToLower().Trim() == "shape_length")
          {
            // field name is direction, distance or radius
            _table.Columns.Add(new DataColumn(field_name, typeof(double)));
            lstDoubleFields.Add(fieldIdx);
          }
          else if (field_name.ToUpper().EndsWith("DATE"))
          {
            _table.Columns.Add(new DataColumn(field_name, typeof(DateTime)));
            lstDateFields.Add(fieldIdx);
          }
          else _table.Columns.Add(new DataColumn(field_name, typeof(string)));
          fieldIdx++;
        }

        //For spatial data...
        //Domain to verify coordinates (2D)
        var sr_extent = new RBush.Envelope(
          MinX: _sr.Domain.XMin,
          MinY: _sr.Domain.YMin,
          MaxX: _sr.Domain.XMax,
          MaxY: _sr.Domain.YMax
        );

        //default to the Spatial Reference domain
        _extent = sr_extent;

        //add a shape column
        _table.Columns.Add(new DataColumn("SHAPE", typeof(System.Byte[])));

        var dictMeasNet = HashtableToDictionary<string, Tuple<double, double, double, double, double, string>>(measurementNetwork);
        foreach (var kvp in dictMeasNet)
        {
          var fromTo = kvp.Key.Split(',');
          var measVals = kvp.Value;
          if (coordinateList.ContainsKey(fromTo[0]))
          {
            if (coordinateList.ContainsKey(fromTo[1]))
            {
              var ptNode = coordinateList[fromTo[0]] as Tuple<double, double, double, Dictionary<string, string>>;
              double xFrom = ptNode.Item1; // ptCoord;
              double yFrom = ptNode.Item2; // ptCoord;

              ptNode = coordinateList[fromTo[1]] as Tuple<double, double, double, Dictionary<string, string>>;
              double xTo = ptNode.Item1; // ptCoord;
              double yTo = ptNode.Item2; // ptCoord;

              if (!sr_extent.ContainsCoordinate2D(new Coordinate3D(xFrom, yFrom,0)))
                throw new GeodatabaseFeatureException(
                  "The feature falls outside the defined spatial reference");

              if (!sr_extent.ContainsCoordinate2D(new Coordinate3D(xTo, yTo, 0)))
                throw new GeodatabaseFeatureException(
                  "The feature falls outside the defined spatial reference");

              double direction = measVals.Item1;
              double distance = measVals.Item2;
              double radius = measVals.Item3;
              double arclength = measVals.Item4;

              ArcGIS.Core.Geometry.Segment newSegment = null;

              var lineCalc = LineBuilderEx.CreateLineSegment(
                  new Coordinate2D(xFrom, yFrom), new Coordinate2D(xTo, yTo));

              if (radius == 0.0)
              {
                newSegment = lineCalc;
              }
              else
              {
                try
                {
                  var chordDirection = ConvertNorthAzimuthDecDegToPolarRadians(direction);
                  ArcOrientation arcOr = radius < 0 ? ArcOrientation.ArcCounterClockwise : ArcOrientation.ArcClockwise;
                  var circArcCalc =
                    EllipticArcBuilderEx.CreateCircularArc(new Coordinate2D(xFrom, yFrom).ToMapPoint(),
                    lineCalc.Length, chordDirection, arclength, arcOr);

                  Coordinate2D ctrPoint = circArcCalc.CenterPoint;

                  newSegment = EllipticArcBuilderEx.CreateCircularArc(
                      new Coordinate2D(xFrom, yFrom).ToMapPoint(),
                      new Coordinate2D(xTo, yTo).ToMapPoint(), ctrPoint, arcOr, _sr);
                }
                catch
                { 
                  newSegment=lineCalc;
                }
              }

              var newPolyline = PolylineBuilderEx.CreatePolyline(newSegment, _sr);


              if (newPolyline != null)
              {
                //load the datatable
                var row = _table.NewRow();
                //store it
                row["SHAPE"] = newPolyline.ToEsriShape();
                row["SHAPE_LENGTH"] = newPolyline.Length;
                row["DIRECTION"] = direction;
                row["DISTANCE"] = distance;

                if(Math.Abs(radius)>0.0)
                  row["RADIUS"] = radius;


                //add the extended attribute values
                var dict = measurementAttributeLookup[measVals.Item6] as Dictionary<string, string>;
                foreach (var item in dict)
                  row[item.Key]=item.Value;

                //add it to the index
                var rbushPolyline = new RBushPolyline(newPolyline, (long)row["OBJECTID"]);
                _rtreeLines.Insert(rbushPolyline);

                //update max and min for use in the extent
                if (_extent == sr_extent)
                {
                  //first record
                  var env = (newPolyline as ArcGIS.Core.Geometry.Geometry).Extent;
                  _extent = Helpers.RBushExtensions.ToRBushEnvelope(env);
                }
                else
                {
                  var env = (newPolyline as ArcGIS.Core.Geometry.Geometry).Extent;
                  _extent = _extent.Union2D(Helpers.RBushExtensions.ToRBushEnvelope(env));
                }

                _table.Rows.Add(row);
              }
            };
          }

          ////Tuple<double, double, double, double, double> LXMLMeasurement = new(direction, distance, 0, 0, 0);
          ////MeasurementNetwork.Add(fromToKey, LXMLMeasurement);

        }
      }

      else if (_tableName.EndsWith("_PARCELS"))
      {
        //column headings
        var fieldIdx = 0;
        List<string> fieldNames = new(["NAME", "SHAPE_AREA"]);
        fieldNames.AddRange(parcelExtendedFldNames);
        fieldNames = fieldNames.Distinct().ToList();

        foreach (var field in fieldNames)
        {
          var field_name = field.Replace(' ', '_').ToUpper();
          if (field_name.ToLower().Trim() == "shape_area")
          {
            // field name is direction, distance or radius
            _table.Columns.Add(new DataColumn(field_name, typeof(double)));
            lstDoubleFields.Add(fieldIdx);
          }
          else if (field_name.ToUpper().EndsWith("DATE"))
          {
            _table.Columns.Add(new DataColumn(field_name, typeof(DateTime)));
            lstDateFields.Add(fieldIdx);
          }
          else _table.Columns.Add(new DataColumn(field_name, typeof(string)));
          fieldIdx++;
        }

        //For spatial data...
        //Domain to verify coordinates (2D)
        var sr_extent = new RBush.Envelope(
          MinX: _sr.Domain.XMin,
          MinY: _sr.Domain.YMin,
          MaxX: _sr.Domain.XMax,
          MaxY: _sr.Domain.YMax
        );

        //default to the Spatial Reference domain
        _extent = sr_extent;

        //add a shape column
        _table.Columns.Add(new DataColumn("SHAPE", typeof(System.Byte[])));

        var measurementSequencesDict = HashtableToDictionary<string, List<string[]>>(measurementSequences);// as string;        
        foreach (var seq in measurementSequencesDict)
        {
          List<ArcGIS.Core.Geometry.Segment> parcelSegments = new();
          bool hasCircArcDictTracker = false;
          if (ParcelCircArcRadiusTracker.TryGetValue(seq.Key, out List<string[]> thisParcelLineSeq))
          {
            //Access circular arc information using the ParcelCircArcRadiusTracker
            //This is specifically for the polygon geometry. The line boundary geometry is handled separately
            hasCircArcDictTracker = true;
          }
          //int i = 0;
          foreach (string[] pointPair in seq.Value)
          {
            //get coordinates, create connecting points for each line and make segments to add to list
            var fromPointNode = coordinateList[pointPair[0]] as Tuple<double, double, double, Dictionary<string, string>>;
            var toPointNode = coordinateList[pointPair[1]] as Tuple<double, double, double, Dictionary<string, string>>;
            var fromPoint = MapPointBuilderEx.CreateMapPoint(fromPointNode.Item1, fromPointNode.Item2, _sr);
            var toPoint = MapPointBuilderEx.CreateMapPoint(toPointNode.Item1, toPointNode.Item2, _sr);
            
            var lineSeg = LineBuilderEx.CreateLineSegment(fromPoint, toPoint, _sr);
            var seg = LineBuilderEx.ConstructSegmentBuilder(lineSeg).ToSegment();

            //Need to create a circular arc segment here if needed, and replace the seg variable with it
            if (hasCircArcDictTracker)
            {//check if there is a circ arc radius for this point pair
              foreach (var lineParam in thisParcelLineSeq)
              {
                //radiusCtrPtRotList.Add([sStartPoint,sEndPoint,sCtrPoint, sRadius, sCCW]);
                if (lineParam[0] == pointPair[0] && lineParam[1] == pointPair[1])
                {//found the corresponding from and to points
                  if (lineParam[2] != string.Empty && lineParam[3] != string.Empty && lineParam[4] != string.Empty)
                  { //enough info to construct the circular arc
                    string sCtrPoint = lineParam[2].Trim();
                    ArcOrientation arcOr = lineParam[4].Trim().ToLower() == "ccw" ? ArcOrientation.ArcCounterClockwise : ArcOrientation.ArcClockwise;
                    var ctrPointNode = coordinateList[sCtrPoint] as Tuple<double, double, double, Dictionary<string, string>>;
                    var ctrPoint = MapPointBuilderEx.CreateMapPoint(ctrPointNode.Item1, ctrPointNode.Item2, _sr); ;
                    var circArcSeg = EllipticArcBuilderEx.CreateCircularArc(fromPoint, toPoint, ctrPoint.Coordinate2D, arcOr, _sr);
                    //confirm as a double check with radius
                    string sRadius = lineParam[3].Trim(); //radius
                    if (Double.TryParse(lineParam[3].Trim(), out double dRadius))
                    {
                      if (Math.Abs(circArcSeg.SemiMajorAxis - dRadius) < 0.005)
                        seg = EllipticArcBuilderEx.ConstructSegmentBuilder(circArcSeg).ToSegment();
                    }
                  }
                }
              }
            }

            parcelSegments.Add(seg);
          }
          var newPolyGon = PolygonBuilderEx.CreatePolygon(parcelSegments);

          if (newPolyGon != null)
          {
            //load the datatable
            var row = _table.NewRow();
            //store it
            row["SHAPE"] = newPolyGon.ToEsriShape();
            row["SHAPE_AREA"] = newPolyGon.Area;
            row["NAME"] = seq.Key;

            //add the extended attribute values
            var dict = parcelAttributeLookup[seq.Key] as Dictionary<string, string>;
            foreach (var item in dict)
              row[item.Key] = item.Value;

            //add it to the index
            var rbushPolygon = new RBushPolygon(newPolyGon, (long)row["OBJECTID"]);
            _rtreePolygons.Insert(rbushPolygon);

            //update max and min for use in the extent
            if (_extent == sr_extent)
            {
              //first record
              _extent = Helpers.RBushExtensions.ToRBushEnvelope(newPolyGon.Extent);
            }
            else
            {
              _extent = _extent.Union2D(Helpers.RBushExtensions.ToRBushEnvelope(newPolyGon.Extent));
            }

            _table.Rows.Add(row);

          }


        }
      }



      //============================
      else if (_tableName.EndsWith("_PLANS"))
      {
        //column headings
        var fieldIdx = 0;
        List<string> fieldNames = new(["NAME", "DISTANCEFACTOR", "SHAPE_AREA"]);
        fieldNames.AddRange(surveyPlanExtendedFldNames);
        fieldNames = fieldNames.Distinct().ToList();

        foreach (var field in fieldNames)
        {
          var field_name = field.Replace(' ', '_').ToUpper();
          if (field_name.ToLower().Trim() == "distancefactor" || field_name.ToLower().Trim() == "shape_area")
          {
            // field name is direction, distance or radius
            _table.Columns.Add(new DataColumn(field_name, typeof(double)));
            lstDoubleFields.Add(fieldIdx);
          }
          else if (field_name.ToUpper().EndsWith("DATE"))
          {
            _table.Columns.Add(new DataColumn(field_name, typeof(DateTime)));
            lstDateFields.Add(fieldIdx);
          }
          else _table.Columns.Add(new DataColumn(field_name, typeof(string)));
          fieldIdx++;
        }

        //For spatial data...
        //Domain to verify coordinates (2D)
        var sr_extent = new RBush.Envelope(
          MinX: _sr.Domain.XMin,
          MinY: _sr.Domain.YMin,
          MaxX: _sr.Domain.XMax,
          MaxY: _sr.Domain.YMax
        );

        //default to the Spatial Reference domain
        _extent = sr_extent;

        //add a shape column
        _table.Columns.Add(new DataColumn("SHAPE", typeof(System.Byte[])));

        List<ArcGIS.Core.Geometry.Geometry> lstPolygonGeoms = new();
        var measurementSequencesDict = HashtableToDictionary<string, List<string[]>>(measurementSequences);// as string;        

        foreach (var seq in measurementSequencesDict)
        {
          List<ArcGIS.Core.Geometry.Segment> parcelSegments = new();
          foreach (string[] pointPair in seq.Value)
          {
            //get coordinates, create connecting points for each line and make segments to add to list
            var fromPointNode = coordinateList[pointPair[0]] as Tuple<double, double, double, Dictionary<string, string>>;
            var toPointNode = coordinateList[pointPair[1]] as Tuple<double, double, double, Dictionary<string, string>>;
            var fromPoint = MapPointBuilderEx.CreateMapPoint(fromPointNode.Item1, fromPointNode.Item2, _sr);
            var toPoint = MapPointBuilderEx.CreateMapPoint(toPointNode.Item1, toPointNode.Item2, _sr);
            var seg = LineBuilderEx.CreateLineSegment(fromPoint, toPoint, _sr);
            parcelSegments.Add(seg);
          }
          var newPolyGon = PolygonBuilderEx.CreatePolygon(parcelSegments);
          lstPolygonGeoms.Add(newPolyGon);
        }

        //merge the parcel polygons to create the Plan extent geometry
        ArcGIS.Core.Geometry.Geometry newPlanPolyGon = 
          GeometryEngine.Instance.Union(lstPolygonGeoms);

        if (newPlanPolyGon != null)
        {
          //load the datatable
          var row = _table.NewRow();
          //store it
          row["SHAPE"] = newPlanPolyGon.ToEsriShape();
          row["SHAPE_AREA"] = (newPlanPolyGon as ArcGIS.Core.Geometry.Polygon).Area;
          //row["NAME"] = seq.Key;

          //add the extended attribute values, assuming only one plan
          var lookUp = surveyPlanAttributeLookup.Keys.GetEnumerator();
          if (lookUp.MoveNext())
          {
            var firstKey = lookUp.Current;
            if (surveyPlanAttributeLookup[firstKey] is not Dictionary<string, string> dict)
              return;
            foreach (var item in dict)
              row[item.Key] = item.Value;
          }


          //add it to the index
          var rbushPlan = new RBushPolygon(newPlanPolyGon as ArcGIS.Core.Geometry.Polygon, (long)row["OBJECTID"]);
          _rtreePlans.Insert(rbushPlan);

          //update max and min for use in the extent
          if (_extent == sr_extent)
          {
            //first record
            _extent = Helpers.RBushExtensions.ToRBushEnvelope(newPlanPolyGon.Extent);
          }
          else
          {
            _extent = _extent.Union2D(Helpers.RBushExtensions.ToRBushEnvelope(newPlanPolyGon.Extent));
          }

          _table.Rows.Add(row);

        }
      }

      //==============================
    }

    public static Dictionary<K, V> HashtableToDictionary<K, V>(Hashtable table)
    {
      return table
        .Cast<DictionaryEntry>()
        .ToDictionary(kvp => (K)kvp.Key, kvp => (V)kvp.Value);
    }

    private PluginCursorTemplate SearchInternal(ArcGIS.Core.Data.QueryFilter qf)
    {
      var oids = this.ExecuteQuery(qf);
      var columns = this.GetQuerySubFields(qf);

      return new ProPluginCursorTemplate(this,
                                      oids,
                                      columns,
                                      qf.OutputSpatialReference);
    }

    /// <summary>
    /// Implement querying with a query filter
    /// </summary>
    /// <param name="qf"></param>
    /// <returns></returns>
    private List<long> ExecuteQuery(ArcGIS.Core.Data.QueryFilter qf)
    {

      //are we empty?
      if (_table.Rows.Count == 0)
        return new List<long>();

      SpatialQueryFilter sqf = null;
      if (qf is SpatialQueryFilter)
      {
        sqf = qf as SpatialQueryFilter;
      }

      List<long> result = new List<long>();
      bool emptyQuery = true;

      //fidset - this takes precedence over anything else in
      //the query. If a fid set is specified then all selections
      //for the given query are intersections from the fidset
      if (qf.ObjectIDs.Count() > 0)
      {
        emptyQuery = false;

        result = null;
        result = _table.AsEnumerable().Where(
          row => qf.ObjectIDs.Contains((long)row["OBJECTID"]))
          .Select(row => (long)row["OBJECTID"]).ToList();

        //anything selected?
        if (result.Count() == 0)
        {
          //no - specifying a fidset trumps everything. The client
          //specified a fidset and nothing was selected so we are done
          return result;
        }
      }

      //where clause
      if (!string.IsNullOrEmpty(qf.WhereClause))
      {
        emptyQuery = false;
        var sort = "OBJECTID";//default
        if (!string.IsNullOrEmpty(qf.PostfixClause))
        {
          //The underlying System.Data.DataTable used by the sample supports "ORDER BY"
          //It should be a comma-separated list of column names and a default direction
          //COL1 ASC, COL2 DESC  (note: "ASC" is not strictly necessary)
          //Anything else and there will be an exception
          sort = qf.PostfixClause;
        }

        //do the selection
        var oids = _table.Select(qf.WhereClause, sort)
                     .Select(row => (long)row["OBJECTID"]).ToList();

        //consolidate whereclause selection with fidset
        if (result.Count > 0 && oids.Count() > 0)
        {
          var temp = result.Intersect(oids).ToList();
          result = null;
          result = temp;
        }
        else
        {
          result = null;
          result = oids;
        }

        //anything selected?
        if (result.Count() == 0)
        {
          //no - where clause returned no rows or returned no rows
          //common to the specified fidset
          return result;
        }
      }

      //filter geometry for spatial select
      if (sqf != null)
      {
        if (sqf.FilterGeometry != null)
        {
          emptyQuery = false;

          bool filterIsEnvelope = sqf.FilterGeometry is ArcGIS.Core.Geometry.Envelope;
          //search spatial index first
          var extent = sqf.FilterGeometry.Extent;

          if (_tableName.EndsWith("_POINTS"))
          {
            var ptCandidates = _rtree.Search(extent.ToRBushEnvelope());
            //consolidate filter selection with current fidset
            if (result.Count > 0 && ptCandidates.Count > 0)
            {
              var temp = ptCandidates.Where(pt => result.Contains(pt.ObjectID)).ToList();
              ptCandidates = null;
              ptCandidates = temp;
            }
            //anything selected?
            if (ptCandidates.Count == 0)
            {
              //no - filter query returned no rows or returned no rows
              //common to the specified fidset
              return new List<long>();
            }

            //do we need to refine the spatial search?
            if (filterIsEnvelope &&
              (sqf.SpatialRelationship == SpatialRelationship.Intersects ||
              sqf.SpatialRelationship == SpatialRelationship.IndexIntersects ||
              sqf.SpatialRelationship == SpatialRelationship.EnvelopeIntersects))
            {
              //no. This is our final list
              return ptCandidates.Select(pt => pt.ObjectID).OrderBy(oid => oid).ToList();
            }

            //refine based on the exact geometry and relationship
            List<long> oids = new List<long>();
            foreach (var candidate in ptCandidates)
            {
              if (GeometryEngine.Instance.HasRelationship(
                      sqf.FilterGeometry, candidate.ToMapPoint(_sr),
                        sqf.SpatialRelationship))
              {
                oids.Add(candidate.ObjectID);
              }
            }
            //anything selected?
            if (oids.Count == 0)
            {
              //no - further processing of the filter geometry query
              //returned no rows
              return new List<long>();
            }
            result = null;
            //oids has already been consolidated with any specified fidset
            result = oids;
          }

          else if (_tableName.EndsWith("_LINES"))
          {
            var lineCandidates = _rtreeLines.Search(extent.ToRBushEnvelope());
            //consolidate filter selection with current fidset
            if (result.Count > 0 && lineCandidates.Count > 0)
            {
              var temp = lineCandidates.Where(pt => result.Contains(pt.ObjectID)).ToList();
              lineCandidates = null;
              lineCandidates = temp;
            }
            //anything selected?
            if (lineCandidates.Count == 0)
            {
              //no - filter query returned no rows or returned no rows
              //common to the specified fidset
              return new List<long>();
            }

            //do we need to refine the spatial search?
            if (filterIsEnvelope &&
              (sqf.SpatialRelationship == SpatialRelationship.Intersects ||
              sqf.SpatialRelationship == SpatialRelationship.IndexIntersects ||
              sqf.SpatialRelationship == SpatialRelationship.EnvelopeIntersects))
            {
              //no. This is our final list
              return lineCandidates.Select(pt => pt.ObjectID).OrderBy(oid => oid).ToList();
            }

            //refine based on the exact geometry and relationship
            List<long> oids = new List<long>();
            foreach (var candidate in lineCandidates)
            {
              if (GeometryEngine.Instance.HasRelationship(
                      sqf.FilterGeometry, candidate.ToPolyline(_sr),
                        sqf.SpatialRelationship))
              {
                oids.Add(candidate.ObjectID);
              }
            }
            //anything selected?
            if (oids.Count == 0)
            {
              //no - further processing of the filter geometry query
              //returned no rows
              return new List<long>();
            }
            result = null;
            //oids has already been consolidated with any specified fidset
            result = oids;
          }

          else if (_tableName.EndsWith("_PARCELS"))
          {
            var polygonCandidates = _rtreePolygons.Search(extent.ToRBushEnvelope());
            //consolidate filter selection with current fidset
            if (result.Count > 0 && polygonCandidates.Count > 0)
            {
              var temp = polygonCandidates.Where(pt => result.Contains(pt.ObjectID)).ToList();
              polygonCandidates = null;
              polygonCandidates = temp;
            }
            //anything selected?
            if (polygonCandidates.Count == 0)
            {
              //no - filter query returned no rows or returned no rows
              //common to the specified fidset
              return new List<long>();
            }

            //do we need to refine the spatial search?
            if (filterIsEnvelope &&
              (sqf.SpatialRelationship == SpatialRelationship.Intersects ||
              sqf.SpatialRelationship == SpatialRelationship.IndexIntersects ||
              sqf.SpatialRelationship == SpatialRelationship.EnvelopeIntersects))
            {
              //no. This is our final list
              return polygonCandidates.Select(pt => pt.ObjectID).OrderBy(oid => oid).ToList();
            }

            //refine based on the exact geometry and relationship
            List<long> oids = new List<long>();
            foreach (var candidate in polygonCandidates)
            {
              if (GeometryEngine.Instance.HasRelationship(
                      sqf.FilterGeometry, candidate.ToPolygon(_sr),
                        sqf.SpatialRelationship))
              {
                oids.Add(candidate.ObjectID);
              }
            }
            //anything selected?
            if (oids.Count == 0)
            {
              //no - further processing of the filter geometry query
              //returned no rows
              return new List<long>();
            }
            result = null;
            //oids has already been consolidated with any specified fidset
            result = oids;
          }

          else if (_tableName.EndsWith("_PLANS"))
          {
            var polygonCandidates = _rtreePlans.Search(extent.ToRBushEnvelope());
            //consolidate filter selection with current fidset
            if (result.Count > 0 && polygonCandidates.Count > 0)
            {
              var temp = polygonCandidates.Where(pt => result.Contains(pt.ObjectID)).ToList();
              polygonCandidates = null;
              polygonCandidates = temp;
            }
            //anything selected?
            if (polygonCandidates.Count == 0)
            {
              //no - filter query returned no rows or returned no rows
              //common to the specified fidset
              return new List<long>();
            }

            //do we need to refine the spatial search?
            if (filterIsEnvelope &&
              (sqf.SpatialRelationship == SpatialRelationship.Intersects ||
              sqf.SpatialRelationship == SpatialRelationship.IndexIntersects ||
              sqf.SpatialRelationship == SpatialRelationship.EnvelopeIntersects))
            {
              //no. This is our final list
              return polygonCandidates.Select(pt => pt.ObjectID).OrderBy(oid => oid).ToList();
            }

            //refine based on the exact geometry and relationship
            List<long> oids = new List<long>();
            foreach (var candidate in polygonCandidates)
            {
              if (GeometryEngine.Instance.HasRelationship(
                      sqf.FilterGeometry, candidate.ToPolygon(_sr),
                        sqf.SpatialRelationship))
              {
                oids.Add(candidate.ObjectID);
              }
            }
            //anything selected?
            if (oids.Count == 0)
            {
              //no - further processing of the filter geometry query
              //returned no rows
              return new List<long>();
            }
            result = null;
            //oids has already been consolidated with any specified fidset
            result = oids;
          }

        }
      }

      //last chance - did we execute any type of query?
      if (emptyQuery)
      {
        //no - the default is to return all rows
        result = null;
        result = _table.Rows.Cast<DataRow>()
          .Select(row => (long)row["OBJECTID"]).OrderBy(x => x).ToList();
      }
      return result;
    }

    private List<string> GetQuerySubFields(ArcGIS.Core.Data.QueryFilter qf)
    {
      //Honor Subfields in Query Filter
      string columns = qf.SubFields ?? "*";
      List<string> subFields;
      if (columns == "*")
      {
        subFields = this.GetFields().Select(col => col.Name.ToUpper()).ToList();
      }
      else
      {
        var names = columns.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        subFields = names.Select(n => n.ToUpper()).ToList();
      }

      return subFields;
    }

    private static bool MeasurementNetworkFromLandXML(XmlDocument xmlDoc, out Hashtable MeasurementNetwork,
      out Hashtable CoordinateList, out Hashtable MeasurementSequences, out Hashtable MeasurementSequenceInfo, 
      out Hashtable PointAttributeLookup, out Hashtable MeasurementAttributeLookup,
      out Hashtable ParcelAttributeLookup, out Hashtable SurveyPlanAttributeLookup, 
      out Dictionary<string, List<string[]>> ParcelCircArcRadiusTracker, out int totalLinesRead)
    {
      totalLinesRead = 0;
      //initialize the containers for building the measurement network
      MeasurementNetwork = new();
      CoordinateList = new();
      MeasurementSequences = new();
      MeasurementSequenceInfo = new();
      PointAttributeLookup = new();
      MeasurementAttributeLookup = new();
      ParcelAttributeLookup = new();
      SurveyPlanAttributeLookup = new(); // assume one Survey Plan per file for now

      ParcelCircArcRadiusTracker = new();

      //TODO: convert Measurement, PointNode, and MeasurementSequence objects to classes
      //Measurement:
      //0-Direction; north azimuth decimal degrees,
      //1-Distance; straight lines only 0.0 for circular arcs
      //2-Radius; circular arcs only, 0.0 for straight lines, negative for circular arcs to the left/counter-clockwise
      //3-Arclength; circular arcs only, 0.0 for straight lines
      //4-Radius2; spiral curves only, 0.0 for straight lines and for circular arcs
      //PointNode:
      //0-X; x-coordinate
      //1-Y; y-coordinate
      //2-Z; z-coordinate
      //3-Fixed; boolean value set to true for control point
      //4-Description; textual description for point
      //Measurement Sequence Hashtable:
      //Key: SequenceName; Value: List of From|To point names in an ordered sequence
      //Measurement Sequence Info Hashtable:
      //Key: SequenceName; Value: String "Direction Offset, Distance Factor, isLoop"

      //Measurement = new (12.0,12.0,12.0,12.0,12.0, "1");
      //PointNode = new (0, 0, 0, false,"");

      //MeasurementNetwork.Add("A07,T27", Measurement); from,to
      //CoordinateList.Add("A07", PointNode);

      //XmlDocument xmlDoc = new XmlDocument(); // Create an XML document object
      //xmlDoc.Load(SourceFiles[0]); // Load the XML document from the specified file

      // Define the namespace manager
      XmlNamespaceManager nsmgr = new XmlNamespaceManager(xmlDoc.NameTable);
      nsmgr.AddNamespace("landxml", "http://www.landxml.org/schema/LandXML-1.2");

      //Get Survey Plan elements
      XmlNodeList surveyHeader = xmlDoc.GetElementsByTagName("SurveyHeader");
      foreach (XmlNode info in surveyHeader)
      {
        var surveyPlanXmlAtts = info.Attributes;
        Dictionary<string, string> planAttributes = new();
        foreach (XmlAttribute xmlAtt in surveyPlanXmlAtts)
        {//get attribute names from the XmlAttributeCollection
          if (!planAttributes.ContainsKey(xmlAtt.Name.ToUpper()))
            planAttributes.Add(xmlAtt.Name.ToUpper(), xmlAtt.InnerText);
        }
        if(!SurveyPlanAttributeLookup.ContainsKey(planAttributes["NAME"]))
          SurveyPlanAttributeLookup.Add(planAttributes["NAME"], planAttributes);
      }

      // Get CgPoint elements
      XmlNodeList cgPointList = xmlDoc.GetElementsByTagName("CgPoint");
      foreach (XmlNode cgPoint in cgPointList)
      {
        var attColl = cgPoint.Attributes;
        Dictionary<string, string> pointAttributes = new();
        foreach (XmlAttribute xmlAtt in attColl)
        {//get attribute names from the XmlAttributeCollection
          if (!pointAttributes.ContainsKey(xmlAtt.Name.ToUpper()))
            pointAttributes.Add(xmlAtt.Name.ToUpper(), xmlAtt.InnerText);
        }

        bool bHasName = pointAttributes.TryGetValue("NAME", out string pointName);

        var sXY = cgPoint.InnerText.Split(" ");

        if (bHasName && double.TryParse(sXY[0], out double Y) && double.TryParse(sXY[1], out double X))
        {//Northing(Y), Easting(X)
          double Z = 0.0;
          Tuple<double, double, double, Dictionary<string, string>> thisPointNode =
            new(X, Y, Z, pointAttributes); //x,y,z,attributes, Northing(Y), Easting(X), 
          CoordinateList.Add(pointName, thisPointNode);
        }
      }

      // Get Parcel elements
      XmlNodeList xmlParcels = xmlDoc.SelectNodes("//landxml:Parcel", nsmgr);
      foreach (XmlNode xmlParcel in xmlParcels)
      {
        string parcelName = xmlParcel.Attributes["name"].Value;

        Dictionary<string, string> parcAtts = new();
        var parcXMLAtts = xmlParcel.Attributes;
        for (int i = 0; i < parcXMLAtts.Count; i++)
          parcAtts.Add(parcXMLAtts[i].Name, parcXMLAtts[i].Value);

        if (!ParcelAttributeLookup.ContainsKey(parcelName))
          ParcelAttributeLookup.Add(parcelName, parcAtts);
        ////else
        ////  continue; //ignore subsequent parcels with the same name TODO: revisit/test with uncommented

        //Get a list of all <Line> and <Curve> elements for the <Parcel>
        List<string[]> fromToList = new();
        List<string[]> radiusCtrPtRotList = new();
        XmlNodeList xmlLinesAndCurves = xmlParcel.SelectNodes(".//landxml:Line | .//landxml:Curve", nsmgr);
        foreach (XmlNode xmlLineCurveNode in xmlLinesAndCurves)
        {
          XmlElement lineCurveElement = (XmlElement)xmlLineCurveNode;
          string[] sFromTo = new string[2];
          sFromTo[0] = lineCurveElement.SelectSingleNode(".//landxml:Start", nsmgr).Attributes["pntRef"].Value;
          sFromTo[1] = lineCurveElement.SelectSingleNode(".//landxml:End", nsmgr).Attributes["pntRef"].Value;
          fromToList.Add(sFromTo);

          if (lineCurveElement.Name == "Curve")
          {//get radius, and get center point
            var sRadius = lineCurveElement.Attributes["radius"].Value;
            var sCCW = lineCurveElement.Attributes["rot"].Value;
            var sStartPoint = lineCurveElement.SelectSingleNode(".//landxml:Start", nsmgr).Attributes["pntRef"].Value;
            var sCtrPoint = lineCurveElement.SelectSingleNode(".//landxml:Center", nsmgr).Attributes["pntRef"].Value;
            var sEndPoint = lineCurveElement.SelectSingleNode(".//landxml:End", nsmgr).Attributes["pntRef"].Value;
            radiusCtrPtRotList.Add([sStartPoint,sEndPoint, sCtrPoint, sRadius, sCCW]);
          }
          else
            radiusCtrPtRotList.Add([String.Empty, String.Empty, String.Empty, String.Empty, String.Empty]);

        }
        if (!MeasurementSequences.ContainsKey(parcelName))
        {
          MeasurementSequences.Add(parcelName, fromToList);
          MeasurementSequenceInfo.Add(parcelName, "0.00,1.00,true");
          ParcelCircArcRadiusTracker.Add(parcelName,radiusCtrPtRotList);
        }
      }

      //build a dictionary; instrumentSetup to point
      Dictionary<string,string> dictSetupToPoint = new();
      XmlNodeList xmlInstrumentSetups = xmlDoc.SelectNodes("//landxml:InstrumentSetup", nsmgr);
      foreach (XmlNode xmlSetup in xmlInstrumentSetups)
      {
        string setupName = xmlSetup.Attributes["id"].Value;
        string pointRef = xmlSetup.SelectSingleNode(".//landxml:InstrumentPoint", nsmgr).Attributes["pntRef"].Value;
        dictSetupToPoint.Add(setupName, pointRef);
      }

      XmlNodeList xmlReducedObservations = xmlDoc.SelectNodes(
        "//landxml:ReducedObservation | //landxml:ReducedArcObservation", nsmgr);
      foreach (XmlNode xmlOb in xmlReducedObservations)
      {
        var attObsColl = xmlOb.Attributes;
        Dictionary<string, string> obsAttributes = new();
        foreach (XmlAttribute obsAtt in attObsColl)
        {//get attribute names from the XmlAttributeCollection
          if (obsAtt != null)
          {
            if (obsAtt.Name.Trim().Length > 0 && obsAtt.InnerText.Trim().Length > 0)
              obsAttributes.Add(obsAtt.Name, obsAtt.InnerText);
          }
        }
        var fieldNote = xmlOb.SelectSingleNode(".//landxml:FieldNote", nsmgr);
        if (fieldNote != null)
        {
          string fieldNoteText = fieldNote.InnerText;
          if (fieldNoteText.Trim() != String.Empty)
            obsAttributes.Add("FieldNote", fieldNoteText);
        }

        //Measurement:
        //0-Direction; north azimuth decimal degrees,
        //1-Distance; straight lines only 0.0 for circular arcs
        //2-Radius; circular arcs only, 0.0 for straight lines, negative for circular arcs to the left/counter-clockwise
        //3-Arclength; circular arcs only, 0.0 for straight lines
        //4-Radius2; spiral curves only, 0.0 for straight lines and for circular arcs

        //Measurement = new(12.0, 12.0, 12.0, 12.0, 12.0);
        //direction, distance, radius, arclength, radius2, ID
        string sDirection = "";
        string sRadius = "0";
        string sArcLength = "0";
        string sDistance = "0";
        if (xmlOb.Name == "ReducedObservation")
        {
          sDirection = obsAttributes["azimuth"];
          sDistance = obsAttributes["horizDistance"]; 
        }
        else if (xmlOb.Name == "ReducedArcObservation")
        {
          sDirection = obsAttributes["chordAzimuth"];
          sRadius = obsAttributes["radius"];
          sArcLength = obsAttributes["length"];
          bool isCCW = obsAttributes["rot"].ToLower().Trim() =="cw" ? false : true;
          if (isCCW) 
            sRadius = "-" + sRadius;
        }
        double direction = ConvertNorthAzimuthDMSStringToNorthAzimuthDecimalDegrees(sDirection);
        //<ReducedObservation name="1" desc="Road" setupID="IS1" targetSetupID="IS2" azimuth="105.4350" horizDistance="37.8" distanceType="Measured" azimuthType="Compiled">

        string sName = obsAttributes["name"];

        _ = double.TryParse(sDistance, out double distance);
        _ = double.TryParse(sRadius, out double radius);
        _ = double.TryParse(sArcLength, out double arclength);
        double radius2 = 0.0;
        
        string fromToKey = dictSetupToPoint[obsAttributes["setupID"]];
        fromToKey += "," + dictSetupToPoint[obsAttributes["targetSetupID"]];

        if (!MeasurementAttributeLookup.ContainsKey(sName))
          MeasurementAttributeLookup.Add(sName, obsAttributes);

        Tuple<double, double, double, double, double, string> LXMLMeasurement 
          = new(direction,distance,radius,arclength,radius2,sName);
        MeasurementNetwork.Add(fromToKey, LXMLMeasurement);
      }

      //Get Monument elements
      //collect attribute field names from the Monuments->Monument tags
      XmlNodeList xmlMonuments = xmlDoc.GetElementsByTagName("Monument");
      foreach (XmlNode xmlMon in xmlMonuments)
      {
        var attMonColl = xmlMon.Attributes;
        Dictionary<string, string> monAttributes = new();
        foreach (XmlAttribute xmlAtt in attMonColl)
        {//get attribute names from the XmlAttributeCollection
          var fldName = xmlAtt.Name.ToUpper();
          if (fldName == "DESC" | fldName == "STATE")
            fldName = "MON_" + fldName;
          monAttributes.Add(fldName, xmlAtt.InnerText);
        }
        if (!PointAttributeLookup.ContainsKey(monAttributes["PNTREF"]))
          PointAttributeLookup.Add(monAttributes["PNTREF"], monAttributes);
      }

      //Get RedHorizontalPosition elements
      //collect attribute field names from the RedHorizontalPosition tags
      //Hashtable redHorizPosAttributesLookup = new();
      XmlNodeList xmlRedHorizPos = xmlDoc.GetElementsByTagName("RedHorizontalPosition");
      foreach (XmlNode xmlHorizPos in xmlRedHorizPos)
      {
        var attHorizPosColl = xmlHorizPos.Attributes;
        Dictionary<string, string> redHorizAttributes = new();
        foreach (XmlAttribute xmlAtt in attHorizPosColl)
        {//get attribute names from the XmlAttributeCollection
          redHorizAttributes.Add(xmlAtt.Name.ToUpper(), xmlAtt.InnerText);
        }

        var pointID = dictSetupToPoint[redHorizAttributes["SETUPID"]];

        if (!PointAttributeLookup.ContainsKey(pointID))
          PointAttributeLookup.Add(pointID, redHorizAttributes);
        else //if the point already exists, just add to the existing Dictionary for the point
        {
          var attDict = PointAttributeLookup[pointID] as Dictionary<string, string>;
          //attDict.Add()
          foreach (var newAttribute in redHorizAttributes)
          {
            if(!attDict.ContainsKey(newAttribute.Key))
              attDict.Add(newAttribute.Key, newAttribute.Value);
          }
          PointAttributeLookup[pointID] = attDict;
        }
      }



      ////      }
      ////      catch
      ////      {
      ////        return false;
      ////      }
      ////      finally
      ////      {
      ////        tr.Close(); //close the file and release resources
      ////      }

      return true;
    }


    internal static string GetNextLineWithAlphanumericData(ref System.IO.TextReader fileReadStream, ref int LineCount)
    {
      var sLine = fileReadStream.ReadLine();
      if (sLine == null)
        return null;
      LineCount++;
      var iLen = sLine.Trim().Length;
      var sFirstTwo = "";
      if (iLen > 2)
        sFirstTwo = sLine[..2];

      if (iLen > 0 && sFirstTwo != "--")
        return sLine;

      while (iLen == 0 || sFirstTwo == "--")
      {
        sLine = fileReadStream.ReadLine();
        if (sLine == null)
          return null;
        LineCount++;
        iLen = sLine.Trim().Length;
        if (iLen > 2)
          sFirstTwo = sLine[..2];
        if (sLine.Trim().Length > 0 && sFirstTwo != "--")
          break;
      }
      return sLine;
    }


    private static bool ParsePointNodeFromString(string inString, out string PointName,
              out Tuple<double, double, double, bool, string> PointNode)
    {
      PointNode = new(0.0, 0.0, 0.0, false, "");
      var sNamedCoordinate = inString.Trim().Split(' ');
      PointName = sNamedCoordinate[0].Trim();
      var sXY = inString.Trim().Insert(PointName.Length + 2, "|");
      sXY = sXY.Insert(sXY.IndexOf(',') + 4, "|");
      sXY = sXY.Replace(",", ".").Replace(" ", "");
      var sX = sXY.Split('|')[1];
      var sY = sXY.Split('|')[2];
      if (double.TryParse(sX, out double X) && double.TryParse(sY, out double Y))
      {
        PointNode = new(-X, -Y, 0.0, false, ""); //negative x and y coordinates
        return true;
      }
      else
        return false;
    }
    private static bool ParseMeasurementFromString(string inString,
              out Tuple<double, double, double, double, double> Measurement)
    {
      Measurement = new(0.0, 0.0, 0.0, 0.0, 0.0);
      var sDirDist = inString.Trim().Replace('.', '-').Replace(',', '.');
      sDirDist = sDirDist.Insert(sDirDist.IndexOf('.') - 6, "|");
      var sDir = sDirDist.Split('|')[0];
      var sDist = sDirDist.Split('|')[1];

      var northAzDir = ConvertNorthAzimuthDMSStringToNorthAzimuthDecimalDegrees(sDir);

      if (double.TryParse(sDist, out double Distance))
      {
        Measurement = new(northAzDir, Distance, 0.0, 0.0, 0.0);
        return true;
      }
      else
        return false;
    }

    private static double ConvertNorthAzimuthDMSStringToNorthAzimuthDecimalDegrees(string InDirection)
    {
      var dirUnitIn = ArcGIS.Core.SystemCore.DirectionUnits.DegreesMinutesSeconds;
      var dirTypeIn = ArcGIS.Core.SystemCore.DirectionType.NorthAzimuth;

      var dirUnitOut = ArcGIS.Core.SystemCore.DirectionUnits.DecimalDegrees;
      var dirTypeOut = ArcGIS.Core.SystemCore.DirectionType.NorthAzimuth;

      var AngConv = DirectionUnitFormatConversion.Instance;
      var ConvDef = new ConversionDefinition()
      {
        DirectionTypeIn = dirTypeIn,
        DirectionUnitsIn = dirUnitIn,
        DirectionTypeOut = dirTypeOut,
        DirectionUnitsOut = dirUnitOut
      };
      return AngConv.ConvertToDouble(InDirection, ConvDef);
    }

    private static double ConvertNorthAzimuthDecDegToPolarRadians(double InDirection)
    {
      var dirUnitIn = ArcGIS.Core.SystemCore.DirectionUnits.DecimalDegrees;
      var dirTypeIn = ArcGIS.Core.SystemCore.DirectionType.NorthAzimuth;

      var dirUnitOut = ArcGIS.Core.SystemCore.DirectionUnits.Radians;
      var dirTypeOut = ArcGIS.Core.SystemCore.DirectionType.Polar;

      var AngConv = DirectionUnitFormatConversion.Instance;
      var ConvDef = new ConversionDefinition()
      {
        DirectionTypeIn = dirTypeIn,
        DirectionUnitsIn = dirUnitIn,
        DirectionTypeOut = dirTypeOut,
        DirectionUnitsOut = dirUnitOut
      };
      return AngConv.ConvertToDouble(InDirection, ConvDef);
    }


    #endregion Private

    #region IDisposable

    private bool _disposed = false;
    /// <summary>
    /// 
    /// </summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
      //TODO free unmanaged resources here
      System.Diagnostics.Debug.WriteLine("Table being disposed");

      if (_disposed)
        return;

      if (disposing)
      {
        _table?.Clear();
        _table = null;
        _rtree?.Clear();
        _rtree = null;
        _rtreeLines?.Clear();
        _rtreeLines = null;
        _rtreePolygons?.Clear();
        _rtreePolygons = null;
        _sr = null;
        _gisExtent = null;
      }
      _disposed = true;
    }
    #endregion
  }

}
