/* Copyright 2023 Esri
 *
 * Licensed under the Apache License Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Core.UnitFormats;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParcelsAddin
{
  internal class Export_TraverseFile : Button
  {
    protected override async void OnClick()
    {
      bool hasParcelSelection = await QueuedTask.Run(() =>
      {
        var selDict = MapView.Active?.Map?.GetSelection().ToDictionary<FeatureLayer>();
        if (selDict == null)
          return false;
        var polygonLayers =
          selDict.Keys.Where(fl => fl.ShapeType == esriGeometryType.esriGeometryPolygon)
          .Where(fl => fl.IsControlledByParcelFabricAsync(ParcelFabricType.ParcelFabric).Result);
        if (!polygonLayers.Any())
          return false;
        return true;
      });
      if (!hasParcelSelection)
      {
        MessageBox.Show("There is no parcel selection.", this.Caption);
        return;
      }

      //browse to a folder location to store the files
      if (!ParcelUtils.GetTargetFolder("ExportTraverseFileLastUsedParams", out string folderPath))
        return;

      if (folderPath == "")
        return;
      Dictionary<FeatureLayer, List<long>> dictLyr2IdsList = new();
      CancelableProgressorSource cps = new("Export traverse files", "Canceled");
      string errorMessage = await QueuedTask.Run(async () =>
      {
        try
        {
          var myParcelFabricLayer =
            MapView.Active?.Map?.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();
          //if there is no fabric in the map then bail
          if (myParcelFabricLayer == null)
            return "There is no fabric layer in the map.";

          var recordsLyr = myParcelFabricLayer.GetRecordsLayerAsync().Result.FirstOrDefault();
          if (recordsLyr == null)
            return "There is no records layer in the map.";
          //...confirm we're not editing default version geodatabase.
          if (ParcelUtils.IsDefaultVersionOnFeatureService(recordsLyr))
            return "Editing on the default version is not available.";

          double _metersPerUnit = 1;
          bool _isPCS = true;
          var pSR = myParcelFabricLayer.GetSpatialReference();
          if (pSR.IsProjected)
            _metersPerUnit = pSR.Unit.ConversionFactor; //meters per unit    
          else
            _isPCS = false;
          ParcelUtils.GetParcelPolygonFeatureLayersSelection(myParcelFabricLayer,
            out Dictionary<FeatureLayer, List<long>> parcelPolygonLayerIds);
          foreach (var featlyr in parcelPolygonLayerIds)
          {
            cps.Progressor.Message = "Saving " + featlyr.Key.ToString();
            foreach (var oid in featlyr.Value)
            {
              ParcelEdgeCollection parcelEdgeCollection = null;
              var tol = 0.03 / _metersPerUnit; //3 cms
              if (!_isPCS)
                tol = Math.Atan(tol / (6378100 / _metersPerUnit));
              try
              {
                parcelEdgeCollection = await myParcelFabricLayer.GetSequencedParcelEdgeInfoAsync(featlyr.Key,
                    oid, null, tol,
                    ParcelLineToEdgeRelationship.BothVerticesMatchAnEdgeEnd |
                    ParcelLineToEdgeRelationship.StartVertexMatchesAnEdgeEnd |
                    ParcelLineToEdgeRelationship.EndVertexMatchesAnEdgeEnd |
                    ParcelLineToEdgeRelationship.StartVertexOnAnEdge |
                    ParcelLineToEdgeRelationship.EndVertexOnAnEdge
                    ); //ParcelLineToEdgeRelationship.All);
              }
              catch
              {
                continue;
              }

              if (parcelEdgeCollection == null)
                continue;

              var startPoint = parcelEdgeCollection.Edges[0].EdgeGeometry.Points[0].Coordinate2D;

              bool isClosedloop = false;
              bool allLinesHaveCogo = false;
              if (!ParcelUtils.ParcelEdgeAnalysis(parcelEdgeCollection, out isClosedloop, out allLinesHaveCogo,
                  out object[] parcelTraverseInfo))
                continue;

              if (isClosedloop && allLinesHaveCogo)
              {
                cps.Progressor.Value += 1;
                //start point coordinates x y
                //traverseInfo object list items:
                //[0] vector, [1] direction, [2] distance,
                //[3] radius, [4] arclength, [5] isMajor, [6] isLineReversed

                var radiusList = new List<double>();
                var arcLengthList = new List<double>();
                //var isMajorList = new List<bool>();
                var traverseCourses = new List<Coordinate3D>();
                #region enhance traverse info with circular arc info
                foreach (var radiusObj in parcelTraverseInfo[3] as List<object>)
                {
                  double radius = 0.0;
                  if (radiusObj != null)
                    radius = (double)radiusObj;
                  radiusList.Add(radius);
                }

                foreach (var arcLengthObj in parcelTraverseInfo[4] as List<object>)
                {
                  double arcLength = 0.0;
                  if (arcLengthObj != null)
                    arcLength = (double)arcLengthObj;
                  arcLengthList.Add(arcLength);
                }
                #endregion
                var sDTDU = 
                   COGOUtils.GetBackstageDirectionTypeAndUnit(DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Direction));

                StreamWriter sw = new(Path.Combine(folderPath, oid.ToString() + ".txt"));
                sw.WriteLine("DT " + sDTDU[0]); //direction type north azimuth
                sw.WriteLine("DU " + sDTDU[1]); //direction units decimal degrees
                sw.WriteLine("SP " + startPoint.X.ToString("F6") + " " +
                  startPoint.Y.ToString("F6"));

                var idx = 0;
                foreach (Coordinate3D vec in parcelTraverseInfo[0] as List<object>)
                {
                  if (vec.Magnitude == 0.0)
                    continue;
                  //get the direction format and units from the backstage default settings
                  var direction = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDisplayUnit(vec.Azimuth * 180.0 / Math.PI,
                    DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Direction), false);

                  bool isCircularArc = radiusList[idx] != 0.0;
                  var leftRight = (radiusList[idx] < 0.0) ? "L" : "R";

                  if (!isCircularArc)
                    sw.WriteLine("DD " + direction + " " + vec.Magnitude.ToString("F3"));
                  else
                    sw.WriteLine("NC A " + arcLengthList[idx].ToString("F3") +
                      " R " + Math.Abs(radiusList[idx]).ToString("F3") +
                      " C " + direction + " " + leftRight);
                  idx++;
                }
                sw.Close();
              }
            }
            if (cps.Progressor.CancellationToken.IsCancellationRequested)
              break;
            cps.Progressor.Status = "Parcels with a valid loop traverse: " + cps.Progressor.Value;
          }
          return "";
        }
        catch (Exception ex)
        { 
          return ex.Message; 
        }
      }, cps.Progressor);
    }

    //this code below is used for tool enablement, based on parcel selection
    //but is commented out for performance related reason. Fix TBD.

    //protected override void OnUpdate()
    //{
    //  QueuedTask.Run(() =>
    //  {
    //    //confirm we have a license...
    //    if (!ParcelUtils.HasValidLicenseForParcelLayer())
    //    {
    //      this.Enabled = false;
    //      this.DisabledTooltip = "Insufficient license level.";
    //      return;
    //    }

    //    //var myParcelFabricLayer =
    //    //MapView.Active?.Map?.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();

    //    ////if there is no fabric in the map then bail
    //    //if (myParcelFabricLayer == null)
    //    //{
    //    //  this.Enabled = false;
    //    //  this.DisabledTooltip = "There is no fabric in the map.";
    //    //  return;
    //    //}
    //    //if (ParcelUtils.HasParcelSelection(myParcelFabricLayer))
    //    if(Module1.Current.HasParcelPolygonSelection)
    //    {
    //      this.Enabled = true;  //tool is enabled  
    //                            //this.Tooltip = "";
    //    }
    //    else
    //    {
    //      this.Enabled = false;  //tool is disabled  
    //                             //customize your disabledText here
    //      this.DisabledTooltip = "There is no parcel selection.";
    //    }
    //  });
    //}
  }

  internal class Export_ParcelReportFile : Button
  {
    protected override async void OnClick()
    {
      bool hasParcelSelection = await QueuedTask.Run(() =>
      {
        var selDict = MapView.Active?.Map?.GetSelection().ToDictionary<FeatureLayer>();
        if (selDict == null)
          return false;
        var polygonLayers =
          selDict.Keys.Where(fl => fl.ShapeType == esriGeometryType.esriGeometryPolygon)
          .Where(fl => fl.IsControlledByParcelFabricAsync(ParcelFabricType.ParcelFabric).Result);
        if (!polygonLayers.Any())
          return false;
        return true;
      });
      if (!hasParcelSelection)
      {
        MessageBox.Show("There is no parcel selection.", this.Caption);
        return;
      }

      //browse to a folder location to store the files
      if (!ParcelUtils.GetTargetFolder("ExportParcelReportFileLastUsedParams", out string folderPath))
        return;

      if (folderPath == "")
        return;
      Dictionary<FeatureLayer, List<long>> dictLyr2IdsList = new();
      CancelableProgressorSource cps = new("Export parcel reports", "Canceled");
      string errorMessage = await QueuedTask.Run(async () =>
      {
        try
        {
          var myParcelFabricLayer =
            MapView.Active?.Map?.GetLayersAsFlattenedList().OfType<ParcelLayer>().FirstOrDefault();
          //if there is no fabric in the map then bail
          if (myParcelFabricLayer == null)
            return "There is no fabric layer in the map.";

          var recordsLyr = myParcelFabricLayer.GetRecordsLayerAsync().Result.FirstOrDefault();
          if (recordsLyr == null)
            return "There is no records layer in the map.";

          //string distUnit = "Meters";
          string distUnitSuff = " m";
          string areaUnit = "Hectares, Square Meters";
          string smallAreaUnitSuff = " sqm";
          string largeAreaUnitSuff = " ha";
          double sqMetersPerLargeAreaUnit = 10000.0;

          double _metersPerDatasetUnit = 1.0;
          bool _isPCS = true;
          var pSR = recordsLyr.GetFeatureClass().GetDefinition().GetSpatialReference();
          if (pSR.IsProjected)
            _metersPerDatasetUnit = pSR.Unit.ConversionFactor; //meters per dataset unit    
          else
            _isPCS = false;

          double _distanceMetersPerBackstageLinearUnit = COGOUtils.GetMetersPerUnitFromProject();
          double _distanceUnitConversionFactor = _metersPerDatasetUnit / _distanceMetersPerBackstageLinearUnit;
          double sqMetersPerSmallAreaUnit = _distanceMetersPerBackstageLinearUnit * _distanceMetersPerBackstageLinearUnit;

          string projectDistanceUnitName = DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Distance).DisplayNamePlural;
          areaUnit = "Hectares, Square " + projectDistanceUnitName;

          distUnitSuff = " " + DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Distance).Abbreviation;
          smallAreaUnitSuff = " sq"+ distUnitSuff;

          if (projectDistanceUnitName.ToLower().Contains("yard") || projectDistanceUnitName.ToLower().Contains("feet") ||
                  projectDistanceUnitName.ToLower().Contains("chain"))
          {
            areaUnit = "Acres, Square " + projectDistanceUnitName;
            largeAreaUnitSuff = " ac";
            sqMetersPerLargeAreaUnit = 4046.86;
          }

          double smallUnitAreaToBigUnitAreaConverter = sqMetersPerSmallAreaUnit / sqMetersPerLargeAreaUnit;
          var sDTDU =
            COGOUtils.GetBackstageDirectionTypeAndUnit(DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Direction), true);

          var Fmt2Str0DecPl = "{0,2:##}";
          var Fmt10Str1DecPl = "{0,10:0.0}";
          var Fmt10Str3DecPl = "{0,10:0.000}";
          var Fmt11Str0DecPl = "{0,11:##}";
          var Fmt10Str4DecPl = "{0,10:0.0000}";
          var Fmt7Str3DecPl = "{0,7:0.000}";
          var Fmt10StrPlusMinus1DecPl = "{0,10:+0.0;-0.0}";
          var Fmt10StrPlusMinus3DecPl = "{0,10:+0.000;-0.000}";
          var Fmt10StrPlusMinus4DecPl = "{0,10:+0.0000;-0.0000}";
          var Fmt7StrPlusMinus3DecPl = "{0,7:+0.000;-0.000}";
          var Fmt5StrPlusMinus3DecPl = "{0,5:+0.000;-0.000}";
          string adjustmentMethod = "Compass";
          var tabDelim = "\t";
          var tabDelim2 = "\t\t";
          var tabDelim3 = "\t\t\t";
          var underLine = "------------------------------------------------------------------";
          var dblUnderLine = "==================================================================";

          ParcelUtils.GetParcelPolygonFeatureLayersSelection(myParcelFabricLayer,
            out Dictionary<FeatureLayer, List<long>> parcelPolygonLayerIds);
          foreach (var featlyr in parcelPolygonLayerIds)
          {
            cps.Progressor.Message = "Saving " + featlyr.Key.ToString();
            foreach (var oid in featlyr.Value)
            {
              var shapeAreaInProjectionSmallUnit = 0.0;
              var shapeAreaInProjectionLargeUnit = 0.0;
              var parcelName = "";
              var parcelRecord = "";
              var parcelRecordGUID = "";
              var fldIdx = -1;
              List<long> oids = new();
              oids.Add(oid);
              QueryFilter pQuFilt = new();
              pQuFilt.ObjectIDs = oids;
              using (RowCursor rowCursor = featlyr.Key.Search(pQuFilt))
              {//only expecting 1 back
                rowCursor.MoveNext();
                using Feature rowFeat = (Feature)rowCursor.Current;
                var polyGeom = rowFeat.GetShape();

                if (pSR.IsProjected)
                  shapeAreaInProjectionSmallUnit = (polyGeom as Polygon).Area;
                else
                  shapeAreaInProjectionSmallUnit = GeometryEngine.Instance.GeodesicArea(polyGeom);

                shapeAreaInProjectionSmallUnit *= _metersPerDatasetUnit * _metersPerDatasetUnit / sqMetersPerSmallAreaUnit;
                shapeAreaInProjectionLargeUnit = shapeAreaInProjectionSmallUnit * smallUnitAreaToBigUnitAreaConverter;
                fldIdx = rowFeat.FindField("Name");
                if (fldIdx!=-1)
                  parcelName = rowFeat.GetOriginalValue(fldIdx) as string;
                fldIdx = rowFeat.FindField("CreatedByRecord");
                if (fldIdx != -1)
                  parcelRecordGUID = rowFeat.GetOriginalValue(fldIdx) as string;
              }
              if (fldIdx != -1)
              {
                var globalIdFldName = recordsLyr.GetFeatureClass().GetDefinition().GetGlobalIDField();
                pQuFilt = new();
                pQuFilt.WhereClause = globalIdFldName + "= '" + parcelRecordGUID + "'";
                using (RowCursor rowCursor = recordsLyr.Search(pQuFilt))
                {//only expecting 1 back
                  rowCursor.MoveNext();
                  using Feature rowFeat = (Feature)rowCursor.Current;
                  fldIdx = rowFeat.FindField("Name");
                  if (fldIdx != -1)
                    parcelRecord = rowFeat.GetOriginalValue(fldIdx) as string;
                }
              }
              ParcelEdgeCollection parcelEdgeCollection = null;
              var tol = 0.03 / _metersPerDatasetUnit; //3 cms
              if (!_isPCS)
                tol = Math.Atan(tol / (6378100.0 / _metersPerDatasetUnit));
              try
              {
                parcelEdgeCollection = await myParcelFabricLayer.GetSequencedParcelEdgeInfoAsync(featlyr.Key,
                    oid, null, tol,
                    ParcelLineToEdgeRelationship.BothVerticesMatchAnEdgeEnd |
                    ParcelLineToEdgeRelationship.StartVertexMatchesAnEdgeEnd |
                    ParcelLineToEdgeRelationship.EndVertexMatchesAnEdgeEnd |
                    ParcelLineToEdgeRelationship.StartVertexOnAnEdge |
                    ParcelLineToEdgeRelationship.EndVertexOnAnEdge
                    ); //ParcelLineToEdgeRelationship.All);
              }
              catch
              {
                continue;
              }

              if (parcelEdgeCollection == null)
                continue;

              var startPoint = parcelEdgeCollection.Edges[0].EdgeGeometry.Points[0].Coordinate2D;

              if (!ParcelUtils.ParcelEdgeAnalysis(parcelEdgeCollection, out bool isClosedloop, out bool allLinesHaveCogo,
                  out object[] parcelTraverseInfo))
                continue;

              if (isClosedloop && allLinesHaveCogo)
              {
                cps.Progressor.Value += 1;
                //start point coordinates x y
                //traverseInfo object list items:
                //[0] vector, [1] direction, [2] distance,
                //[3] radius, [4] arclength, [5] isMajor, [6] isLineReversed

                var radiusList = new List<double>();
                var arcLengthList = new List<double>();
                var isMajorList = new List<bool>();
                var traverseCourses = new List<Coordinate3D>();
                //create the vector list for the traverse

                var attributeLength = 0.0;
                foreach (Coordinate3D vec in parcelTraverseInfo[0] as List<object>)
                {
                  vec.Scale(_distanceUnitConversionFactor);
                  traverseCourses.Add(vec);
                  attributeLength += vec.Magnitude;
                }
                #region enhance traverse info with circular arc data
                foreach (var radiusObj in parcelTraverseInfo[3] as List<object>)
                {
                  double radius = 0.0;
                  if (radiusObj != null)
                    radius = (double)radiusObj;
                  radiusList.Add(radius * _distanceUnitConversionFactor);
                }

                foreach (var arcLengthObj in parcelTraverseInfo[4] as List<object>)
                {
                  double arcLength = 0.0;
                  if (arcLengthObj != null)
                    arcLength = (double)arcLengthObj;
                  arcLengthList.Add(arcLength * _distanceUnitConversionFactor);
                }

                foreach (var arcIsMajor in parcelTraverseInfo[5] as List<bool>)
                  isMajorList.Add(arcIsMajor);

                #endregion

                var count = (parcelTraverseInfo[0] as List<object>).Count();

                var AdjustedCoordinates = COGOUtils.CompassRuleAdjust(traverseCourses, startPoint, startPoint, radiusList, arcLengthList, isMajorList,
                  out Coordinate2D miscloseVector, out double dRatio, out double calcArea);

                var smallUnitAreaStr = string.Format(Fmt10Str1DecPl, calcArea);
                var largeUnitAreaStr = string.Format(Fmt10Str4DecPl, calcArea * smallUnitAreaToBigUnitAreaConverter);
                var areaInProjectionSmallUnitStr = string.Format(Fmt10Str1DecPl, shapeAreaInProjectionSmallUnit);
                var areaInProjectionLargeUnitStr = string.Format(Fmt10Str4DecPl, shapeAreaInProjectionLargeUnit);

                var areaSmallUnitDiff = calcArea - shapeAreaInProjectionSmallUnit;
                if (Math.Abs(areaSmallUnitDiff) < 0.1)
                  areaSmallUnitDiff = 0.0;

                var areaLargeUnitDiff = (calcArea * smallUnitAreaToBigUnitAreaConverter) - shapeAreaInProjectionLargeUnit;
                if (Math.Abs(areaLargeUnitDiff) < 0.0001)
                  areaLargeUnitDiff = 0.0;

                var areaSmallUnitDiffStr = string.Format(Fmt10StrPlusMinus1DecPl, areaSmallUnitDiff);
                var areaLargeUnitDiffStr = string.Format(Fmt10StrPlusMinus4DecPl, areaLargeUnitDiff);

                var misClose = miscloseVector.QueryComponents();
                var misCloseX = misClose.Item1;
                var misCloseY = misClose.Item2;

                if (Math.Abs(misCloseX) < 0.001)
                  misCloseX = 0.0;
                if (Math.Abs(misCloseY) < 0.001)
                  misCloseY = 0.0;

                var pt1 = MapPointBuilderEx.CreateMapPoint(0, 0);
                var pt2 = MapPointBuilderEx.CreateMapPoint(misCloseX, misCloseY);
                var misCloseXStr = string.Format(Fmt5StrPlusMinus3DecPl, misCloseX);
                var misCloseYStr = string.Format(Fmt5StrPlusMinus3DecPl, misCloseY);

                var lineVec = LineBuilderEx.CreateLineSegment(pt1, pt2);
                var misCloseDirectionStr = COGOUtils.ConvertPolarRadiansToDisplayUnit(lineVec.Angle,
                  DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Direction), true);

                var misCloseDistanceStr = string.Format(Fmt7Str3DecPl, miscloseVector.Magnitude);

                var adjustedLength = 0.0;
                for (int i=0; i < AdjustedCoordinates.Count - 1 ; i++)
                {
                  var point1 = AdjustedCoordinates[i];
                  var point2 = AdjustedCoordinates[i+1];
                  var line = LineBuilderEx.CreateLineSegment(point1, point2);
                  adjustedLength += line.Length;
                }
                //for loop traverse add the closing line distance
                var lastPoint = AdjustedCoordinates[^1];
                var firstPoint = AdjustedCoordinates[0];
                var closingLine = LineBuilderEx.CreateLineSegment(firstPoint, lastPoint);
                adjustedLength += closingLine.Length;

                var relErrorRatioStr = "1:" + (adjustedLength / miscloseVector.Magnitude).ToString("F0");
                
                var lengthDiff = attributeLength - adjustedLength;
                if (Math.Abs(lengthDiff) < 0.001)
                  lengthDiff = 0;

                var adjustedLengthStr = string.Format(Fmt10Str3DecPl, adjustedLength);
                var attributeLengthStr = string.Format(Fmt10Str3DecPl, attributeLength);
                var lengthDiffStr = string.Format(Fmt10StrPlusMinus3DecPl, lengthDiff);

                StreamWriter sw = new(Path.Combine(folderPath, oid.ToString() + ".txt"));
                sw.WriteLine(tabDelim3 + "Parcel Report");
                sw.WriteLine(dblUnderLine);
                sw.WriteLine("             Name:" + tabDelim + parcelName);
                sw.WriteLine("           Record:" + tabDelim + parcelRecord);
                sw.WriteLine("  Number of lines:" + tabDelim + count.ToString());
                sw.WriteLine("     Length units:" + tabDelim + projectDistanceUnitName);
                sw.WriteLine(" Direction format:" + tabDelim + sDTDU[0]);
                sw.WriteLine("   Direction unit:" + tabDelim + sDTDU[1]);
                sw.WriteLine("       Area units:" + tabDelim + areaUnit);
                sw.WriteLine("Adjustment Method:" + tabDelim + adjustmentMethod);
                sw.WriteLine("");
                sw.WriteLine("Area");
                sw.WriteLine(underLine);
                sw.WriteLine(tabDelim2 + "   Ground-Area:" + largeUnitAreaStr + largeAreaUnitSuff + ", " 
                  + smallUnitAreaStr + smallAreaUnitSuff);
                sw.WriteLine(tabDelim2 + "      Map-Area:" + areaInProjectionLargeUnitStr + largeAreaUnitSuff + ", " 
                  + areaInProjectionSmallUnitStr + smallAreaUnitSuff);
                sw.WriteLine(tabDelim2 + "    Difference:" + areaLargeUnitDiffStr + largeAreaUnitSuff + ", "
                  + areaSmallUnitDiffStr + smallAreaUnitSuff);
                sw.WriteLine("");
                sw.WriteLine("Perimeter");
                sw.WriteLine(underLine);
                sw.WriteLine(tabDelim + "      Attribute Length:" + tabDelim + attributeLengthStr);
                sw.WriteLine(tabDelim + "       Adjusted Length:" + tabDelim + adjustedLengthStr);
                sw.WriteLine(tabDelim2 + "    Difference:" + tabDelim + lengthDiffStr);
                sw.WriteLine("");
                sw.WriteLine("Traverse");
                sw.WriteLine(underLine);
                sw.WriteLine("Misclosure Direction, Distance:	" + tabDelim + misCloseDirectionStr + ", " 
                      + misCloseDistanceStr + distUnitSuff);
                sw.WriteLine("                Misclosure X,Y:	" + tabDelim + misCloseXStr + ", " + misCloseYStr);
                sw.WriteLine("          Relative Error Ratio:	" + tabDelim + relErrorRatioStr);
                sw.WriteLine("");
                sw.WriteLine("Original Description");
                sw.WriteLine(underLine);
                sw.WriteLine(" #   " + tabDelim + "Direction" + tabDelim + 
                  "  Distance" + tabDelim + "   Radius" + tabDelim + " Major");
                #region Parcel Report text example
                //              Parcel Report
                //================================================================
                //  Number of lines:	13
                //     Length units:	Feet US
                // Direction format:	Quadrant Bearing
                //   Direction unit:	Degrees Minutes Seconds
                //       Area units:	Acres, Square Feet US
                //Adjustment Method:	Compass

                //Area
                //----------------------------------------------------------------
                //		Ground-Area:	0.858 acres, 37374 sqft
                //    Ground-Area, unadjusted:	0.860 acres, 37460 sqft
                //		 Difference: 	-0.002 acres,  -86 sqft

                //  	     Projected-Area:	0.858 acres, 37374 sqft

                //Perimeter
                //----------------------------------------------------------------
                // 	    Adjusted Length: 	843.317 ft
                //   	   Attribute Length:	843.318 ft
                //		 Difference: 	 -0.001 ft

                //Traverse
                //----------------------------------------------------------------
                //Misclosure Direction, Distance:		N39°26'28"E,  1.338 ft
                //                Misclosure X,Y:		+0.85,  +1.033
                //          Relative Error Ratio:		1:630

                //Original Description
                //----------------------------------------------------------------
                // #   	Direction	Distance	   Radius	 Major			
                // 1- 2	N 0°30'00"W	   49.000		
                // 2- 3	N 0°30'00"W	   50.000
                // 3- 4	N63°27'06"E	  111.803		
                // 4- 5	N90°00'00"E	   50.000		
                // 5- 6	N 0°00'00"E	   50.000	  -150.000	 No
                // 6- 7	N90°00'00"E	   50.000	  +150.000	 Yes
                // 7- 8	S45°00'00"E	   70.711		
                // 8- 9	S 0°00'00"W	  100.000		
                // 9-10	S 0°00'00"W	   50.000
                //10-11	N63°26'06"W	   55.902		
                //11-12	S63°26'06"W	   55.902		
                //12-13	S90°00'00"W	  100.000		
                //13- 1	S90°00'00"W	   50.000	

                //Adjusted Values
                //----------------------------------------------------------------
                // #	Direction	Distance/Chord		Residuals
                // 1- 2	N 0°26'30"W	   49.060	  +0°03'30"    +0.060
                // 2- 3	N 0°26'30"W	   50.061	  +0°03'30"    +0.061
                // 3- 4   N63°24'53"E	  111.965	  -0°02'13"    +0.162
                // 4- 5   N89°55'48"E	   50.050	  -0°04'12"    +0.050
                // 5- 6	N 0°03'28"E	   50.061	  +0°03'28"    +0.061
                // 6- 7	N89°55'48"E	   50.050	  -0°04'12"    +0.050
                // 7- 8	S45°05'26"E	   70.700	  -0°05'26"    -0.011
                // 8- 9	S 0°03'28"E	   99.878	  -0°03'28"    -0.122
                // 9-10	S 0°03'28"E	   49.939	  -0°03'28"    -0.061
                //10-11	N63°20'47"W	   55.882	  +0°05'19"    -0.020
                //11-12	S63°28'19"W	   55.821	  +0°02'13"    -0.081
                //12-13	N89°55'47"W	   99.899	  +0°04'13"    -0.101
                //13- 1	N89°55'47"W	   49.950	  +0°04'13"    -0.050
                #endregion

                #region Original Description
                var idx = 0;
                foreach (Coordinate3D vec in parcelTraverseInfo[0] as List<object>)
                {
                  if (vec.Magnitude == 0.0)
                    continue;
                  //get the direction format and units from the backstage default settings
                  var direction = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDisplayUnit(vec.Azimuth * 180.0 / Math.PI,
                    DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Direction));

                  bool isCircularArc = radiusList[idx] != 0.0;
                  var leftRight = (radiusList[idx] < 0.0) ? "-" : "+";

                  var toPointStr = string.Format(Fmt2Str0DecPl, idx + 2);

                  if (idx == count - 1)
                    toPointStr = " 1";

                  vec.Scale(_distanceUnitConversionFactor);
                  var sCourse = string.Format(Fmt2Str0DecPl, idx+1) + "-" + toPointStr + tabDelim + 
                    direction + tabDelim + string.Format(Fmt10Str3DecPl, vec.Magnitude);
                  if (isCircularArc)
                    sCourse += tabDelim + string.Format(Fmt10StrPlusMinus3DecPl, radiusList[idx])
                    + tabDelim + "  " + isMajorList[idx].ToString().Replace("False","No").Replace("True", "Yes");

                  sw.WriteLine(sCourse);
                  idx++;
                }
                #endregion
                sw.WriteLine("");
                sw.WriteLine("Adjusted Values");
                sw.WriteLine(underLine);
                sw.WriteLine(" #   " + tabDelim + "Direction" + tabDelim + "  Distance/Chord" + tabDelim + "Residuals");

                #region Adjusted Values
                //for loop traverse start with the initial line to match the first vector
                lastPoint = AdjustedCoordinates[^1];
                firstPoint = AdjustedCoordinates[0];
                var initialLine = LineBuilderEx.CreateLineSegment(lastPoint, firstPoint);
                var adjustedDirection = COGOUtils.ConvertPolarRadiansToNorthAzimuth(initialLine.Angle);
                var originalDirection = traverseCourses[0].Azimuth * 180.0/Math.PI;
                var t = Math.Abs(adjustedDirection - originalDirection) % 360.0; //fMOD in C++
                var delta = 180.0 - Math.Abs(t - 180.0);
                var directionResidual = adjustedDirection > originalDirection ? delta : -delta;

                var adjustedDirectionStr = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDisplayUnit(adjustedDirection,
                  DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Direction));
                var directionResidualStr = COGOUtils.ConvertDirectionDifferenceInDecimalDegreesToDisplayUnitAngle(directionResidual,
                    DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Direction));
                directionResidualStr = string.Format(Fmt11Str0DecPl, directionResidualStr);

                var adjustedDistance = initialLine.Length;
                var originalDistance = traverseCourses[0].Magnitude;
                var distanceResidual = adjustedDistance - originalDistance;

                var adjustedDistanceStr = string.Format(Fmt10Str3DecPl, adjustedDistance);
                var distanceResidualStr = string.Format(Fmt7StrPlusMinus3DecPl, distanceResidual);
                sw.WriteLine(" 1- 2" + tabDelim + adjustedDirectionStr + tabDelim + adjustedDistanceStr 
                  + tabDelim + directionResidualStr + distanceResidualStr);
                int skip = 0;
                for (int i = 0; i < AdjustedCoordinates.Count - 1; i++)
                {
                  var point1 = AdjustedCoordinates[i];
                  var point2 = AdjustedCoordinates[i + 1];
                  var line = LineBuilderEx.CreateLineSegment(point1, point2);
                  adjustedDirection = COGOUtils.ConvertPolarRadiansToNorthAzimuth(line.Angle);
                  originalDirection = traverseCourses[i+1].Azimuth * 180.0/Math.PI;//todo

                  t = Math.Abs(adjustedDirection - originalDirection) % 360.0; //fMOD in C++
                  delta = 180.0 - Math.Abs(t - 180.0);
                  directionResidual = adjustedDirection > originalDirection ? delta : -delta;

                  adjustedDirectionStr = COGOUtils.ConvertNorthAzimuthDecimalDegreesToDisplayUnit(adjustedDirection,
                    DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Direction));
                  directionResidualStr = COGOUtils.ConvertDirectionDifferenceInDecimalDegreesToDisplayUnitAngle(directionResidual,
                    DisplayUnitFormats.Instance.GetDefaultProjectUnitFormat(UnitFormatType.Direction));
                  directionResidualStr = string.Format(Fmt11Str0DecPl, directionResidualStr);
                  adjustedDistance = line.Length;
                  originalDistance = traverseCourses[i+1].Magnitude;
                  distanceResidual = adjustedDistance - originalDistance;

                  adjustedDistanceStr = string.Format(Fmt10Str3DecPl, adjustedDistance);
                  distanceResidualStr = string.Format(Fmt7StrPlusMinus3DecPl, distanceResidual);

                  var toPointStr = string.Format(Fmt2Str0DecPl, i + 3 - skip);
                  if (i == AdjustedCoordinates.Count-2)
                    toPointStr = " 1";
                  var fromToStr = string.Format(Fmt2Str0DecPl, i + 2 - skip) + "-" + toPointStr;
                  if (originalDistance == 0.0)
                  {
                    skip++;
                    continue;
                  }
                  sw.WriteLine(fromToStr + tabDelim + adjustedDirectionStr + tabDelim + adjustedDistanceStr
                    + tabDelim + directionResidualStr + distanceResidualStr);
                }

                #endregion
                sw.Close();
              }
            }
            if (cps.Progressor.CancellationToken.IsCancellationRequested)
              break;
            cps.Progressor.Status = "Parcels with a valid loop traverse: " + cps.Progressor.Value;
          }
          return "";
        }
        catch (Exception ex)
        {
          return ex.Message;
        }
      }, cps.Progressor);
    }

    //this code below is used for tool enablement, based on parcel selection
    //but is commented out for performance related reason. Fix TBD.

    //protected override void OnUpdate()
    //{
    //  QueuedTask.Run(() =>
    //  {
    //    if(Module1.Current.HasParcelPolygonSelection)
    //    {
    //      this.Enabled = true;  //tool is enabled  
    //                            //this.Tooltip = "";
    //    }
    //    else
    //    {
    //      this.Enabled = false;  //tool is disabled  
    //                             //customize your disabledText here
    //      this.DisabledTooltip = "There is no parcel selection.";
    //    }
    //  });
    //}
  }
}
